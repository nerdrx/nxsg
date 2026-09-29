using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        /// <summary>Parent-owned hook: caller adds this menu to its existing toolbar.</summary>
        void AddTextureToolsMenu(ToolbarMenu menu)
        {
            menu.menu.AppendAction("Review selected textures…", _ => TextureSetReviewWindow.Open(this, Selection.GetFiltered<Texture2D>(SelectionMode.Assets)));
            menu.menu.AppendAction("Import texture set…", _ => TextureSetReviewWindow.Open(this, Array.Empty<Texture2D>()));
        }

        void ApplyTextureSet(ShaderGraph imported)
        {
            if (imported == null) return;
            if (!CanDiscard()) return;
            if (graph != null) GraphRecovery.Write(graph, string.IsNullOrEmpty(sourcePath) ? "Untitled" : Path.GetFileNameWithoutExtension(sourcePath), true);
            foreach (var resource in imported.Resources)
            {
                var path = resource.Uri != null && resource.Uri.StartsWith("project://", StringComparison.Ordinal) ? resource.Uri.Substring(10) : null;
                var guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;
                if (imported.Adapter == null) imported.Adapter = new Newtonsoft.Json.Linq.JObject();
                var bindings = imported.Adapter["textures"] as Newtonsoft.Json.Linq.JObject;
                if (bindings == null) { bindings = new Newtonsoft.Json.Linq.JObject(); imported.Adapter["textures"] = bindings; }
                bindings[resource.Id] = guid;
            }
            ClearPreview();
            Edit("Import texture set", () =>
            {
                graph = imported; graph.GraphId = Guid.NewGuid().ToString("N");
                sourcePath = null; diskSource = null; contextMaterial = null; selected = null; selection.Clear();
                previewNodeId = previewNodePort = null; autoScene = false; scenePending = false; sceneQueuedHash = null; sceneSource = null;
            });
            Undo.ClearUndo(session);
            canvas?.schedule.Execute(()=>FrameNodes(false));
            SetStatus("Imported reviewed texture set as untitled graph. Existing graph saved to Recovery.");
        }

        sealed class TextureSetReviewWindow : EditorWindow
        {
            GraphWindow owner;
            readonly Dictionary<TextureSetSlot, Texture2D> assigned = new Dictionary<TextureSetSlot, Texture2D>();
            Vector2 scroll;
            Texture2D preview;
            bool pbr = true;
            float strength = 1;
            bool invert;
            TextureSetSlot previewSlot;
            int previewChannel;
            int maskChannel;
            Material channelMaterial;
            Material sourceMaterial;
            readonly List<string> skippedProperties = new List<string>();
            readonly Dictionary<TextureSetSlot, string> sourceProperties = new Dictionary<TextureSetSlot, string>();

            static readonly Dictionary<string, TextureSetSlot> MaterialSlots = new Dictionary<string, TextureSetSlot>(StringComparer.OrdinalIgnoreCase)
            {
                { "_MainTex", TextureSetSlot.Albedo }, { "_BaseMap", TextureSetSlot.Albedo }, { "_BaseColorMap", TextureSetSlot.Albedo },
                { "_BumpMap", TextureSetSlot.Normal }, { "_NormalMap", TextureSetSlot.Normal },
                { "_RoughnessMap", TextureSetSlot.Roughness }, { "_MetallicGlossMap", TextureSetSlot.Metallic }, { "_MetallicMap", TextureSetSlot.Metallic },
                { "_OcclusionMap", TextureSetSlot.AmbientOcclusion }, { "_ParallaxMap", TextureSetSlot.Height }, { "_HeightMap", TextureSetSlot.Height },
                { "_AlphaMask", TextureSetSlot.Mask }, { "_ClippingMask", TextureSetSlot.Mask }, { "_EmissionMap", TextureSetSlot.Emission }
            };

            public static void Open(GraphWindow owner, IEnumerable<Texture2D> textures)
            {
                var window = CreateInstance<TextureSetReviewWindow>(); window.owner = owner; window.titleContent = new GUIContent("NXSG Texture Set Review");
                window.minSize = new Vector2(560, 440); window.Assign(textures); window.ShowUtility();
            }

            public static void Open(GraphWindow owner, Material material)
            {
                if (material == null || material.shader == null)
                {
                    EditorUtility.DisplayDialog("NXSG material import", "Select a material asset in the Project window first.", "OK");
                    return;
                }
                var window = CreateInstance<TextureSetReviewWindow>();
                window.owner = owner; window.sourceMaterial = material;
                window.titleContent = new GUIContent("NXSG Material Review"); window.minSize = new Vector2(560, 440);
                window.AssignMaterial(material); window.ShowUtility();
            }

            void AssignMaterial(Material material)
            {
                var shader = material.shader;
                for (var i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var name = shader.GetPropertyName(i);
                    if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        if ((shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) == 0)
                            skippedProperties.Add(name);
                        continue;
                    }
                    var texture = material.GetTexture(name) as Texture2D;
                    if (texture == null) continue;
                    var path = AssetDatabase.GetAssetPath(texture);
                    if (string.IsNullOrEmpty(path) || !(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)))
                    { skippedProperties.Add(name + " (no project asset)"); continue; }
                    if (MaterialSlots.TryGetValue(name, out var slot) && !assigned.ContainsKey(slot))
                    { assigned[slot] = texture; sourceProperties[slot] = name; }
                    else skippedProperties.Add(name);
                }
            }

            void Assign(IEnumerable<Texture2D> textures)
            {
                var list = (textures ?? Enumerable.Empty<Texture2D>()).Where(t => t != null).ToArray();
                var suggestions = TextureSetMatcher.Suggest(list.Select(AssetDatabase.GetAssetPath));
                foreach (var pair in suggestions) assigned[pair.Key] = list.FirstOrDefault(t => AssetDatabase.GetAssetPath(t) == pair.Value.Path);
            }

            void OnGUI()
            {
                HandleDrop();
                EditorGUILayout.LabelField("Texture Set Review", EditorStyles.boldLabel);
                if (sourceMaterial == null)
                    EditorGUILayout.HelpBox("Filename suggestions are editable. Review assignments before creating graph. Importer settings stay unchanged.", MessageType.Info);
                if (sourceMaterial != null)
                {
                    EditorGUILayout.HelpBox("Starting from " + sourceMaterial.name + " (" + sourceMaterial.shader.name + "). Only reviewed texture slots become graph nodes. Colors, values, render state and animation settings stay on the source material; recreate those separately.", MessageType.Info);
                    if (skippedProperties.Count > 0)
                        EditorGUILayout.HelpBox("Not mapped: " + string.Join(", ", skippedProperties.Take(16)) + (skippedProperties.Count > 16 ? "…" : ""), MessageType.Warning);
                }
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (TextureSetSlot slot in Enum.GetValues(typeof(TextureSetSlot))) DrawSlot(slot);
                EditorGUILayout.Space(8);
                pbr = EditorGUILayout.Toggle("Create PBR surface", pbr);
                if(!pbr)EditorGUILayout.HelpBox("Roughness and Metallic are only connected for PBR. Height uses a small 0.02 displacement scale; adjust it in the new graph.",MessageType.Info);
                EditorGUILayout.LabelField("Helpers", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create mask graph") && assigned.TryGetValue(TextureSetSlot.Mask, out var mask) && mask != null)
                    { owner.ApplyTextureSet(Bind(TextureSetGraphBuilder.BuildMask(AssetDatabase.GetAssetPath(mask), invert, strength, new[] { "r", "g", "b", "a" }[maskChannel]))); Close(); }
                    if (GUILayout.Button("Create flow graph") && assigned.TryGetValue(TextureSetSlot.Flow, out var flow) && flow != null)
                    { owner.ApplyTextureSet(Bind(TextureSetGraphBuilder.BuildFlow(AssetDatabase.GetAssetPath(flow), strength))); Close(); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    invert = EditorGUILayout.Toggle("Invert mask", invert);
                    maskChannel = EditorGUILayout.Popup("Mask channel", maskChannel, new[] { "Red", "Green", "Blue", "Alpha" });
                    strength = EditorGUILayout.Slider("Strength", strength, 0, 4);
                }
                if (GUILayout.Button("Create reviewed graph"))
                {
                    var paths = assigned.Where(p => p.Value != null).ToDictionary(p => p.Key, p => AssetDatabase.GetAssetPath(p.Value));
                    owner.ApplyTextureSet(Bind(TextureSetGraphBuilder.Build(paths, pbr))); Close();
                }
                EditorGUILayout.EndScrollView();
                if (preview != null) DrawPreview();
            }

            void OnDestroy()
            {
                if (channelMaterial != null) DestroyImmediate(channelMaterial);
            }

            void DrawSlot(TextureSetSlot slot)
            {
                assigned.TryGetValue(slot, out var value);
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(slot.ToString(), GUILayout.Width(120));
                    var next = (Texture2D)EditorGUILayout.ObjectField(value, typeof(Texture2D), false);
                    if (next != value) { assigned[slot] = next; sourceProperties.Remove(slot); }
                    if (value != null && GUILayout.Button("Preview", GUILayout.Width(70))) { preview = value; previewSlot = slot; previewChannel = 0; Repaint(); }
                    if (value != null) EditorGUILayout.LabelField("~" + FormatBytes(Profiler.GetRuntimeMemorySizeLong(value)) + " runtime estimate", GUILayout.Width(150));
                }
                if(value!=null && slot!=TextureSetSlot.Albedo && slot!=TextureSetSlot.Normal)
                {
                    var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(value)) as TextureImporter;
                    if(importer!=null && importer.sRGBTexture)EditorGUILayout.HelpBox("This data map has sRGB enabled. Review its Import Settings: masks, flow, roughness and height usually need linear sampling.",MessageType.Warning);
                }
                if (sourceMaterial != null && sourceProperties.TryGetValue(slot, out var property))
                    EditorGUILayout.LabelField("From " + property, EditorStyles.miniLabel);
                if (slot == TextureSetSlot.Normal && value != null)
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(value)) as TextureImporter;
                    if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                        EditorGUILayout.HelpBox("Normal map importer is currently " + importer.textureType + ". Review or change this in Unity's Import Settings; NXSG does not mutate importer state.", MessageType.Warning);
                }
            }

            void DrawPreview()
            {
                EditorGUILayout.LabelField("GPU preview · " + previewSlot + " · RGBA", EditorStyles.boldLabel);
                previewChannel = EditorGUILayout.Popup("Channel", previewChannel, new[] { "RGBA", "Red", "Green", "Blue", "Alpha" });
                var rect = GUILayoutUtility.GetRect(240, 160, GUILayout.ExpandWidth(true));
                if (channelMaterial == null) channelMaterial = new Material(Shader.Find("Hidden/NXSG/TextureChannelPreview")) { hideFlags = HideFlags.HideAndDontSave };
                channelMaterial.SetFloat("_Channel", previewChannel);
                var normalImporter=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(preview)) as TextureImporter;
                channelMaterial.SetFloat("_DecodeNormal", previewSlot == TextureSetSlot.Normal && normalImporter!=null && normalImporter.textureType==TextureImporterType.NormalMap ? 1 : 0);
                var rendered = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(preview, rendered, channelMaterial);
                EditorGUI.DrawPreviewTexture(rect, rendered, null, ScaleMode.ScaleToFit);
                RenderTexture.ReleaseTemporary(rendered);
                EditorGUILayout.LabelField(previewChannel == 0 ? "R  G  B  A" : new[] { "RGBA", "R", "G", "B", "A" }[previewChannel], EditorStyles.centeredGreyMiniLabel);
            }

            void HandleDrop()
            {
                var evt = Event.current;
                if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
                var textures = DragAndDrop.objectReferences.OfType<Texture2D>().ToArray();
                if (textures.Length == 0) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Assign(textures); Repaint(); }
                evt.Use();
            }

            ShaderGraph Bind(ShaderGraph graph)
            {
                if (graph.Adapter == null) graph.Adapter = new Newtonsoft.Json.Linq.JObject();
                var bindings = new Newtonsoft.Json.Linq.JObject(); graph.Adapter["textures"] = bindings;
                foreach (var resource in graph.Resources)
                {
                    var path = resource.Uri.StartsWith("project://", StringComparison.Ordinal) ? resource.Uri.Substring(10) : "";
                    bindings[resource.Id] = AssetDatabase.AssetPathToGUID(path);
                }
                return graph;
            }

            static string FormatBytes(long bytes)
            {
                if (bytes < 1024) return bytes + " B";
                if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
                return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            }
        }
    }
}
