using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using NXSG.Core;

namespace NXSG.Backend
{
    // Graph values select from fixed tokens; imported JSON never becomes ShaderLab text.
    internal sealed class SurfaceRenderState
    {
        readonly GraphNode output;
        public SurfaceRenderState(GraphNode output) { this.output = output; }
        int Get(string key, int fallback, int min, int max)
        {
            var value = output.Properties?[key];
            return value == null ? fallback : Math.Max(min, Math.Min(max, (int?)value ?? fallback));
        }
        int Get(string key, int fallback) { return Get(key, fallback, int.MinValue, int.MaxValue); }
        public int Mode => Get("renderMode", 0);
        public bool Blends => Mode == 3 || Mode == 4;
        public bool ForceOpaque => Mode == 1;
        public bool FlipBackfaceNormals => Get("flipBackfaceNormals", 0, 0, 1) == 1;
        public bool AlphaToCoverage => Get("alphaToCoverage", 0, 0, 1) == 1;
        public bool TwoPassTransparency => Get("twoPassTransparency", 0, 0, 1) == 1 && (Blends || Mode == 0);
        public bool IsPremultiplied(bool backFace) => Get((backFace ? "back" : "front") + "PassBlend", 0, 0, 3) == 3;
        public double AlphaEdgeSharpness
        {
            get
            {
                var token = output.Properties?["alphaEdgeSharpness"];
                if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return 0;
                var value = (double)token;
                return double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value));
            }
        }
        public string Cull => new[] { "Back", "Front", "Off" }[Get("cull", 0)];
        public string Tags(bool autoTransparent)
        {
            var transparent = TwoPassTransparency || (Mode == 0 ? autoTransparent : Blends);
            var queue = transparent ? "Transparent" : Mode == 2 ? "AlphaTest" : "Geometry";
            var offset = Get("queueOffset", 0, -50, 50);
            if (offset != 0) queue += (offset > 0 ? "+" : "") + offset.ToString(CultureInfo.InvariantCulture);
            return "\"RenderType\"=\"" + (transparent ? "Transparent" : Mode == 2 ? "TransparentCutout" : "Opaque") + "\" \"Queue\"=\"" + queue + "\"";
        }
        public string DepthTest => new[] { "LEqual", "Less", "Equal", "Greater", "GEqual", "Always", "NotEqual", "Never" }[Get("zTest", 0)];
        public string BaseState(bool shell, bool autoTransparent = false, int face = -1)
        {
            if (face >= 0) return TwoPassState(face == 0, true);
            var zwrite = Get("zWrite", 0);
            var blends = TwoPassTransparency || Blends || Mode == 0 && autoTransparent;
            return "Cull " + Cull + "\nZTest " + DepthTest +
                "\nZWrite " + (shell || zwrite == 2 || zwrite == 0 && blends ? "Off" : "On") + "\n" +
                (shell || TwoPassTransparency || Mode == 3 || Mode == 0 && autoTransparent ? "Blend SrcAlpha OneMinusSrcAlpha\n" : Mode == 4 ? "Blend SrcAlpha One\n" : "") +
                (AlphaToCoverage && !shell ? "AlphaToMask On\n" : "") + Stencil();
        }
        public string TwoPassState(bool backFace, bool autoTransparent)
        {
            var prefix = backFace ? "back" : "front";
            var blend = Get(prefix + "PassBlend", 0, 0, 3);
            var zwrite = Get(prefix + "PassZWrite", 0, 0, 2);
            string blendState = blend == 0
                ? (Mode == 4 ? "Blend SrcAlpha One\n" : "Blend SrcAlpha OneMinusSrcAlpha\n")
                : blend == 1 ? "Blend SrcAlpha OneMinusSrcAlpha\n"
                : blend == 2 ? "Blend SrcAlpha One\n"
                : "Blend One OneMinusSrcAlpha\n";
            var automaticWrite = zwrite == 0 && !(Blends || Mode == 0 && autoTransparent || TwoPassTransparency);
            return "Cull " + (backFace ? "Front" : "Back") + "\nZTest " + DepthTest + "\nZWrite " + (zwrite == 1 || automaticWrite ? "On" : "Off") + "\n" + blendState + (AlphaToCoverage ? "AlphaToMask On\n" : "") + Stencil();
        }
        public string SharpenAlpha(string alphaExpression, string cutoffExpression)
        {
            if (AlphaEdgeSharpness <= 0) return alphaExpression;
            var sharpness = AlphaEdgeSharpness.ToString("R", CultureInfo.InvariantCulture);
            return "lerp(" + alphaExpression + ",saturate((" + alphaExpression + "-" + cutoffExpression + ")/max(fwidth(" + alphaExpression + "),.00001)+.5)," + sharpness + ")";
        }
        public string AddState(bool autoTransparent = false, int face = -1)
        {
            if (face >= 0) return "Cull " + (face == 0 ? "Front" : "Back") + "\nZTest " + DepthTest + "\nBlend SrcAlpha One\nZWrite Off\n" + (AlphaToCoverage ? "AlphaToMask On\n" : "");
            return "Cull " + Cull + "\nZTest " + DepthTest + "\nBlend " + (Blends || Mode == 0 && autoTransparent ? "SrcAlpha" : "One") + " One\n" + (AlphaToCoverage ? "AlphaToMask On\n" : "");
        }
        public string Stencil()
        {
            if (Get("stencilEnabled", 0) == 0) return "";
            return "Stencil { Ref " + Get("stencilRef", 0) + " ReadMask " + Get("stencilReadMask", 255) + " WriteMask " + Get("stencilWriteMask", 255) +
                " Comp " + new[] { "Always", "Equal", "NotEqual", "Less", "LEqual", "Greater", "GEqual", "Never" }[Get("stencilCompare", 0)] +
                " Pass " + new[] { "Keep", "Replace", "Zero", "IncrSat", "DecrSat", "Invert", "IncrWrap", "DecrWrap" }[Get("stencilPass", 0)] + " Fail Keep ZFail Keep }\n";
        }
    }
}
