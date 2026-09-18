using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NXSG.Core;

namespace NXSG.Backend
{
    // Extended lowering uses one bounded function per node/output/stage, avoiding expression explosion.
    internal sealed class AdvancedShaderEmitter
    {
        readonly ShaderGraph graph;
        readonly EmitterOptions options;
        readonly Dictionary<string, GraphNode> nodes;
        readonly Dictionary<string, GraphConnection> edges;
        readonly Dictionary<string, string> types;
        readonly List<MaterialProperty> properties = new List<MaterialProperty>();
        readonly Dictionary<string, string> textureNames = new Dictionary<string, string>();
        readonly Dictionary<string, string> functions = new Dictionary<string, string>();
        readonly StringBuilder code = new StringBuilder();
        readonly List<Diagnostic> diagnostics = new List<Diagnostic>();
        int depth;

        public static bool IsAdvanced(ShaderGraph graph)
        {
            if (graph?.Nodes == null) return false;
            var ops = new HashSet<string> { "core.unlitSurface", "core.pbrSurface", "core.fresnel", "core.colorRamp", "core.layer", "core.sticker", "core.dissolve", "core.flipbook", "core.uvDistort", "core.vertexMotion", "core.audioLink", "core.shell", "core.normalMap", "core.previewVector" };
            // Only reachable effects select the extended lowering; disconnected nodes never change shading.
            var connected = new HashSet<string>();
            var queue = new Queue<string>(graph.Nodes.Where(n => n?.Operation == "core.output").Select(n => n.Id));
            var incoming = (graph.Connections ?? new List<GraphConnection>()).Where(e => e?.From?.NodeId != null && e.To?.NodeId != null).ToLookup(e => e.To.NodeId);
            while (queue.Count > 0) { var id = queue.Dequeue(); if (!connected.Add(id)) continue; foreach (var edge in incoming[id]) queue.Enqueue(edge.From.NodeId); }
            var live = graph.Nodes.Where(n => n != null && connected.Contains(n.Id)).ToArray();
            return live.Any(n => ops.Contains(n.Operation)) || live.Count(n => n.Operation == "core.texture2D") > 1 ||
                live.Any(n => n.Operation == "core.toonSurface" && (n.Properties?["opacity"] != null || n.Properties?["displacement"] != null || incoming[n.Id].Any(e => e.To.PortId == "opacity" || e.To.PortId == "displacement" || e.To.PortId == "normal")));
        }

        AdvancedShaderEmitter(ShaderGraph graph, EmitterOptions options)
        {
            this.graph = graph; this.options = options;
            nodes = (graph.Nodes ?? new List<GraphNode>()).ToDictionary(n => n.Id, n => new GraphNode { Id=n.Id, Operation=n.Operation, Version=n.Version, Properties=n.Properties ?? new JObject() });
            edges = (graph.Connections ?? new List<GraphConnection>()).ToDictionary(e => Key(e.To.NodeId, e.To.PortId));
            types = GraphTypes.Infer(graph);
        }

        public static EmissionResult Emit(ShaderGraph graph, EmitterOptions options)
        {
            var validation = GraphValidator.Validate(graph);
            if (!validation.IsValid) return new EmissionResult(null, validation.Diagnostics, new List<MaterialProperty>(), new List<SourceMapEntry>());
            AdvancedShaderEmitter emitter = null;
            try
            {
                emitter = new AdvancedShaderEmitter(graph, options);
                var source = emitter.Build();
                if (Encoding.UTF8.GetByteCount(source) > 8 * 1024 * 1024) throw new InvalidOperationException("Generated shader exceeds 8 MiB.");
                return new EmissionResult(source, emitter.diagnostics, emitter.properties, new List<SourceMapEntry>());
            }
            catch (InvalidOperationException exception)
            {
                return new EmissionResult(null, new[] { new Diagnostic(DiagnosticSeverity.Error, "backend.effects", "$", exception.Message) }, emitter?.properties ?? new List<MaterialProperty>(), new List<SourceMapEntry>());
            }
        }

