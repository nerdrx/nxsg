using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        bool audioDataPreview;
        float spectrumPreview = .5f, chronoPreview;
        Color themePreview = Color.white;

        void ApplyAudioDataPreview()
        {
            if(preview==null) return;
            preview.SetFloat("_NXSG_AudioDataPreview",audioDataPreview?1:0);
            preview.SetFloat("_NXSG_AudioSpectrumPreview",spectrumPreview);
            preview.SetFloat("_NXSG_AudioChronotensityPreview",chronoPreview);
            preview.SetColor("_NXSG_AudioThemePreview",themePreview);
        }

        void AddAudioDataControls(GraphNode node)
        {
            if(node.Operation=="core.audioVisualizer")
            {
                AddIntegerField(node,"bars","Bars",4,128,32); AddIndexedChoice(node,"radial","Layout",new[]{"Horizontal bars","Radial bars"});
                AddNumber(node,"minFrequency","Lowest frequency (Hz)",40); AddNumber(node,"maxFrequency","Highest frequency (Hz)",14000);
                AddNumber(node,"gain","Gain",1,"gain"); AddBoundedNumber(node,"gap","Bar gap",0,1,.12f);
            }
            else if(node.Operation=="core.audioThemeColor")
            { AddIntegerField(node,"index","Theme color",0,3,0); AddColorField(node,"fallback","Fallback",Color.white); }
            else if(node.Operation=="core.audioChronotensity")
            {
                AddIndexedChoice(node,"index","Motion mode",new[]{"Forward with intensity","Forward with intensity · filtered","Back and forth","Back and forth · filtered","Forward when quiet","Forward when quiet · filtered","Forward quiet / reverse loud","Quiet / loud · filtered"});
                AddIndexedChoice(node,"band","Band",new[]{"Bass","Low mids","High mids","Treble"});
                AddNumber(node,"speed","Speed",1,"speed"); AddIndexedChoice(node,"normalized","Output",new[]{"Accumulated time","Wrapped 0–1"}); AddNumber(node,"fallback","Fallback",0);
            }
            else
            {
                var hz=node.Operation=="core.audioSpectrum";
                AddNumber(node,hz?"frequency":"bin",hz?"Frequency (Hz)":"Chromatic bin",hz?440:48,hz?"frequency":"bin");
                AddIndexedChoice(node,"channel","Spectrum",new[]{"Raw magnitude","EQ magnitude","ColorChord filtered"});
                AddNumber(node,"gain","Gain",1); AddNumber(node,"fallback","Fallback",0);
            }
            var enabled=new Toggle("Preview audio data"){value=audioDataPreview};
            enabled.RegisterValueChangedCallback(e=>{audioDataPreview=e.newValue;ApplyAudioDataPreview();}); inspector.Add(enabled);
            var magnitude=new FloatField("Preview magnitude"){value=spectrumPreview};
            magnitude.RegisterValueChangedCallback(e=>{spectrumPreview=e.newValue;ApplyAudioDataPreview();});inspector.Add(magnitude);
            var time=new FloatField("Preview accumulated time"){value=chronoPreview};
            time.RegisterValueChangedCallback(e=>{chronoPreview=e.newValue;ApplyAudioDataPreview();});inspector.Add(time);
            var color=new UnityEditor.UIElements.ColorField("Preview theme"){value=themePreview};
            color.RegisterValueChangedCallback(e=>{themePreview=e.newValue;ApplyAudioDataPreview();});inspector.Add(color);
        }
        void AddOutputControls(GraphNode node)
        {
            AddInspectorSection("RENDERING");
            AddIndexedChoice(node,"renderMode","Rendering",new[]{"Automatic","Opaque","Cutout","Alpha blend","Additive"});
            AddIndexedChoice(node,"cull","Visible faces",new[]{"Front","Back","Both"});
            AddIndexedChoice(node,"zWrite","Depth write",new[]{"Automatic","On","Off"});
            AddIndexedChoice(node,"zTest","Depth test",new[]{"Less or equal","Less","Equal","Greater","Greater or equal","Always","Not equal","Never"});
            AddIntegerField(node,"queueOffset","Queue offset",-50,50,0);
            FeatureNote("Automatic preserves the surface's rendering mode. Alpha blend and Additive disable depth writes by default. Particle and Volume surfaces require Automatic.");
            FurSection("Stencil",()=>
            {
                AddIndexedChoice(node,"stencilEnabled","Stencil",new[]{"Disabled","Enabled"});
                AddIntegerField(node,"stencilRef","Reference",0,255,0);
                AddIntegerField(node,"stencilReadMask","Read mask",0,255,255);
                AddIntegerField(node,"stencilWriteMask","Write mask",0,255,255);
                AddIndexedChoice(node,"stencilCompare","Compare",new[]{"Always","Equal","Not equal","Less","Less or equal","Greater","Greater or equal","Never"});
                AddIndexedChoice(node,"stencilPass","On pass",new[]{"Keep","Replace","Zero","Increment saturated","Decrement saturated","Invert","Increment wrap","Decrement wrap"});
                FeatureNote("Stencil is shared with the scene. Coordinate values with other materials; camera render targets must contain stencil. It does not persist between cameras.");
            });
        }

        void AddToonLightingControls(GraphNode node)
        {
            AddInspectorSection("TOON SHADING");
            var choice = new PopupField<string>("Shading",new List<string>{"Threshold","Multiple bands","Texture ramp","Layered shadows"},Mathf.Clamp((int?)node.Properties["lightingMode"]??0,0,3));
            choice.RegisterValueChangedCallback(e=>Edit("Change toon shading",()=>
            {
                node.Properties["lightingMode"]=choice.index;
                if(choice.index==2 && !graph.Resources.Any(r=>r.Id==(string)node.Properties["resourceId"]))
                {
                    var resource=new GraphResource{Id="ramp-"+node.Id,Name="Lighting ramp",Kind="texture2D",Uri="builtin://white"};
                    graph.Resources.Add(resource); node.Properties["resourceId"]=resource.Id;
                }
            }));
            inspector.Add(choice);
            AddBoundedNumber(node,"receiveShadow","Receive scene shadows",0,1,1,"receiveShadow","Adjust scene-shadow strength while keeping light distance and cookie attenuation.");
            FurSection("Integrated rim shading",()=>
            {
                AddBoundedNumber(node,"rimStrength","Strength",0,1,0,"rimStrength");
                AddColorField(node,"rimColor","Rim tint",Color.white,"rimColor");
                AddBoundedNumber(node,"rimWidth","Width",0,1,.2f,"rimWidth");
                AddBoundedNumber(node,"rimSoftness","Softness",0,1,.05f,"rimSoftness");
                AddBoundedNumber(node,"rimLightAlignment","Light alignment",0,1,1,"rimLightAlignment");
                FeatureNote("Rim tint blends into direct toon shading. Width and softness shape the camera-facing edge; light alignment makes the rim follow the main light. Strength 0 disables rim shading.");
            });
            if (((int?)node.Properties["lightingMode"]??0)!=2) FurSection("Shadow border tint",()=>
            {
                AddBoundedNumber(node,"borderStrength","Tint strength",0,1,0,"borderStrength");
                AddColorField(node,"borderColor","Border color",new Color(1,.3f,.15f,1),"borderColor");
                AddBoundedNumber(node,"borderWidth","Border width",0,.5f,.05f,"borderWidth");
            });
            var mode=(int?)node.Properties["lightingMode"]??0;
            if(mode==3)
            {
                AddIntegerField(node,"shadowLayers","Shadow layers",1,3,3);
                var layers=Mathf.Clamp((int?)node.Properties["shadowLayers"]??3,1,3);
                for(var i=1;i<=layers;i++)
                {
                    var layer=i; FurSection("Shadow layer "+layer,()=>AddToonShadowLayerControls(node,layer));
                }
                FeatureNote("Layers blend in order: 1, then 2, then 3. Lower borders place later layers in deeper shadow. Strength also works as a mask. Wires on inactive layers are kept but ignored. Normal influence: 0 uses mesh normals, 1 uses the connected surface normal.");
            }
            else
            {
                if(mode==1) AddIntegerField(node,"bands","Light bands",2,8,3);
                if(mode==2) { AddTexturePicker(node,"Lighting ramp"); AddBoundedNumber(node,"rampRow","Ramp row",0,1,.5f); FeatureNote("Ramp X runs from shadow to light. Set the texture's wrap mode to Clamp. RGB colors the direct lighting; ambient and emission remain separate."); }
                else AddColorField(node,"shadeColor","Shadow tint",Color.black,"shadeColor");
                AddBoundedNumber(node,"shadeMap","Shade map",0,1,.5f,"shadeMap","0.5 leaves lighting unchanged. Dark values move the shadow boundary toward light; bright values move it toward shadow.");
                FeatureNote("Border, blur and shadow strength use the material's Toon controls. Connect their sockets to drive them from the graph. Layered shadows keeps each layer's settings here.");
            }
        }

        void AddToonShadowLayerControls(GraphNode node,int layer)
        {
            var suffix=layer==1?"":layer.ToString();
            var color=layer==1?Color.black:layer==2?new Color(.35f,.25f,.5f,1):new Color(.08f,.04f,.15f,1);
            AddColorField(node,"shadeColor"+suffix,"Shadow tint",color,"shadeColor"+suffix);
            AddBoundedNumber(node,"threshold"+suffix,"Border",0,1,layer==1?.5f:layer==2?.35f:.2f,"threshold"+suffix);
            AddBoundedNumber(node,"softness"+suffix,"Blur",0,1,.05f,"softness"+suffix);
            AddBoundedNumber(node,"shadowStrength"+suffix,"Strength / mask",0,1,1,"shadowStrength"+suffix);
            AddBoundedNumber(node,"shadeMap"+suffix,"Shade map",0,1,.5f,"shadeMap"+suffix);
            AddBoundedNumber(node,"normalStrength"+suffix,"Normal influence",0,1,1,"normalStrength"+suffix);
            var receiveShadowPort=layer==1?"layerReceiveShadow":"layerReceiveShadow"+layer;
            AddBoundedNumber(node,receiveShadowPort,"Receive scene shadows",0,1,1,receiveShadowPort,"Controls this layer's response to the scene shadow map. 1 follows the global receive-shadow setting; 0 keeps this layer's shade tint unattenuated by cast shadows.");
        }

        bool AddRenderingFeatureControls(GraphNode node)
        {
            if(AudioDataNodes.IsKnown(node.Operation)) { AddAudioDataControls(node); return true; }
            switch(node.Operation)
            {
                case "core.lightVolumes":
                    AddColorField(node,"albedo","Albedo",Color.white,"albedo");
                    AddBoundedNumber(node,"roughness","Roughness",0,1,.5f,"roughness");
                    AddBoundedNumber(node,"metallic","Metallic",0,1,0,"metallic"); AddNumber(node,"strength","Strength",1,"strength");
                    FeatureNote("Requires VRC Light Volumes v3. Normal is world-space. Connect Color to Unlit Albedo, or use Diffuse and Specular separately. Already includes surface color; adding it to a lit surface may count ambient twice.");return true;
                case "core.ssao": case "core.contactShadow":
                    var ao=node.Operation=="core.ssao";
                    var values=new List<int>{4,8,16,32};
                    var samples=new PopupField<int>("Depth samples",values,Mathf.Max(0,values.IndexOf((int?)node.Properties["samples"]??8)));
                    samples.RegisterValueChangedCallback(e=>Edit("Change depth samples",()=>node.Properties["samples"]=e.newValue));inspector.Add(samples);
                    AddNumber(node,ao?"radius":"distance",ao?"Radius (m)":"Trace distance (m)",ao?.3f:.5f,ao?"radius":"distance");
                    AddBoundedNumber(node,"strength","Strength",0,1,ao?.65f:.7f,"strength");
                    AddNumber(node,"thickness","Thickness (m)",ao?.2f:.12f,"thickness");
                    AddNumber(node,"bias","Bias (m)",ao?.02f:.015f,"bias");
                    FeatureNote(ao?"Connect Visibility to surface Occlusion. Requires camera depth. Hidden or off-screen geometry cannot contribute.":"Connect Visibility to surface Shadow. Direction defaults to each light; an optional direction input uses world space. Screen depth cannot detect hidden or off-screen blockers."); return true;
                case "core.geometryDissolve":
                    AddBoundedNumber(node,"amount","Dissolve amount",0,1,0,"amount");
                    AddBoundedNumber(node,"mask","Mask",0,1,1,"mask");
                    AddNumber(node,"distance","Travel distance (m)",.3f,"distance");
                    AddNumber(node,"rotation","Rotation (turns)",1,"rotation");
                    AddBoundedNumber(node,"shrink","Shrink",0,1,1,"shrink");
                    FeatureNote("Triangles follow the current animated pose. Direction defaults to each triangle's mesh normal; a connected vector uses world space. Amount 1 removes fully masked triangles. Expand renderer bounds. PC only."); return true;
                case "core.softOutline":
                    AddIndexedChoice(node,"widthMode","Width units",new[]{"World metres","Screen pixels"});
                    if(((int?)node.Properties["widthMode"]??0)==0) AddNumber(node,"width","Width (m)",.04f,"width");
                    else AddNumber(node,"pixelWidth","Width (px)",12,"pixelWidth");
                    AddBoundedNumber(node,"mask","Width mask",0,1,1,"mask");
                    AddColorField(node,"color","Aura color",new Color(.47f,.05f,1,1),"color");
                    AddBoundedNumber(node,"opacity","Opacity",0,1,1,"opacity");
                    AddNumber(node,"falloff","Edge falloff",1,"falloff","1 gives a linear fade. Higher values concentrate color near the mesh; lower values spread it outward.");
                    FeatureNote("Feathered fins follow the silhouette from smooth normals. Connect Noise or AudioLink to Width, Opacity or Color. Hard/split normals can create gaps. Expand renderer bounds. PC only; no bloom required."); return true;
                case "core.outline":
                    AddIndexedChoice(node,"widthMode","Width units",new[]{"World metres","Screen pixels"});
                    if(((int?)node.Properties["widthMode"]??0)==0) AddNumber(node,"width","Width (m)",.003f,"width");
                    else AddNumber(node,"pixelWidth","Width (px)",2,"pixelWidth");
                    AddBoundedNumber(node,"mask","Width mask",0,1,1,"mask");
                    AddColorField(node,"color","Outline color",Color.black,"color");
                    FurSection("Lighting and shape",()=>
                    {
                        AddBoundedNumber(node,"lighting","Lighting influence",0,1,0,"lighting","0 keeps the outline unlit. 1 uses ambient and the main light, including its available shadow map.");
                        AddColorField(node,"emission","Emission",Color.black,"emission");
                        AddBoundedNumber(node,"directionStrength","Direction influence",0,1,1,"directionStrength","Blend mesh normals toward the optional Direction input. Direction uses world space; a zero vector falls back to mesh normals.");
                        AddNumber(node,"depthBias","Depth bias",0,"depthBias","Normalized depth offset. Positive pushes away from the camera; negative pulls toward it. Use small values such as 0.0001.");
                    });
                    FeatureNote("Connect the finished mesh surface to Base, then Outline to Output. Pixel width stays approximately constant with distance; world width follows perspective. Only the selected width unit is used; other width wires are kept. Hard normals can split the hull. Fur and particle geometry are not outlined."); return true;
                case "core.uvTileDiscard":
                    AddNumber(node,"tileX","Tile X",0,"tileX"); AddNumber(node,"tileY","Tile Y",0,"tileY");
                    AddBoundedNumber(node,"enabled","Discard amount",0,1,1,"enabled");
                    AddIndexedChoice(node,"invert","Selection",new[]{"Hide this tile","Keep only this tile"});
                    FeatureNote("Use unwrapped mesh UVs. Connect Visibility to Opacity and use a cutoff above zero. This clips pixels in the visible and shadow passes; it does not remove triangles."); return true;
                case "core.cubemap": AddTexturePicker(node,"Cubemap"); AddNumber(node,"lod","Mip level",0,"lod"); return true;
                case "core.textureArray": AddTexturePicker(node,"Texture array"); AddNumber(node,"slice","Slice",0,"slice"); AddNumber(node,"lod","Mip level",0,"lod"); return true;
                default: return false;
            }
        }
    }
}
