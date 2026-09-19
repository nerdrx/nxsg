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
            var sample = Path.Combine(package.resolvedPath, "Samples~", "Shiny Surface.nxsg");
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
            if (phase == 0) { Invoke("FrameNodes", false); phase = 1; ticks = 0; return; }
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
        Check(window.rootVisualElement.Query<VisualElement>().ToList().All(element => !float.IsNaN(element.worldBound.x)), "Narrow layout produced invalid geometry.");
    }
}
