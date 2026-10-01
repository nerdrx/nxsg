using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NXSG.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        void OpenScaleSetup()
        {
            if (SaveGraph()) AvatarScaleSetupWindow.Open(sourcePath);
        }
    }

    public sealed class AvatarScaleSetupWindow : EditorWindow
    {
        public const string Property = "_NXSG_AvatarScaleFactor";
        const string Layer = "NXSG Avatar Scale";
        const string Parameter = "ScaleFactor";
        const float MaximumFactor = 1000f;
        string graphPath;
        Transform avatarRoot;
        string status;

        public static void Open(string path)
        {
            var window = GetWindow<AvatarScaleSetupWindow>("NXSG Scale Factor");
            window.graphPath = path;
            window.avatarRoot = FindAvatar(Selection.activeGameObject);
            if (window.avatarRoot == null)
            {
                var avatars = Resources.FindObjectsOfTypeAll<Transform>()
                    .Where(t => t.gameObject.scene.IsValid() && HasDescriptor(t.gameObject)).ToArray();
                if (avatars.Length == 1) window.avatarRoot = avatars[0];
            }
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Adds a VRCFury Full Controller to the avatar. VRChat's built-in ScaleFactor drives this graph's Scale Factor node. Existing FX layers stay in place.", MessageType.Info);
            avatarRoot = (Transform)EditorGUILayout.ObjectField("Avatar root", avatarRoot, typeof(Transform), true);
            if (GUILayout.Button("Set up VRCFury Scale Factor"))
            {
                try { status = Setup(graphPath, avatarRoot); }
                catch (Exception e) { status = e.Message; Debug.LogException(e); }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }

        static Transform FindAvatar(GameObject selected)
        {
            for (var t = selected == null ? null : selected.transform; t != null; t = t.parent)
                if (HasDescriptor(t.gameObject)) return t;
            return null;
        }

        static bool HasDescriptor(GameObject obj)
        {
            return obj.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
        }

        public static string Setup(string path, Transform root)
        {
            if (root == null || !HasDescriptor(root.gameObject)) throw new InvalidOperationException("Choose the VRChat avatar root.");
            if (!root.gameObject.scene.IsValid() || string.IsNullOrEmpty(root.gameObject.scene.path))
                throw new InvalidOperationException("Save the avatar scene before setup.");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new InvalidOperationException("Save the graph before setup.");
            var api = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("com.vrcfury.api.FuryComponents", false)).FirstOrDefault(t => t != null);
            if (api == null) throw new InvalidOperationException("Install VRCFury in this Unity project before setup.");
            var graph = GraphJson.Parse(File.ReadAllText(path));
            if (!graph.Nodes.Any(n => n.Operation == "core.avatarScaleFactor")) throw new InvalidOperationException("Add a Scale Factor node to this graph first.");
            var material = GraphBuild.Build(graph, path);
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m != null && m.shader == material.shader)).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No skinned mesh under this avatar uses the graph's shader. Assign the built material first.");
            if (!material.HasProperty(Property)) throw new InvalidOperationException("Connect Scale Factor to the output branch, then build the graph.");

            var graphAsset = FileUtil.GetProjectRelativePath(path);
            var guid = AssetDatabase.AssetPathToGUID(graphAsset);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("The graph must be saved inside Assets.");
            var identity = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(root.gameObject).ToString();
            string suffix;
            using (var sha = SHA256.Create()) suffix = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 12);
            var controllerPath = "Assets/NXSGGenerated/" + guid + "/AvatarScale-" + suffix + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var created = controller == null;
            if (created) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            Component attached = null;
            try
            {
                ConfigureController(controller, root, renderers, created);
                if (!HasLinkedController(root, controller)) attached = AttachVrcFury(api, root, controller);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                Selection.activeObject = root.gameObject;
                return "Scale Factor set up for " + renderers.Length + " skinned renderer" + (renderers.Length == 1 ? "" : "s") + ". VRCFury will merge the controller into FX at build time.";
            }
            catch
            {
                if (attached != null) DestroyImmediate(attached);
                if (created) AssetDatabase.DeleteAsset(controllerPath);
                throw;
            }
        }

        static void ConfigureController(AnimatorController controller, Transform root, SkinnedMeshRenderer[] renderers, bool created)
        {
            if (created)
            {
                while (controller.layers.Length > 0) controller.RemoveLayer(0);
                controller.AddParameter(new AnimatorControllerParameter { name = Parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
                var assetPath = AssetDatabase.GetAssetPath(controller);
                var machine = new AnimatorStateMachine { name = Layer };
                var tree = new BlendTree { name = Layer, blendType = BlendTreeType.Simple1D, blendParameter = Parameter, useAutomaticThresholds = false };
                var low = new AnimationClip { name = "Scale 0", frameRate = 60 };
                var high = new AnimationClip { name = "Scale 1000", frameRate = 60 };
                AssetDatabase.AddObjectToAsset(machine, assetPath);
                AssetDatabase.AddObjectToAsset(tree, assetPath);
                AssetDatabase.AddObjectToAsset(low, assetPath);
                AssetDatabase.AddObjectToAsset(high, assetPath);
                tree.AddChild(low, 0);
                tree.AddChild(high, MaximumFactor);
                var state = machine.AddState(Layer);
                state.motion = tree;
                state.writeDefaultValues = true;
                controller.AddLayer(new AnimatorControllerLayer { name = Layer, defaultWeight = 1, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = machine });
            }
            var layers = controller.layers;
            if (layers.Length != 1 || layers[0].name != Layer || layers[0].stateMachine.states.Length != 1 ||
                !(layers[0].stateMachine.states[0].state.motion is BlendTree blend) || blend.blendParameter != Parameter || blend.children.Length != 2 ||
                !(blend.children[0].motion is AnimationClip lowClip) || !(blend.children[1].motion is AnimationClip highClip))
                throw new InvalidOperationException("The generated scale controller was edited. Remove it before setting up again.");
            SetCurves(lowClip, root, renderers, 0);
            SetCurves(highClip, root, renderers, MaximumFactor);
            EditorUtility.SetDirty(controller);
        }

        static void SetCurves(AnimationClip clip, Transform root, SkinnedMeshRenderer[] renderers, float value)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.propertyName == "material." + Property) AnimationUtility.SetEditorCurve(clip, binding, null);
            var curve = AnimationCurve.Constant(0, 1f / 60f, value);
            foreach (var renderer in renderers)
            {
                var binding = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(renderer.transform, root), typeof(SkinnedMeshRenderer), "material." + Property);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            EditorUtility.SetDirty(clip);
        }

        static bool HasLinkedController(Transform root, AnimatorController controller)
        {
            foreach (var component in root.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != "VF.Model.VRCFury") continue;
                var iterator = new SerializedObject(component).GetIterator();
                while (iterator.Next(true))
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == controller) return true;
            }
            return false;
        }

        static Component AttachVrcFury(Type api, Transform root, AnimatorController controller)
        {
            var before = root.GetComponents<Component>();
            var create = api.GetMethod("CreateFullController", new[] { typeof(GameObject) });
            if (create == null) throw new InvalidOperationException("This VRCFury version lacks the Full Controller public API.");
            try
            {
                var wrapper = create.Invoke(null, new object[] { root.gameObject });
                var global = wrapper.GetType().GetMethod("AddGlobalParam", new[] { typeof(string) });
                if (global == null) throw new InvalidOperationException("This VRCFury version cannot preserve the built-in ScaleFactor parameter.");
                global.Invoke(wrapper, new object[] { Parameter });
                var add = wrapper.GetType().GetMethods().FirstOrDefault(m => m.Name == "AddController" && m.GetParameters().Length == 2);
                if (add == null) throw new InvalidOperationException("This VRCFury version cannot add an FX controller.");
                var fx = Enum.Parse(add.GetParameters()[1].ParameterType, "FX");
                add.Invoke(wrapper, new[] { (object)controller, fx });
                var component = root.GetComponents<Component>().Except(before).Single();
                Undo.RegisterCreatedObjectUndo(component, "Set up NXSG Scale Factor");
                EditorUtility.SetDirty(component);
                return component;
            }
            catch { foreach (var component in root.GetComponents<Component>().Except(before)) DestroyImmediate(component); throw; }
        }
    }
}