        string Build()
        {
            if (graph.Format != "nxsg" || graph.SchemaVersion != 1) throw new InvalidOperationException("Unsupported graph format/version.");
            if (nodes.Values.Count(n => n.Operation == "core.output") != 1) throw new InvalidOperationException("A graph needs exactly one Output.");
            var output = nodes.Values.Single(n => n.Operation == "core.output");
            var root = Source(output, "surface");
            if (root == null) throw new InvalidOperationException("Connect a Surface to Output.");
            var shell = root.Operation == "core.shell" ? root : null;
            var surface = shell == null ? root : Source(shell, "base");
            var overlay = shell == null ? null : Source(shell, "layer");
            CheckSurface(surface); if (shell != null) CheckSurface(overlay);
            var name = options.ShaderName;
            if (string.IsNullOrEmpty(name) || name.Length > 180 || name.Any(c => char.IsControl(c) || c == '"' || c == '\\')) throw new InvalidOperationException("Invalid shader name.");
            var fallback = options.IncludeVrcFallback ? options.VrcFallbackTag : null;
            if (fallback != null && !new[] { "toonstandard", "standard", "unlit", "toon", "hidden" }.Contains(fallback)) throw new InvalidOperationException("Unsupported fallback tag.");
            var live = new HashSet<string>();
            Visit(root, live);
            foreach (var node in live.Select(id => nodes[id]).OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                if (node.Version != 1) throw new InvalidOperationException("Unsupported node version: " + node.Id);
                if (node.Operation != "core.texture2D" && node.Operation != "core.sticker") continue;
                var id = (string)node.Properties["resourceId"];
                var resource = (graph.Resources ?? new List<GraphResource>()).FirstOrDefault(r => r.Id == id);
                if (resource == null || resource.Kind != "texture2D") throw new InvalidOperationException("Missing texture resource: " + id);
                if (textureNames.ContainsKey(id)) continue;
                var symbol = textureNames.Count == 0 ? "_MainTex" : "_NXSG_Tex_" + Hash(id);
                textureNames.Add(id, symbol);
                properties.Add(new MaterialProperty { Name = symbol, DisplayName = "Texture " + textureNames.Count, Type = GraphValueType.Texture2D, Binding = GraphBindingKind.Material, ResourceId = id, ResourceUri = resource.Uri });
            }
            var b = new StringBuilder();
            b.AppendLine("Shader \"" + name + "\" {\nProperties {");
            foreach (var prop in properties) b.AppendLine(prop.Name + " (\"" + prop.DisplayName + "\", 2D) = \"white\" {}");
            b.AppendLine("_Color (\"Tint\", Color) = (1,1,1,1)");
            b.AppendLine("[HideInInspector] _NXSG_AudioLinkPreview (\"Preview audio\", Float) = 0\n[HideInInspector] _NXSG_AudioLinkValue (\"Preview value\", Float) = 0");
            var symbols = new HashSet<string>(properties.Select(p => p.Name));
            foreach (var parameter in graph.Parameters ?? new List<GraphParameter>())
            {
                if (parameter.Binding == GraphBindingKind.Constant) continue;
                if (parameter.Binding != GraphBindingKind.Material && parameter.Binding != GraphBindingKind.AnimatedMaterial) throw new InvalidOperationException("Use the AudioLink node for audio; only constant/material/animated bindings are supported here.");
                var symbol = ParameterName(parameter.Id);
                if (!symbols.Add(symbol)) throw new InvalidOperationException("Parameter symbols collide: " + parameter.Id);
                var type = parameter.Type == GraphValueType.Float ? "Float" : parameter.Type == GraphValueType.Color ? "Color" : parameter.Type == GraphValueType.Vector4 ? "Vector" : null;
                if (type == null) throw new InvalidOperationException("Unsupported parameter type: " + parameter.Type);
                var literal = Literal(parameter.DefaultValue, parameter.Type == GraphValueType.Float ? "float" : "color");
                b.AppendLine(symbol + " (\"" + (parameter.Name ?? parameter.Id).Replace("\"", "").Replace("\\", "").Replace("\n", " ").Replace("\r", " ") + "\", " + type + ") = " + (type == "Float" ? literal : literal.Substring(6)));
                properties.Add(new MaterialProperty { Name = symbol, DisplayName = parameter.Name, Type = parameter.Type, Binding = parameter.Binding, ParameterId = parameter.Id });
            }
            AddToonProperties(b, surface, false);
            if (overlay != null) AddToonProperties(b, overlay, true);
            var basePass = Pass(surface, false, null);
            var shellPass = shell == null ? "" : Pass(overlay, true, shell);
            var shadowPass = options.IncludeShadowCaster ? Shadow(surface) : "";
            b.AppendLine("}\nSubShader {\nTags { \"RenderType\"=\"Opaque\" \"Queue\"=\"Geometry\"" + (fallback == null ? "" : " \"VRCFallback\"=\"" + fallback + "\"") + " }");
            b.AppendLine("CGINCLUDE\n#include \"UnityCG.cginc\"\n#include \"Lighting.cginc\"\n#include \"AutoLight.cginc\"\n#include \"UnityPBSLighting.cginc\"");
            b.AppendLine("float4 _Color;");
            foreach (var prop in properties) b.AppendLine(prop.Type == GraphValueType.Texture2D ? "sampler2D " + prop.Name + "; float4 " + prop.Name + "_ST;" : (prop.Type == GraphValueType.Float ? "float " : "float4 ") + prop.Name + ";");
            if (live.Any(id => nodes[id].Operation == "core.audioLink")) b.AppendLine(AudioLinkShader.Hlsl);
            b.AppendLine("#ifndef SHADOW_COORDS\n#define SHADOW_COORDS(index)\n#endif");
            b.AppendLine(Helpers);
            b.AppendLine(code.ToString());
            b.AppendLine("ENDCG\n" + basePass + shellPass + shadowPass + "}\nFallback Off\n}");
            if (shell != null) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.shell", shell.Id, "One extra transparent mesh pass per view; normal offset does not expand renderer bounds. Overlapping transparent objects can sort imperfectly."));
            if (live.Any(id => nodes[id].Operation == "core.vertexMotion")) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "bounds.displacement", "$", "Vertex displacement requires mesh/SkinnedMeshRenderer bounds large enough for the motion."));
            return b.ToString();
        }

        void Visit(GraphNode node, HashSet<string> live)
        {
            var queue = new Queue<GraphNode>(); queue.Enqueue(node);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); if (!live.Add(current.Id)) continue;
                foreach (var port in NodeCatalog.Ports(current.Operation, false)) { var source = Source(current, port); if (source != null) queue.Enqueue(source); }
            }
        }
        static void CheckSurface(GraphNode n) { if (n == null || !new[] { "core.toonSurface", "core.unlitSurface", "core.pbrSurface" }.Contains(n.Operation)) throw new InvalidOperationException("Connect Toon, Unlit or PBR to each surface socket. Nested shells are not supported."); }
        GraphNode Source(GraphNode n, string port) { return edges.TryGetValue(Key(n.Id, port), out var edge) ? nodes[edge.From.NodeId] : null; }
        static string Key(string a, string b) { return a + ":" + b; }
        static string Hash(string value) { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2"))); }
        static string ParameterName(string id) { if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,96}$")) throw new InvalidOperationException("Unsupported parameter ID."); return "_NXSG_P_" + id.Replace('-', '_'); }
        static string Num(double value) { if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue) throw new InvalidOperationException("Shader numbers must be finite floats."); return ((float)value).ToString("R", CultureInfo.InvariantCulture); }
        string Prop(GraphNode n, string key, double fallback) { var t = n.Properties[key]; if (t == null) return Num(fallback); if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new InvalidOperationException("Expected a number: " + n.Id + "." + key); return Num((double)t); }
        string Vec(GraphNode n, string key, double x, double y) { var t = n.Properties[key]; return t == null ? "float2(" + Num(x) + "," + Num(y) + ")" : Literal(t, "vector2"); }
        static string HlslType(string type) { switch (type) { case "float": return "float"; case "vector2": return "float2"; case "vector3": return "float3"; case "color": case "vector4": return "float4"; default: throw new InvalidOperationException("Unsupported value type: " + type); } }
        static string Literal(JToken token, string type)
        {
            if (token == null) return type == "float" ? "0" : HlslType(type) + "(0,0" + (type == "vector2" ? ")" : type == "vector3" ? ",1)" : ",0,1)");
            if (type == "float") { if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new InvalidOperationException("Scalar literal required."); return Num((double)token); }
            var size = type == "vector2" ? 2 : type == "vector3" ? 3 : 4;
            if (!(token is JArray array) || array.Count != size || array.Any(t => t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) throw new InvalidOperationException("Vector literal has the wrong shape.");
            return HlslType(type) + "(" + string.Join(",", array.Select(t => Num((double)t))) + ")";
        }
        string Input(GraphNode n, string port, string fallback, string expected, bool vertex = false)
        {
            if (!edges.TryGetValue(Key(n.Id, port), out var edge)) return fallback;
            var source = nodes[edge.From.NodeId];
            var value = Eval(source, edge.From.PortId, vertex);
            var actual = GraphTypes.PortType(graph, source, edge.From.PortId, types);
            if (actual == "float" && expected == "color") return "NX_Splat(" + value + ")";
            if (actual != expected) throw new InvalidOperationException("Unsupported conversion " + actual + " → " + expected + " at " + n.Id + "." + port);
            return value;
        }
        string Scalar(GraphNode n, string port, double fallback, bool vertex = false) { return Input(n, port, Prop(n, port, fallback), "float", vertex); }

        string Eval(GraphNode n, string port, bool vertex)
        {
            var key = Key(n.Id, port) + (vertex ? "v" : "f");
            if (functions.TryGetValue(key, out var cached)) return cached + "(input)";
            if (++depth > 64 || functions.Count > 8192) throw new InvalidOperationException("Graph evaluation exceeds depth/work limit.");
            var type = GraphTypes.PortType(graph, n, port, types);
            var symbol = "NX_" + Hash(key);
            string body;
            string P(string p, string def, string t = null) { return Input(n, p, def, t ?? type, vertex); }
            string S(string p, double def) { return Scalar(n, p, def, vertex); }
            string uv = "input.uv";
            switch (n.Operation)
            {
                case "core.previewVector": body = Source(n,"normal") != null ? "float4(" + P("normal","float3(0,0,1)","vector3") + "*.5+.5,1)" : "float4(" + P("uv",uv,"vector2") + ",0,1)"; break;
                case "core.value": body = Prop(n, "value", 0); break;
                case "core.constant": body = Literal(n.Properties["value"], type); break;
                case "core.parameter":
                    var parameter = graph.Parameters.Single(p => p.Id == (string)n.Properties["parameterId"]);
                    body = parameter.Binding == GraphBindingKind.Constant ? Literal(parameter.DefaultValue, type) : ParameterName(parameter.Id); break;
                case "core.time": body = "(_Time.y*" + Prop(n, "speed", 1) + "+" + Prop(n, "offset", 0) + ")"; break;
                case "core.uv0": body = uv; break;
                case "core.objectUV": body = "input.local.xz"; break;
                case "core.worldUV": body = "input.ws.xz"; break;
                case "core.uvTransform": body = "(" + P("uv", uv) + "*" + Vec(n, "tiling", 1, 1) + "+" + Vec(n, "offset", 0, 0) + ")"; break;
                case "core.uvScroll": body = "(" + P("uv", uv) + "+" + Vec(n, "speed", .1, 0) + "*" + P("time", "_Time.y", "float") + ")"; break;
                case "core.uvRotate": body = "NX_Rotate(" + P("uv", uv) + "," + Vec(n, "center", .5, .5) + "," + S("angle", 0) + ")"; break;
                case "core.polarUV": body = "NX_Polar(" + P("uv", uv) + "," + Vec(n, "center", .5, .5) + "," + Prop(n, "radialScale", 1) + "," + Prop(n, "angleScale", 1) + ")"; break;
                case "core.texture2D": body = Sample(n, P("uv", uv, "vector2"), vertex); break;
                case "core.noise":
                    body = "NX_Noise(" + P("uv", uv, "vector2") + "*" + Prop(n, "scale", 5) + "+float2(1,.731)*" + P("time", "_Time.y", "float") + "*" + Prop(n, "speed", 1) + ")";
                    if (port == "color") body = "float4(" + body + "," + body + "," + body + ",1)"; break;
                case "core.add": body = "(" + P("a", "0") + "+" + P("b", "0") + ")"; break;
                case "core.subtract": body = "(" + P("a", "0") + "-" + P("b", "0") + ")"; break;
                case "core.multiply": body = "(" + P("a", "1") + "*" + P("b", "1") + ")"; break;
                case "core.divide": body = "NX_Div(" + P("a", "1") + "," + P("b", "1") + ")"; break;
                case "core.minimum": body = "min(" + P("a", "0") + "," + P("b", "0") + ")"; break;
                case "core.maximum": body = "max(" + P("a", "0") + "," + P("b", "0") + ")"; break;
                case "core.mix": body = "lerp(" + P("a", "0") + "," + P("b", "1") + ",saturate(" + S("factor", .5) + "))"; break;
                case "core.oneMinus": body = "(1-" + P("color", "0") + ")"; break;
                case "core.clamp": body = "saturate(" + P("color", "0") + ")"; break;
                case "core.emission": body = "(" + P("color", "float4(0,0,0,1)", "color") + "*" + S("strength", 1) + ")"; break;
                case "core.ramp":
                    body = CurveBody(n, P("value", "0", "float"), false); break;
                case "core.colorRamp": body = CurveBody(n, P("value", "0", "float"), true); break;
                case "core.fresnel": body = "pow(saturate(1-dot(normalize(input.n),normalize(_WorldSpaceCameraPos-input.ws))),max(0.0001," + S("power", 5) + "))"; break;
                case "core.layer": body = "lerp(" + P("base", "float4(0,0,0,1)") + "," + P("overlay", "float4(1,1,1,1)") + ",saturate(" + S("mask", 1) + "))"; break;
                case "core.sticker":
                    var stickerUV = "((NX_Rotate(" + P("uv", uv, "vector2") + "-" + Vec(n, "position", 0, 0) + ",float2(.5,.5),-" + Prop(n, "rotation", 0) + ")-.5)/max(abs(" + Vec(n, "size", 1, 1) + "),float2(.00001,.00001))+.5)";
                    body = "float2 u=" + stickerUV + "; float4 decal=" + Sample(n, "u", vertex) + "; float m=step(0,u.x)*step(0,u.y)*step(u.x,1)*step(u.y,1)*saturate(decal.a*" + S("mask", 1) + "); return lerp(" + P("base", "float4(0,0,0,1)") + ",decal,m);"; break;
                case "core.dissolve":
                    body = port == "mask" ? "step(" + S("threshold", .5) + "," + S("value", 0) + ")" : "(step(" + S("threshold", .5) + "," + S("value", 0) + ")*(1-step(" + S("threshold", .5) + "+max(.00001," + Prop(n, "edgeWidth", .05) + ")," + S("value", 0) + ")))"; break;
                case "core.flipbook": body = "NX_Flipbook(" + P("uv", uv) + "," + P("time", "_Time.y", "float") + "*" + Prop(n, "speed", 1) + "," + Prop(n, "columns", 1) + "," + Prop(n, "rows", 1) + ")"; break;
                case "core.uvDistort": body = "NX_Distort(" + P("uv", uv) + "," + S("strength", .05) + "," + Prop(n, "scale", 5) + ",_Time.y*" + Prop(n, "speed", 1) + ")"; break;
                case "core.vertexMotion": body = "(sin(" + P("time", "_Time.y", "float") + "*" + Prop(n, "speed", 1) + "+input.local.y*" + Prop(n, "frequency", 2) + ")*" + S("strength", .02) + ")"; break;
                case "core.audioLink": body = "NXSG_Audio(" + Prop(n, "band", 0) + "," + Prop(n, "gain", 1) + "," + Prop(n, "smoothing", .5) + "," + Prop(n, "fallback", 0) + ")"; break;
                case "core.normalMap": body = "NX_Normal(" + P("color", "float4(.5,.5,1,1)", "color") + "," + Prop(n, "strength", 1) + ")"; break;
                default: throw new InvalidOperationException("Unsupported value operation: " + n.Operation);
            }
            functions.Add(key, symbol);
            code.AppendLine(HlslType(type) + " " + symbol + "(NXInput input) { " + (body.Contains("return ") ? body : "return " + body + ";") + " }");
            depth--;
            return symbol + "(input)";
        }

        string Sample(GraphNode n, string uv, bool vertex)
        {
            var tex = textureNames[(string)n.Properties["resourceId"]];
            var transformed = "(" + uv + "*" + tex + "_ST.xy+" + tex + "_ST.zw)";
            return vertex ? "tex2Dlod(" + tex + ",float4(" + transformed + ",0,0))" : "tex2D(" + tex + "," + transformed + ")";
        }
        string CurveBody(GraphNode n, string value, bool color)
        {
            var points = n.Properties[color ? "stops" : "points"] as JArray ?? (color ? new JArray(new JArray(0,0,0,0,1),new JArray(1,1,1,1,1)) : new JArray(new JArray(0,0),new JArray(1,1)));
            string At(JToken p) { return color ? "float4(" + string.Join(",", p.Skip(1).Select(v => Num((double)v))) + ")" : Num((double)p[1]); }
            var b = new StringBuilder("float x=" + value + "; ");
            if (!color) b.Append("float lo=" + Prop(n,"blackPoint",0) + ",hi=" + Prop(n,"whitePoint",1) + "; x=abs(hi-lo)<.000001?step(lo,x):saturate((x-lo)/(hi-lo)); ");
            b.Append("if(x<=" + Num((double)points[0][0]) + ") return " + At(points[0]) + "; ");
            for(var i=1;i<points.Count;i++)
            {
                var a=points[i-1];var z=points[i];
                b.Append("if(x<="+Num((double)z[0])+"){float t=saturate((x-"+Num((double)a[0])+")/"+Num((double)z[0]-(double)a[0])+"); ");
                if(!color)b.Append("t=lerp(t,t*t*(3-2*t),saturate("+Prop(n,"smoothness",0)+")); ");
                b.Append("return lerp("+At(a)+","+At(z)+",t);} ");
            }
            return b.Append("return "+At(points.Last)+";").ToString();
        }

        void AddToonProperties(StringBuilder b, GraphNode surface, bool shell)
        {
            if (surface.Operation != "core.toonSurface") return;
            foreach (var setting in new[] { "threshold", "softness", "shadowStrength" })
            {
                if (surface.Properties[setting + "ParameterId"] != null) continue;
                var symbol = ToonSymbol(setting,shell);
                b.AppendLine(symbol + " (\"" + (shell ? "Shell " : "") + setting + "\", Range(0,1)) = " + Prop(surface,setting,setting == "threshold" ? .5 : setting == "softness" ? .05 : 1));
                properties.Add(new MaterialProperty { Name=symbol, DisplayName=setting, Type=GraphValueType.Float, Binding=GraphBindingKind.Material });
            }
        }
        static string ToonSymbol(string setting, bool shell)
        {
            return "_NXSG_" + (shell ? "Shell" : "") + (setting == "threshold" ? "ToonThreshold" : setting == "softness" ? "ToonSoftness" : "ShadowStrength");
        }
        string ToonSetting(GraphNode surface,string setting,bool shell)
        {
            if (surface.Operation != "core.toonSurface") return "0";
            var id=(string)surface.Properties[setting+"ParameterId"];
            if(id==null) return ToonSymbol(setting,shell);
            var parameter=graph.Parameters.FirstOrDefault(p=>p.Id==id);
            if(parameter==null || parameter.Type!=GraphValueType.Float) throw new InvalidOperationException("Missing scalar Toon parameter: "+id);
            return parameter.Binding==GraphBindingKind.Constant ? Literal(parameter.DefaultValue,"float") : ParameterName(id);
        }

        string Pass(GraphNode surface, bool shell, GraphNode shellNode)
        {
            var displacement = Scalar(surface,"displacement",0,true);
            var offset = shell ? Scalar(shellNode,"offset",.02,true) : "0";
            var color = Input(surface,"albedo","float4(1,1,1,1)","color");
            var emission = Input(surface,"emission","float4(0,0,0,1)","color");
            var opacity = Scalar(surface,"opacity",1);
            var normal = Input(surface,"normal","float3(0,0,1)","vector3");
            var metallic = Scalar(surface,"metallic",0);
            var roughness = Scalar(surface,"roughness",.5);
            var threshold=ToonSetting(surface,"threshold",shell);var softness=ToonSetting(surface,"softness",shell);var shadow=ToonSetting(surface,"shadowStrength",shell);
            var b=new StringBuilder("Pass {\nName \""+(shell?"Shell":"ForwardBase")+"\"\nTags { \"LightMode\"=\""+(shell?"Always":"ForwardBase")+"\" }\nCull Back\n"+(shell?"ZWrite Off\nBlend SrcAlpha OneMinusSrcAlpha":"ZWrite On")+"\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vert\n#pragma fragment frag\n#pragma multi_compile_fwdbase\n#pragma multi_compile_instancing\n");
            b.AppendLine("NXInput vert(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*("+displacement+"+"+offset+"); NXInput o=NX_Make(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_SHADOW(o); return o; }");
            b.AppendLine("float4 frag(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float4 c="+color+"*_Color; float3 emission=("+emission+").rgb; float alpha=saturate(c.a*"+opacity+");");
            if(!shell)b.AppendLine("clip(alpha-"+Prop(surface,"cutoff",.001)+");");
            if(surface.Operation=="core.unlitSurface")b.AppendLine("return float4(c.rgb+emission,alpha);");
            else
            {
                b.AppendLine("float3 tn="+normal+"; float3 n=normalize(normalize(input.tangent)*tn.x+normalize(input.bitangent)*tn.y+normalize(input.n)*tn.z); float3 view=normalize(_WorldSpaceCameraPos-input.ws); float3 lightDir=normalize(UnityWorldSpaceLightDir(input.ws)); UNITY_LIGHT_ATTENUATION(atten,input,input.ws);");
                if(surface.Operation=="core.pbrSurface")
                {
                    b.AppendLine("half3 spec; half reflectivity; half3 diffuse=DiffuseAndSpecularFromMetallic(c.rgb,saturate("+metallic+"),spec,reflectivity); UnityLight light; light.color=_LightColor0.rgb*atten; light.dir=lightDir; light.ndotl=saturate(dot(n,lightDir)); UnityIndirect indirect; indirect.diffuse=max(0,ShadeSH9(float4(n,1))); half rough=saturate("+roughness+"); half4 env=UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflect(-view,n),rough*6); indirect.specular=DecodeHDR(env,unity_SpecCube0_HDR); return float4(UNITY_BRDF_PBS(diffuse,spec,reflectivity,1-rough,n,view,light,indirect).rgb+emission,alpha);");
                }
                else b.AppendLine("float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),dot(n,lightDir)*.5+.5); return float4(c.rgb*(max(0,ShadeSH9(float4(n,1)))+_LightColor0.rgb*lerp(1-saturate("+shadow+"),1,lit*atten))+emission,alpha);");
            }
            return b.AppendLine("}\nENDCG\n}").ToString();
        }
        string Shadow(GraphNode surface)
        {
            var displacement=Scalar(surface,"displacement",0,true);
            var opacity=Scalar(surface,"opacity",1);
            var color=Input(surface,"albedo","float4(1,1,1,1)","color");
            return "Pass {\nName \"ShadowCaster\"\nTags { \"LightMode\"=\"ShadowCaster\" }\nZWrite On\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vertShadow\n#pragma fragment fragShadow\n#pragma multi_compile_shadowcaster\n#pragma multi_compile_instancing\nstruct NXShadow { V2F_SHADOW_CASTER; float2 uv:TEXCOORD1; float3 ws:TEXCOORD2; float3 normal:TEXCOORD3; float3 local:TEXCOORD4; UNITY_VERTEX_OUTPUT_STEREO };\nNXShadow vertShadow(NXApp v){UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*("+displacement+"); NXShadow o; UNITY_INITIALIZE_OUTPUT(NXShadow,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.uv=v.uv; o.ws=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); o.local=v.vertex.xyz; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o); return o;}\nfloat4 fragShadow(NXShadow i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); NXInput input=(NXInput)0; input.uv=i.uv; input.ws=i.ws; input.n=i.normal; input.local=i.local; clip(("+color+").a*_Color.a*("+opacity+")-"+Prop(surface,"cutoff",.001)+"); SHADOW_CASTER_FRAGMENT(i);}\nENDCG\n}\n";
        }
        const string Helpers=@"
struct NXApp { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct NXInput { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 ws:TEXCOORD1; float3 n:TEXCOORD2; float3 local:TEXCOORD3; float3 tangent:TEXCOORD4; float3 bitangent:TEXCOORD5; SHADOW_COORDS(6) UNITY_VERTEX_OUTPUT_STEREO };
NXInput NX_Make(NXApp v){ NXInput o=(NXInput)0; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.local=v.vertex.xyz; o.ws=mul(unity_ObjectToWorld,v.vertex).xyz; o.n=UnityObjectToWorldNormal(v.normal); o.tangent=UnityObjectToWorldDir(v.tangent.xyz); o.bitangent=cross(o.n,o.tangent)*v.tangent.w*unity_WorldTransformParams.w; return o; }
float4 NX_Splat(float x){return float4(x,x,x,x);}
float NX_Div(float a,float b){return a/((b<0?-1:1)*max(abs(b),.00001));}
float4 NX_Div(float4 a,float4 b){return a/((step(0,b)*2-1)*max(abs(b),.00001));}
float2 NX_Rotate(float2 uv,float2 center,float degrees){float a=radians(degrees);float s=sin(a),c=cos(a);uv-=center;return float2(c*uv.x-s*uv.y,s*uv.x+c*uv.y)+center;}
float2 NX_Polar(float2 uv,float2 center,float radial,float angular){float2 p=uv-center;return float2(length(p)*2*radial,(dot(p,p)<1e-12?.5:atan2(p.y,p.x)/6.28318530718+.5)*angular);}
float NX_Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float NX_Noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(NX_Hash(i),NX_Hash(i+float2(1,0)),f.x),lerp(NX_Hash(i+float2(0,1)),NX_Hash(i+float2(1,1)),f.x),f.y);}
float2 NX_Flipbook(float2 uv,float frame,float cols,float rows){float count=cols*rows;frame=floor(frame);frame=frame-floor(frame/count)*count;return (frac(uv)+float2(fmod(frame,cols),rows-1-floor(frame/cols)))/float2(cols,rows);}
float2 NX_Distort(float2 uv,float strength,float scale,float time){return uv+(float2(NX_Noise(uv*scale+time),NX_Noise(uv*scale+time+17.2))-.5)*strength;}
float3 NX_Normal(float4 encoded,float strength){float3 n=UnpackNormal(encoded);n.xy*=strength;n.z=sqrt(saturate(1-dot(n.xy,n.xy)));return normalize(n);}
";
    }
}
