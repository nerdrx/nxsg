using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class ToonShadowControlSmoke
{
    const int Size=192;
    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            var cam=new GameObject("Shadow camera").AddComponent<Camera>();cam.transform.position=new Vector3(0,0,-5);cam.transform.LookAt(Vector3.zero);cam.orthographic=true;cam.orthographicSize=2;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;
            var target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();cam.targetTexture=target;
            var receiver=GameObject.CreatePrimitive(PrimitiveType.Quad);receiver.transform.localScale=Vector3.one*4;var renderer=receiver.GetComponent<Renderer>();renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.transform.position=new Vector3(0,0,-.7f);caster.transform.localScale=Vector3.one*.4f;
            var light=new GameObject("Shadow key").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(0,35,0);light.shadows=LightShadows.Hard;light.shadowBias=.001f;light.shadowNormalBias=.001f;light.renderMode=LightRenderMode.ForcePixel;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Custom;RenderSettings.ambientProbe=new UnityEngine.Rendering.SphericalHarmonicsL2();RenderSettings.reflectionIntensity=0;
            QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=30;QualitySettings.shadowResolution=ShadowResolution.High;
            Directory.CreateDirectory("Assets/SmokeResults/ToonShadowControls");
            Color[] shadowed,unshadowed;
            using(var on=GraphPreview.Create(Graph(1),null)) { renderer.sharedMaterial=on.Material; shadowed=Capture(cam,target); }
            using(var off=GraphPreview.Create(Graph(0),null)) { renderer.sharedMaterial=off.Material; unshadowed=Capture(cam,target); }
            Require(Enumerable.Range(0,shadowed.Length).Count(i=>unshadowed[i].r-shadowed[i].r>.1f)>30,"Receive shadow control did not lift cast shadow");
            Save(unshadowed,"receive-off.png");Save(shadowed,"receive-on.png");
            caster.SetActive(false);light.shadows=LightShadows.None;light.type=LightType.Point;light.range=8;light.intensity=3;light.transform.position=new Vector3(0,0,-1);
            using(var off=GraphPreview.Create(Graph(0),null))
            {
                renderer.sharedMaterial=off.Material;var near=Capture(cam,target);light.transform.position=new Vector3(0,0,-6);var far=Capture(cam,target);
                Require(near[Size*Size/2+Size/2].r>far[Size*Size/2+Size/2].r+.05f,"Disabling shadows removed point-light distance falloff");
            }
            light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(0,90,0);
            var border=Graph(1);border.Nodes[0].Properties["shadowStrength"]=1;border.Nodes[0].Properties["borderStrength"]=1;border.Nodes[0].Properties["borderWidth"]=.2;
            using(var preview=GraphPreview.Create(border,null)) { renderer.sharedMaterial=preview.Material;var pixels=Capture(cam,target);Require(pixels[Size*Size/2+Size/2].r>pixels[Size*Size/2+Size/2].b+.1f,"Warm border tint missing");Save(pixels,"border-tint.png"); }
            foreach(var amount in new[]{0,1})File.WriteAllText("Assets/SmokeResults/ToonShadowControls/shadow-"+amount+".nxsg",GraphJson.Serialize(Graph(amount)));
            File.WriteAllText("Assets/SmokeResults/ToonShadowControls/border.nxsg",GraphJson.Serialize(border));
            Debug.Log("NXSG TOON SHADOW CONTROL SMOKE PASSED");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static ShaderGraph Graph(int receive)
    {
        var g=new ShaderGraph{GraphId="toon-shadow-control-"+receive};var surface=NodeCatalog.Create("core.toonSurface");surface.Id="surface";surface.Properties["shadowStrength"]=0;surface.Properties["receiveShadow"]=receive;
        var output=NodeCatalog.Create("core.output");output.Id="output";g.Nodes.Add(surface);g.Nodes.Add(output);g.Connections.Add(new GraphConnection{Id="out",From=new GraphPortRef{NodeId="surface",PortId="surface"},To=new GraphPortRef{NodeId="output",PortId="surface"}});return g;
    }
    static Color[] Capture(Camera camera,RenderTexture target) { camera.Render();RenderTexture.active=target;var t=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);t.ReadPixels(new Rect(0,0,Size,Size),0,0);t.Apply();var p=t.GetPixels();UnityEngine.Object.DestroyImmediate(t);RenderTexture.active=null;Require(p.All(c=>!float.IsNaN(c.r)&&!float.IsInfinity(c.r)),"Nonfinite output");return p; }
    static void Save(Color[] pixels,string name) {var t=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);t.SetPixels(pixels);t.Apply();File.WriteAllBytes("Assets/SmokeResults/ToonShadowControls/"+name,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
