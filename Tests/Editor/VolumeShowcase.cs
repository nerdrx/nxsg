using System;
using System.IO;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

namespace NXSG.Tests
{
    public static class VolumeShowcase
    {
        static readonly string[] Samples = { "Volume Nebula", "Volume Carved Orb", "Volume Smoke Ring" };
        static readonly string[] Outputs = { "volume-nebula.png", "volume-carved-orb.png", "volume-smoke-ring.png" };

        public static void Run()
        {
            GameObject subject = null, cameraObject = null;
            RenderTexture target = null;
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
                RenderSettings.ambientLight = new Color(.2f,.23f,.3f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                var light = new GameObject("Showcase key").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.4f; light.transform.rotation=Quaternion.Euler(38,-35,0);
                subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                subject.transform.position = Vector3.zero;

                cameraObject = new GameObject("NXSG Volume Showcase Camera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.002f, .004f, .012f, 1f);
                camera.transform.position = new Vector3(1f, .65f, -1.5f).normalized * 1.4f;
                camera.transform.LookAt(Vector3.zero);
                camera.fieldOfView = 45f;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 20f;

                target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.Create();
                camera.targetTexture = target;
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NXSG.Backend.ShaderEmitter).Assembly).resolvedPath;
                var root = Directory.GetParent(Directory.GetParent(package).FullName).FullName;
                var outputDirectory = Path.Combine(root, "work", "showcase");
                Directory.CreateDirectory(outputDirectory);

                for (var i = 0; i < Samples.Length; i++)
                {
                    var path = Path.Combine(package, "Samples~", Samples[i] + ".nxsg");
                    var graph = GraphJson.Parse(File.ReadAllText(path));
                    using (var preview = NXSG.Editor.GraphPreview.Create(graph, null))
                    {
                        NXSG.Editor.PreviewClock.Apply(preview.Material, 1.25f);
                        subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                        camera.Render();
                        RenderTexture.active = target;
                        var capture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true);
                        capture.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                        capture.Apply();
                        File.WriteAllBytes(Path.Combine(outputDirectory, Outputs[i]), capture.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(capture);
                    }
                }
                Debug.Log("NXSG volume showcase rendered: " + outputDirectory);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                RenderTexture.active = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
