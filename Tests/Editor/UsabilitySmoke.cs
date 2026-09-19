using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class UsabilitySmoke
{
    static GraphWindow window;
    static int ticks;
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Flags).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Flags).Invoke(window, args);
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.position = new Rect(0, 0, 1000, 740);
        typeof(GraphWindow).GetField("livePreview", Flags).SetValue(window, false);
        typeof(GraphWindow).GetField("autoScene", Flags).SetValue(window, false);
        Invoke("NewGraph"); Invoke("AddNode", "core.surfaceParticles");
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (++ticks < 50) return;
        EditorApplication.update -= Tick;
        try
        {
            var inspector = (VisualElement)Field("inspector");
            var slider = inspector.Query<Slider>().ToList().Single(s => s.label == "Triangle density");
            var number = slider.parent.Q<FloatField>();
            slider.value = .4f; slider.value = .7f;
            Require(slider.panel != null && ReferenceEquals(slider, inspector.Query<Slider>().ToList().Single(s=>s.label=="Triangle density")), "Slider was replaced during a continuous edit");
            Require(Mathf.Approximately(number.value,.7f), "Number does not follow slider");
            number.value = 1000;
            Require(Mathf.Approximately(slider.value,1), "Soft range not reflected by slider");
            number.value = float.NaN;
            Require(Mathf.Approximately(number.value,1000), "Invalid entry did not restore current value");
            Undo.IncrementCurrentGroup();
            slider.value = .35f;
            ((VisualElement)Field("canvas")).Focus();
            using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, modifiers = EventModifiers.Control }))
                window.rootVisualElement.SendEvent(key);
            var restored = (ShaderGraph)Field("graph");
            Require(Mathf.Approximately((float)restored.Nodes.Single(n=>n.Operation=="core.surfaceParticles").Properties["density"],1000), "Graph Ctrl+Z handler did not restore the previous value");
            Invoke("FocusNodeSearch");
            Require(((VisualElement)Field("libraryPanel")).resolvedStyle.display == DisplayStyle.Flex, "Node search did not open Nodes tab");
            Invoke("ShowSidebarTab", 2);
            typeof(GraphWindow).GetField("diagnosticsDue", Flags).SetValue(window, 0d);
            Invoke("UpdateDiagnostics");
            Require(((VisualElement)Field("problemsPanel")).childCount > 0, "Diagnostics tab empty");
            Require(window.rootVisualElement.Q<TwoPaneSplitView>() != null, "Resizable sidebar missing");
            Debug.Log("NXSG USABILITY CHECK PASSED: retained slider, soft input synchronization, invalid value, Ctrl+Z routing, Nodes tab, diagnostics and splitter");
            window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); window.DiscardChanges(); window.Close(); EditorApplication.Exit(1); }
    }
}
