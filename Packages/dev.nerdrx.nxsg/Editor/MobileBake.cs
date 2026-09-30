using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NXSG.Editor
{
    [Serializable]
    public sealed class MobileBakeEntry
    {
        public Material desktop;
        public Material mobile;
        public string graphPath;
        public string graphHash;
    }

    public sealed class MobileBakeRegistry : ScriptableObject
    {
        public List<MobileBakeEntry> entries = new List<MobileBakeEntry>();
    }

    public static class MobileBake
    {
        const string RegistryPath = "Assets/NXSGGenerated/MobileBakes.asset";
        const string MobileShader = "VRChat/Mobile/Toon Standard";

        public static string Bake(ShaderGraph graph, string sourcePath, Material desktop, int resolution = 1024,
            float snapshotSeconds = 0, bool audioEnabled = false, float audioValue = 0)
        {
            if (graph == null || desktop == null) throw new InvalidOperationException("Build a graph and choose its desktop material first.");
            var shader = Shader.Find(MobileShader);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("VRChat SDK's " + MobileShader + " shader is required in this project.");
            var graphPath = ProjectPath(sourcePath);
            var graphGuid = AssetDatabase.AssetPathToGUID(graphPath);
            var desktopPath = AssetDatabase.GetAssetPath(desktop);
            var hasDesktopId = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(desktop, out string desktopGuid, out long desktopLocalId);
            if (string.IsNullOrEmpty(graphGuid) || !hasDesktopId || string.IsNullOrEmpty(desktopGuid) || string.IsNullOrEmpty(desktopPath))
                throw new InvalidOperationException("Save both the graph and desktop material as project assets before baking.");

            var output = graph.Nodes.SingleOrDefault(n => n.Operation == "core.output");
            var surfaceLink = graph.Connections.SingleOrDefault(e => output != null && e.To.NodeId == output.Id && e.To.PortId == "surface");
            var surface = graph.Nodes.SingleOrDefault(n => surfaceLink != null && n.Id == surfaceLink.From.NodeId);
            var notes = new List<string>();
            while (surface != null && new[] { "core.surfaceParticles", "core.softOutline", "core.outline", "core.fur", "core.shell" }.Contains(surface.Operation))
            {
                notes.Add(NodeCatalog.Title(surface.Operation) + " geometry omitted");
                var baseLink = Input(graph, surface, "base");
                surface = graph.Nodes.SingleOrDefault(n => baseLink != null && n.Id == baseLink.From.NodeId);
            }
            if (surface == null || !new[] { "core.toonSurface", "core.pbrSurface", "core.unlitSurface" }.Contains(surface.Operation))
                throw new InvalidOperationException("Mobile baking needs a direct Toon, PBR, or Unlit surface connected to Output. Geometry, particles, and volume surfaces cannot be flattened into a mobile material.");

            var opacityLink = Input(graph, surface, "opacity");
            if (opacityLink != null || ((double?)surface.Properties["opacity"] ?? 1) < .999 ||
                ((int?)surface.Properties["useAlbedoAlpha"] ?? 0) != 0 || ((double?)surface.Properties["cutoff"] ?? 0) > 0)
                notes.Add("opacity and cutout flattened to opaque");
            if (Input(graph, surface, "displacement") != null || Math.Abs((double?)surface.Properties["displacement"] ?? 0) > .0001)
                notes.Add("displacement geometry omitted");

            var albedoLink = Input(graph, surface, "albedo");
            var emissionLink = Input(graph, surface, "emission");
            var metallicLink = Input(graph, surface, "metallic");
            var roughnessLink = Input(graph, surface, "roughness");
            var occlusionLink = Input(graph, surface, "occlusion");
            var normalLink = Input(graph, surface, "normal");
            if (emissionLink != null && HasUpstream(graph, emissionLink.From.NodeId, "core.ltcgi"))
                notes.Add("LTCGI lighting omitted; input color passed through when connected");

            var directory = "Assets/NXSGGenerated/" + graphGuid + "/Mobile/" + desktopGuid + "-" + desktopLocalId;
            Directory.CreateDirectory(directory);
            var albedoPath = directory + "/Albedo.png";
            var emissionPath = directory + "/Emission.png";
            var metallicPath = directory + "/Metallic.png";
            var glossPath = directory + "/Gloss.png";
            var occlusionPath = directory + "/Occlusion.png";
            var normalPath = directory + "/Normal.png";
            var materialPath = directory + "/Mobile.mat";
            Texture2D albedo = null, emission = null, metallic = null, gloss = null, occlusion = null, normal = null;
            try
            {
                if (albedoLink == null)
                {
                    albedo = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                    albedo.SetPixel(0, 0, new Color(0, 0, 0, 1));
                    albedo.Apply();
                }
                else albedo = GraphBaker.RenderSnapshot(graph, albedoLink.From.NodeId, albedoLink.From.PortId, resolution, desktop,
                    snapshotSeconds, audioEnabled, audioValue);
                // Toon Standard is opaque even when the desktop color branch carries alpha.
                var albedoPixels = albedo.GetPixels();
                for (var i = 0; i < albedoPixels.Length; i++)
                {
                    var pixel = albedoPixels[i]; pixel.a = 1; albedoPixels[i] = pixel;
                }
                albedo.SetPixels(albedoPixels);
                albedo.Apply();
                if (emissionLink != null) emission = GraphBaker.RenderSnapshot(graph, emissionLink.From.NodeId, emissionLink.From.PortId,
                    resolution, desktop, snapshotSeconds, audioEnabled, audioValue);
                if (metallicLink != null) metallic = GraphBaker.RenderSnapshot(graph, metallicLink.From.NodeId, metallicLink.From.PortId,
                    resolution, desktop, snapshotSeconds, audioEnabled, audioValue);
                if (roughnessLink != null)
                {
                    gloss = GraphBaker.RenderSnapshot(graph, roughnessLink.From.NodeId, roughnessLink.From.PortId,
                        resolution, desktop, snapshotSeconds, audioEnabled, audioValue);
                    var values = gloss.GetPixels();
                    for (var i = 0; i < values.Length; i++) values[i] = new Color(1-values[i].r, 1-values[i].r, 1-values[i].r, 1);
                    gloss.SetPixels(values); gloss.Apply();
                }
                if (occlusionLink != null) occlusion = GraphBaker.RenderSnapshot(graph, occlusionLink.From.NodeId, occlusionLink.From.PortId,
                    resolution, desktop, snapshotSeconds, audioEnabled, audioValue);
                if (normalLink != null) normal = GraphBaker.RenderSnapshot(graph, normalLink.From.NodeId, normalLink.From.PortId,
                    resolution, desktop, snapshotSeconds, audioEnabled, audioValue);
                WriteColorPng(albedo, albedoPath);
                if (emission != null) WriteColorPng(emission, emissionPath);
                if (metallic != null) WriteColorPng(metallic, metallicPath, false);
                if (gloss != null) WriteColorPng(gloss, glossPath, false);
                if (occlusion != null) WriteColorPng(occlusion, occlusionPath, false);
                if (normal != null) WriteColorPng(normal, normalPath, false, true);
                var mobile = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mobile == null)
                {
                    mobile = new Material(shader) { name = desktop.name + " Mobile" };
                    AssetDatabase.CreateAsset(mobile, materialPath);
                }
                else mobile.shader = shader;
                mobile.SetColor("_Color", Color.white);
                mobile.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
                mobile.SetFloat("_Culling", 2);
                mobile.enableInstancing = true;
                if (surface.Operation == "core.pbrSurface")
                {
                    mobile.SetTexture("_MetallicMap", metallic == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath));
                    mobile.SetFloat("_MetallicMapChannel", 0);
                    mobile.SetFloat("_MetallicStrength", metallic == null ? Mathf.Clamp01((float?)surface.Properties["metallic"] ?? 0) : 1);
                    mobile.SetTexture("_GlossMap", gloss == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(glossPath));
                    mobile.SetFloat("_GlossMapChannel", 0);
                    mobile.SetFloat("_GlossStrength", gloss == null ? 1 - Mathf.Clamp01((float?)surface.Properties["roughness"] ?? .5f) : 1);
                }
                else
                {
                    mobile.SetTexture("_MetallicMap", null);
                    mobile.SetFloat("_MetallicStrength", 0);
                    mobile.SetTexture("_GlossMap", null);
                    mobile.SetFloat("_GlossStrength", .5f);
                }
                mobile.SetTexture("_OcclusionMap", occlusion == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(occlusionPath));
                mobile.SetFloat("_OcclusionMapChannel", 0);
                mobile.SetFloat("_OcclusionStrength", 1);
                mobile.SetTexture("_BumpMap", normal == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                mobile.SetFloat("_BumpScale", normal == null ? 0 : 1);
                if (emission != null)
                {
                    mobile.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath));
                    mobile.SetColor("_EmissionColor", Color.white);
                    mobile.SetFloat("_EmissionStrength", 1);
                    mobile.EnableKeyword("_EMISSION");
                }
                else
                {
                    mobile.SetTexture("_EmissionMap", null);
                    mobile.SetColor("_EmissionColor", Color.black);
                    mobile.DisableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(mobile);
                var registry = LoadRegistry(true);
                var entry = registry.entries.FirstOrDefault(e => e.desktop == desktop);
                if (entry == null) { entry = new MobileBakeEntry(); registry.entries.Add(entry); }
                entry.desktop = desktop;
                entry.mobile = mobile;
                entry.graphPath = graphPath;
                entry.graphHash = GraphJson.ComputeSemanticHash(graph);
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                MobileMaterialSwap.SyncOpenScenes();
                var handledPorts = new[] { "albedo", "emission", "metallic", "roughness", "occlusion", "normal", "opacity", "displacement" };
                var omitted = graph.Connections.Where(e => e.To.NodeId == surface.Id && !handledPorts.Contains(e.To.PortId))
                    .Select(e => e.To.PortId).Distinct().ToArray();
                if (omitted.Length > 0) notes.Add("not baked: " + string.Join(", ", omitted));
                return "Mobile snapshot at " + snapshotSeconds.ToString("F2") + " s: " + materialPath +
                    (notes.Count == 0 ? "" : ". " + string.Join("; ", notes) + ".");
            }
            finally
            {
                if (albedo != null) UnityEngine.Object.DestroyImmediate(albedo);
                if (emission != null) UnityEngine.Object.DestroyImmediate(emission);
                if (metallic != null) UnityEngine.Object.DestroyImmediate(metallic);
                if (gloss != null) UnityEngine.Object.DestroyImmediate(gloss);
                if (occlusion != null) UnityEngine.Object.DestroyImmediate(occlusion);
                if (normal != null) UnityEngine.Object.DestroyImmediate(normal);
            }
        }

        static GraphConnection Input(ShaderGraph graph, GraphNode node, string port) =>
            graph.Connections.SingleOrDefault(e => e.To.NodeId == node.Id && e.To.PortId == port);

        static bool HasUpstream(ShaderGraph graph, string nodeId, string operation)
        {
            var seen = new HashSet<string>();
            var pending = new Stack<string>(); pending.Push(nodeId);
            while (pending.Count > 0)
            {
                var id = pending.Pop(); if (!seen.Add(id)) continue;
                if (graph.Nodes.Any(n => n.Id == id && n.Operation == operation)) return true;
                foreach (var edge in graph.Connections.Where(e => e.To.NodeId == id)) pending.Push(edge.From.NodeId);
            }
            return false;
        }


        static string ProjectPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) throw new InvalidOperationException("Save the graph inside Assets first.");
            var root = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(absolute);
            if (!path.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Save the graph inside Assets first.");
            return "Assets/" + path.Substring(root.Length).Replace('\\', '/');
        }

        static void WriteColorPng(Texture2D linear, string path, bool srgb = true, bool normalMap = false)
        {
            // Readback is linear; Unity's color texture importer expects sRGB PNG values.
            var colors = linear.GetPixels();
            for (var i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                if (srgb) { var gamma = c.gamma; gamma.a = c.a; colors[i] = gamma; }
            }
            var png = new Texture2D(linear.width, linear.height, TextureFormat.RGBA32, false, !srgb);
            try { png.SetPixels(colors); png.Apply(); File.WriteAllBytes(path, png.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(png); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = srgb;
                if (normalMap) { importer.textureType = TextureImporterType.NormalMap; importer.convertToNormalmap = false; }
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.maxTextureSize = linear.width;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        internal static MobileBakeRegistry LoadRegistry(bool create = false)
        {
            var registry = AssetDatabase.LoadAssetAtPath<MobileBakeRegistry>(RegistryPath);
            if (registry != null || !create) return registry;
            Directory.CreateDirectory("Assets/NXSGGenerated");
            registry = ScriptableObject.CreateInstance<MobileBakeRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
            return registry;
        }

        public static Material DesktopFor(Material selected, Material built)
        {
            var registry = LoadRegistry();
            var entry = registry != null ? registry.entries.FirstOrDefault(e => e.mobile == selected && e.desktop != null) : null;
            if (entry != null) return entry.desktop;
            return selected != null && selected.shader == built.shader ? selected : built;
        }
    }

    public sealed class MobileTargetWatcher : IActiveBuildTargetChanged
    {
        public int callbackOrder => 0;
        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => MobileMaterialSwap.SyncOpenScenes();
    }

    [InitializeOnLoad]
    public static class MobileMaterialSwap
    {
        static readonly HashSet<string> WarnedStale = new HashSet<string>();
        static MobileMaterialSwap()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += SyncOpenScenes;
            EditorApplication.hierarchyChanged += QueueSync;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode) => SyncOpenScenes();
        static bool queued;
        static void QueueSync()
        {
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += () => { queued = false; SyncOpenScenes(); };
        }

        [MenuItem("Tools/NXSG/Sync Mobile Materials For Build Target")]
        public static void SyncOpenScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SyncOpenScenesForTarget(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);
        }

        public static void SyncOpenScenesForTarget(bool android)
        {
            var registry = MobileBake.LoadRegistry();
            if (registry == null) return;
            if (android)
            {
                foreach (var entry in registry.entries.Where(e => e.desktop != null && e.mobile != null))
                {
                    var current = false;
                    try { current = GraphJson.ComputeSemanticHash(GraphJson.Parse(File.ReadAllText(entry.graphPath))) == entry.graphHash; }
                    catch (Exception) { }
                    if (!current && WarnedStale.Add(entry.graphPath ?? ""))
                        Debug.LogWarning("NXSG mobile bake is stale for " + entry.graphPath + ". Open its graph and Bake for Mobile again.");
                    if (current) WarnedStale.Remove(entry.graphPath ?? "");
                }
            }
            var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded) continue;
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var entry = registry.entries.FirstOrDefault(e => e.desktop != null && e.mobile != null &&
                        (android ? materials[i] == e.desktop : materials[i] == e.mobile));
                    if (entry == null) continue;
                    materials[i] = android ? entry.mobile : entry.desktop;
                    changed = true;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "NXSG mobile material swap");
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            }
        }
    }
}
