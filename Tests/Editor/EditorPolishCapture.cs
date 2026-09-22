using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

// Run in a graphics-enabled Unity editor. Captures the actual editor window, not a render fixture.
public static class EditorPolishCapture
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int phase;
    static int ticks;

    static object Get(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void Run()
    {
        try
        {
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show();
            window.Focus();
            window.position = new Rect(0, 0, 1280, 800);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GraphWindow).Assembly);
            var sample = Path.Combine(package.resolvedPath, "Samples~", "Particle Lifetime.nxsg");
            if (File.Exists(sample)) Invoke("LoadPath", sample);
            else Invoke("NewGraph");

            phase = 0;
            ticks = 0;
            EditorApplication.update += Tick;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void Tick()
    {
        if (++ticks < 90) return;
        try
        {
            if (phase == 0) { var g=(NXSG.Core.ShaderGraph)Get("graph"); Invoke("SelectNode", g.Nodes.First(n=>n.Operation=="core.surfaceParticles").Id, false); Invoke("FrameNodes", false); phase = 1; ticks = 0; return; }
            if (phase == 1)
            {
                CaptureEditor();
                phase = 2;
                ticks = 0;
                return;
            }

            if (phase == 2)
            {
                Check(File.Exists("/tmp/nxsg-editor-current.png"), "Gamescope did not write its screenshot.");
                File.Copy("/tmp/nxsg-editor-current.png", "Library/NXSG/editor-current.png", true);
                window.position = new Rect(0, 0, 850, 500);
                phase = 3; ticks = 0; return;
            }
            CheckNarrowLayout();
            if (phase == 3)
            {
                File.Copy("Library/NXSG/editor-current.png", "Library/NXSG/ui-wide.png", true);
                ((VisualElement)Get("previewHost")).Q<Foldout>().value=false;
                phase=7; ticks=0; return;
            }
            if (phase == 7)
            {
                Check(!((VisualElement)Get("previewHost")).Q<Foldout>().value,"Preview did not collapse");
                CaptureEditor(); phase=4; ticks=0; return;
            }
            if (phase == 4)
            {
                File.Copy("/tmp/nxsg-editor-current.png", "Library/NXSG/ui-narrow.png", true);
                var g=(NXSG.Core.ShaderGraph)Get("graph");
                var particle=g.Nodes.First(n=>n.Operation=="core.surfaceParticles");
                typeof(GraphWindow).GetField("pendingNode",Private).SetValue(window,particle.Id);
                typeof(GraphWindow).GetField("pendingPort",Private).SetValue(window,"surface");
                typeof(GraphWindow).GetField("pendingOutput",Private).SetValue(window,true);
                var c=(VisualElement)Get("canvas");Invoke("ShowSpawnMenu",new Vector2(c.worldBound.xMax-5,c.worldBound.yMax-5));
                phase=5;ticks=0;return;
            }
            if(phase==5)
            {
                var c=(VisualElement)Get("canvas");var menu=(VisualElement)Get("spawnMenu");
                Check(menu.worldBound.xMin>=c.worldBound.xMin && menu.worldBound.xMax<=c.worldBound.xMax+1 && menu.worldBound.yMin>=c.worldBound.yMin && menu.worldBound.yMax<=c.worldBound.yMax+1,"Connected-node popup escaped canvas");
                var search=menu.Q<ToolbarSearchField>();
                Check(search.worldBound.xMax<=menu.worldBound.xMax && search.worldBound.xMin>=menu.worldBound.xMin,"Popup search escaped menu");
                CaptureEditor();phase=6;ticks=0;return;
            }
            File.Copy("/tmp/nxsg-editor-current.png", "Library/NXSG/ui-popup.png", true);
            EditorApplication.update -= Tick;
            Debug.Log("NXSG EDITOR POLISH CAPTURE PASSED");
            if (window != null) { window.DiscardChanges(); window.Close(); }
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Tick;
            if (window != null) { window.DiscardChanges(); window.Close(); }
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void CaptureEditor()
    {
        Directory.CreateDirectory("Library/NXSG");
        // ReadScreenPixel returns a black X11 root under headless Gamescope.
        // Ask the fixture's own compositor for its actual composed frame.
        var nested = Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY");
        if (string.IsNullOrEmpty(nested)) throw new InvalidOperationException("Run this capture inside Gamescope.");
        if (File.Exists("/tmp/nxsg-editor-current.png")) File.Delete("/tmp/nxsg-editor-current.png");
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gamescopectl", "screenshot /tmp/nxsg-editor-current.png") { UseShellExecute = false });
        if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new InvalidOperationException("Gamescope capture request failed.");
        Debug.Log("NXSG EDITOR SCREENSHOT REQUESTED FROM " + nested);

    }

    static void CheckNarrowLayout()
    {
        var root = window.rootVisualElement.worldBound;
        Check(root.width >= 840 && root.height >= 490, "Narrow GraphWindow did not reach the requested 850x500 layout.");
        var toolbar = window.rootVisualElement.Q<Toolbar>();
        Check(toolbar != null && toolbar.worldBound.yMin >= root.yMin - 1 && toolbar.worldBound.yMax <= root.yMax + 1, "Toolbar escaped the narrow window bounds.");
        var canvas = (VisualElement)Get("canvas");
        var inspector = (VisualElement)Get("inspector");
        Check(canvas.worldBound.width > 0 && inspector.worldBound.width > 0, "Narrow layout lost canvas or inspector width.");
        var sidebar=window.rootVisualElement.Q("nxsg-sidebar");
        Check(sidebar.worldBound.xMax<=root.xMax+1,"Sidebar escaped window");
        foreach(var field in inspector.Query<FloatField>().ToList())
        {
            Check(field.worldBound.width>50 && field.worldBound.xMax<=sidebar.worldBound.xMax+1,"Numeric field clipped horizontally");
        }
        Check(window.rootVisualElement.Query<VisualElement>().ToList().All(element => !float.IsNaN(element.worldBound.x)), "Narrow layout produced invalid geometry.");
    }
}
