using System;
using System.IO;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Presentation-only studio, HDR bloom and tone mapping; samples use the shipped backend.
public static class VolumeArtShowcase
{
    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.055f,.065f,.09f);
            RenderSettings.fog=false; RenderSettings.fogColor=new Color(.0003f,.0005f,.0012f); RenderSettings.fogMode=FogMode.Exponential; RenderSettings.fogDensity=.18f;
            var key=new GameObject("Large key light").AddComponent<Light>(); key.type=LightType.Directional; key.intensity=1.4f; key.color=new Color(1,.89f,.76f); key.transform.rotation=Quaternion.Euler(25,45,0);
            var subject=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var camera=new GameObject("Studio camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;camera.nearClipPlane=.02f;camera.farClipPlane=50;camera.fieldOfView=35;camera.allowHDR=true;camera.depthTextureMode=DepthTextureMode.Depth;
            const int width=1600,height=1100;
            var hdr=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);hdr.Create();camera.targetTexture=hdr;
            var bloomA=new RenderTexture(width/4,height/4,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);bloomA.Create();
            var bloomB=new RenderTexture(width/4,height/4,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);bloomB.Create();
            var output=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);output.Create();
            Directory.CreateDirectory("Assets/SmokeResults");File.WriteAllText("Assets/SmokeResults/VolumeArtPost.shader",Post);AssetDatabase.ImportAsset("Assets/SmokeResults/VolumeArtPost.shader",ImportAssetOptions.ForceSynchronousImport);
            var postShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/SmokeResults/VolumeArtPost.shader");if(ShaderUtil.ShaderHasError(postShader))throw new Exception("Studio post shader failed");var post=new Material(postShader);
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NXSG.Backend.ShaderEmitter).Assembly).resolvedPath;
            var root=Directory.GetParent(Directory.GetParent(package).FullName).FullName;var dest=Path.Combine(root,"work/showcase/art");Directory.CreateDirectory(dest);
            string[] names={"Volume Dust Nebula","Volume Pearl Sculpture","Volume Filament Ring"};string[] files={"nebula","pearl","filaments"};
            for(int i=0;i<names.Length;i++)
            {
                bool sculpture=i==1;
                camera.transform.position=sculpture?new Vector3(.8f,.6f,-1.8f):new Vector3(.85f,.95f,-1.7f);
                camera.transform.LookAt(sculpture?new Vector3(0,-.07f,0):Vector3.zero);
                camera.fieldOfView=sculpture?35:(i==2?25:31);
                subject.transform.localScale=i==0?new Vector3(1.2f,.85f,1):Vector3.one;
                subject.transform.rotation=i==2?Quaternion.Euler(12,0,-18):Quaternion.identity;
                var graph=GraphJson.Parse(File.ReadAllText(Path.Combine(package,"Samples~",names[i]+".nxsg")));
                using(var preview=NXSG.Editor.GraphPreview.Create(graph,null))
                {
                    NXSG.Editor.PreviewClock.Apply(preview.Material,1.25f);subject.GetComponent<Renderer>().sharedMaterial=preview.Material;camera.Render();
                    Graphics.Blit(hdr,bloomA,post,0);
                    for(int blur=0;blur<3;blur++){post.SetVector("_Direction",new Vector4(1f/bloomA.width,0,0,0));Graphics.Blit(bloomA,bloomB,post,1);post.SetVector("_Direction",new Vector4(0,1f/bloomA.height,0,0));Graphics.Blit(bloomB,bloomA,post,1);}
                    post.SetTexture("_Bloom",bloomA);post.SetFloat("_Exposure",sculpture?1.05f:1.3f);Graphics.Blit(hdr,output,post,2);
                    RenderTexture.active=output;var capture=new Texture2D(width,height,TextureFormat.RGB24,false,true);capture.ReadPixels(new Rect(0,0,width,height),0,0);capture.Apply();File.WriteAllBytes(Path.Combine(dest,files[i]+".png"),capture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(capture);
                }
            }
            RenderTexture.active=null;camera.targetTexture=null;foreach(var rt in new[]{hdr,bloomA,bloomB,output}){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}UnityEngine.Object.DestroyImmediate(post);
            Debug.Log("NXSG ART SHOWCASE PASSED: "+dest);EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    const string Post=@"Shader ""Hidden/NXSG/StudioPost"" { Properties { _MainTex (""Source"", 2D) = ""white"" {} _Bloom (""Bloom"", 2D) = ""black"" {} } SubShader { Cull Off ZWrite Off ZTest Always
    CGINCLUDE
    #include ""UnityCG.cginc""
    sampler2D _MainTex,_Bloom; float2 _Direction; float _Exposure;
    float4 extract(v2f_img i):SV_Target {return float4(max(0,tex2D(_MainTex,i.uv).rgb-.6),1);}
    float4 blur(v2f_img i):SV_Target {float3 c=tex2D(_MainTex,i.uv).rgb*.375;c+=(tex2D(_MainTex,i.uv+_Direction*2).rgb+tex2D(_MainTex,i.uv-_Direction*2).rgb)*.25;c+=(tex2D(_MainTex,i.uv+_Direction*4).rgb+tex2D(_MainTex,i.uv-_Direction*4).rgb)*.0625;return float4(c,1);}
    float4 tone(v2f_img i):SV_Target {float3 c=max(0,tex2D(_MainTex,i.uv).rgb+tex2D(_Bloom,i.uv).rgb*.18);return float4(pow(saturate(1-exp(-c*_Exposure)),1.0/2.2),1);}
    ENDCG
    Pass { CGPROGRAM
    #pragma vertex vert_img
    #pragma fragment extract
    ENDCG }
    Pass { CGPROGRAM
    #pragma vertex vert_img
    #pragma fragment blur
    ENDCG }
    Pass { CGPROGRAM
    #pragma vertex vert_img
    #pragma fragment tone
    ENDCG }
    }}";
}
