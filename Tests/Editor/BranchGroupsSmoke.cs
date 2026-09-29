using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

// Run in the isolated Unity project with -executeMethod BranchGroupsSmoke.Run.
public static class BranchGroupsSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int phase;
    static double nextCheck;
    static string semanticHash, edges;
    static JObject manualBefore, unknownBefore, renamedSnapshot;
    const string RenamedKey = "surface-a/Albedo";
    const string RenamedId = "organize-surface-a/Albedo";

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static object Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static GraphSession Session => (GraphSession)Field("session");

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        try
        {
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1280, 800); window.CreateGUI();
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            Invoke("NewGraph");
            BuildGraph();
            Session.json = GraphJson.Serialize(Graph, true);
            Undo.ClearUndo(Session);
            Invoke("Rebuild");
            semanticHash = GraphJson.ComputeSemanticHash(Graph);
            edges = EdgeSignature();
            manualBefore = (JObject)FindGroup("manual-frame").DeepClone();
            unknownBefore = (JObject)FindUnknownGroup().DeepClone();
            Require(unknownBefore["organizeKey"]?.Type == JTokenType.String && unknownBefore["organizeRole"] == null && unknownBefore["nxsgOrganizeVersion"] == null,
                "Unknown imported group fixture must contain a legacy organizeKey without a current marker.");
            phase = 0; nextCheck = EditorApplication.timeSinceStartup + .8;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void BuildGraph()
    {
        Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>(), ExtensionData = new Dictionary<string, JToken>() };
        AddNode("shared-color", "core.vertexColor", 0, 0);
        AddNode("surface-a-albedo", "core.vertexColor", 0, 150);
        AddNode("surface-a-emission", "core.vertexColor", 0, 300);
        AddNode("surface-b-albedo", "core.vertexColor", 0, 450);
        AddNode("surface-b-emission", "core.vertexColor", 0, 600);
        AddNode("mix-a-albedo", "core.layer", 260, 150);
        AddNode("mix-a-emission", "core.layer", 260, 300);
        AddNode("mix-b-albedo", "core.layer", 260, 450);
        AddNode("mix-b-emission", "core.layer", 260, 600);
        AddNode("surface-a", "core.toonSurface", 600, 120);
        AddNode("surface-b", "core.toonSurface", 600, 460);
        AddNode("output-a", "core.output", 1000, 120);
        AddNode("output-b", "core.output", 1000, 460);
        AddNode("manual-a", "core.vertexColor", 100, 800);
        AddNode("manual-b", "core.vertexColor", 300, 800);

        AddBranch("a-albedo", "surface-a-albedo", "mix-a-albedo", "surface-a", "albedo");
        AddBranch("a-emission", "surface-a-emission", "mix-a-emission", "surface-a", "emission");
        AddBranch("b-albedo", "surface-b-albedo", "mix-b-albedo", "surface-b", "albedo");
        AddBranch("b-emission", "surface-b-emission", "mix-b-emission", "surface-b", "emission");
        AddEdge("surface-a-output", "surface-a", "surface", "output-a", "surface");
        AddEdge("surface-b-output", "surface-b", "surface", "output-b", "surface");

        var manual = GraphGroups.Add(Graph, new[] { "manual-a", "manual-b" }, "Manual frame");
        manual["id"] = "manual-frame"; manual["frame"] = true; manual["collapsed"] = false;
        manual["note"] = "Keep this note"; manual["customMetadata"] = new JObject { ["source"] = "imported" };
        var groups = (JArray)Graph.Layout.ExtensionData["groups"];
        groups.Add(new JObject
        {
            ["id"] = "future-group-record", ["name"] = "Imported future frame", ["members"] = new JArray(),
            ["frame"] = true, ["collapsed"] = false, ["organizeKey"] = "future-owned-key",
            ["futureSchema"] = new JObject { ["keep"] = true }
        });
    }

    static void AddNode(string id, string operation, float x, float y)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; Graph.Nodes.Add(node);
        Graph.Layout.Nodes[id] = new GraphNodeLayout { X = x, Y = y };
    }

    static void AddEdge(string id, string from, string fromPort, string to, string toPort)
    {
        Graph.Connections.Add(new GraphConnection { Id = id,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }

    static void AddBranch(string key, string uniqueSource, string mix, string surface, string port)
    {
        AddEdge("shared-" + key, "shared-color", "color", mix, "base");
        AddEdge("unique-" + key, uniqueSource, "color", mix, "overlay");
        AddEdge("surface-" + key, mix, "color", surface, port);
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .8;
        try
        {
            if (phase == 0)
            {
                Require(((Dictionary<string, VisualElement>)Field("nodes")).Count == Graph.Nodes.Count,
                    "Synthetic branch graph did not render all nodes.");
                Invoke("AutoOrganize", false);
                phase = 1;
            }
            else if (phase == 1)
            {
                CheckIdentity(); CheckInferredGroups();
                Require(JToken.DeepEquals(manualBefore, FindGroup("manual-frame")), "Auto-organize changed the original manual frame metadata.");
                Require(JToken.DeepEquals(unknownBefore, FindUnknownGroup()), "Auto-organize dropped or changed unknown imported group metadata.");

                var card = FindFrameCard(RenamedKey);
                var title = card.Children().FirstOrDefault()?.Q<TextField>();
                Require(title != null, "Inferred frame title field is missing.");
                title.value = "Creator-named albedo frame";
                phase = 2;
            }
            else if (phase == 2)
            {
                var renamed = FindGroup(RenamedId);
                Require((string)renamed["name"] == "Creator-named albedo frame", "Renaming the frame did not update its saved name.");
                Require(renamed["organizeKey"] == null && renamed["organizeRole"] == null && renamed["nxsgOrganizeVersion"] == null,
                    "Renaming an inferred frame did not convert it to a manual frame.");
                renamedSnapshot = (JObject)renamed.DeepClone();
                Invoke("AutoOrganize", false);
                phase = 3;
            }
            else
            {
                CheckIdentity();
                Require(JToken.DeepEquals(renamedSnapshot, FindGroup(RenamedId)), "A later organize did not preserve the creator-named manual frame.");
                Require(FindGroup(RenamedId)["organizeKey"] == null, "A later organize inferred the renamed manual frame again.");
                Require(JToken.DeepEquals(unknownBefore, FindUnknownGroup()), "A later organize dropped or changed unknown imported group metadata.");
                Debug.Log("NXSG BRANCH GROUPS SMOKE PASSED: four surface-input branches, shared source, manual preservation, rename, and unknown metadata.");
                Finish(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    static void CheckIdentity()
    {
        Require(GraphJson.ComputeSemanticHash(Graph) == semanticHash, "Organize or rename changed graph semantic identity.");
        Require(EdgeSignature() == edges, "Organize or rename changed graph connections.");
    }

    static void CheckInferredGroups()
    {
        var expected = new[] { "surface-a/Albedo", "surface-a/Emission", "surface-b/Albedo", "surface-b/Emission", "shared", "output" };
        var groups = GraphGroups.All(Graph).ToArray();
        foreach (var key in expected)
            Require(groups.Any(g => (string)g["organizeKey"] == key), "Expected inferred group is missing: " + key);
        Require(groups.Where(g => expected.Contains((string)g["organizeKey"])).All(g => (int?)g["nxsgOrganizeVersion"] == 1),
            "Generated groups are missing the current organizer marker.");
        Require(GraphGroups.Members(Graph, groups.Single(g => (string)g["organizeKey"] == "shared")).SequenceEqual(new[] { "shared-color" }),
            "The source used by multiple surface branches was not grouped as shared input.");
        foreach (var pair in new[]
        {
            (key: "surface-a/Albedo", node: "surface-a-albedo"), (key: "surface-a/Emission", node: "surface-a-emission"),
            (key: "surface-b/Albedo", node: "surface-b-albedo"), (key: "surface-b/Emission", node: "surface-b-emission")
        })
        {
            var group = groups.Single(g => (string)g["organizeKey"] == pair.key);
            Require(GraphGroups.Members(Graph, group).Contains(pair.node), "Surface branch " + pair.key + " lost its unique source.");
        }
    }

    static JObject FindGroup(string id) => GraphGroups.All(Graph).Single(g => (string)g["id"] == id);
    static JObject FindUnknownGroup() => ((JArray)Graph.Layout.ExtensionData["groups"]).OfType<JObject>().Single(g => (string)g["id"] == "future-group-record");
    static VisualElement FindFrameCard(string key) => ((VisualElement)Field("layer")).Children().Single(e => e.name == "frame-card" &&
        e.userData is JObject group && (string)group["organizeKey"] == key);
    static string EdgeSignature() => string.Join("|", Graph.Connections.OrderBy(e => e.Id, StringComparer.Ordinal)
        .Select(e => e.Id + ":" + e.From.NodeId + "/" + e.From.PortId + ">" + e.To.NodeId + "/" + e.To.PortId));

    static void Fail(Exception e) { Debug.LogException(e); Finish(1); }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        if (window != null) UnityEngine.Object.DestroyImmediate(window);
        EditorApplication.Exit(code);
    }
}
