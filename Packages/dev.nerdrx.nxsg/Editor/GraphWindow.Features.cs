using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        bool AddFeatureControls(GraphNode node)
        {
            if (AddRenderingFeatureControls(node) || AddSpatialControls(node) || AddUtilityControls(node)) return true;
            if(node.Operation == "core.frontFace") { AddFrontFaceControls(node); return true; }
            switch (node.Operation)
            {
                case "core.layeredPbrSurface": AddLayeredSurfaceControls(node); return true;
                case "core.fur": AddFurControls(node); return true;
                case "core.depthBulge":
                    AddNumber(node, "height", "Bulge height", -.03f, "height", "Signed object units: negative presses inward, positive pushes outward.");
                    AddNumber(node, "distance", "Touch distance (m)", .1f, "distance", "Camera depth separation in metres. Zero or negative disables the effect.");
                    AddNumber(node, "falloff", "Falloff", 1, "falloff", "Higher values concentrate the deformation near the touching depth.");
                    AddNumber(node, "bias", "Self-depth bias (m)", .002f, "bias", "Ignores almost equal depths. Increase slightly if the untouched mesh ripples; too much suppresses small contacts.");
                    AddBoundedNumber(node, "mask", "Mask", 0, 1, 1, "mask");
                    FeatureNote("Height uses signed object units: negative dents, positive bulges. Distance and bias use metres; falloff shapes the effect; mask is 0–1. Touch outputs proximity weight. Connect Displacement to surface Displacement. Needs a camera depth texture and enough mesh vertices. Skipped in mirrors and shadow/depth passes; not physics or contact detection.");
                    return true;
                case "core.sdfFaceShadow":
                    AddIndexedChoice(node, "basis", "Basis", new[] { "Object axes", "Custom head axes" });
                    AddNumber(node, "sdfLeft", "Left SDF sample", .5f, "sdfLeft");
                    AddNumber(node, "sdfRight", "Right SDF sample", .5f, "sdfRight");
                    AddDirectionField(node, "headRight", "Head right", new Vector3(1, 0, 0));
                    AddDirectionField(node, "headForward", "Head forward", new Vector3(0, 0, 1));
                    AddNumber(node, "threshold", "SDF threshold", .5f, "threshold");
                    AddBoundedNumber(node, "softness", "Shadow softness", 0, 1, .04f, "softness");
                    AddBoundedNumber(node, "strength", "Mask strength", 0, 1, 1, "strength");
                    AddBoundedNumber(node, "angleStrength", "Angle threshold shift", 0, 1, .3f, "angleStrength");
                    AddNumber(node, "offset", "User offset", 0, "offset");
                    FeatureNote("Connect two mirrored SDF texture samples as scalar values. Light angle shifts the threshold continuously. Custom head axes come from the graph; no bone is tracked.");
                    return true;
                case "core.depthRim":
                    AddBoundedNumber(node, "width", "Width (pixels)", 0, 8, 2, "width");
                    AddBoundedNumber(node, "softness", "Depth softness (m)", .001f, .5f, .02f, "softness");
                    AddBoundedNumber(node, "bias", "Depth bias (m)", 0, .5f, .01f, "bias");
                    AddBoundedNumber(node, "strength", "Strength", 0, 1, 1, "strength");
                    FeatureNote("Camera-depth edge mask. Missing depth and oblique mirror projections return zero; this is not a mesh outline.");
                    return true;
                case "core.gem":
                    AddColorField(node, "color", "Gem tint", Color.white, "color");
                    AddBoundedNumber(node, "ior", "Index of refraction", 1, 4, 1.5f, "ior");
                    AddBoundedNumber(node, "refraction", "Screen refraction", 0, .5f, .06f, "refraction");
                    AddBoundedNumber(node, "reflection", "Probe reflection", 0, 1, .8f, "reflection");
                    AddBoundedNumber(node, "dispersion", "Chromatic dispersion", 0, .1f, .015f, "dispersion");
                    AddBoundedNumber(node, "roughness", "Reflection roughness", 0, 1, .15f, "roughness");
                    AddColorField(node, "sparkleColor", "Interior sparkle color", Color.white, "sparkleColor");
                    AddBoundedNumber(node, "sparkleStrength", "Interior sparkle strength", 0, 8, 0, "sparkleStrength");
                    AddBoundedNumber(node, "sparkleDensity", "Interior sparkle density", 0, 1, .35f, "sparkleDensity");
                    AddBoundedNumber(node, "sparkleSize", "Interior sparkle size", 0, .5f, .2f, "sparkleSize");
                    AddBoundedNumber(node, "sparkleDepth", "Interior path depth", 0, 2, .35f, "sparkleDepth");
                    FeatureNote("Screen refraction and first-probe reflection remain approximations. Optional sparkles sample four points along the refracted object-space view path; they do not trace geometry or internal bounces.");
                    return true;
                case "core.parallaxUV": AddCoordinateChoice(node); AddNumber(node, "height", "Height", .5f, "height"); AddNumber(node, "strength", "Depth strength", .05f); AddNumber(node, "reference", "Reference height", .5f); return true;
                case "core.avatarMotion": FeatureNote("Create an FX motion driver from the Create menu, then merge its layers into your avatar FX controller. Reads locomotion, not individual bones. Matching properties on other materials on this renderer are animated too.");return true;
                case "core.motionResponse": AddNumber(node,"startSpeed","Start speed (m/s)",.1f);AddNumber(node,"fullSpeed","Full speed (m/s)",4);AddNumber(node,"curve","Response curve",1);return true;
                case "core.motionSway": AddNumber(node,"strength","Maximum displacement",.02f);AddNumber(node,"frequency","Frequency (Hz)",2);AddNumber(node,"spatialScale","Spatial scale",3);AddNumber(node,"fullSpeed","Full speed (m/s)",4);FeatureNote("Connect Speed and send Value to a surface Displacement input. Mask zero or speed zero removes motion. Expand renderer bounds for large displacement.");return true;
                case "core.motionStretchUV": AddNumber(node,"strength","Stretch per m/s",.25f);AddNumber(node,"maxStretch","Maximum stretch",3);AddIndexedChoice(node,"axis","Axis",new[]{"Horizontal","Vertical"});return true;
                case "core.parallaxOcclusion": AddCoordinateChoice(node); AddTexturePicker(node, "Height texture"); AddNumber(node, "strength", "Depth strength", .05f); AddIntegerField(node, "steps", "Ray steps", 4, 64, 16); FeatureNote("Needs mesh tangents. Does not change silhouette or cast displaced shadows."); return true;
                case "core.furMask": AddCoordinateChoice(node); AddNumber(node, "density", "Strand density", 100, "density"); AddBoundedNumber(node, "thickness", "Strand thickness", 0, 1, .35f, "thickness"); AddBoundedNumber(node, "height", "Strand height", 0, 1, 0, "height"); AddBoundedNumber(node, "taper", "Tip taper", 0, 1, 1); return true;
                case "core.flowMapUV": AddCoordinateChoice(node); AddNumber(node, "strength", "Flow strength", .1f); AddNumber(node, "speed", "Flow speed", 1); return true;
                case "core.ditherMask": AddCoordinateChoice(node); AddBoundedNumber(node, "value", "Mask value", 0, 1, .5f, "value"); AddNumber(node, "scale", "Pattern scale", 64); return true;
                case "core.truchet": AddCoordinateChoice(node); AddNumber(node, "scale", "Tile scale", 8); AddBoundedNumber(node, "width", "Path width", 0, 1, .08f); AddNumber(node, "seed", "Seed", 0); return true;
                case "core.weave": AddCoordinateChoice(node); AddNumber(node, "scale", "Thread scale", 30); AddBoundedNumber(node, "width", "Thread width", 0, 1, .75f); return true;
                case "core.scales": AddCoordinateChoice(node); AddNumber(node, "scale", "Scale count", 12); AddBoundedNumber(node, "width", "Outline width", 0, 1, .06f); return true;
                case "core.dots": AddCoordinateChoice(node); AddNumber(node, "scale", "Dot count", 10); AddBoundedNumber(node, "radius", "Dot radius", 0, .5f, .25f); return true;
                case "core.scratches": AddCoordinateChoice(node); AddNumber(node, "scale", "Scratch count", 30); AddBoundedNumber(node, "width", "Scratch width", 0, 1, .025f); AddBoundedNumber(node, "length", "Scratch length", 0, 1, .7f); AddNumber(node, "seed", "Seed", 0); return true;
                case "core.cracks": AddCoordinateChoice(node); AddNumber(node, "scale", "Cell scale", 8); AddBoundedNumber(node, "width", "Crack width", 0, 1, .04f); return true;
                case "core.woodRings": AddCoordinateChoice(node); AddNumber(node, "scale", "Ring scale", 12); AddBoundedNumber(node, "distortion", "Distortion", 0, 1, .3f); return true;
                case "core.marble": AddCoordinateChoice(node); AddNumber(node, "scale", "Vein scale", 5); AddNumber(node, "distortion", "Distortion", 3); return true;
                case "core.clouds": AddCoordinateChoice(node); AddNumber(node, "scale", "Cloud scale", 4); AddNumber(node, "speed", "Drift speed", .1f); AddNumber(node, "contrast", "Contrast", 1); return true;
                case "core.sparkleMask": AddCoordinateChoice(node); AddNumber(node, "scale", "Sparkle scale", 30); AddNumber(node, "speed", "Blink speed", 2); AddBoundedNumber(node, "density", "Sparkle density", 0, 1, .2f); AddBoundedNumber(node, "size", "Sparkle size", 0, 1, .08f); return true;
                case "core.glitter":
                    AddCoordinateChoice(node);
                    AddNumber(node, "scale", "Flake scale", 60);
                    AddBoundedNumber(node, "density", "Flake density", 0, 1, .6f);
                    AddBoundedNumber(node, "size", "Flake size", 0, 1, .16f);
                    AddBoundedNumber(node, "sharpness", "Sparkle sharpness", 1, 512, 32);
                    AddBoundedNumber(node, "viewStrength", "View angle strength", 0, 1, 1);
                    AddNumber(node, "speed", "Sparkle speed", 1);
                    AddBoundedNumber(node, "twinkle", "Twinkle amount", 0, 1, .3f);
                    AddBoundedNumber(node, "brightness", "HDR brightness", 0, 10, 2);
                    AddNumber(node, "seed", "Flake seed", 0);
                    AddBoundedNumber(node, "mask", "Flake mask", 0, 1, 1, "mask");
                    AddColorField(node, "color", "Flake color", Color.white, "color");
                    FeatureNote("Stable surface UVs place flakes. View angle and time drive sparkle. Mask is 0–1. Brightness changes HDR color only.");
                    return true;
                case "core.scanlines": AddCoordinateChoice(node); AddNumber(node, "scale", "Line count", 100); AddNumber(node, "speed", "Scroll speed", .2f); AddBoundedNumber(node, "width", "Line width", 0, 1, .3f); return true;
                case "core.glitchUV": AddCoordinateChoice(node); AddNumber(node, "strength", "Glitch strength", .05f); AddNumber(node, "speed", "Glitch speed", 5); AddIntegerField(node, "rows", "Rows", 1, 256, 20); return true;
                case "core.pixelateUV": AddCoordinateChoice(node); AddIntegerField(node, "cells", "Cells", 1, 256, 64); return true;
                case "core.kaleidoscopeUV": AddCoordinateChoice(node); AddIntegerField(node, "segments", "Segments", 1, 64, 6); AddNumber(node, "rotation", "Rotation (degrees)", 0); return true;
                case "core.swapUV": AddCoordinateChoice(node); return true;
                case "core.spherizeUV": AddCoordinateChoice(node); AddNumber(node, "strength", "Spherize strength", 1); return true;
                case "core.pinchUV": AddCoordinateChoice(node); AddNumber(node, "strength", "Pinch strength", .5f); AddBoundedNumber(node, "radius", "Radius", 0, 1, .5f); return true;
                case "core.barrelUV": AddCoordinateChoice(node); AddNumber(node, "strength", "Distortion strength", .5f); return true;
                case "core.chromaticTexture": AddCoordinateChoice(node); AddTexturePicker(node, "Texture"); AddNumber(node, "strength", "Fringe strength", .005f); return true;
                case "core.normalBlend": case "core.reflectionDirection": case "core.objectScale": case "core.objectOrigin": return true;
                case "core.normalStrength": AddNumber(node, "strength", "Normal strength", 1, "strength"); return true;
                case "core.normalFromHeight": AddNumber(node, "strength", "Normal strength", 1); return true;
                case "core.objectRandom": AddNumber(node, "seed", "Seed", 0); return true;
                case "core.distanceToPoint": AddNumber(node, "x", "Point X", 0, "position"); AddNumber(node, "y", "Point Y", 0, "position"); AddNumber(node, "z", "Point Z", 0, "position"); return true;
                case "core.sphereMask": AddNumber(node, "x", "Center X", 0, "position"); AddNumber(node, "y", "Center Y", 0, "position"); AddNumber(node, "z", "Center Z", 0, "position"); AddBoundedNumber(node, "radius", "Radius", 0, 10, .5f); AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .05f); return true;
                case "core.boxVolumeMask": AddNumber(node, "x", "Center X", 0, "position"); AddNumber(node, "y", "Center Y", 0, "position"); AddNumber(node, "z", "Center Z", 0, "position"); AddNumber(node, "width", "Width", 1); AddNumber(node, "height", "Height", 1); AddNumber(node, "depth", "Depth", 1); AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .05f); return true;
                case "core.capsuleMask": AddNumber(node, "x", "Center X", 0, "position"); AddNumber(node, "y", "Center Y", 0, "position"); AddNumber(node, "z", "Center Z", 0, "position"); AddNumber(node, "height", "Height", 1); AddNumber(node, "radius", "Radius", .2f); AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .05f); return true;
                case "core.stripes3D": AddNumber(node, "scale", "Stripe scale", 10); AddIndexedChoice(node, "axis", "Stripe axis", new[] { "X", "Y", "Z" }, 1, 0); AddBoundedNumber(node, "width", "Stripe width", 0, 1, .5f); return true;
                case "core.snowMask": AddBoundedNumber(node, "coverage", "Snow coverage", 0, 1, .5f); AddBoundedNumber(node, "breakup", "Surface breakup", 0, 1, .3f); AddNumber(node, "scale", "Breakup scale", 10); return true;
                case "core.wetnessColor": AddBoundedNumber(node, "strength", "Wet strength", 0, 1, .5f); AddBoundedNumber(node, "mask", "Wetness mask", 0, 1, 1, "mask"); return true;
                case "core.anisotropicHighlight":
                    AddBoundedNumber(node, "roughness", "Primary roughness", 0, 1, .3f, "roughness");
                    AddBoundedNumber(node, "dualLobe", "Second lobe", 0, 1, 0, "dualLobe");
                    AddBoundedNumber(node, "secondaryRoughness", "Second roughness", 0, 1, .6f, "secondaryRoughness");
                    AddColorField(node, "secondaryTint", "Second tint", Color.white, "secondaryTint");
                    AddBoundedNumber(node, "shift", "Primary tangent shift", -1, 1, 0, "shift");
                    AddBoundedNumber(node, "secondaryShift", "Second lobe shift", -1, 1, 0, "secondaryShift");
                    AddBoundedNumber(node, "tangentStrength", "Tangent strength", 0, 1, 1, "tangentStrength");
                    AddBoundedNumber(node, "shiftNoise", "Primary shift noise", -1, 1, 0, "shiftNoise");
                    AddBoundedNumber(node, "secondaryShiftNoise", "Second shift noise", -1, 1, 0, "secondaryShiftNoise");
                    AddNumber(node, "longitudinalWidth", "Longitudinal width", 1, "longitudinalWidth", "Width scale; 1 preserves current lobe.");
                    AddNumber(node, "azimuthalWidth", "Azimuthal width", 1, "azimuthalWidth", "Width scale; 1 uses full width; values below 1 narrow around the strand.");
                    AddBoundedNumber(node, "reflectionStrength", "Probe reflection", 0, 1, 0, "reflectionStrength");
                    AddNumber(node, "reflectionStretch", "Probe stretch", 0, "reflectionStretch");
                    AddBoundedNumber(node, "reflectionRoughness", "Probe roughness", 0, 1, .3f, "reflectionRoughness");
                    return true;
                case "core.iridescence": AddBoundedNumber(node, "thickness", "Film thickness", 0, 1, .5f, "thickness"); AddBoundedNumber(node, "strength", "Color strength", 0, 2, 1); AddNumber(node, "phase", "Phase", 0); return true;
                case "core.refraction": AddBoundedNumber(node, "strength", "Refraction strength", 0, 1, .05f, "strength"); AddBoundedNumber(node, "ior", "Index of refraction", 1, 4, 1.33f, "ior"); FeatureNote("Uses a screen GrabPass. Refraction bends the captured screen and does not trace scene geometry."); return true;
                case "core.interiorMapping": AddCoordinateChoice(node); AddTexturePicker(node, "Room atlas"); AddIntegerField(node, "roomsX", "Rooms across", 1, 32, 4); AddIntegerField(node, "roomsY", "Rooms down", 1, 32, 4); AddNumber(node, "depth", "Room depth", 1, "depth"); FeatureNote("Tangent view ray enters a box room and samples a UV atlas. No interior geometry is created."); return true;
                case "core.textureBomb": AddCoordinateChoice(node); AddTexturePicker(node, "Texture"); AddIntegerField(node, "cells", "Cells", 1, 32, 4); AddBoundedNumber(node, "blend", "Cell blend", 0, 1, 1, "blend"); AddNumber(node, "seed", "Seed", 0); AddBoundedNumber(node, "rotation", "Rotation", 0, 1, 1); FeatureNote("Cell transforms are deterministic. Soft edge blending reduces seams."); return true;
                case "core.subsurface":
                    AddBoundedNumber(node, "thickness", "Thickness", 0, 1, .5f, "thickness");
                    AddBoundedNumber(node, "strength", "Scatter strength", 0, 2, .7f, "strength");
                    AddColorField(node, "tint", "Scatter tint", new Color(1f, .35f, .2f, 1f), "tint");
                    AddBoundedNumber(node, "spread", "Additional spread", 0, 2, 0, "spread");
                    AddBoundedNumber(node, "distortion", "Light distortion", -1, 1, 0, "distortion");
                    AddBoundedNumber(node, "viewResponse", "View response", 0, 1, 0, "viewResponse");
                    AddBoundedNumber(node, "attenuation", "Thickness attenuation", 0, 1, 0, "attenuation");
                    AddBoundedNumber(node, "shadowResponse", "Scene shadow response", 0, 1, 0, "shadowResponse");
                    FeatureNote("Additional spread widens the wrapped light without changing thickness. Distortion bends the scattering light toward the normal. Scene shadow response affects the added scattering only; it needs a shadow-casting scene light. These controls default to 0 to preserve existing materials."); return true;
                case "core.tessellation":
                    AddIntegerField(node, "factor", "Tessellation factor", 1, 63, 8);
                    AddIntegerField(node, "minFactor", "Minimum factor", 1, 63, 1);
                    AddNumber(node, "nearDistance", "Near distance", 2);
                    AddNumber(node, "farDistance", "Far distance", 15);
                    AddNumber(node, "height", "Height", .5f, "height");
                    AddNumber(node, "strength", "Displacement strength", .1f);
                    AddNumber(node, "reference", "Reference height", .5f);
                    AddBoundedNumber(node, "smoothing", "Smoothing", 0, 1, 0);
                    FeatureNote("PC GPU tessellation. Connect a surface to Base, then this node to Output. Height texture: Texture → Split Color R → Height. Expand renderer bounds for displacement; UV seams can remain visible.");
                    return true;
                default: return false;
            }
        }

        void AddLayeredSurfaceControls(GraphNode node)
        {
            AddInspectorSection("BASE SURFACE");
            AddAlbedoAlphaToggle(node);
            AddBoundedNumber(node, "opacity", "Opacity", 0, 1, 1, "opacity");
            AddNumber(node, "cutoff", "Cutoff", .001f);
            AddNumber(node, "displacement", "Displacement", 0, "displacement");
            AddBoundedNumber(node, "metallic", "Metallic", 0, 1, 0, "metallic");
            AddBoundedNumber(node, "roughness", "Roughness", 0, 1, .5f, "roughness");
            AddBoundedNumber(node, "specularAa", "Specular anti-aliasing", 0, 1, 0, "specularAa", "Uses fragment normal derivatives to soften roughness where the normal changes rapidly. Zero leaves roughness unchanged.");
            AddInspectorSection("CLEARCOAT");
            AddBoundedNumber(node, "coat", "Coat weight", 0, 1, 0, "coat", "Glossy dielectric coating. 0 disables the layer; 1 gives full coverage. Connected inputs remain dynamic.");
            AddBoundedNumber(node, "coatRoughness", "Coat roughness", 0, 1, .1f, "coatRoughness", "Independent coating roughness. Low values make a sharp reflection; high values soften it.");
            FeatureNote("Connect a Normal Map to Coat normal for coating detail. Unconnected uses the smooth mesh normal, independent of the base normal map.");
            AddInspectorSection("VELVET SHEEN");
            AddBoundedNumber(node, "sheen", "Sheen weight", 0, 1, 0, "sheen", "Soft fabric reflection, strongest at grazing angles. Zero removes the layer when unconnected.");
            AddColorInput(node, "sheenColor", "Sheen color", Color.white, "Color of the fabric sheen. Alpha is ignored; evaluated RGB stays between 0 and 1.");
            AddBoundedNumber(node, "sheenRoughness", "Sheen roughness", 0, 1, .5f, "sheenRoughness");
            FeatureNote("Layers share the PBR passes and respond to pixel lights. Clearcoat uses reflection probes; ambient sheen is an approximation. Zero unconnected weights compile out their layers.");
            AddInspectorSection("LIGHTING");
            AddLightingControls(node);
        }

        void AddLightingControls(GraphNode node)
        {
            AddBoundedNumber(node,"occlusion","Ambient occlusion",0,1,1,"occlusion","Visibility of ambient lighting and reflection probes. 1 is unoccluded; 0 blocks indirect light.");
            AddBoundedNumber(node,"shadow","Direct light visibility",0,1,1,"shadow","Multiplies direct-light attenuation. Connect Contact Shadows here. Emission remains unchanged.");
            AddIndirectLightingControls(node);
            inspector.Add(new Label("Lighting influence") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            AddNumber(node, "lightingMin", "Minimum brightness", 0);
            AddNumber(node, "lightingMax", "Maximum brightness", 0);
            AddNumber(node, "lightingSaturation", "Lighting saturation", 1);
            FeatureNote("Maximum 0 = unlimited. Saturation: 0 = neutral light, 1 = original, above 1 = stronger color. Affects lighting, not albedo or emission. Brightness limits apply per lighting contribution; multiple lights can add above the maximum.");
        }

        void AddAlbedoAlphaToggle(GraphNode node)
        {
            var field = new Toggle("Use albedo alpha")
            {
                value = ((int?)node.Properties["useAlbedoAlpha"] ?? 1) == 1,
                tooltip = "Use transparency from the Albedo color or texture. Off: only Opacity and material Tint alpha control transparency."
            };
            field.RegisterValueChangedCallback(evt => Edit("Toggle albedo alpha", () => node.Properties["useAlbedoAlpha"] = evt.newValue ? 1 : 0));
            inspector.Add(field);
        }

        void AddFurControls(GraphNode node)
        {
            var cardsOnly = (int?)node.Properties["cardsOnly"] == 1;
            AddIndexedChoice(node, "cardsOnly", "Fur geometry", new[] { "Shells", "Cards only" });
            if (cardsOnly) FeatureNote("Generate cards from mesh triangle edges. No shell layers. Shared edges can overlap; denser meshes produce more cards. Expand renderer bounds for fur length.");
            var fins = new Toggle("Fur fins") { value = (int?)node.Properties["fins"] == 1, tooltip = "Add grazing edge strips to fill the fur silhouette. Extra geometry pass; triangle edges are approximated." };
            fins.RegisterValueChangedCallback(e => Edit("Toggle fur fins", () => node.Properties["fins"] = e.newValue ? 1 : 0));
            if (!cardsOnly) inspector.Add(fins);
            AddBoundedNumber(node, "finOpacity", cardsOnly ? "Card opacity" : "Fin opacity", 0, 1, .7f);

            FurSection(cardsOnly ? "Cards" : "Shells", () =>
            {
                if (!cardsOnly) AddIntegerField(node, "layers", "Shell layers", 4, 32, 16);
                AddNumber(node, "length", "Fur length (m)", .04f, "length");
                AddNumber(node, "density", cardsOnly ? "Card coverage / strands (100 = all triangles)" : "Strands / m²", 100, "density");
                AddBoundedNumber(node, "thickness", "Strand thickness", 0, 1, .35f, "thickness");
                AddBoundedNumber(node, "taper", "Tip taper", 0, 1, 1);
            });
            FurSection("Strands", () =>
            {
                AddNumber(node, "gravity", "Gravity", .1f);
                AddBoundedNumber(node, "rimStrength", "Rim strength", 0, 2, .25f);
                if (!cardsOnly) AddIntegerField(node, "minLayers", "Minimum LOD layers", 1, 32, 4);
            });
            FurSection("Grooming & wind", () =>
            {
                AddBoundedNumber(node, "windStrength", "Wind strength", 0, 5, .1f);
                AddNumber(node, "windSpeed", "Wind speed", 1);
                AddNumber(node, "windScale", "Wind scale", 2);
                FeatureNote("Connect Groom to a 3D vector to control strand direction in object space.");
            });
            FurSection("Shadows", () =>
            {
                var receive = new Toggle("Receive scene shadows") { value = ((int?)node.Properties["receiveShadows"] ?? 1) == 1,
                    tooltip = "Receive shadows from the main directional light on fur shells and fins." };
                receive.RegisterValueChangedCallback(e => Edit("Toggle fur scene shadows", () => node.Properties["receiveShadows"] = e.newValue ? 1 : 0));
                inspector.Add(receive);
                AddIndexedChoice(node, "selfShadowQuality", "Self-shadow samples", new[] { "Off", "Low · 4", "Medium · 8", "High · 16" });
                AddBoundedNumber(node, "selfShadowStrength", "Self-shadow strength", 0, 4, 1);
                AddBoundedNumber(node, "selfShadowBias", "Self-shadow bias", 0, .25f, .03f);
                FeatureNote(cardsOnly ? "Approximates local fur volume, not exact card-to-card occlusion. Main light only." : "Samples through local fur volume using the main light only. Ambient and rim lighting stay unchanged. Cost steps multiply by shell count.");
            });
            if (!cardsOnly) FurSection("Distance LOD", () =>
            {
                AddNumber(node, "lodNear", "Full detail distance (m)", 5);
                AddNumber(node, "lodFar", "Fade out distance (m)", 15);
                FeatureNote("Fur adds shell draw calls. More layers increase mesh passes and cost.");
            });
        }

        void FurSection(string title, Action controls)
        {
            var section = new Foldout { text = title, value = true };
            inspector.Add(section);
            var previous = inspector;
            inspector = section;
            controls();
            inspector = previous;
        }

        void FeatureNote(string text)
        {
            inspector.Add(new Label(text) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 4 } });
        }

        void AddDirectionField(GraphNode node, string property, string label, Vector3 fallback)
        {
            var values = node.Properties[property] as JArray;
            var field = new Vector3Field(label) { value = values != null && values.Count == 3 ? new Vector3((float)values[0], (float)values[1], (float)values[2]) : fallback };
            field.SetEnabled(!graph.Connections.Any(edge => edge.To.NodeId == node.Id && edge.To.PortId == property));
            field.RegisterValueChangedCallback(evt => Edit("Change " + label, () => node.Properties[property] = new JArray(evt.newValue.x, evt.newValue.y, evt.newValue.z)));
            inspector.Add(field);
        }

        void AddColorField(GraphNode node, string property, string label, Color fallback, string inputPort = null)
        {
            var values = node.Properties[property] as JArray;
            var value = values != null && values.Count == 4 ? new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]) : fallback;
            var field = new ColorField(label) { value = value };
            if (inputPort != null) field.SetEnabled(!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == inputPort));
            field.RegisterValueChangedCallback(evt => Edit("Change " + label, () => node.Properties[property] = new JArray(evt.newValue.r, evt.newValue.g, evt.newValue.b, evt.newValue.a)));
            inspector.Add(field);
        }

        void AddIntegerField(GraphNode node, string property, string label, int min, int max, int fallback, string inputPort = null)
        {
            var field = new IntegerField(label) { value = Mathf.Clamp((int?)node.Properties[property] ?? fallback, min, max) };
            field.SetEnabled(inputPort == null || !graph.Connections.Any(edge => edge.To.NodeId == node.Id && edge.To.PortId == inputPort));
            field.tooltip = label + ": " + min + "–" + max + ".";
            field.RegisterValueChangedCallback(evt => Edit("Change " + label, () => node.Properties[property] = Mathf.Clamp(evt.newValue, min, max)));
            inspector.Add(field);
        }
    }
}
