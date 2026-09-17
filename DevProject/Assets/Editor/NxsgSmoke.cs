using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Small editor-owned smoke fixture. It exercises the current core contract,
/// imports the emitted Built-In shader, and captures a real RenderTexture when
/// the Editor has a graphics device. It is intentionally not a client upload
/// or VR headset test.
/// </summary>
public static class NxsgSmoke
{
    private const string ResultsDirectory = "Assets/SmokeResults";
    private const string ShaderAssetPath = ResultsDirectory + "/NxsgGenerated.shader";
    private const string ResultsPath = ResultsDirectory + "/NxsgSmokeResults.json";
    private const string PreviewPath = ResultsDirectory + "/NxsgPreview.png";

    [MenuItem("Tools/NXSG/Run Smoke")]
    public static void Run()
    {
        var result = new SmokeResult
        {
            unityVersion = Application.unityVersion,
            startedUtc = DateTime.UtcNow.ToString("O"),
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            graphicsDevice = SystemInfo.graphicsDeviceName
        };

        try
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "SmokeResults"));
            RunCore(result);
        }
        catch (Exception exception)
        {
            result.errors.Add(exception.ToString());
        }
        finally
        {
            result.finishedUtc = DateTime.UtcNow.ToString("O");
            var resultPath = Path.Combine(Application.dataPath, "SmokeResults", "NxsgSmokeResults.json");
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            AssetDatabase.Refresh();
            Debug.Log("NXSG smoke result: " + resultPath + " passed=" + result.passed);
            EditorApplication.Exit(result.passed ? 0 : 1);
        }
    }

    private static void RunCore(SmokeResult result)
    {
        var graph = GraphSamples.CreateDefault();
        CheckSdkFixture(result);
        var source = GraphJson.Serialize(graph, false);
        var parsed = GraphJson.Parse(source);
        result.sourceBytes = System.Text.Encoding.UTF8.GetByteCount(source);
        result.semanticHash = GraphJson.ComputeSemanticHash(graph);
        result.roundTripHash = GraphJson.ComputeSemanticHash(parsed);
        result.roundTripStable = result.semanticHash == result.roundTripHash;

        var validation = GraphValidator.Validate(parsed);
        result.validationValid = validation.IsValid;
        result.validationDiagnostics = validation.Diagnostics.Count;

        var layoutHash = result.semanticHash;
        if (parsed.Layout == null) parsed.Layout = new GraphLayout();
        if (parsed.Layout.Nodes == null) parsed.Layout.Nodes = new Dictionary<string, GraphNodeLayout>();
        parsed.Layout.Nodes[parsed.Nodes[0].Id] = new GraphNodeLayout { X = 400, Y = 200 };
        result.layoutIgnoredByHash = layoutHash == GraphJson.ComputeSemanticHash(parsed);

        var changed = GraphJson.Parse(source);
        changed.Nodes[0].Properties["smokeMutation"] = 1;
        result.semanticMutationChangesHash = layoutHash != GraphJson.ComputeSemanticHash(changed);

        var emission = ShaderEmitter.Emit(graph);
        if (!emission.Succeeded) throw new InvalidOperationException(string.Join("; ", emission.Diagnostics.Select(d => d.Message)));
        var shaderText = emission.ShaderSource;
        result.shaderBytes = System.Text.Encoding.UTF8.GetByteCount(shaderText);
        var absoluteShaderPath = Path.Combine(Application.dataPath, "SmokeResults", "NxsgGenerated.shader");
        File.WriteAllText(absoluteShaderPath, shaderText);
        AssetDatabase.ImportAsset(ShaderAssetPath, ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
        result.shaderImported = shader != null;
        result.shaderSupported = shader != null && shader.isSupported;
        if (shader == null) throw new InvalidOperationException("Generated shader did not import: " + ShaderAssetPath);

        ShaderUtil.allowAsyncCompilation = false;
        var material = new Material(shader) { name = "NXSG Smoke Material" };
        result.materialCreated = material != null;
        TryRender(result, material);
        UnityEngine.Object.DestroyImmediate(material);

        RunBuildTransactionSmoke(result, graph);

        result.passed = result.roundTripStable && result.validationValid &&
            result.layoutIgnoredByHash && result.semanticMutationChangesHash &&
            result.shaderImported && result.materialCreated && result.shaderSupported &&
            result.renderCaptured && result.renderHasSubject && !ShaderUtil.ShaderHasError(shader) &&
            result.sdkAssembliesPresent && result.buildSucceeded && result.rollbackShaderCheckpoint &&
            result.rollbackMaterialCheckpoint && result.rebuildPreservedGuids &&
            result.rebuildPreservedTint && result.rebuildPreservedTexture;
    }

    private static void CheckSdkFixture(SmokeResult result)
    {
        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var baseManifestPath = Path.Combine(projectRoot, "Packages/com.vrchat.base/package.json");
        var avatarsManifestPath = Path.Combine(projectRoot, "Packages/com.vrchat.avatars/package.json");
        var baseAssembly = Path.Combine(projectRoot, "Packages/com.vrchat.base/Runtime/VRCSDK/VRC.SDKBase.asmdef");
        var avatarsAssembly = Path.Combine(projectRoot, "Packages/com.vrchat.avatars/Runtime/VRCSDK/SDK3A/VRC.SDK3A.asmdef");
        if (!File.Exists(baseManifestPath) || !File.Exists(avatarsManifestPath) ||
            !File.Exists(baseAssembly) || !File.Exists(avatarsAssembly))
            throw new InvalidOperationException("Pinned VRChat SDK fixture is incomplete; run setup-vrchat-fixture.py.");
        var baseManifest = JObject.Parse(File.ReadAllText(baseManifestPath));
        var avatarsManifest = JObject.Parse(File.ReadAllText(avatarsManifestPath));
        result.sdkBaseVersion = (string)baseManifest["version"];
        result.sdkAvatarsVersion = (string)avatarsManifest["version"];
        result.sdkAvatarsBaseRequirement = (string)avatarsManifest["vpmDependencies"]?["com.vrchat.base"];
        result.sdkAssembliesPresent = result.sdkBaseVersion == "3.10.5" &&
            result.sdkAvatarsVersion == "3.10.5" && result.sdkAvatarsBaseRequirement == "3.10.5";
        if (!result.sdkAssembliesPresent)
            throw new InvalidOperationException("Pinned VRChat SDK fixture versions do not match 3.10.5.");
    }

    private static void RunBuildTransactionSmoke(SmokeResult result, ShaderGraph graph)
    {
        var graphPath = ResultsDirectory + "/Example.nxsg";
        var graphAbsolute = Path.Combine(Application.dataPath, "SmokeResults", "Example.nxsg");
        var texturePath = ResultsDirectory + "/NxsgFixtureTexture.asset";
        var textureAbsolute = Path.Combine(Application.dataPath, "SmokeResults", "NxsgFixtureTexture.asset");
        Texture2D fixtureTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (fixtureTexture == null)
        {
            fixtureTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            fixtureTexture.SetPixels(new[] { Color.cyan, Color.magenta, Color.yellow, Color.black });
            fixtureTexture.Apply();
            AssetDatabase.CreateAsset(fixtureTexture, texturePath);
            AssetDatabase.SaveAssets();
        }
        var adapter = new JObject { ["textures"] = new JObject { ["white"] = AssetDatabase.AssetPathToGUID(texturePath) } };
        graph.Adapter = adapter;
        var originalSource = GraphJson.Serialize(graph, true);
        File.WriteAllText(graphAbsolute, originalSource);
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var graphGuid = AssetDatabase.AssetPathToGUID(graphPath);
        var generatedDirectory = "Assets/NXSGGenerated/" + graphGuid;
        var shaderPath = generatedDirectory + "/Material.shader";
        var materialPath = generatedDirectory + "/Material.mat";
        var material = NXSG.Editor.GraphBuild.Build(graph, graphAbsolute);
        result.buildSucceeded = material != null;
        result.buildMaterialGuid = AssetDatabase.AssetPathToGUID(materialPath);
        result.buildShaderGuid = AssetDatabase.AssetPathToGUID(shaderPath);
        result.customTintBefore = material.GetColor("_Color");
        var customTint = new Color(.13f, .71f, .42f, 1f);
        material.SetColor("_Color", customTint);
        material.SetTexture("_MainTex", fixtureTexture);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        result.customTintAfterSave = material.GetColor("_Color");
        result.customTextureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.GetTexture("_MainTex")));
        var originalShaderBytes = File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + shaderPath));
        var originalMaterialBytes = File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + materialPath));

        var variant = GraphJson.Parse(originalSource);
        variant.Parameters.Add(new GraphParameter
        {
            Id = "smoke_parameter",
            Name = "Smoke Parameter",
            Type = GraphValueType.Float,
            Binding = GraphBindingKind.Material,
            DefaultValue = 0.25f,
            Exposed = true
        });
        var variantSource = GraphJson.Serialize(variant, true);
        File.WriteAllText(graphAbsolute, variantSource);
        AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        result.rollbackShaderCheckpoint = RunRollbackCheckpoint(variant, graphAbsolute, "shader-promoted", shaderPath, materialPath, originalShaderBytes, originalMaterialBytes);
        result.rollbackMaterialCheckpoint = RunRollbackCheckpoint(variant, graphAbsolute, "material-promoted", shaderPath, materialPath, originalShaderBytes, originalMaterialBytes);
        GraphBuild.Checkpoint = null;
        File.WriteAllText(graphAbsolute, originalSource);
        AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var rebuilt = NXSG.Editor.GraphBuild.Build(graph, graphAbsolute);
        result.rebuildPreservedGuids = AssetDatabase.AssetPathToGUID(materialPath) == result.buildMaterialGuid &&
            AssetDatabase.AssetPathToGUID(shaderPath) == result.buildShaderGuid;
        result.rebuildPreservedTint = NearlyEqual(rebuilt.GetColor("_Color"), customTint);
        result.rebuildPreservedTexture = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(rebuilt.GetTexture("_MainTex"))) == result.customTextureGuid;
    }

    private static bool RunRollbackCheckpoint(ShaderGraph graph, string sourcePath, string phase,
        string shaderPath, string materialPath, byte[] originalShader, byte[] originalMaterial)
    {
        try
        {
            GraphBuild.Checkpoint = current => { if (current == phase) throw new InvalidOperationException("NXSG smoke rollback at " + phase); };
            NXSG.Editor.GraphBuild.Build(graph, sourcePath);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            if (!exception.Message.Contains("NXSG smoke rollback", StringComparison.Ordinal)) throw;
            var shader = File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + shaderPath));
            var material = File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + materialPath));
            return originalShader.SequenceEqual(shader) && originalMaterial.SequenceEqual(material);
        }
        finally { GraphBuild.Checkpoint = null; }
    }

    private static bool NearlyEqual(Color left, Color right)
    {
        return Vector4.Distance(left, right) < .0001f;
    }

    [MenuItem("Tools/NXSG/Open Smoke Graph")]
    public static void OpenEditor()
    {
        var path = Path.Combine(Application.dataPath, "SmokeResults/Example.nxsg");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, GraphJson.Serialize(GraphSamples.CreateDefault(), true));
            AssetDatabase.Refresh();
        }
        EditorApplication.delayCall += () =>
        {
            NXSG.Editor.GraphWindow.Open(path);
            var window = EditorWindow.GetWindow<NXSG.Editor.GraphWindow>();
            window.position = new Rect(0, 0, 1380, 820);
            window.Focus();
        };
        EditorApplication.update += CloseHiddenEditor;
    }

    private static void CloseHiddenEditor()
    {
        var signal = Path.GetFullPath(Path.Combine(Application.dataPath, "../../work/unity/close-editor"));
        if (!File.Exists(signal)) return;
        File.Delete(signal);
        EditorApplication.update -= CloseHiddenEditor;
        var window = EditorWindow.GetWindow<NXSG.Editor.GraphWindow>();
        window.DiscardChanges();
        window.Close();
        EditorApplication.Exit(0);
    }

    private static void TryRender(SmokeResult result, Material material)
    {
        GameObject subject = null;
        GameObject cameraObject = null;
        GameObject lightObject = null;
        RenderTexture target = null;
        Texture2D capture = null;
        try
        {
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            subject.name = "NXSG Smoke Subject";
            subject.GetComponent<Renderer>().sharedMaterial = material;

            lightObject = new GameObject("NXSG Smoke Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightObject.transform.rotation = Quaternion.Euler(35, -25, 0);

            cameraObject = new GameObject("NXSG Smoke Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1);
            camera.transform.position = new Vector3(0, 0, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            capture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            capture.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "SmokeResults", "NxsgPreview.png"), capture.EncodeToPNG());
            result.renderCaptured = true;
            var center = capture.GetPixel(128, 128);
            var corner = capture.GetPixel(2, 2);
            result.renderHasSubject = Vector3.Distance(new Vector3(center.r, center.g, center.b), new Vector3(corner.r, corner.g, corner.b)) > .05f
                && !(center.r > .8f && center.b > .8f && center.g < .2f);
        }
        catch (Exception exception)
        {
            result.renderError = exception.Message;
        }
        finally
        {
            RenderTexture.active = null;
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            if (cameraObject != null) cameraObject.GetComponent<Camera>().targetTexture = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
        }
    }

    [Serializable]
    private sealed class SmokeResult
    {
        public bool passed;
        public string unityVersion;
        public string graphicsApi;
        public string graphicsDevice;
        public bool renderHasSubject;
        public string startedUtc;
        public string finishedUtc;
        public int sourceBytes;
        public int shaderBytes;
        public string semanticHash;
        public string roundTripHash;
        public bool roundTripStable;
        public bool validationValid;
        public int validationDiagnostics;
        public bool layoutIgnoredByHash;
        public bool semanticMutationChangesHash;
        public bool shaderImported;
        public bool shaderSupported;
        public bool materialCreated;
        public bool renderCaptured;
        public string renderError;
        public bool sdkAssembliesPresent;
        public string sdkBaseVersion;
        public string sdkAvatarsVersion;
        public string sdkAvatarsBaseRequirement;
        public bool buildSucceeded;
        public string buildMaterialGuid;
        public string buildShaderGuid;
        public Color customTintBefore;
        public Color customTintAfterSave;
        public string customTextureGuid;
        public bool rollbackShaderCheckpoint;
        public bool rollbackMaterialCheckpoint;
        public bool rebuildPreservedGuids;
        public bool rebuildPreservedTint;
        public bool rebuildPreservedTexture;
        public List<string> errors = new List<string>();
    }
}
