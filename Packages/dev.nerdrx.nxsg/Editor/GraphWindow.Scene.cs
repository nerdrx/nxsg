using System;
using System.IO;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        [SerializeField] bool autoScene = true;
        Label sceneStatus;
        bool scenePending;
        double sceneDue;
        string sceneBuiltHash, sceneQueuedHash, sceneSource;

        void AddSceneToggle(Toolbar toolbar)
        {
            var toggle = new ToolbarToggle { text = "Auto scene", value = autoScene,
                tooltip = "Automatically save and build edits after a short pause. Updates every material using this graph's shader." };
            toggle.RegisterValueChangedCallback(evt => { autoScene = evt.newValue; sceneQueuedHash = null; QueueSceneUpdate(); });
            toolbar.Add(toggle);
        }

        void SceneStatus(string message, bool attention = false)
        {
            if (sceneStatus == null) return;
            sceneStatus.text = message;
            sceneStatus.style.color = attention ? new Color(1f, .72f, .32f) : new Color(.65f, .85f, .72f);
        }

        void QueueSceneUpdate()
        {
            if (graph == null) return;
            if (sceneSource != sourcePath)
            {
                sceneSource = sourcePath; sceneBuiltHash = sceneQueuedHash = null; scenePending = false;
            }
            if (string.IsNullOrEmpty(sourcePath))
            {
                SceneStatus("Scene: save this graph inside Assets to enable automatic updates.", true);
                return;
            }
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(sourcePath).StartsWith(assets, StringComparison.Ordinal))
            {
                SceneStatus("Scene: save this graph inside Assets to update the scene.", true);
                return;
            }
            if (sceneBuiltHash == null)
            {
                var relative = "Assets/" + Path.GetFullPath(sourcePath).Substring(assets.Length).Replace('\\', '/');
                var guid = AssetDatabase.AssetPathToGUID(relative);
                var shaderPath = "Assets/NXSGGenerated/" + guid + "/Material.shader";
                if (File.Exists(shaderPath))
                    using (var reader = File.OpenText(shaderPath))
                    {
                        var line = reader.ReadLine();
                        const string prefix = "// NXSG graph hash: ";
                        if (line != null && line.StartsWith(prefix, StringComparison.Ordinal)) sceneBuiltHash = line.Substring(prefix.Length);
                    }
            }
            var hash = GraphJson.ComputeSemanticHash(graph);
            if (hash == sceneBuiltHash)
            {
                scenePending = false; sceneQueuedHash = null;
                SceneStatus("Scene: up to date"); return;
            }
            if (!autoScene)
            {
                scenePending = false;
                SceneStatus("Scene: changes not applied · click Build for VRChat or enable Auto scene", true); return;
            }
            if (hash == sceneQueuedHash) return;
            sceneQueuedHash = hash; scenePending = true;
            sceneDue = EditorApplication.timeSinceStartup + .65;
            SceneStatus("Scene: updating after your edits…", true);
        }

        void UpdateScene()
        {
            if (!autoScene || !scenePending || graph == null || EditorApplication.timeSinceStartup < sceneDue
                || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            scenePending = false;
            SceneStatus("Scene: building…", true);
            try
            {
                if (!SaveGraph()) throw new IOException(status != null ? status.text : "Could not save the graph.");
                GraphBuild.Build(graph, sourcePath);
                SceneBuildSucceeded();
            }
            catch (Exception exception) { SceneBuildFailed(exception); }
        }

        void SceneBuildSucceeded()
        {
            sceneBuiltHash = GraphJson.ComputeSemanticHash(graph);
            sceneQueuedHash = null; scenePending = false;
            SceneStatus("Scene: up to date");
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        void SceneBuildFailed(Exception exception)
        {
            scenePending = false;
            SceneStatus("Scene: needs attention · " + exception.Message + " · keeping the last successful build", true);
        }
    }
}
