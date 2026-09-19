using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MotionDriverSmoke
{
    public static void Run()
    {
        const string shaderPath = "Assets/NXSG Motion Driver Smoke.shader";
        const string controllerPath = "Assets/NXSG Motion Driver Smoke.controller";
        GameObject root = null;
        try
        {
            File.WriteAllText(shaderPath, @"Shader ""NXSG/MotionDriverSmoke"" { Properties { _NXSG_MotionSpeed(""Speed"",Float)=0 _NXSG_MotionX(""X"",Float)=0 _NXSG_MotionY(""Y"",Float)=0 _NXSG_MotionZ(""Z"",Float)=0 } SubShader { Pass { } } }");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            root = new GameObject("Motion Driver Root");
            var child = new GameObject("Renderer"); child.transform.SetParent(root.transform);
            var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
            var controller = NXSG.Editor.MotionDriverBuilder.Build(root.transform, renderer, controllerPath, 8f);
            if (controller.layers.Length != 4 || controller.parameters.Length != 4) throw new Exception("Motion controller shape is wrong.");
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(controller), ImportAssetOptions.ForceSynchronousImport);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(controller));
            var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
            animator.SetFloat(NXSG.Editor.MotionDriverBuilder.MagnitudeParameter, 4f); animator.SetFloat(NXSG.Editor.MotionDriverBuilder.XParameter, -3f); animator.SetFloat(NXSG.Editor.MotionDriverBuilder.YParameter, 2f); animator.SetFloat(NXSG.Editor.MotionDriverBuilder.ZParameter, 1f); animator.Update(1f);
            var material = new MaterialPropertyBlock(); renderer.GetPropertyBlock(material);
            Debug.Log("MOTION PLAYBACK properties: "+material.GetFloat(NXSG.Editor.MotionDriverBuilder.SpeedProperty)+", "+material.GetFloat(NXSG.Editor.MotionDriverBuilder.XProperty));
            Require(Mathf.Abs(material.GetFloat(NXSG.Editor.MotionDriverBuilder.SpeedProperty) - 4f) < .01f, "Speed playback failed");
            Require(Mathf.Abs(material.GetFloat(NXSG.Editor.MotionDriverBuilder.XProperty) + 3f) < .01f, "Signed X playback failed");
            Require(Mathf.Abs(material.GetFloat(NXSG.Editor.MotionDriverBuilder.YProperty) - 2f) < .01f && Mathf.Abs(material.GetFloat(NXSG.Editor.MotionDriverBuilder.ZProperty) - 1f) < .01f, "Signed axis playback failed");
            var existing = AnimatorController.CreateAnimatorControllerAtPath("Assets/NXSG Motion Driver Existing.controller");
            existing.AddLayer(new AnimatorControllerLayer { name = "User FX", defaultWeight = 1f });
            NXSG.Editor.MotionDriverBuilder.AddToExisting(existing, controller);
            if (existing.layers.Length != 6) throw new Exception("Existing FX layer was not preserved.");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); if (existing.layers.Length != 2 || existing.parameters.Length != 0) throw new Exception("FX merge undo failed.");
            Undo.PerformRedo(); if (existing.layers.Length != 6 || existing.parameters.Length != 4) throw new Exception("FX merge redo failed.");
            var duplicateRejected = false; try { NXSG.Editor.MotionDriverBuilder.AddToExisting(existing, controller); } catch (InvalidOperationException) { duplicateRejected = true; }
            Require(duplicateRejected, "Duplicate motion layers were accepted.");
            var wrongType = AnimatorController.CreateAnimatorControllerAtPath("Assets/NXSG Motion Driver Wrong Type.controller");
            wrongType.AddParameter(NXSG.Editor.MotionDriverBuilder.XParameter, AnimatorControllerParameterType.Bool);
            var wrongTypeRejected = false; try { NXSG.Editor.MotionDriverBuilder.AddToExisting(wrongType, controller); } catch (InvalidOperationException) { wrongTypeRejected = true; }
            Require(wrongTypeRejected, "Wrong existing parameter type was accepted.");
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(existing),ImportAssetOptions.ForceSynchronousImport);
            existing=AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(existing));
            Require(existing.layers[2].stateMachine!=null && existing.layers[2].stateMachine.states[0].state.motion!=null,"Reload lost merged motion assets");
            var skinnedObject=new GameObject("Skinned");skinnedObject.transform.SetParent(root.transform);
            var skinned=skinnedObject.AddComponent<SkinnedMeshRenderer>();skinned.sharedMaterial=renderer.sharedMaterial;
            var skinnedController=NXSG.Editor.MotionDriverBuilder.Build(root.transform,skinned,"Assets/NXSG Motion Skinned.controller");
            animator.runtimeAnimatorController=skinnedController;animator.Rebind();animator.Update(0);
            animator.SetFloat(NXSG.Editor.MotionDriverBuilder.MagnitudeParameter,3f);animator.SetFloat(NXSG.Editor.MotionDriverBuilder.YParameter,-2f);animator.Update(.1f);
            skinned.GetPropertyBlock(material);Require(Mathf.Abs(material.GetFloat(NXSG.Editor.MotionDriverBuilder.SpeedProperty)-3)<.01f && Mathf.Abs(material.GetFloat(NXSG.Editor.MotionDriverBuilder.YProperty)+2)<.01f,"Skinned renderer playback failed");
            Debug.Log("NXSG MOTION DRIVER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset("Assets/NXSG Motion Skinned.controller");
            AssetDatabase.DeleteAsset(shaderPath); AssetDatabase.DeleteAsset(controllerPath); AssetDatabase.DeleteAsset("Assets/NXSG Motion Driver Existing.controller"); AssetDatabase.DeleteAsset("Assets/NXSG Motion Driver Wrong Type.controller");
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
