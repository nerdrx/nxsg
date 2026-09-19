using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Collections.Generic;

namespace NXSG.Editor
{
    /// <summary>Creates an ordinary VRChat FX controller which maps builtin motion parameters to material floats.</summary>
    public static class MotionDriverBuilder
    {
        public const string SpeedProperty = "_NXSG_MotionSpeed";
        public const string XProperty = "_NXSG_MotionX";
        public const string YProperty = "_NXSG_MotionY";
        public const string ZProperty = "_NXSG_MotionZ";
        public const string MagnitudeParameter = "VelocityMagnitude";
        public const string XParameter = "VelocityX";
        public const string YParameter = "VelocityY";
        public const string ZParameter = "VelocityZ";

        public static AnimatorController Build(Transform avatarRoot, Renderer renderer, string assetPath, float maxSpeed = 8f)
        {
            Validate(avatarRoot, renderer, assetPath, maxSpeed);
            var path = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            try
            {
                // Unity creates a Base Layer by default; this controller is deliberately four motion-only layers.
                while (controller.layers.Length > 0) controller.RemoveLayer(0);
                AddParameter(controller, MagnitudeParameter);
                AddParameter(controller, XParameter);
                AddParameter(controller, YParameter);
                AddParameter(controller, ZParameter);
                AddLayer(controller, "NXSG Motion Speed", MagnitudeParameter, SpeedProperty, renderer, avatarRoot, 0f, maxSpeed);
                AddLayer(controller, "NXSG Motion X", XParameter, XProperty, renderer, avatarRoot, -maxSpeed, maxSpeed);
                AddLayer(controller, "NXSG Motion Y", YParameter, YProperty, renderer, avatarRoot, -maxSpeed, maxSpeed);
                AddLayer(controller, "NXSG Motion Z", ZParameter, ZProperty, renderer, avatarRoot, -maxSpeed, maxSpeed);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            catch
            {
                AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        static void Validate(Transform root, Renderer renderer, string path, float maxSpeed)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (renderer.transform != root && !renderer.transform.IsChildOf(root)) throw new InvalidOperationException("Renderer must be on the avatar root or one of its descendants.");
            if (!IsFinite(maxSpeed) || maxSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpeed), "Maximum speed must be finite and greater than zero.");
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || !path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a .controller path inside Assets.");
            var directory = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory)) throw new InvalidOperationException("The controller folder does not exist: " + directory);
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) throw new InvalidOperationException("The renderer must have a material.");
            var found = false;
            foreach (var material in materials)
            {
                if (material == null) continue;
                if (material.HasProperty(SpeedProperty) && material.HasProperty(XProperty) && material.HasProperty(YProperty) && material.HasProperty(ZProperty)) found = true;
            }
            if (!found) throw new InvalidOperationException("At least one renderer material must expose all four NXSG motion properties.");
        }

        public static AnimatorController AddToExisting(AnimatorController existing, AnimatorController motionDriver)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (motionDriver == null) throw new ArgumentNullException(nameof(motionDriver));
            var required = new[] { MagnitudeParameter, XParameter, YParameter, ZParameter };
            var existingParameters = ValidateExisting(existing, required, motionDriver.layers);
            Undo.IncrementCurrentGroup();
            var undoGroup=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add NXSG motion layers");
            Undo.RegisterCompleteObjectUndo(existing, "Add NXSG motion layers");
            foreach (var name in required) if (!existingParameters.ContainsKey(name)) existing.AddParameter(name, AnimatorControllerParameterType.Float);
            foreach (var source in motionDriver.layers) existing.AddLayer(source);
            Undo.CollapseUndoOperations(undoGroup);
            Undo.IncrementCurrentGroup();
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        public static AnimatorController AddToExisting(AnimatorController existing, Transform avatarRoot, Renderer renderer, string assetPath, float maxSpeed = 8f)
        {
            ValidateExisting(existing, new[] { MagnitudeParameter, XParameter, YParameter, ZParameter }, new[] {
                new AnimatorControllerLayer { name = "NXSG Motion Speed" }, new AnimatorControllerLayer { name = "NXSG Motion X" },
                new AnimatorControllerLayer { name = "NXSG Motion Y" }, new AnimatorControllerLayer { name = "NXSG Motion Z" }});
            var generated = Build(avatarRoot, renderer, assetPath, maxSpeed);
            return AddToExisting(existing, generated);
        }

        static Dictionary<string, AnimatorControllerParameterType> ValidateExisting(AnimatorController existing, string[] required, AnimatorControllerLayer[] incoming)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            var parameters = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (var parameter in existing.parameters) parameters[parameter.name] = parameter.type;
            foreach (var name in required)
                if (parameters.TryGetValue(name, out var type) && type != AnimatorControllerParameterType.Float)
                    throw new InvalidOperationException("Existing FX parameter has the wrong type: " + name);
            if (incoming != null)
            {
                var names = new HashSet<string>();
                foreach (var layer in existing.layers) names.Add(layer.name);
                foreach (var layer in incoming)
                    if (!names.Add(layer.name)) throw new InvalidOperationException("Existing FX already contains motion layer: " + layer.name);
            }
            return parameters;
        }

        static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void AddParameter(AnimatorController controller, string name) { controller.AddParameter(name, AnimatorControllerParameterType.Float); }

        static void AddLayer(AnimatorController controller, string name, string parameter, string property, Renderer renderer, Transform root, float min, float max)
        {
            var layer = new AnimatorControllerLayer { name = name, defaultWeight = 1f, blendingMode = AnimatorLayerBlendingMode.Override };
            var stateMachine = new AnimatorStateMachine { name = name + " State Machine" };
            var tree = new BlendTree { name = name + " Blend Tree", blendType = BlendTreeType.Simple1D, blendParameter = parameter, useAutomaticThresholds = false };
            var low = Clip(name + " Low", renderer, root, property, min);
            var high = Clip(name + " High", renderer, root, property, max);
            tree.AddChild(low, min);
            tree.AddChild(high, max);
            AssetDatabase.AddObjectToAsset(stateMachine, AssetDatabase.GetAssetPath(controller));
            AssetDatabase.AddObjectToAsset(tree, AssetDatabase.GetAssetPath(controller));
            AssetDatabase.AddObjectToAsset(low, AssetDatabase.GetAssetPath(controller));
            AssetDatabase.AddObjectToAsset(high, AssetDatabase.GetAssetPath(controller));
            var state = stateMachine.AddState(name);
            state.motion = tree;
            state.writeDefaultValues = false;
            layer.stateMachine = stateMachine;
            controller.AddLayer(layer);
        }

        static AnimationClip Clip(string name, Renderer renderer, Transform root, string property, float value)
        {
            var clip = new AnimationClip { name = name, legacy = false, frameRate = 60f };
            var curve = new AnimationCurve(new Keyframe(0f, value, 0f, 0f), new Keyframe(1f / 60f, value, 0f, 0f));
            var path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
            clip.SetCurve(path, renderer.GetType(), "material." + property, curve);
            return clip;
        }
    }

    public sealed class MotionDriverWindow : EditorWindow
    {
        Transform avatarRoot;
        Renderer renderer;
        AnimatorController existingFx;
        float maxSpeed = 8f;
        string assetPath = "Assets/NXSG Motion Driver.controller";
        string status;

        [MenuItem("Tools/NXSG/Create motion driver")]
        public static void Open() { GetWindow<MotionDriverWindow>("NXSG Motion Driver"); }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Creates a standalone FX AnimatorController. Merged FX controllers retain a dependency on this generated asset's state machines. Renderer bindings affect every matching material slot.", MessageType.Info);
            avatarRoot = (Transform)EditorGUILayout.ObjectField("Avatar root", avatarRoot, typeof(Transform), true);
            renderer = (Renderer)EditorGUILayout.ObjectField("Root renderer", renderer, typeof(Renderer), true);
            existingFx = (AnimatorController)EditorGUILayout.ObjectField("Existing FX (optional)", existingFx, typeof(AnimatorController), false);
            maxSpeed = EditorGUILayout.FloatField("Maximum speed (m/s)", maxSpeed);
            assetPath = EditorGUILayout.TextField("Controller path", assetPath);
            if (GUILayout.Button("Create standalone motion driver"))
            {
                try
                {
                    var controller = MotionDriverBuilder.Build(avatarRoot, renderer, assetPath, maxSpeed);
                    Selection.activeObject = controller;
                    status = "Created " + AssetDatabase.GetAssetPath(controller);
                }
                catch (Exception exception) { status = exception.Message; }
            }
            using (new EditorGUI.DisabledScope(existingFx == null))
            if (GUILayout.Button("Add motion layers to selected FX"))
            {
                try
                {
                    MotionDriverBuilder.AddToExisting(existingFx, avatarRoot, renderer, assetPath, maxSpeed);
                    status = "Added motion layers to " + existingFx.name;
                }
                catch (Exception exception) { status = exception.Message; }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }
    }
}
