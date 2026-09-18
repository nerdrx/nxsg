using System;
using System.Linq;
using NXSG.Core;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        // Adds beginner-facing controls for visual and coordinate nodes.
        // The caller may still run its ordinary switch; handled nodes have no duplicate cases there.
        bool AddVisualControls(GraphNode node)
        {
            switch (node.Operation)
            {
                case "core.position":
                    AddIndexedChoice(node, "space", "Position space", new[] { "Object space", "World space" }, 0, 0);
                    return true;
                case "core.normalDirection":
                    AddIndexedChoice(node, "space", "Normal space", new[] { "Object space", "World space" }, 1, 0);
                    return true;
                case "core.viewDirection":
                case "core.vertexColor":
                case "core.cameraDistance":
                case "core.screenUV":
                    return true;

                case "core.circleMask":
                    AddCoordinateChoice(node);
                    AddBoundedNumber(node, "radius", "Radius", 0, 1, .4f);
                    AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .02f);
                    return true;
                case "core.boxMask":
                    AddCoordinateChoice(node);
                    AddBoundedNumber(node, "width", "Width", 0, 1, .7f);
                    AddBoundedNumber(node, "height", "Height", 0, 1, .7f);
                    AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .02f);
                    return true;
                case "core.polygonMask":
                    AddCoordinateChoice(node);
                    AddIndexedChoice(node, "sides", "Sides", Enumerable.Range(3, 30).Select(value => value.ToString()).ToArray(), 6, 3);
                    AddBoundedNumber(node, "radius", "Radius", 0, 1, .4f);
                    AddNumber(node, "rotation", "Rotation (degrees)", 0);
                    AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .02f);
                    return true;
                case "core.starMask":
                    AddCoordinateChoice(node);
                    AddIndexedChoice(node, "points", "Points", Enumerable.Range(3, 30).Select(value => value.ToString()).ToArray(), 5, 3);
                    AddBoundedNumber(node, "inner", "Inner radius", 0, 1, .2f);
                    AddBoundedNumber(node, "outer", "Outer radius", 0, 1, .45f);
                    AddNumber(node, "rotation", "Rotation (degrees)", 0);
                    AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .02f);
                    return true;
                case "core.radialRays":
                    AddCoordinateChoice(node);
                    AddIndexedChoice(node, "count", "Ray count", Enumerable.Range(1, 128).Select(value => value.ToString()).ToArray(), 12, 1);
                    AddNumber(node, "rotation", "Rotation (degrees)", 0);
                    AddBoundedNumber(node, "softness", "Edge softness", 0, 1, .02f);
                    return true;
                case "core.spiral":
                    AddCoordinateChoice(node);
                    AddNumber(node, "turns", "Turns", 3);
                    AddBoundedNumber(node, "width", "Line width", 0, 1, .2f);
                    AddNumber(node, "rotation", "Rotation (degrees)", 0);
                    return true;
                case "core.brick":
                    AddCoordinateChoice(node);
                    AddNumber(node, "tilingX", "Horizontal tiles", 5);
                    AddNumber(node, "tilingY", "Vertical tiles", 8);
                    AddBoundedNumber(node, "mortar", "Mortar width", 0, 1, .08f);
                    return true;
                case "core.hexGrid":
                    AddCoordinateChoice(node);
                    AddNumber(node, "scale", "Grid scale", 8);
                    AddBoundedNumber(node, "width", "Line width", 0, 1, .05f);
                    return true;

                case "core.triplanarTexture":
                    AddTexturePicker(node, "Texture");
                    AddNumber(node, "scale", "Texture scale", 1);
                    AddNumber(node, "sharpness", "Blend sharpness", 4);
                    return true;
                case "core.matcapTexture":
                    AddTexturePicker(node, "Matcap texture");
                    return true;
                case "core.rimGlow":
                    AddNumber(node, "power", "Power", 3, "power");
                    return true;
                case "core.heightMask":
                    AddNumber(node, "low", "Low height", 0);
                    AddNumber(node, "high", "High height", 1);
                    AddIndexedChoice(node, "axis", "Height axis", new[] { "X", "Y", "Z" }, 1, 0);
                    return true;
                case "core.slopeMask":
                    AddNumber(node, "low", "Low slope", 0);
                    AddNumber(node, "high", "High slope", 1);
                    return true;
                case "core.distanceFade":
                    AddNumber(node, "near", "Near distance", 0);
                    AddNumber(node, "far", "Far distance", 10);
                    return true;
                case "core.wireframe":
                    AddNumber(node, "width", "Line width", 1);
                    AddNumber(node, "softness", "Edge softness", 1);
                    return true;
                default:
                    return false;
            }
        }
    }
}
