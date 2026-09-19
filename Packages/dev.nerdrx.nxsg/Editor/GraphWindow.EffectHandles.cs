using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        void AddEffectHandlesInspectorHook(GraphNode node)
        {
            if (node == null || node.Operation != "core.sticker") return;
            inspector.Add(new UnityEngine.UIElements.Button(() => OpenEffectHandles(node))
            {
                text = "Place sticker…",
                tooltip = "Pick placement on a mesh, or move, resize and rotate on its UV plane."
            });
        }

        [MenuItem("Tools/NXSG/Place selected sticker")]
        static void OpenSelectedEffectHandles()
        {
            var window = EditorWindow.focusedWindow as GraphWindow;
            if (window != null) window.OpenEffectHandlesForSelectedNode();
        }

        void OpenEffectHandlesForSelectedNode()
        {
            var node = graph == null ? null : graph.Nodes.FirstOrDefault(value => value.Id == selected);
            OpenEffectHandles(node);
        }

        void OpenEffectHandles(GraphNode node)
        {
            if (node == null || node.Operation != "core.sticker")
            {
                SetStatus("Select a Sticker node first.");
                return;
            }
            EffectHandlesWindow.Open(node.Id, () => this != null ? graph : null, (name, action) => { if(this != null) Edit(name, action); });
        }
    }

    /// <summary>Small UV-space editor for Sticker. Position is an offset from UV center; size is UV extent.</summary>
    public sealed class EffectHandlesWindow : EditorWindow
    {
        const float Handle = 9f;
        string nodeId;
        Func<ShaderGraph> graphProvider;
        Action<string, Action> edit;
        GraphPreview preview;
        PreviewRenderUtility meshPreview;
        Mesh previewMesh;
        Mesh uvPreviewMesh;
        bool ownsPreviewMesh;
        GameObject meshSource;
        bool meshMode = true;
        bool rawUv = true;
        Vector2 dragStart, startPosition, startSize;
        float startRotation;
        int dragMode;
        int dragUndoGroup = -1;
        Rect previewRect;
        double previewDue;
        Vector2 orbit = new Vector2(25,0);
        int resizeCorner;
        string previewHash;
        Matrix4x4 MeshMatrix => Matrix4x4.Rotate(Quaternion.Euler(orbit.y,orbit.x,0))*Matrix4x4.Translate(-previewMesh.bounds.center);

        public static EffectHandlesWindow Open(string nodeId, Func<ShaderGraph> graphProvider, Action<string, Action> edit)
        {
            var window = CreateInstance<EffectHandlesWindow>();
            window.nodeId = nodeId; window.graphProvider = graphProvider; window.edit = edit;
            window.titleContent = new GUIContent("NXSG Effect Handles");
            window.minSize = new Vector2(420, 440);
            window.ShowUtility();
            window.RebuildPreview();
            window.EnsureMeshPreview();
            Undo.undoRedoPerformed += window.RefreshAfterUndo;
            return window;
        }

        void RefreshAfterUndo() { RefreshRawUvFlag(); previewDue = EditorApplication.timeSinceStartup + .15; }
        void OnDisable() { Undo.undoRedoPerformed -= RefreshAfterUndo; if (preview != null) preview.Dispose(); if (meshPreview != null) meshPreview.Cleanup(); if (ownsPreviewMesh && previewMesh != null) UnityEngine.Object.DestroyImmediate(previewMesh); if (uvPreviewMesh != null) UnityEngine.Object.DestroyImmediate(uvPreviewMesh); preview = null; meshPreview = null; previewMesh = null; uvPreviewMesh = null; }
        void OnInspectorUpdate() { if(Graph!=null && previewDue==0 && GraphJson.ComputeSemanticHash(Graph)!=previewHash) previewDue=EditorApplication.timeSinceStartup+.15; if (previewDue > 0 && EditorApplication.timeSinceStartup >= previewDue) { previewDue = 0; RebuildPreview(); } Repaint(); }

        void RebuildPreview()
        {
            if (preview != null) preview.Dispose();
            preview = null;
            try { var graph = Graph; if (graph != null) { preview = GraphPreview.Create(graph, null); previewHash = GraphJson.ComputeSemanticHash(graph); } }
            catch (Exception exception) { Debug.LogWarning("NXSG effect preview unavailable: " + exception.Message); }
        }

        void OnGUI()
        {
            if(CurrentNode()==null) { EditorGUILayout.HelpBox("The graph or Sticker node is no longer open.",MessageType.Info); return; }
            RefreshRawUvFlag();
            EditorGUILayout.LabelField("Sticker placement", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                meshMode = GUILayout.Toggle(meshMode, "Mesh", EditorStyles.toolbarButton);
                meshMode = !GUILayout.Toggle(!meshMode, "UV plane", EditorStyles.toolbarButton);
                var picked = (GameObject)EditorGUILayout.ObjectField(meshSource, typeof(GameObject), true, GUILayout.Width(220));
                if (picked != meshSource) { meshSource = picked; RefreshMesh(); }
            }
            EditorGUILayout.HelpBox(meshMode ? (rawUv ? "Click mesh to place from UV0. Right-drag to orbit. Use UV plane to resize and rotate." : "Rendered mesh preview: connected UV mapping is not raw UV0, so mesh picking is disabled.") : "UV preview plane: handles edit mesh UV placement. This is not a perspective mesh surface preview.", MessageType.Info);
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            rect = FitSquare(rect, 12);
            previewRect = rect;
            EditorGUI.DrawRect(rect, new Color(.08f, .08f, .09f));
            if (meshMode) { DrawMeshPreview(rect); if(Event.current.type==EventType.MouseDrag && Event.current.button==1 && rect.Contains(Event.current.mousePosition)) { orbit += Event.current.delta; Event.current.Use(); Repaint(); } } else { DrawPlanePreview(rect); DrawHandles(rect); HandleInput(rect); }
            if (meshMode && rawUv && Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition)) PickMesh(rect, Event.current.mousePosition);
            EditorGUILayout.LabelField("Position", ReadVector("position", Vector2.zero).ToString("F3"));
            EditorGUILayout.LabelField("Size", ReadVector("size", Vector2.one).ToString("F3"));
            EditorGUILayout.LabelField("Rotation", ReadFloat("rotation", 0).ToString("F1") + "°");
        }

        ShaderGraph Graph { get { return graphProvider == null ? null : graphProvider(); } }
        GraphNode CurrentNode() { var graph = Graph; return graph == null ? null : graph.Nodes.FirstOrDefault(value => value.Id == nodeId && value.Operation == "core.sticker"); }
        void EnsureMeshPreview() { if (meshPreview != null) return; meshPreview = new PreviewRenderUtility(); meshPreview.cameraFieldOfView = 30; meshPreview.camera.nearClipPlane = .01f; meshPreview.camera.farClipPlane = 100; RefreshMesh(); }
        void RefreshMesh()
        {
            if (meshPreview == null) return;
            if (ownsPreviewMesh && previewMesh != null) UnityEngine.Object.DestroyImmediate(previewMesh); previewMesh = null; ownsPreviewMesh = false;
            var skinned = meshSource == null ? null : meshSource.GetComponentInChildren<SkinnedMeshRenderer>();
            var filter = meshSource == null ? null : meshSource.GetComponentInChildren<MeshFilter>();
            if (skinned != null) { previewMesh = new Mesh(); ownsPreviewMesh = true; skinned.BakeMesh(previewMesh); }
            else if (filter != null) previewMesh = filter.sharedMesh;
            if (previewMesh == null) previewMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            RefreshRawUvFlag();
        }
        void RefreshRawUvFlag()
        {
            var graph = Graph; var edge = graph?.Connections.FirstOrDefault(value => value.To.NodeId == nodeId && value.To.PortId == "uv");
            var source = graph?.Nodes.FirstOrDefault(value => value.Id == edge?.From.NodeId); rawUv = edge == null || (source != null && source.Operation == "core.uv0" && ((string)source.Properties["coordinateSource"] ?? "uv0") == "uv0");

        }
        void DrawMeshPreview(Rect rect)
        {
            EnsureMeshPreview(); if (previewMesh == null) return;
            if (preview == null || preview.Material == null) { EditorGUI.HelpBox(rect, "Graph preview unavailable.", MessageType.Warning); return; }
            if(Event.current.type != EventType.Repaint) return;
            meshPreview.BeginPreview(rect, GUIStyle.none);
            meshPreview.ambientColor = new Color(.25f,.25f,.25f);
            meshPreview.lights[0].intensity=1; meshPreview.lights[0].transform.rotation=Quaternion.Euler(40,30,0);
            for(var sub=0;sub<previewMesh.subMeshCount;sub++) meshPreview.DrawMesh(previewMesh,MeshMatrix,preview.Material,sub);
            var radius=Mathf.Max(.01f,previewMesh.bounds.extents.magnitude);
            var camera=meshPreview.camera;camera.orthographic=false;
            camera.transform.position=new Vector3(0,0,-radius/Mathf.Sin(15*Mathf.Deg2Rad)*1.15f);
            camera.nearClipPlane=Mathf.Max(.001f,radius*.001f);camera.farClipPlane=radius*12;
            camera.transform.LookAt(Vector3.zero);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.02f,.02f,.025f);
            meshPreview.Render(true);GUI.DrawTexture(rect,meshPreview.EndPreview(),ScaleMode.StretchToFill,false);
        }
        void DrawPlanePreview(Rect rect)
        {
            EnsureMeshPreview(); if(preview==null || Event.current.type!=EventType.Repaint)return;
            if(uvPreviewMesh==null) {
                uvPreviewMesh=new Mesh { vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)}, triangles=new[]{0,2,1,0,3,2}, uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up} };
                uvPreviewMesh.RecalculateNormals();uvPreviewMesh.RecalculateBounds();
            }
            meshPreview.BeginPreview(rect,GUIStyle.none);meshPreview.DrawMesh(uvPreviewMesh,Matrix4x4.identity,preview.Material,0);
            var camera=meshPreview.camera;camera.orthographic=true;camera.orthographicSize=1;camera.nearClipPlane=.01f;camera.farClipPlane=10;
            camera.transform.position=new Vector3(0,0,-4);camera.transform.LookAt(Vector3.zero);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.02f,.025f);
            meshPreview.Render(true);GUI.DrawTexture(rect,meshPreview.EndPreview(),ScaleMode.StretchToFill,false);
        }

        void PickMesh(Rect rect, Vector2 mouse)
        {
            if (!rawUv || previewMesh == null || !previewMesh.isReadable) return;
            var viewport = new Vector3((mouse.x-rect.x)/rect.width,1-(mouse.y-rect.y)/rect.height,0); var ray = meshPreview.camera.ViewportPointToRay(viewport); var hit = EffectHandlesMath.RaycastUv(previewMesh, ray, MeshMatrix);
            if (!hit.HasValue) return;
            Event.current.Use(); var uv = hit.Value; Apply("Place sticker on mesh", uv - Vector2.one * .5f, ReadVector("size", Vector2.one), ReadFloat("rotation", 0));
        }

        static Rect FitSquare(Rect value, float inset)
        {
            value.x += inset; value.y += inset; value.width -= inset * 2; value.height -= inset * 2;
            var side = Mathf.Min(value.width, value.height);
            return new Rect(value.center.x - side * .5f, value.center.y - side * .5f, side, side);
        }

        void DrawHandles(Rect canvas)
        {
            var position = ReadVector("position", Vector2.zero);
            var size = ReadVector("size", Vector2.one);
            var lowerLeft = EffectHandlesMath.PositionToLowerLeft(position, size); var center = EffectHandlesMath.UvToGui(canvas, position + Vector2.one * .5f);
            var corners = EffectHandlesMath.RotatedCorners(canvas, lowerLeft, size, ReadFloat("rotation", 0));
            Handles.color = new Color(.45f, .9f, 1f, .95f);
            Handles.DrawAAPolyLine(2f, new Vector3[] { corners[0], corners[1], corners[2], corners[3], corners[0] });
            Handles.color = Color.white;
            foreach (var corner in corners) Handles.DrawSolidDisc(corner, Vector3.forward, Handle * .5f);
            Handles.color = new Color(1f, .7f, .25f);
            Handles.DrawLine(center, center + (corners[2] - corners[1]).normalized * 24f);
            Handles.DrawSolidDisc(center + (corners[2] - corners[1]).normalized * 28f, Vector3.forward, Handle * .5f);
        }

        void HandleInput(Rect canvas)
        {
            var evt = Event.current;
            var position = ReadVector("position", Vector2.zero);
            var size = ReadVector("size", Vector2.one);
            var rotation = ReadFloat("rotation", 0);
            var center = EffectHandlesMath.UvToGui(canvas, position + Vector2.one * .5f); var corners = EffectHandlesMath.RotatedCorners(canvas, EffectHandlesMath.PositionToLowerLeft(position, size), size, rotation);
            if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition))
            {
                dragMode = 0;
                if (Vector2.Distance(evt.mousePosition, center + (corners[2] - corners[1]).normalized * 28f) < 14) dragMode = 3;
                else if (corners.Any(corner => Vector2.Distance(evt.mousePosition, corner) < 14)) { dragMode = 2; resizeCorner=Array.FindIndex(corners,corner=>Vector2.Distance(evt.mousePosition,corner)<14); }
                else if (EffectHandlesMath.PointInRotatedRect(evt.mousePosition, corners)) dragMode = 1;
                if (dragMode != 0) { dragStart = evt.mousePosition; startPosition = position; startSize = size; startRotation = rotation; Undo.IncrementCurrentGroup(); dragUndoGroup = Undo.GetCurrentGroup(); GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); evt.Use(); }
            }
            if (evt.type == EventType.MouseDrag && dragMode != 0)
            {
                var delta = new Vector2((evt.mousePosition.x - dragStart.x) / canvas.width, -(evt.mousePosition.y - dragStart.y) / canvas.height);
                var nextPosition = startPosition; var nextSize = startSize; var nextRotation = startRotation;
                if (dragMode == 1) nextPosition = startPosition + delta;
                if (dragMode == 2) { var r=-startRotation*Mathf.Deg2Rad; var local=new Vector2(delta.x*Mathf.Cos(r)-delta.y*Mathf.Sin(r),delta.x*Mathf.Sin(r)+delta.y*Mathf.Cos(r)); nextSize=EffectHandlesMath.ClampSize(startSize+2*Vector2.Scale(local,new Vector2(resizeCorner==0||resizeCorner==3?-1:1,resizeCorner<2?1:-1))); }
                if (dragMode == 3) nextRotation = 90f-Mathf.Atan2(evt.mousePosition.y - center.y, evt.mousePosition.x - center.x) * Mathf.Rad2Deg;
                Apply("Move sticker", nextPosition, nextSize, nextRotation);
                evt.Use(); Repaint();
            }
            if (evt.type == EventType.MouseUp && dragMode != 0)
            {
                if (dragUndoGroup >= 0) Undo.CollapseUndoOperations(dragUndoGroup); dragUndoGroup = -1; dragMode = 0; GUIUtility.hotControl = 0; evt.Use();
            }
        }

        void Apply(string name, Vector2 position, Vector2 size, float rotation)
        {
            if(CurrentNode()==null)return;
            var p = position; var id = nodeId;
            edit(name, () => { var graph = Graph; var node = graph == null ? null : graph.Nodes.FirstOrDefault(value => value.Id == id); if (node == null) return; node.Properties["position"] = new JArray(p.x, p.y); node.Properties["size"] = new JArray(size.x, size.y); node.Properties["rotation"] = rotation; });
            previewDue = EditorApplication.timeSinceStartup + .15;
        }

        Vector2 ReadVector(string property, Vector2 fallback)
        {
            var node = CurrentNode(); var values = node?.Properties[property] as JArray;
            return values != null && values.Count == 2 ? new Vector2((float)values[0], (float)values[1]) : fallback;
        }
        float ReadFloat(string property, float fallback) { var node = CurrentNode(); return node?.Properties[property] == null ? fallback : node.Properties[property].Value<float>(); }
    }

    public static class EffectHandlesMath
    {
        public static Vector2 UvToGui(Rect canvas, Vector2 uv) { return new Vector2(canvas.x + uv.x * canvas.width, canvas.yMax - uv.y * canvas.height); }
        public static Vector2[] RotatedCorners(Rect canvas, Vector2 position, Vector2 size, float angle)
        {
            var center = UvToGui(canvas, position + size * .5f); var half = new Vector2(size.x * canvas.width, size.y * canvas.height) * .5f;
            var radians = -angle * Mathf.Deg2Rad; var c = Mathf.Cos(radians); var s = Mathf.Sin(radians);
            Func<Vector2, Vector2> rotate = point => center + new Vector2(point.x * c - point.y * s, point.x * s + point.y * c);
            return new[] { rotate(new Vector2(-half.x, -half.y)), rotate(new Vector2(half.x, -half.y)), rotate(new Vector2(half.x, half.y)), rotate(new Vector2(-half.x, half.y)) };
        }
        public static bool PointInRotatedRect(Vector2 point, Vector2[] corners)
        {
            var sign = 0f;
            for (var i = 0; i < 4; i++) { var cross = Vector2.SignedAngle(corners[(i + 1) % 4] - corners[i], point - corners[i]); if (Mathf.Abs(cross) > 1) { if (sign == 0) sign = Mathf.Sign(cross); else if (Mathf.Sign(cross) != sign) return false; } }
            return true;
        }
        public static Vector2 ClampSize(Vector2 size) { return new Vector2(Mathf.Max(.001f, size.x), Mathf.Max(.001f, size.y)); }
        public static Vector2 ClampPosition(Vector2 position, Vector2 size) { return position; }
        public static Vector2 PositionToLowerLeft(Vector2 position, Vector2 size) { return position + Vector2.one * .5f - size * .5f; }
        public static Vector2? RaycastUv(Mesh mesh, Ray ray, Matrix4x4 matrix)
        {
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0 || mesh.uv == null || mesh.uv.Length != mesh.vertexCount) return null;
            var vertices = mesh.vertices; var uv = mesh.uv; var triangles = mesh.triangles; var localRay = new Ray(matrix.inverse.MultiplyPoint(ray.origin), matrix.inverse.MultiplyVector(ray.direction)); float nearest = float.PositiveInfinity; Vector2 result = default;
            for (var i = 0; i < triangles.Length; i += 3) { var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]]; if (!Intersect(localRay, a, b, c, out var t, out var bary)) continue; if (t >= nearest) continue; nearest = t; result = uv[triangles[i]] * bary.x + uv[triangles[i + 1]] * bary.y + uv[triangles[i + 2]] * bary.z; }
            return float.IsPositiveInfinity(nearest) ? (Vector2?)null : result;
        }
        static bool Intersect(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t, out Vector3 bary)
        {
            t = 0; bary = default; var edge1 = b - a; var edge2 = c - a; var p = Vector3.Cross(ray.direction, edge2); var determinant = Vector3.Dot(edge1, p); if (Mathf.Abs(determinant) < .000001f) return false; var inverse = 1f / determinant; var s = ray.origin - a; var u = Vector3.Dot(s, p) * inverse; if (u < 0 || u > 1) return false; var q = Vector3.Cross(s, edge1); var v = Vector3.Dot(ray.direction, q) * inverse; if (v < 0 || u + v > 1) return false; t = Vector3.Dot(edge2, q) * inverse; bary = new Vector3(1 - u - v, u, v); return t > 0;
        }
    }
}
