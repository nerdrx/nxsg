using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    public sealed class FeatureNode
    {
        public readonly string Title, Category, Description; public readonly Dictionary<string,string> Inputs, Outputs; public readonly JObject Defaults; public readonly bool Texture;
        public FeatureNode(string title,string category,string description,string inputs,string outputs,bool texture, JObject defaults)
        { Title=title; Category=category; Description=description; Inputs=Ports(inputs); Outputs=Ports(outputs); Texture=texture; Defaults=defaults; }
        static Dictionary<string,string> Ports(string text) { var result=new Dictionary<string,string>(StringComparer.Ordinal); if (string.IsNullOrEmpty(text)) return result; foreach(var item in text.Split(',')){var p=item.Split(':');result[p[0]]=p[1];} return result; }
    }

    public static class FeatureNodes
    {
        static readonly Dictionary<string,FeatureNode> Items = new Dictionary<string,FeatureNode>(StringComparer.Ordinal)
        {
            ["core.fur"] = D("Fur","Surface","Layered shell fur over an existing surface. Control length and coverage, groom in object space, color roots and tips, and animate wind. More layers add mesh passes.","base:surface,rootColor:color,tipColor:color,length:float,density:float,thickness:float,mask:float,groom:vector3,time:float","surface:surface",false,new JObject{{"fins",0},{"finOpacity",.7},{"layers",16},{"length",.04},{"density",100},{"thickness",.35},{"taper",1},{"gravity",.1},{"windStrength",.1},{"windSpeed",1},{"windScale",2},{"rimStrength",.25},{"lodNear",5},{"lodFar",15},{"minLayers",4}}),
            ["core.parallaxUV"] = D("Parallax UVs","Coordinates","Offset UVs using height and tangent-space view direction. Changes texture depth, not the mesh silhouette.","uv:vector2,height:float","uv:vector2",false,new JObject{{"height",.5},{"strength",.05},{"reference",.5}}),
            ["core.parallaxOcclusion"] = D("Parallax Occlusion","Coordinates","Ray-march the red channel of a height texture for layered depth. Requires mesh tangents; does not change silhouette or cast displaced shadows.","uv:vector2","uv:vector2",true,new JObject{{"resourceId",""},{"strength",.05},{"steps",16}}),
            ["core.furMask"] = D("Fur Strand Mask","Textures","Procedural tapered strand dots for fur masks; height zero is the root, one is the tip.","uv:vector2","value:float",false,new JObject{{"density",100},{"thickness",.35},{"height",0},{"taper",1}}),
            ["core.flowMapUV"] = D("Flow Map UVs","Coordinates","Animate UV motion from a flow map: red/green encode direction and 0.5 is neutral.","uv:vector2,flow:color,time:float","uv:vector2",false,new JObject{{"strength",.1},{"speed",1}}),
            ["core.ditherMask"] = D("Dither Mask","Textures","Turn a soft mask into a repeating four-by-four ordered dither pattern.","uv:vector2,value:float","value:float",false,new JObject{{"value",.5},{"scale",64}}),
            ["core.truchet"] = D("Truchet Tiles","Textures","Randomly oriented curved tile paths. Scale controls tile count; seed changes the maze.","uv:vector2","value:float",false,new JObject{{"scale",8},{"width",.08},{"seed",0}}),
            ["core.weave"] = D("Woven Fabric","Textures","Alternating over-under threads for a woven fabric mask.","uv:vector2","value:float",false,new JObject{{"scale",30},{"width",.75}}),
            ["core.scales"] = D("Scale Pattern","Textures","Staggered curved scale outlines for reptile, fish or fantasy materials.","uv:vector2","value:float",false,new JObject{{"scale",12},{"width",.06}}),
            ["core.dots"] = D("Polka Dots","Textures","Repeating round dots. Radius is measured inside each tile.","uv:vector2","value:float",false,new JObject{{"scale",10},{"radius",.25}}),
            ["core.scratches"] = D("Scratches","Textures","Sparse short vertical scratches; rotate incoming UVs to change their direction.","uv:vector2","value:float",false,new JObject{{"scale",30},{"width",.025},{"length",.7},{"seed",0}}),
            ["core.cracks"] = D("Cracks","Textures","Cellular fracture lines. Scale controls the size of pieces; width thickens the cracks.","uv:vector2","value:float",false,new JObject{{"scale",8},{"width",.04}}),
            ["core.woodRings"] = D("Wood Rings","Textures","Distorted concentric growth rings for wood and organic bands.","uv:vector2","value:float",false,new JObject{{"scale",12},{"distortion",.3}}),
            ["core.marble"] = D("Marble","Textures","Wavy veins distorted by layered noise. Feed the result into a Color Ramp.","uv:vector2","value:float",false,new JObject{{"scale",5},{"distortion",3}}),
            ["core.clouds"] = D("Clouds","Textures","Four-layer drifting soft noise for clouds, mist masks and soft organic patches.","uv:vector2,time:float","value:float",false,new JObject{{"scale",4},{"speed",.1},{"contrast",1}}),
            ["core.sparkleMask"] = D("Sparkle Mask","Textures","Small procedural sparkles blink independently across UV space.","uv:vector2,time:float","value:float",false,new JObject{{"scale",30},{"speed",2},{"density",.2},{"size",.08}}),
            ["core.scanlines"] = D("Hologram Scanlines","Textures","Moving horizontal scanlines for hologram emission or opacity.","uv:vector2,time:float","value:float",false,new JObject{{"scale",100},{"speed",.2},{"width",.3}}),
            ["core.glitchUV"] = D("Glitch UVs","Coordinates","Jitter horizontal strips in discrete time steps for a digital glitch.","uv:vector2,time:float","uv:vector2",false,new JObject{{"strength",.05},{"speed",5},{"rows",20}}),
            ["core.pixelateUV"] = D("Pixelate UVs","Coordinates","Snap UV sampling to cell centers for a blocky pixelated texture.","uv:vector2","uv:vector2",false,new JObject{{"cells",64}}),
            ["core.kaleidoscopeUV"] = D("Kaleidoscope UVs","Coordinates","Fold UV space into mirrored radial sectors. Rotation is in degrees.","uv:vector2","uv:vector2",false,new JObject{{"segments",6},{"rotation",0}}),
            ["core.swapUV"] = D("Swap UV Axes","Coordinates","Swap horizontal and vertical coordinates to transpose a texture.","uv:vector2","uv:vector2",false,new JObject()),
            ["core.spherizeUV"] = D("Spherize UVs","Coordinates","Stretch UVs outward with a rounded spherical warp centered at 0.5.","uv:vector2","uv:vector2",false,new JObject{{"strength",1}}),
            ["core.pinchUV"] = D("Pinch UVs","Coordinates","Pinch or expand UVs near their center. Negative strength expands.","uv:vector2","uv:vector2",false,new JObject{{"strength",.5},{"radius",.5}}),
            ["core.barrelUV"] = D("Barrel Distortion","Coordinates","Radial lens distortion; negative strength produces a pincushion effect.","uv:vector2","uv:vector2",false,new JObject{{"strength",.5}}),
            ["core.chromaticTexture"] = D("Chromatic Texture","Textures","Sample red and blue at opposite UV offsets for color-fringe distortion. Green and alpha stay centered.","uv:vector2","color:color",true,new JObject{{"resourceId",""},{"strength",.005}}),
            ["core.normalBlend"] = D("Blend Normals","Surface","Combine two tangent-space normals using whiteout blending.","a:vector3,b:vector3","normal:vector3",false,new JObject()),
            ["core.normalStrength"] = D("Normal Strength","Surface","Adjust tangent-space normal strength. Zero flattens the normal; negative values invert bumps.","normal:vector3,strength:float","normal:vector3",false,new JObject{{"strength",1}}),
            ["core.normalFromHeight"] = D("Normal from Height","Surface","Turn a height signal into a tangent-space bump normal using screen derivatives and mesh UV0. Pixel shading only.","height:float","normal:vector3",false,new JObject{{"strength",1}}),
            ["core.reflectionDirection"] = D("Reflection Direction","Inputs","World-space mirror reflection of the camera direction around a surface normal.","normal:vector3","direction:vector3",false,new JObject()),
            ["core.objectScale"] = D("Object Scale","Inputs","Lengths of the object transform axes. Negative scale signs are not preserved.","","scale:vector3",false,new JObject()),
            ["core.objectOrigin"] = D("Object Origin","Inputs","The object origin in world space, constant across the mesh.","","position:vector3",false,new JObject()),
            ["core.objectRandom"] = D("Object Random","Inputs","Deterministic random value based on object origin and a seed. Moving the object changes the value.","","value:float",false,new JObject{{"seed",0}}),
            ["core.distanceToPoint"] = D("Distance to Point","Inputs","Distance to a chosen point. Uses object-space mesh position unless Position is connected.","position:vector3","value:float",false,new JObject{{"x",0},{"y",0},{"z",0}}),
            ["core.sphereMask"] = D("Sphere Volume Mask","Textures","A soft spherical mask in object space, or the space of connected Position.","position:vector3","value:float",false,new JObject{{"x",0},{"y",0},{"z",0},{"radius",.5},{"softness",.05}}),
            ["core.boxVolumeMask"] = D("Box Volume Mask","Textures","A soft three-dimensional box mask. Width, height and depth are full extents.","position:vector3","value:float",false,new JObject{{"x",0},{"y",0},{"z",0},{"width",1},{"height",1},{"depth",1},{"softness",.05}}),
            ["core.capsuleMask"] = D("Capsule Volume Mask","Textures","A vertical capsule-shaped mask in object space, useful for local effect regions.","position:vector3","value:float",false,new JObject{{"x",0},{"y",0},{"z",0},{"height",1},{"radius",.2},{"softness",.05}}),
            ["core.stripes3D"] = D("Volume Stripes","Textures","Repeating bands along X, Y or Z in object space. Width is the filled fraction of each band.","position:vector3","value:float",false,new JObject{{"scale",10},{"axis",1},{"width",.5}}),
            ["core.snowMask"] = D("Snow Coverage","Textures","Upward-facing world surfaces collect a noisy snow mask. Connect matching world-space Position and Normal.","normal:vector3,position:vector3","value:float",false,new JObject{{"coverage",.5},{"breakup",.3},{"scale",10}}),
            ["core.wetnessColor"] = D("Wet Color","Color","Darken RGB under a wetness mask while preserving alpha. Pair with lower PBR roughness for a wet material.","color:color,mask:float","color:color",false,new JObject{{"strength",.5},{"mask",1}}),
            ["core.anisotropicHighlight"] = D("Anisotropic Highlight","Surface","Directional strand highlight using world-space normal and tangent. Defaults to mesh directions and the main light.","normal:vector3,tangent:vector3,color:color,roughness:float","color:color",false,new JObject{{"roughness",.3}})
            , ["core.tessellation"] = D("Tessellation","Surface","PC GPU tessellation for controlled displacement. Connect a surface to Base and this node to Output. Height displacement needs bounds and can show UV seams.","base:surface,height:float","surface:surface",false,new JObject{{"factor",8},{"minFactor",1},{"nearDistance",2},{"farDistance",15},{"strength",.1},{"reference",.5},{"smoothing",0},{"height",.5}})
            , ["core.iridescence"] = D("Iridescence","Color","Thin-film-style view-angle color shift. Artistic approximation driven by surface normal and film thickness.","color:color,normal:vector3,thickness:float","color:color",false,new JObject{{"thickness",.5},{"strength",1},{"phase",0}})
            , ["core.refraction"] = D("Refraction","Color","Bend captured screen pixels with a world-space normal and index of refraction. Does not trace scene geometry.","color:color,normal:vector3,strength:float,ior:float","color:color",false,new JObject{{"strength",.05},{"ior",1.33}})
            , ["core.interiorMapping"] = D("Interior Mapping","Color","Ray-box room mapping into a texture atlas. Uses tangent view direction, room UV grid and depth; no interior geometry is created.","uv:vector2,view:vector3,depth:float","color:color",true,new JObject{{"resourceId",""},{"roomsX",4},{"roomsY",4},{"depth",1}})
            , ["core.textureBomb"] = D("Texture Bomb","Textures","Deterministic randomized cell texture offsets and rotations with soft cell blending to hide seams.","uv:vector2,blend:float","color:color",true,new JObject{{"resourceId",""},{"cells",4},{"blend",1},{"seed",0},{"rotation",1}})
            , ["core.subsurface"] = D("Subsurface","Color","Wrapped backlight scattering approximation from main light, thickness and tint. Artistic, view-independent surface response.","color:color,normal:vector3,thickness:float,tint:color","color:color",false,new JObject{{"thickness",.5},{"strength",.7},{"tint",new JArray(1,.35,.2,1)}})
        };
        static FeatureNode D(string title,string category,string description,string inputs,string outputs,bool texture,JObject defaults) { return new FeatureNode(title,category,description,inputs,outputs,texture,defaults); }
        public static IEnumerable<string> All { get { return Items.Keys; } }
        public static bool IsKnown(string op) { return op != null && Items.ContainsKey(op); }
        public static bool TryGet(string op,out FeatureNode node) { return Items.TryGetValue(op,out node); }
        public static string[] Ports(string op,bool output) { if(!Items.TryGetValue(op,out var n)) return new string[0]; return new List<string>(output?n.Outputs.Keys:n.Inputs.Keys).ToArray(); }
        public static string PortType(string op,string port,bool output) { if(!Items.TryGetValue(op,out var n)) return null; var map=output?n.Outputs:n.Inputs; return map.TryGetValue(port,out var type)?type:null; }
        public static GraphNode Create(string op) { if(!Items.TryGetValue(op,out var n)) return null; var node=new GraphNode { Id=Guid.NewGuid().ToString("N"),Operation=op,Properties=(JObject)n.Defaults.DeepClone() }; if(n.Texture && node.Properties["resourceId"]==null) node.Properties["resourceId"]=""; return node; }
        public static IEnumerable<string> Numeric(string op) { if(!Items.TryGetValue(op,out var n)) yield break; foreach(var p in n.Defaults.Properties()) if(p.Value.Type==JTokenType.Integer||p.Value.Type==JTokenType.Float) yield return p.Name; }
        public static bool NeedsResource(string op) { return Items.TryGetValue(op,out var n) && n.Texture; }
    }
}
