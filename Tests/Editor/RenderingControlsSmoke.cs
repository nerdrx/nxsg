using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// Run in an isolated Unity editor project with -executeMethod RenderingControlsSmoke.Run.
public static class RenderingControlsSmoke
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int phase, ticks;
    static GraphWindow Window => window;
    static ShaderGraph Graph => (ShaderGraph)typeof(GraphWindow).GetField("graph", Private).GetValue(Window);
    static VisualElement Inspector => (VisualElement)typeof(GraphWindow).GetField("inspector", Private).GetValue(Window);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(Window, args);

    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show();
        window.position = new Rect(0, 0, 1160, 820);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        BuildGraph();
        EditorApplication.update += Tick;
    }

    static void BuildGraph()
    {
        Invoke("NewGraph");
        Graph.Nodes.Clear(); Graph.Connections.Clear(); Graph.Resources.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };

        AddNode("core.value", "source", new JObject { ["value"] = .5 });
        AddNode("core.position", "position");
        AddNode("core.outline", "outline");
        AddNode("core.ssao", "ssao");
        AddNode("core.contactShadow", "contactShadow");
        AddNode("core.lightVolumes", "lightVolumes");
        AddTextureNode("core.cubemap", "cubemap", "cube-resource", "cubemap");
        AddTextureNode("core.textureArray", "textureArray", "array-resource", "texture2DArray");
        AddNode("core.audioSpectrum", "audioSpectrum");
        AddNode("core.audioSpectrumBin", "audioSpectrumBin");
        AddNode("core.audioChronotensity", "audioChronotensity");
        AddNode("core.audioThemeColor", "audioThemeColor");
        AddNode("core.audioVisualizer", "audioVisualizer");
        AddNode("core.toonSurface", "toon");
        AddNode("core.output", "output");

        Wire("source", "value", "outline", "width", "source-outline-width");
        Wire("source", "value", "ssao", "radius", "source-ssao-radius");
        Wire("source", "value", "contactShadow", "distance", "source-contact-distance");
        Wire("source", "value", "lightVolumes", "strength", "source-light-strength");
        Wire("position", "position", "cubemap", "direction", "position-cubemap-direction");
        Wire("source", "value", "textureArray", "slice", "source-array-slice");
        Wire("source", "value", "audioSpectrum", "frequency", "source-spectrum-frequency");
        Wire("source", "value", "audioChronotensity", "speed", "source-chrono-speed");
        Wire("source", "value", "toon", "shadeMap", "source-toon-shade-map");
        Wire("toon", "surface", "outline", "base", "toon-outline-base");
        Wire("outline", "surface", "output", "surface", "outline-output");

        Graph.Layout.Nodes["source"] = new GraphNodeLayout { X = 40, Y = 40 };
        var i = 0;
        foreach (var node in Graph.Nodes.Where(n => n.Id != "source"))
            Graph.Layout.Nodes[node.Id] = new GraphNodeLayout { X = 80 + (i++ % 5) * 220, Y = 100 + (i / 5) * 160 };
        var session = (GraphSession)typeof(GraphWindow).GetField("session", Private).GetValue(Window);
        session.json = GraphJson.Serialize(Graph, true);
        Undo.ClearUndo(session);
        Invoke("Rebuild");
    }

    static void AddNode(string operation, string id, JObject properties = null)
    {
        var node = NodeCatalog.Create(operation);
        Require(node != null, "Node definition missing: " + operation);
        node.Id = id;
        if (properties != null) foreach (var property in properties.Properties()) node.Properties[property.Name] = property.Value.DeepClone();
        Graph.Nodes.Add(node);
    }

    static void AddTextureNode(string operation, string id, string resourceId, string kind)
    {
        AddNode(operation, id, new JObject { ["resourceId"] = resourceId });
        Graph.Resources.Add(new GraphResource { Id = resourceId, Name = id == "cubemap" ? "Sky" : "Array", Kind = kind, Uri = "builtin://unassigned" });
    }

    static void Wire(string from, string output, string to, string input, string id)
    {
        Graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }

    static void Select(string id) => Invoke("SelectNode", id, false);
    static T Field<T>(string name) where T : VisualElement => Inspector.Query<T>().ToList().SingleOrDefault(field => field.name == name);
    static GraphNode Node(string id) => Graph.Nodes.Single(node => node.Id == id);

    static void CheckRenderingInspectors()
    {
        Select("outline");
        Require(Field<FloatField>("node-property-width") != null && Field<FloatField>("node-property-mask") != null, "Outline width and mask fields missing.");
        Require(!Field<FloatField>("node-property-width").enabledSelf, "Wired outline width should be disabled.");
        Require(Inspector.Query<ColorField>().ToList().Any(field => field.label == "Outline color"), "Outline color field missing.");

        Select("ssao");
        Require(Inspector.Query<PopupField<int>>().ToList().Any(field => field.label == "Depth samples"), "SSAO sample choices missing.");
        Require(Field<FloatField>("node-property-radius") != null && !Field<FloatField>("node-property-radius").enabledSelf, "Wired SSAO radius control missing or enabled.");
        Require(new[] { "Strength", "Thickness (m)", "Bias (m)" }.All(label => Inspector.Query<Label>().ToList().Any(field => field.text == label)), "SSAO controls have missing or generic labels.");

        Select("contactShadow");
        Require(Field<FloatField>("node-property-distance") != null && !Field<FloatField>("node-property-distance").enabledSelf, "Wired contact-shadow distance control missing or enabled.");
        Require(new[] { "Strength", "Thickness (m)", "Bias (m)" }.All(label => Inspector.Query<Label>().ToList().Any(field => field.text == label)), "Contact-shadow controls have missing or generic labels.");

        Select("lightVolumes");
        Require(Inspector.Query<ColorField>().ToList().Any(field => field.label == "Albedo"), "Light Volumes albedo control missing.");
        Require(Field<FloatField>("node-property-roughness") != null && Field<FloatField>("node-property-metallic") != null, "Light Volumes material controls missing.");
        Require(!Field<FloatField>("node-property-strength").enabledSelf, "Wired Light Volumes strength should be disabled.");

        Select("cubemap");
        var cubemap = Inspector.Query<ObjectField>().ToList().SingleOrDefault(field => field.label == "Cubemap");
        Require(cubemap != null && cubemap.objectType == typeof(Cubemap) && cubemap.enabledSelf, "Cubemap picker missing, wrong type, or disabled.");
        Require(Field<FloatField>("node-property-lod") != null, "Cubemap mip field missing.");

        Select("textureArray");
        var array = Inspector.Query<ObjectField>().ToList().SingleOrDefault(field => field.label == "Texture array");
        Require(array != null && array.objectType == typeof(Texture2DArray) && array.enabledSelf, "Texture-array picker missing, wrong type, or disabled.");
        Require(Field<FloatField>("node-property-slice") != null && !Field<FloatField>("node-property-slice").enabledSelf, "Wired texture-array slice should be disabled.");

        Select("audioSpectrum");
        Require(Field<FloatField>("node-property-frequency") != null && !Field<FloatField>("node-property-frequency").enabledSelf, "Wired spectrum frequency should be disabled.");
        Require(Inspector.Query<PopupField<string>>().ToList().Any(field => field.label == "Spectrum"), "Spectrum channel choices missing.");
        Require(Inspector.Query<Toggle>().ToList().Any(field => field.label == "Preview audio data"), "Audio data preview toggle missing.");

        Select("audioSpectrumBin");
        Require(Field<FloatField>("node-property-bin") != null && Inspector.Query<PopupField<string>>().ToList().Any(), "Spectrum-bin controls missing.");
        Select("audioChronotensity");
        var modes = Inspector.Query<PopupField<string>>().ToList().Single(field => field.label == "Motion mode");
        Require(modes.choices.Count == 8 && modes.choices.All(label => !string.IsNullOrWhiteSpace(label) && label != "Option 1"), "Chronotensity mode labels are blank or generic.");
        Require(Inspector.Query<PopupField<string>>().ToList().Any(field => field.label == "Band" && field.choices.SequenceEqual(new[] { "Bass", "Low mids", "High mids", "Treble" })), "Chronotensity band names missing.");
        Require(Field<FloatField>("node-property-speed") != null && !Field<FloatField>("node-property-speed").enabledSelf, "Wired chronotensity speed should be disabled.");

        Select("audioThemeColor");
        Require(Inspector.Query<IntegerField>().ToList().Any(field => field.label == "Theme color"), "Theme-color selection missing.");
        Require(Inspector.Query<ColorField>().ToList().Any(field => field.label == "Fallback"), "Theme-color fallback missing.");

        Select("audioVisualizer");
        Require(Inspector.Query<IntegerField>().ToList().Any(field => field.label == "Bars") && Inspector.Query<PopupField<string>>().ToList().Any(field => field.label == "Layout"), "Audio spectrum bars controls missing.");
        Require(Inspector.Query<FloatField>().ToList().Any(field => field.label == "Lowest frequency (Hz)"), "Audio spectrum frequency range label missing.");
    }

    static void CheckOutputAndToonModes()
    {
        Select("output");
        var render = Inspector.Query<PopupField<string>>().ToList().Single(field => field.label == "Rendering");
        Require(render.choices.SequenceEqual(new[] { "Automatic", "Opaque", "Cutout", "Alpha blend", "Additive" }), "Output rendering labels are incomplete.");
        Require(Inspector.Query<PopupField<string>>().ToList().Any(field => field.label == "Visible faces") &&
            Inspector.Query<PopupField<string>>().ToList().Any(field => field.label == "Depth test") &&
            Inspector.Query<IntegerField>().ToList().Any(field => field.label == "Queue offset"), "Output render-state controls missing.");
        render.value = "Cutout";
        Require((int?)Node("output").Properties["renderMode"] == 2, "Rendering control wrote the wrong property.");
        Undo.PerformUndo();
        phase = 1;
    }

    static void CheckOutputUndoThenToonModes()
    {
        Require(((int?)Node("output").Properties["renderMode"] ?? 0) == 0, "Undo did not restore output render mode.");
        Select("output");
        var stencil = Inspector.Query<PopupField<string>>().ToList().SingleOrDefault(field => field.label == "Stencil");
        Require(stencil != null, "Output stencil controls missing.");

        Select("toon");
        var shading = Inspector.Query<PopupField<string>>().ToList().Single(field => field.label == "Shading");
        Require(shading.choices.SequenceEqual(new[] { "Threshold", "Multiple bands", "Texture ramp", "Layered shadows" }), "Toon shading modes have missing labels.");
        shading.value = "Multiple bands";
        Require((int?)Node("toon").Properties["lightingMode"] == 1 && Inspector.Query<IntegerField>().ToList().Any(field => field.label == "Light bands"), "Multiple-band mode did not set toon properties or expose band count.");
        Select("toon");
        shading = Inspector.Query<PopupField<string>>().ToList().Single(field => field.label == "Shading");
        // Separate simulated user actions need distinct Unity undo groups.
        Undo.IncrementCurrentGroup();
        shading.value = "Texture ramp";
        var resourceId = (string)Node("toon").Properties["resourceId"];
        Require((int?)Node("toon").Properties["lightingMode"] == 2 && !string.IsNullOrEmpty(resourceId), "Texture-ramp mode did not create its resource.");
        var ramp = Graph.Resources.SingleOrDefault(resource => resource.Id == resourceId);
        Require(ramp != null && ramp.Kind == "texture2D" && ramp.Name == "Lighting ramp", "Toon ramp resource metadata is incorrect.");
        Require(Inspector.Query<ObjectField>().ToList().Any(field => field.label == "Lighting ramp") && Field<FloatField>("node-property-rampRow") != null, "Texture-ramp controls missing.");
        Undo.PerformUndo();
        phase = 2;
    }

    static void CheckToonUndo()
    {
        Require(((int?)Node("toon").Properties["lightingMode"] ?? 0) == 1, "Undo did not restore multiple-band mode.");
        Require(Graph.Resources.All(resource => resource.Name != "Lighting ramp"), "Undo did not remove the ramp resource created by the control.");
    }

    static void CheckDetailedControls()
    {
        Select("toon");
        Inspector.Query<PopupField<string>>().ToList().Single(f=>f.label=="Shading").value="Layered shadows";
        Require(Field<FloatField>("node-property-threshold3")!=null && Field<FloatField>("node-property-normalStrength2")!=null,"Independent shadow layer fields missing.");
        Require(Inspector.Query<Foldout>().ToList().Count(f=>f.text.StartsWith("Shadow layer "))==3,"Layer controls are not grouped.");
        Inspector.Query<IntegerField>().ToList().Single(f=>f.label=="Shadow layers").value=1;
        Require(Field<FloatField>("node-property-threshold2")==null,"Inactive layer controls remain visible.");
        Wire("source","value","toon","threshold3","detail-border"); Invoke("Rebuild"); Select("toon");
        var ports=(IEnumerable<string>)typeof(GraphWindow).GetMethod("VisibleInputPorts",Private).Invoke(Window,new object[]{Node("toon")});
        Require(ports.Contains("threshold3") && !ports.Contains("shadeColor3"),"Mode changes must retain connected sockets and hide unused ones.");
        Select("outline");
        Inspector.Query<PopupField<string>>().ToList().Single(f=>f.label=="Width units").value="Screen pixels";
        Require(Field<FloatField>("node-property-pixelWidth")!=null && Field<FloatField>("node-property-width")==null,"Pixel-width control did not replace world width.");
        Require(Field<FloatField>("node-property-depthBias")!=null && Field<FloatField>("node-property-lighting")!=null,"Outline detail controls missing.");
        Wire("source","value","outline","lighting","detail-lighting"); Invoke("Rebuild"); Select("outline");
        Require(!Field<FloatField>("node-property-lighting").enabledInHierarchy,"Connected lighting input remains editable.");
    }

    static void Tick()
    {
        if (++ticks % 20 != 0) return;
        try
        {
            if (phase == 0) { CheckRenderingInspectors(); CheckOutputAndToonModes(); }
            else if (phase == 1) CheckOutputUndoThenToonModes();
            else
            {
                CheckToonUndo();
                CheckDetailedControls();
                Debug.Log("NXSG RENDERING CONTROLS SMOKE PASSED: feature controls, connected-input disablement, output/toon modes, ramp resource, and Undo");
                EditorApplication.update -= Tick; Window.DiscardChanges(); Window.Close(); EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Tick;
            if (Window != null) { Window.DiscardChanges(); Window.Close(); }
            Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
