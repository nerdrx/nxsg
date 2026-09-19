using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NXSG.Editor
{
    /// <summary>Small, non-mutating material lab. Preview materials are always clones.</summary>
    public sealed class MaterialPlayground : EditorWindow
    {
        private enum Shape { Sphere, Cube }
        private enum Lighting { Studio, Dark, ColoredLights }

        private sealed class CompareSlot
        {
            public string Name;
            public Texture2D Image;
            public double CpuMilliseconds;
            public string GpuMilliseconds;
        }

        private readonly List<CompareSlot> _slots = new List<CompareSlot>();
        private PreviewRenderUtility _preview;
        private Material _previewMaterial;
        private Material _sourceMaterial;
        private GraphPreview _ownedGraph;
        private Mesh _mesh;
        private Texture _lastImage;
        private Vector2 _scroll;
        private Shape _shape;
        private Lighting _lighting = Lighting.Studio;
        private bool _playing = true;
        private bool _loop = true;
        private float _time;
        private float _duration = 10f;
        private float _speed = 1f;
        private bool _audioEnabled;
        private float _audioValue;
        private double _cpuMs;
        private string _gpuMs = "Unavailable";
        private string _status = "Drop a material here or call Show(material).";

        [MenuItem("Tools/NXSG/Material Playground")]
        public static void ShowWindow() { GetWindow<MaterialPlayground>("Material Playground"); }

        public static void Show(Material material)
        {
            var window = GetWindow<MaterialPlayground>("Material Playground");
            window.SetMaterial(material);
            window.Focus();
        }

        public static void ShowGraph(NXSG.Core.ShaderGraph graph, Material context)
        {
            var owned=GraphPreview.Create(graph,context);
            var window=GetWindow<MaterialPlayground>("Material Playground");
            window.SetMaterial(owned.Material);
            window._ownedGraph=owned;
            window._status="Graph snapshot · Reopen from Create to load later graph edits.";
            window.Focus();
        }

        private void OnEnable()
        {
            minSize=new Vector2(660,640);
            wantsMouseMove = true;
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EnsurePreview();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            DisposePreview();
            foreach (var slot in _slots) { Destroy(slot.Image); }
            _slots.Clear();
        }

        private void Tick()
        {
            if(EditorApplication.timeSinceStartup-_lastTick<1.0/30.0)return;
            if (_playing) _time = PreviewClock.Advance(_time, (float)(EditorApplication.timeSinceStartup - _lastTick), _speed, _loop, _duration);
            _lastTick = EditorApplication.timeSinceStartup;
            if (_previewMaterial != null) ApplyPreviewUniforms(_previewMaterial);
            if(_playing)Repaint();
        }

        private double _lastTick;

        private void SetMaterial(Material material)
        {
            if(_ownedGraph!=null && material!=_ownedGraph.Material){_ownedGraph.Dispose();_ownedGraph=null;}
            _sourceMaterial = material;
            Destroy(_previewMaterial);
            _previewMaterial = material == null ? null : new Material(material) { name = "NXSG Preview (temporary)" };
            _status = material == null ? "No material selected." : "Previewing clone of " + material.name;
            EnsurePreview();
            Repaint();
        }

        private void EnsurePreview()
        {
            if (_preview == null)
            {
                _preview = new PreviewRenderUtility();
                _preview.cameraFieldOfView = 30f;
                _preview.camera.nearClipPlane = .01f;
                _preview.camera.farClipPlane = 100f;
            }
            if (_mesh == null) _mesh = _shape == Shape.Cube ? Resources.GetBuiltinResource<Mesh>("Cube.fbx") : Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        private void DisposePreview()
        {
            if (_preview != null) { _preview.Cleanup(); _preview = null; }
            Destroy(_previewMaterial);
            _previewMaterial = null;
            _mesh = null;
            if(_ownedGraph!=null){_ownedGraph.Dispose();_ownedGraph=null;}
        }

        private static new void Destroy(UnityEngine.Object value)
        {
            if (value != null) UnityEngine.Object.DestroyImmediate(value);
        }

        private void OnGUI()
        {
            DrawToolbar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var previewRect = GUILayoutUtility.GetRect(10, 300, GUILayout.ExpandWidth(true));
            if (_previewMaterial == null) EditorGUI.HelpBox(previewRect, _status, MessageType.Info);
            else DrawPreview(previewRect);
            if (_previewMaterial != null) EditorGUILayout.HelpBox(_status, MessageType.Info);
            DrawControls();
            DrawCompare();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var picked = (Material)EditorGUILayout.ObjectField(_sourceMaterial, typeof(Material), false, GUILayout.Width(220));
                if (picked != _sourceMaterial) SetMaterial(picked);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(62))) SetMaterial(_sourceMaterial);
                if (GUILayout.Button("Snapshot A", EditorStyles.toolbarButton, GUILayout.Width(90))) Capture("A");
                if (GUILayout.Button("Snapshot B", EditorStyles.toolbarButton, GUILayout.Width(90))) Capture("B");
                if (GUILayout.Button("Unity Profiler", EditorStyles.toolbarButton, GUILayout.Width(95))) EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
            }
        }

        private void DrawPreview(Rect rect)
        {
            if(Event.current.type!=EventType.Repaint)return;
            EnsurePreview();
            ConfigureLighting();
            var watch = Stopwatch.StartNew();
            FrameTimingManager.CaptureFrameTimings();
            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.DrawMesh(_mesh, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 25, 0), Vector3.one), _previewMaterial, 0);
            _preview.camera.transform.position = new Vector3(0, 0, -4f);
            _preview.camera.transform.LookAt(Vector3.zero);
            _preview.Render(true);
            var image = _preview.EndPreview();
            watch.Stop();
            _lastImage = image;
            _cpuMs = watch.Elapsed.TotalMilliseconds;
            _gpuMs = TryGetGpuMilliseconds(out var gpu) ? gpu.ToString("0.00") + " ms" : "unavailable on this setup";
            GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, false);
        }

        private void ConfigureLighting()
        {
            var camera = _preview.camera;
            camera.clearFlags=CameraClearFlags.SolidColor;
            _preview.ambientColor=_lighting==Lighting.Dark?Color.black:new Color(.08f,.08f,.08f);
            var light = _preview.lights[0];
            var fill = _preview.lights[1];
            light.type = LightType.Directional;
            fill.type = LightType.Directional;
            light.color = Color.white;
            fill.color = Color.white;
            light.transform.rotation = Quaternion.Euler(35, -35, 0);
            fill.transform.rotation = Quaternion.Euler(60, 145, 0);
            if (_lighting == Lighting.Dark) { light.intensity = .45f; fill.intensity = .1f; camera.backgroundColor = new Color(.015f, .015f, .02f); }
            else if (_lighting == Lighting.ColoredLights) { light.intensity = 1.4f; fill.intensity = 1.2f; light.color = new Color(1f, .35f, .2f); fill.color = new Color(.2f, .45f, 1f); camera.backgroundColor = new Color(.035f, .035f, .05f); }
            else { light.intensity = 1.2f; fill.intensity = .55f; light.color = Color.white; fill.color = Color.white; camera.backgroundColor = new Color(.16f, .16f, .18f); }
        }

        private void DrawControls()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var shape = (Shape)EditorGUILayout.EnumPopup("Shape", _shape);
                var lighting = (Lighting)EditorGUILayout.EnumPopup("Lighting", _lighting);
                if (shape != _shape) { _shape = shape; _mesh = null; }
                _lighting = lighting;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _playing = GUILayout.Toggle(_playing, _playing ? "Pause" : "Play", "Button", GUILayout.Width(70));
                _loop = GUILayout.Toggle(_loop, "Loop", GUILayout.Width(55));
                _speed = EditorGUILayout.Slider("Speed", _speed, 0, 4);
            }
            _duration = EditorGUILayout.FloatField("Duration (s)", Mathf.Max(.01f, _duration));
            _time = EditorGUILayout.Slider("Time (s)", _time, 0, _duration);
            EditorGUILayout.LabelField(new GUIContent("CPU preview: " + _cpuMs.ToString("0.00") + " ms · GPU editor frame: " + _gpuMs,"CPU timing covers preview rendering on the editor thread. GPU timing is for the whole editor frame, not this material."),EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("AudioLink preview uniforms", EditorStyles.boldLabel);
            _audioEnabled = EditorGUILayout.Toggle("Enabled", _audioEnabled);
            _audioValue = EditorGUILayout.Slider("Preview value", _audioValue, 0, 1);
            if (_previewMaterial != null) ApplyPreviewUniforms(_previewMaterial);
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("LTCGI", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Uses an active scene LTCGI controller when present. Preview does not create or fake one.", MessageType.Info);
            if (GUILayout.Button("Check active scene for LTCGI")) CheckLtcgiScene();
        }

        private void DrawCompare()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Before / after snapshots", EditorStyles.boldLabel);
            using(new EditorGUILayout.HorizontalScope())
            foreach (var slot in _slots)
            {
                using (new EditorGUILayout.VerticalScope("box",GUILayout.Width(Mathf.Max(250,(position.width-40)/2))))
                {
                    EditorGUILayout.LabelField("Snapshot "+slot.Name,EditorStyles.boldLabel);
                    var rect=GUILayoutUtility.GetRect(240,190,GUILayout.ExpandWidth(true));
                    if(slot.Image!=null)GUI.DrawTexture(rect,slot.Image,ScaleMode.ScaleToFit,false);
                    EditorGUILayout.LabelField("CPU preview: "+slot.CpuMilliseconds.ToString("0.00")+" ms",EditorStyles.miniLabel);
                }
            }
        }

        private void ApplyPreviewUniforms(Material material)
        {
            PreviewClock.Apply(material, _time);
            SetFloatIfPresent(material, "_NXSG_AudioLinkPreview", _audioEnabled ? 1f : 0f);
            SetFloatIfPresent(material, "_NXSG_AudioLinkValue", _audioValue);
        }

        private void Capture(string name)
        {
            if (_previewMaterial == null || _lastImage == null) { _status = "Assign material before snapshot."; return; }
            var image = CaptureImage(_lastImage, name);
            foreach (var old in _slots) if (old.Name == name) { Destroy(old.Image); }
            _slots.RemoveAll(slot => slot.Name == name);
            _slots.Add(new CompareSlot { Name = name, Image = image, CpuMilliseconds = _cpuMs, GpuMilliseconds = _gpuMs });
        }

        private static Texture2D CaptureImage(Texture source, string name)
        {
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { name = "NXSG Snapshot " + name };
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply(false, false);
                return image;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void CheckLtcgiScene()
        {
            var controllerType = Type.GetType("pi.LTCGI.LTCGI_Controller, LTCGI");
            if (controllerType == null)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var candidate = assembly.GetType("pi.LTCGI.LTCGI_Controller");
                    if (candidate != null) { controllerType = candidate; break; }
                }
            
            if (controllerType == null)
            {
                _status = "LTCGI package/controller type unavailable. Add/configure LTCGI in active scene for a real test.";
                return;
            }
            foreach (var objectInScene in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!objectInScene.scene.IsValid() || !objectInScene.scene.isLoaded) continue;
                if (objectInScene.GetComponent(controllerType) != null)
                {
                    _status = "LTCGI candidate found in active scene: " + objectInScene.name + ". Preview uses scene setup.";
                    return;
                }
            }
            _status = "No LTCGI controller found. Add/configure LTCGI in active scene for a real test.";
        }

        private static bool TryGetGpuMilliseconds(out float milliseconds)
        {
            milliseconds = 0;
            if (!FrameTimingManager.IsFeatureEnabled()) return false;
            var timings = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timings) == 0 || timings[0].gpuFrameTime <= 0) return false;
            milliseconds = (float)timings[0].gpuFrameTime;
            return true;
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }

    public static class PreviewClock
    {
        public const string TimeUniform = "_NXSG_PreviewTime";
        public const string ClockUniform = "_NXSG_PreviewClock";

        public static float Advance(float time, float delta, float speed, bool loop, float duration = 10f)
        {
            var next = time + Mathf.Max(0, delta) * speed;
            duration = Mathf.Max(.0001f, duration);
            return loop ? Mathf.Repeat(next, duration) : Mathf.Clamp(next, 0, duration);
        }

        public static void Apply(Material material, float time)
        {
            if (material == null) return;
            if (material.HasProperty(TimeUniform)) material.SetFloat(TimeUniform, time);
            if (material.HasProperty(ClockUniform)) material.SetFloat(ClockUniform, 1f);
        }
    }
}
