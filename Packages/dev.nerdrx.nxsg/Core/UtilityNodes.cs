using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Definitions and validation for shader utilities owned by the utility backend slice.</summary>
    public static class UtilityNodes
    {
        public const string Clock = "core.clock";
        public const string MsdfDecal = "core.msdfDecal";
        public const string NumericText = "core.numericText";
        public const string ViewerStats = "core.viewerStats";

        public static void Register(IDictionary<string, FeatureNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            nodes[Clock] = new FeatureNode("Network Clock", "Animation",
                "Unity time by default. Select VRChat network milliseconds for observer-local synchronized animation. Its uint counter wraps about every 49.7 days; clients can still differ by network and frame timing. Outside VRChat the network global is zero, so Network mode freezes at zero.",
                "period:float,offset:float", "seconds:float,phase:float,cycle:float", false,
                new JObject { ["source"] = 0, ["period"] = 1.0, ["offset"] = 0.0 });
            nodes[MsdfDecal] = new FeatureNode("MSDF Decal", "Textures",
                "Sharp vector-style decal from a multi-channel signed-distance font or decal atlas. Supply an actual RGB MSDF texture; ordinary color images and single-channel SDF atlases are not MSDF.",
                "uv:vector2,color:color,outlineColor:color,outlineWidth:float,softness:float,distanceRange:float", "color:color,alpha:float", true,
                new JObject { ["resourceId"] = "", ["color"] = new JArray(1, 1, 1, 1), ["outlineColor"] = new JArray(0, 0, 0, 1), ["outlineWidth"] = 0.0, ["softness"] = 0.0, ["distanceRange"] = 4.0 });
            nodes[NumericText] = new FeatureNode("Numeric Text", "Textures",
                "Draw a signed-distance numeric string (digits, minus and decimal point) from the generated atlas. Feed a number or stat value; world position and VRChat time can come from Viewer Stats. Render-frame rate is a viewer-local estimate, not headset compositor FPS.",
                "uv:vector2,value:float,color:color,scale:float,spacing:float", "color:color,alpha:float", true,
                new JObject { ["resourceId"] = "", ["color"] = new JArray(1, 1, 1, 1), ["digits"] = 6, ["decimals"] = 1, ["scale"] = 1.0, ["spacing"] = 0.08 });
            nodes[ViewerStats] = new FeatureNode("Viewer Stats", "Inputs",
                "Observer-local rendering measurements and coordinates. Frame Rate is the current Unity render-frame reciprocal (not a headset display or compositor FPS counter). World Position is the shaded point; Camera Distance is from the current viewer camera. Network and Unity clocks follow their respective contracts.",
                "", "renderFps:float,deltaSeconds:float,worldPosition:vector3,cameraDistance:float,unitySeconds:float,networkSeconds:float", false,
                new JObject());
        }

        /// <summary>Validate source selector and bounded numeric layout fields without Unity dependencies.</summary>
        public static void Validate(GraphNode node, Action<string, string> error)
        {
            if (node == null || error == null) return;
            if (node.Operation == Clock)
            {
                CheckEnum(node, "source", 0, 1, error);
                CheckFinite(node, "period", error);
                CheckFinite(node, "offset", error);
            }
            else if (node.Operation == NumericText)
            {
                var digits = ReadInteger(node, "digits", 6, error);
                var decimals = ReadInteger(node, "decimals", 1, error);
                if (digits < 1 || digits > 8) error("digits", "Numeric text supports 1 to 8 digits.");
                if (decimals < 0 || decimals > 4) error("decimals", "Decimal places must be 0 to 4.");
                CheckFinite(node, "scale", error);
                CheckFinite(node, "spacing", error);
            }
            else if (node.Operation == ViewerStats) { }
            else if (node.Operation == MsdfDecal)
            {
                CheckFinite(node, "outlineWidth", error);
                CheckFinite(node, "softness", error);
                CheckFinite(node, "distanceRange", error);
            }
        }

        private static void CheckEnum(GraphNode node, string key, int min, int max, Action<string, string> error)
        {
            var token = node.Properties[key];
            if (token == null) return;
            if (token.Type != JTokenType.Integer) { error(key, "Expected an integer enum value."); return; }
            long value;
            try { value = (long)token; } catch { error(key, "Expected an integer enum value."); return; }
            if (value < min || value > max) error(key, "Enum value must be between " + min + " and " + max + ".");
        }

        private static int ReadInteger(GraphNode node, string key, int fallback, Action<string, string> error)
        {
            var token = node.Properties[key];
            if (token == null) return fallback;
            if (token.Type != JTokenType.Integer) { error(key, "Expected an integer value."); return fallback; }
            try { return (int)token; } catch { error(key, "Integer value is outside the supported range."); return fallback; }
        }

        private static void CheckFinite(GraphNode node, string key, Action<string, string> error)
        {
            var token = node.Properties[key];
            if (token == null) return;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            { error(key, "Expected a finite number."); return; }
            double value;
            try { value = (double)token; } catch { error(key, "Expected a finite number."); return; }
            if (double.IsNaN(value) || double.IsInfinity(value)) error(key, "Expected a finite number.");
        }
    }

    /// <summary>Integer reference math matching VRChat's wrapping uint millisecond shader global.</summary>
    public static class NetworkClockMath
    {
        public static uint Shift(uint networkMilliseconds, int offsetMilliseconds) { return unchecked(networkMilliseconds + (uint)offsetMilliseconds); }
        public static float Phase(uint networkMilliseconds, uint periodMilliseconds, int offsetMilliseconds)
        {
            var period = Math.Max(1u, periodMilliseconds);
            return Shift(networkMilliseconds, offsetMilliseconds) % period / (float)period;
        }
        public static uint Cycle(uint networkMilliseconds, uint periodMilliseconds, int offsetMilliseconds)
        { return Shift(networkMilliseconds, offsetMilliseconds) / Math.Max(1u, periodMilliseconds); }
    }

    /// <summary>Reference fixed-width number layout for the Numeric Text shader node.</summary>
    public static class NumericTextMath
    {
        public static string Format(double value, int integerDigits, int decimals)
        {
            integerDigits = Math.Max(1, Math.Min(8, integerDigits));
            decimals = Math.Max(0, Math.Min(4, decimals));
            if (double.IsNaN(value) || double.IsInfinity(value)) return new string(' ', integerDigits + (decimals == 0 ? 0 : decimals + 1) + 1);
            var scale = (long)Math.Pow(10, decimals);
            var max = (long)Math.Pow(10, integerDigits + decimals) - 1;
            var scaled = Math.Min((long)Math.Floor(Math.Abs(value) * scale + .5000001), max);
            var whole = scaled / scale;
            var fraction = scaled % scale;
            var text = new char[integerDigits + (decimals == 0 ? 0 : decimals + 1) + 1];
            var pos = 0;
            text[pos++] = value < 0 && scaled > 0 ? '-' : ' ';
            for (var place = integerDigits - 1; place >= 0; place--)
            {
                var divisor = (long)Math.Pow(10, place);
                var digit = (whole / divisor) % 10;
                text[pos++] = place > 0 && whole < divisor ? ' ' : (char)('0' + digit);
            }
            if (decimals > 0)
            {
                text[pos++] = '.';
                for (var place = decimals - 1; place >= 0; place--)
                {
                    var divisor = (long)Math.Pow(10, place);
                    text[pos++] = (char)('0' + (fraction / divisor) % 10);
                }
            }
            return new string(text).TrimStart(' ');
        }
    }
}
