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
            var choice = new PopupField<string>("Shading",new List<string>{"Threshold","Multiple bands","Texture ramp"},Mathf.Clamp((int?)node.Properties["lightingMode"]??0,0,2));
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
            var mode=(int?)node.Properties["lightingMode"]??0;
            if(mode==1) AddIntegerField(node,"bands","Light bands",2,8,3);
            if(mode==2) { AddTexturePicker(node,"Lighting ramp"); AddBoundedNumber(node,"rampRow","Ramp row",0,1,.5f); FeatureNote("Ramp X runs from shadow to light. Set the texture's wrap mode to Clamp. RGB colors the direct lighting; ambient and emission remain separate."); }
            else AddColorField(node,"shadeColor","Shadow tint",Color.black,"shadeColor");
            AddBoundedNumber(node,"shadeMap","Shade map",0,1,.5f,"shadeMap","0.5 leaves lighting unchanged. Dark values move the shadow boundary toward light; bright values move it toward shadow.");
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
                case "core.outline":
                    AddNumber(node,"width","Width (m)",.003f,"width");
                    AddBoundedNumber(node,"mask","Width mask",0,1,1,"mask");
                    AddColorField(node,"color","Outline color",Color.black,"color");
                    FeatureNote("Connect the finished mesh surface to Base, then Outline to Output. Smooth normals give a continuous hull. Width does not expand fur or particle geometry."); return true;
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
