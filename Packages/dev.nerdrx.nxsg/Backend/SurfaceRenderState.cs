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
        int Get(string key, int fallback) { return (int?)output.Properties?[key] ?? fallback; }
        public int Mode => Get("renderMode", 0);
        public bool Blends => Mode == 3 || Mode == 4;
        public bool ForceOpaque => Mode == 1;
        public string Cull => new[] { "Back", "Front", "Off" }[Get("cull", 0)];
        public string Tags(bool autoTransparent)
        {
            var transparent = Mode == 0 ? autoTransparent : Blends;
            var queue = transparent ? "Transparent" : Mode == 2 ? "AlphaTest" : "Geometry";
            var offset = Get("queueOffset", 0);
            if (offset != 0) queue += (offset > 0 ? "+" : "") + offset.ToString(CultureInfo.InvariantCulture);
            return "\"RenderType\"=\"" + (transparent ? "Transparent" : Mode == 2 ? "TransparentCutout" : "Opaque") + "\" \"Queue\"=\"" + queue + "\"";
        }
        public string DepthTest => new[] { "LEqual", "Less", "Equal", "Greater", "GEqual", "Always", "NotEqual", "Never" }[Get("zTest", 0)];
        public string BaseState(bool shell, bool autoTransparent = false)
        {
            var zwrite = Get("zWrite", 0);
            var blends = Blends || Mode == 0 && autoTransparent;
            return "Cull " + Cull + "\nZTest " + DepthTest +
                "\nZWrite " + (shell || zwrite == 2 || zwrite == 0 && blends ? "Off" : "On") + "\n" +
                (shell || Mode == 3 || Mode == 0 && autoTransparent ? "Blend SrcAlpha OneMinusSrcAlpha\n" : Mode == 4 ? "Blend SrcAlpha One\n" : "") + Stencil();
        }
        public string AddState(bool autoTransparent = false) => "Cull " + Cull + "\nZTest " + DepthTest + "\nBlend " + (Blends || Mode == 0 && autoTransparent ? "SrcAlpha" : "One") + " One\n";
        public string Stencil()
        {
            if (Get("stencilEnabled", 0) == 0) return "";
            return "Stencil { Ref " + Get("stencilRef", 0) + " ReadMask " + Get("stencilReadMask", 255) + " WriteMask " + Get("stencilWriteMask", 255) +
                " Comp " + new[] { "Always", "Equal", "NotEqual", "Less", "LEqual", "Greater", "GEqual", "Never" }[Get("stencilCompare", 0)] +
                " Pass " + new[] { "Keep", "Replace", "Zero", "IncrSat", "DecrSat", "Invert", "IncrWrap", "DecrWrap" }[Get("stencilPass", 0)] + " Fail Keep ZFail Keep }\n";
        }
    }
}
