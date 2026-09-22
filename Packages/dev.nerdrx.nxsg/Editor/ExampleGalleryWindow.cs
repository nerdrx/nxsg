using System;
using System.IO;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed class ExampleGalleryWindow : EditorWindow
    {
        [MenuItem("Window/NXSG/Example Gallery")]
        public static void Open() => GetWindow<ExampleGalleryWindow>("NXSG Examples").Show();

        public void CreateGUI()
        {
            minSize = new Vector2(420, 400);
            var scroll = new ScrollView(); rootVisualElement.Add(scroll);
            scroll.Add(new Label("Start with a working material") { style = { fontSize = 20, marginTop = 12, marginLeft = 12 } });
            scroll.Add(new Label("Each button creates your own graph in Assets/NXSGExamples. Originals stay in the package. PC Built-In; test expensive effects on your avatar.") { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 12, marginRight = 12, marginBottom = 12 } });
            Add(scroll, "Showcase Warm Fur", "Fur", "Short shell fur with root/tip colors and gentle movement. 24 layers: start small and measure the cost.");
            Add(scroll, "Glitter Fabric", "Glitter", "View-reactive sparkles across a fabric surface. Explore masks, density, and color.");
            Add(scroll, "Showcase Hologram", "Hologram", "Animated scanlines above a dark base. No external textures needed.");
            Add(scroll, "Audio Hologram", "Music", "Bass drives glow and opacity. Enable Preview AudioLink in the graph inspector to try it without music.");
            Add(scroll, "Surface Sparkles", "Particles", "Particles emitted by the mesh wearing the material. Explore edge sharpness and lifetime controls.");
            Add(scroll, "Volume Nebula", "Raymarching", "Animated 4D noise fills a glowing nebula. Apply to a Unity Cube with its default mesh; 128 steps per pixel; lower Steps for everyday use.");
            Add(scroll, "Volume Carved Orb", "Raymarching", "SDF shapes carve a lit solid orb. Closed cube proxy, no external textures.");
            Add(scroll, "Volume Smoke Ring", "Raymarching", "A torus filled with procedural smoke. Open the graph to edit density and colors.");
            Add(scroll, "Particle Lifetime", "Lifetime & random", "Particle Info drives a color ramp over each particle's life. Random varies particle size.");
        }

        static void Add(VisualElement parent, string name, string category, string description)
        {
            var card = new VisualElement { style = { marginLeft = 12, marginRight = 12, marginBottom = 10, paddingLeft = 12, paddingRight = 12, paddingTop = 10, paddingBottom = 10, backgroundColor = new Color(.16f,.16f,.18f) } };
            card.Add(new Label(category.ToUpperInvariant()) { style = { color = new Color(.65f,.75f,1), fontSize = 11 } });
            card.Add(new Label(name) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16 } });
            card.Add(new Label(description) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 8 } });
            card.Add(new Button(() => CreateCopy(name)) { text = "Create editable copy", tooltip = "Creates a uniquely named graph without overwriting existing examples." });
            parent.Add(card);
        }

        public static string CreateCopy(string name)
        {
            try
            {
                // Accept only a filename, including when invoked by another editor tool.
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] {'/', '\\'}) >= 0 || name == "." || name == "..") throw new ArgumentException("Invalid example name.");
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/dev.nerdrx.nxsg");
                var root = package == null ? "Packages/dev.nerdrx.nxsg" : package.resolvedPath;
                var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(root, "Samples~", name + ".nxsg")));
                graph.GraphId = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory("Assets/NXSGExamples");
                var path = AssetDatabase.GenerateUniqueAssetPath("Assets/NXSGExamples/" + name + ".nxsg");
                File.WriteAllText(path, GraphJson.Serialize(graph));
                AssetDatabase.ImportAsset(path); GraphWindow.Open(path);
                return path;
            }
            catch (Exception exception) { Debug.LogException(exception); EditorUtility.DisplayDialog("Cannot open example", exception.Message, "OK"); return null; }
        }
    }
}
