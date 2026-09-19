using System;
using System.Linq;
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
