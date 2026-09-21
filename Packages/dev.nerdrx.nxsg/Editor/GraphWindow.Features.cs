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
            switch (node.Operation)
            {
                case "core.fur": AddFurControls(node); return true;
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
                case "core.anisotropicHighlight": AddBoundedNumber(node, "roughness", "Highlight roughness", 0, 1, .3f, "roughness"); return true;
                case "core.iridescence": AddBoundedNumber(node, "thickness", "Film thickness", 0, 1, .5f, "thickness"); AddBoundedNumber(node, "strength", "Color strength", 0, 2, 1); AddNumber(node, "phase", "Phase", 0); return true;
                case "core.refraction": AddBoundedNumber(node, "strength", "Refraction strength", 0, 1, .05f, "strength"); AddBoundedNumber(node, "ior", "Index of refraction", 1, 4, 1.33f, "ior"); FeatureNote("Uses a screen GrabPass. Refraction bends the captured screen and does not trace scene geometry."); return true;
                case "core.interiorMapping": AddCoordinateChoice(node); AddTexturePicker(node, "Room atlas"); AddIntegerField(node, "roomsX", "Rooms across", 1, 32, 4); AddIntegerField(node, "roomsY", "Rooms down", 1, 32, 4); AddNumber(node, "depth", "Room depth", 1, "depth"); FeatureNote("Tangent view ray enters a box room and samples a UV atlas. No interior geometry is created."); return true;
                case "core.textureBomb": AddCoordinateChoice(node); AddTexturePicker(node, "Texture"); AddIntegerField(node, "cells", "Cells", 1, 32, 4); AddBoundedNumber(node, "blend", "Cell blend", 0, 1, 1, "blend"); AddNumber(node, "seed", "Seed", 0); AddBoundedNumber(node, "rotation", "Rotation", 0, 1, 1); FeatureNote("Cell transforms are deterministic. Soft edge blending reduces seams."); return true;
                case "core.subsurface": AddBoundedNumber(node, "thickness", "Thickness", 0, 1, .5f, "thickness"); AddBoundedNumber(node, "strength", "Scatter strength", 0, 2, .7f); AddColorField(node, "tint", "Scatter tint", new Color(1f, .35f, .2f, 1f), "tint"); FeatureNote("Wrapped and backlight terms approximate shallow scattering from the main light."); return true;
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

        void AddFurControls(GraphNode node)
        {
            var fins = new Toggle("Fur fins") { value = (int?)node.Properties["fins"] == 1, tooltip = "Add grazing edge strips to fill the fur silhouette. Extra geometry pass; triangle edges are approximated." };
            fins.RegisterValueChangedCallback(e => Edit("Toggle fur fins", () => node.Properties["fins"] = e.newValue ? 1 : 0));
            inspector.Add(fins);
            AddBoundedNumber(node, "finOpacity", "Fin opacity", 0, 1, .7f);

            FurSection("Shells", () =>
            {
                AddIntegerField(node, "layers", "Shell layers", 4, 32, 16);
                AddNumber(node, "length", "Fur length (m)", .04f, "length");
                AddNumber(node, "density", "Strands / m²", 100, "density");
                AddBoundedNumber(node, "thickness", "Strand thickness", 0, 1, .35f, "thickness");
                AddBoundedNumber(node, "taper", "Tip taper", 0, 1, 1);
            });
            FurSection("Strands", () =>
            {
                AddNumber(node, "gravity", "Gravity", .1f);
                AddBoundedNumber(node, "rimStrength", "Rim strength", 0, 2, .25f);
                AddIntegerField(node, "minLayers", "Minimum LOD layers", 1, 32, 4);
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
                FeatureNote("Samples through local fur volume using the main light only. Ambient and rim lighting stay unchanged. Cost steps multiply by shell count.");
            });
            FurSection("Distance LOD", () =>
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
