using System.Collections.Generic;
using System;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Portable definitions for graph operations that depend on rasterized face orientation.</summary>
    public static class FaceNodes
    {
        public const string FrontFace = "core.frontFace";
        static readonly string[] OutputPorts = { "isFront", "normalWorld" };
        static readonly FeatureNode Definition = new FeatureNode(
            "Front Face", "Inputs",
            "Read whether this fragment faces the camera. Is Front is 1 on front faces and 0 on back faces; connect it to Mix to choose front/back colors or UVs. Normal World optionally flips the geometric world normal on back faces. Requires both faces to be rendered (Output → Visible faces → Both).",
            "", "isFront:float,normalWorld:vector3", false,
            new JObject { ["flipBackfaceNormal"] = 1 });

        public static IEnumerable<string> All { get { yield return FrontFace; } }
        internal static void Register(Dictionary<string, FeatureNode> nodes) { nodes.Add(FrontFace, Definition); }
        public static bool IsKnown(string operation) { return operation == FrontFace; }
        public static string[] Ports(string operation, bool output) { return IsKnown(operation) ? output ? (string[])OutputPorts.Clone() : new string[0] : new string[0]; }
        public static string PortType(string operation, string port, bool output)
        {
            if (!IsKnown(operation) || !output) return null;
            return port == "isFront" ? "float" : port == "normalWorld" ? "vector3" : null;
        }
        public static string Title(string operation) { return IsKnown(operation) ? Definition.Title : null; }
        public static string Description(string operation) { return IsKnown(operation) ? Definition.Description : null; }
        public static GraphNode Create(string operation)
        {
            return IsKnown(operation)
                ? new GraphNode { Id = Guid.NewGuid().ToString("N"), Operation = operation, Properties = (JObject)Definition.Defaults.DeepClone() }
                : null;
        }

        /// <summary>Checks finite bounded numeric controls without embedding imported text in generated code.</summary>
        public static bool IsFiniteInRange(JToken value, double minimum, double maximum)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return false;
            var number = (double)value;
            return !double.IsNaN(number) && !double.IsInfinity(number) && number >= minimum && number <= maximum;
        }
    }
}
