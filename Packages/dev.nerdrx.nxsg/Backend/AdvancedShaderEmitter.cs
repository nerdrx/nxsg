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

        sealed class SurfacePass
        {
            public GraphNode Surface;
            public string Offset;
        }

        public static bool IsAdvanced(ShaderGraph graph)
        {
            if (graph?.Nodes == null) return false;
            var ops = new HashSet<string> { "core.unlitSurface", "core.pbrSurface", "core.particleSurface", "core.particleColor", "core.surfaceParticles", "core.fresnel", "core.colorRamp", "core.layer", "core.sticker", "core.dissolve", "core.flipbook", "core.uvDistort", "core.vertexMotion", "core.audioLink", "core.shell", "core.normalMap", "core.previewVector", "core.musgrave", "core.voronoi", "core.checker", "core.wave", "core.gradient", "core.uvTile", "core.posterize" };
            // Only reachable effects select the extended lowering; disconnected nodes never change shading.
            var connected = new HashSet<string>();
            var queue = new Queue<string>(graph.Nodes.Where(n => n?.Operation == "core.output").Select(n => n.Id));
            var incoming = (graph.Connections ?? new List<GraphConnection>()).Where(e => e?.From?.NodeId != null && e.To?.NodeId != null).ToLookup(e => e.To.NodeId);
            while (queue.Count > 0) { var id = queue.Dequeue(); if (!connected.Add(id)) continue; foreach (var edge in incoming[id]) queue.Enqueue(edge.From.NodeId); }
            var live = graph.Nodes.Where(n => n != null && connected.Contains(n.Id)).ToArray();
            return live.Any(n => ops.Contains(n.Operation)) || live.Any(n => (n.Operation == "core.noise" || n.Operation == "core.uv0" || n.Operation == "core.polarUV" || n.Operation == "core.texture2D") && IsAdvancedCoordinates(n, incoming[n.Id])) || live.Count(n => n.Operation == "core.texture2D") > 1 ||
                live.Any(n => n.Operation == "core.toonSurface" && (n.Properties?["opacity"] != null || n.Properties?["displacement"] != null || incoming[n.Id].Any(e => e.To.PortId == "opacity" || e.To.PortId == "displacement" || e.To.PortId == "normal")));
        }

        static bool IsAdvancedNoise(GraphNode n, IEnumerable<GraphConnection> incoming)
        {
            var dimensions = n.Properties?["dimensions"];
            if (dimensions != null && (dimensions.Type == JTokenType.Integer || dimensions.Type == JTokenType.Float) && (double)dimensions != 2) return true;
            if (n.Properties?["coordinateSource"] != null && (string)n.Properties["coordinateSource"] != "uv0") return true;
            if (n.Properties?["coordinateSpace"] != null && (string)n.Properties["coordinateSpace"] != "object") return true;
            return incoming.Any(e => e.To.PortId == "x" || e.To.PortId == "position");
        }
        static bool IsAdvancedCoordinates(GraphNode n, IEnumerable<GraphConnection> incoming)
        {
            if (n.Operation == "core.noise") return IsAdvancedNoise(n, incoming);
            return n.Properties?["coordinateSource"] != null && (string)n.Properties["coordinateSource"] != "uv0";
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
            var particle = root.Operation == "core.particleSurface";
            var surfaceParticles = root.Operation == "core.surfaceParticles";
            var baseRoot = surfaceParticles ? Source(root, "base") : root;
            if (surfaceParticles && baseRoot == null) throw new InvalidOperationException("Connect a surface to Surface Particles Base.");
            var name = options.ShaderName;
            if (string.IsNullOrEmpty(name) || name.Length > 180 || name.Any(c => char.IsControl(c) || c == '"' || c == '\\')) throw new InvalidOperationException("Invalid shader name.");
            var fallback = options.IncludeVrcFallback ? options.VrcFallbackTag : null;
            if (fallback != null && !new[] { "toonstandard", "standard", "unlit", "toon", "hidden" }.Contains(fallback)) throw new InvalidOperationException("Unsupported fallback tag.");
            if (particle && fallback == "toonstandard") fallback = "Particle";
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
            var passes = new List<SurfacePass>();
            if (!particle) FlattenSurfaces(baseRoot, "0", 0, passes);
            var b = new StringBuilder();
            b.AppendLine("Shader \"" + name + "\" {\nProperties {");
            foreach (var prop in properties.Where(p => p.Type == GraphValueType.Texture2D)) b.AppendLine(prop.Name + " (\"" + prop.DisplayName + "\", 2D) = \"white\" {}");
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
            for (var i = 0; i < passes.Count; i++) AddToonProperties(b, passes[i].Surface, i);
            var passCode = new StringBuilder();
            if (particle) passCode.Append(ParticlePass(root));
            else for (var i = 0; i < passes.Count; i++) passCode.Append(Pass(passes[i].Surface, i, passes[i].Offset));
            if (surfaceParticles) passCode.Append(SurfaceParticleShader.Pass(
                Scalar(root,"mask",1,true), Input(root,"albedo","float4(1,1,1,1)","color"), Input(root,"emission","float4(0,0,0,1)","color"), Scalar(root,"opacity",1), Input(root,"time","_Time.y","float",true),
                Prop(root,"density",.1), Prop(root,"size",.03), Prop(root,"lifetime",2), Prop(root,"speed",.2), Prop(root,"gravity",0), Prop(root,"spread",.05), IntProp(root,"blendMode",1,0,1), IntProp(root,"sourceUV",0,0,1) == 1));
            var shadowPass = !particle && options.IncludeShadowCaster ? Shadow(passes[0].Surface, passes[0].Offset) : "";
            b.AppendLine("}\nSubShader {\nTags { \"RenderType\"=\"" + (particle ? "Transparent" : "Opaque") + "\" \"Queue\"=\"" + (particle ? "Transparent" : "Geometry") + "\"" + (surfaceParticles ? " \"DisableBatching\"=\"True\"" : "") + (fallback == null ? "" : " \"VRCFallback\"=\"" + fallback + "\"") + " }");
            b.AppendLine("CGINCLUDE\n#include \"UnityCG.cginc\"\n#include \"Lighting.cginc\"\n#include \"AutoLight.cginc\"\n#include \"UnityPBSLighting.cginc\"");
            b.AppendLine("float4 _Color;");
            foreach (var prop in properties) b.AppendLine(prop.Type == GraphValueType.Texture2D ? "sampler2D " + prop.Name + "; float4 " + prop.Name + "_ST;" : (prop.Type == GraphValueType.Float ? "float " : "float4 ") + prop.Name + ";");
            if (live.Any(id => nodes[id].Operation == "core.audioLink")) b.AppendLine(AudioLinkShader.Hlsl);
            b.AppendLine("#ifndef SHADOW_COORDS\n#define SHADOW_COORDS(index)\n#endif");
            b.AppendLine(Helpers);
            b.AppendLine(ProceduralShader.Hlsl);
            b.AppendLine(DistortionShader.Hlsl);
            b.AppendLine(code.ToString());
            b.AppendLine("ENDCG\n" + passCode + shadowPass + "}\nFallback Off\n}");
            if (passes.Count > 1) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.shell", root.Id, (passes.Count - 1) + " extra transparent mesh pass" + (passes.Count == 2 ? "" : "es") + " per view; normal offset does not expand renderer bounds. Overlapping transparent objects can sort imperfectly."));
            if (live.Any(id => nodes[id].Operation == "core.musgrave" || nodes[id].Operation == "core.voronoi" || (nodes[id].Operation == "core.noise" && (int?)nodes[id].Properties["dimensions"] == 4)))
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.procedural", "$", "Fractal, cellular and 4D patterns cost more than 2D noise; start with few detail layers, especially across shells."));
            if (live.Any(id => nodes[id].Operation == "core.vertexMotion")) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "bounds.displacement", "$", "Vertex displacement requires mesh/SkinnedMeshRenderer bounds large enough for the motion."));
            if (surfaceParticles) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.surfaceParticles", root.Id, "An extra PC geometry pass processes every source triangle; density controls visible particles, not geometry work. Density depends on mesh topology; particles follow the current pose. Expand renderer bounds for outward motion. Stereo and VRChat client behavior need validation."));
            if (particle && root.Properties["softDistance"] != null && (double)root.Properties["softDistance"] > 0)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.particleDepth", root.Id, "Soft intersections require a camera depth texture. Set soft distance to 0 when unavailable; transparent overdraw and depth sampling add cost."));
            return b.ToString();
        }

        void Visit(GraphNode node, HashSet<string> live)
        {
            var queue = new Queue<GraphNode>(); queue.Enqueue(node);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); if (!live.Add(current.Id)) continue;
            var ports = NodeCatalog.Ports(current.Operation, false).Concat(new[] { "x", "position", "uv", "time" });
            foreach (var port in ports.Distinct()) { var source = Source(current, port); if (source != null) queue.Enqueue(source); }
            }
        }
        void FlattenSurfaces(GraphNode node, string inheritedOffset, int traversalDepth, List<SurfacePass> result)
        {
            if (traversalDepth > 64) throw new InvalidOperationException("Nested shell traversal exceeds depth limit.");
            if (node == null) throw new InvalidOperationException("Connect Toon, Unlit or PBR to each surface socket.");
            if (node.Operation == "core.surfaceParticles") throw new InvalidOperationException("Surface Particles must be the final surface before Output; nesting is not supported.");
            if (node.Operation != "core.shell") { CheckSurface(node); if (result.Count >= 9) throw new InvalidOperationException("Nested shells support at most 8 transparent shell layers (9 leaf surfaces)."); result.Add(new SurfacePass { Surface = node, Offset = inheritedOffset }); return; }
            FlattenSurfaces(Source(node, "base"), inheritedOffset, traversalDepth + 1, result);
            var offset = "(" + inheritedOffset + "+" + Scalar(node, "offset", .02, true) + ")";
            FlattenSurfaces(Source(node, "layer"), offset, traversalDepth + 1, result);
        }
        static void CheckSurface(GraphNode n) { if (n == null || !new[] { "core.toonSurface", "core.unlitSurface", "core.pbrSurface" }.Contains(n.Operation)) throw new InvalidOperationException(n != null && n.Operation == "core.particleSurface" ? "Particle Surface cannot be combined with Shell or other surface passes." : "Connect Toon, Unlit or PBR to each surface socket."); }
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
            string uv = DefaultUV(n);
            switch (n.Operation)
            {
                case "core.previewVector": body = Source(n,"normal") != null ? "float4(" + P("normal","float3(0,0,1)","vector3") + "*.5+.5,1)" : "float4(" + P("uv",uv,"vector2") + ",0,1)"; break;
                case "core.particleColor": body = port == "alpha" ? "input.color.a" : "input.color"; break;
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
                    body = NoiseBody(n, P("uv", uv, "vector2"), vertex);
                    if (port == "color") body = "float4(" + body + "," + body + "," + body + ",1)"; break;
                case "core.musgrave": case "core.voronoi": case "core.checker": case "core.wave":
                    body = ProceduralBody(n, uv, vertex, port); break;
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
                case "core.uvDistort":
                    var baseUV = P("uv", uv, "vector2");
                    body = "NX_Warp(" + baseUV + "," + IntProp(n,"mode",0,0,6) + ",(" + S("strength",.05) + "*saturate(" + S("mask",1) + "))," + Prop(n,"scale",5) + "," + P("time","_Time.y","float") + "*" + Prop(n,"speed",1) + "," + Vec(n,"center",.5,.5) + "," + Vec(n,"direction",1,1) + "," + Vec(n,"axes",1,1) + "," + Prop(n,"radius",.5) + "," + Prop(n,"falloff",1) + "," + IntProp(n,"detail",1,1,6) + "," + "(" + P("flow","float4(.5,.5,0,1)","color") + ").rg" + ")";
                    if (port == "offset") body = "(" + body + "-" + baseUV + ")";
                    break;
                case "core.gradient":
                    body = "NX_Gradient(" + P("uv",uv,"vector2") + "," + IntProp(n,"mode",0,0,2) + "," + Vec(n,"center",.5,.5) + "," + Prop(n,"angle",0) + "," + Prop(n,"radius",.5) + ")";
                    if (port == "color") body = "float v=" + body + "; return float4(v,v,v,1);";
                    break;
                case "core.uvTile": body = "NX_TileUV(" + P("uv",uv,"vector2") + "," + IntProp(n,"mode",0,0,2) + "," + Vec(n,"tiling",1,1) + "," + Vec(n,"offset",0,0) + ")"; break;
                case "core.posterize": body = "NX_Posterize(" + S("value",0) + "," + S("levels",4) + ")"; break;
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
        string StringProp(GraphNode n, string key, string fallback, params string[] allowed)
        {
            var value = n.Properties[key] == null ? fallback : (string)n.Properties[key];
            if (!allowed.Contains(value, StringComparer.Ordinal)) throw new InvalidOperationException("Unsupported " + key + ": " + value);
            return value;
        }
        int IntProp(GraphNode n, string key, int fallback, int min, int max)
        {
            var token = n.Properties[key];
            if (token == null) return fallback;
            if ((token.Type != JTokenType.Integer && token.Type != JTokenType.Float) || (double)token != Math.Truncate((double)token) || (double)token < min || (double)token > max) throw new InvalidOperationException("Expected integer " + key + " in range " + min + ".." + max + ".");
            return (int)token;
        }
        string DefaultUV(GraphNode n)
        {
            var source = StringProp(n, "coordinateSource", "uv0", "uv0", "uv1", "uv2", "uv3", "object", "world", "polar", "panosphere", "matcap");
            switch (source)
            {
                case "uv1": return "input.uv1";
                case "uv2": return "input.uv2";
                case "uv3": return "input.uv3";
                case "object": return "input.originalLocal.xz";
                case "world": return "input.originalWs.xz";
                case "polar": return "NX_Polar(input.uv,float2(.5,.5),1,1)";
                case "panosphere": return "NX_PanoUV(normalize(input.originalWs-_WorldSpaceCameraPos))";
                case "matcap": return "NX_MatcapUV(normalize(input.originalWs-_WorldSpaceCameraPos),normalize(input.n))";
                default: return "input.uv";
            }
        }
        string NoiseBody(GraphNode n, string selectedUV, bool vertex)
        {
            var dimensions = IntProp(n, "dimensions", 2, 1, 4);
            var time = Input(n, "time", "_Time.y", "float", vertex);
            var speed = Prop(n, "speed", 1);
            var scale = Prop(n, "scale", 5);
            selectedUV = Input(n, "uv", selectedUV, "vector2", vertex);
            if (dimensions == 1) return "NX_Noise1(" + Input(n, "x", "(" + selectedUV + ").x", "float", vertex) + "*" + scale + "+" + time + "*" + speed + ")";
            if (dimensions == 2) return "NX_Noise(" + selectedUV + "*" + scale + "+float2(1,.731)*" + time + "*" + speed + ")";
            var space = StringProp(n, "coordinateSpace", "object", "object", "world");
            var pos = Input(n, "position", space == "world" ? "input.originalWs" : "input.originalLocal", "vector3", vertex);
            if (dimensions == 3) return "NX_Noise3(" + pos + "*" + scale + "+float3(1,.731,.357)*" + time + "*" + speed + ")";
            return "NX_Noise4(float4(" + pos + ".xyz*" + scale + "," + time + "*" + speed + "))";
        }
        string ProceduralBody(GraphNode n, string selectedUV, bool vertex, string port)
        {
            var dimensions = IntProp(n, "dimensions", 2, 2, 3);
            var time = Input(n, "time", "_Time.y", "float", vertex);
            var space = StringProp(n, "coordinateSpace", "object", "object", "world");
            var p = Input(n, "position", space == "world" ? "input.originalWs" : "input.originalLocal", "vector3", vertex);
            var source = Input(n, "uv", selectedUV, "vector2", vertex);
            var scale = Prop(n, "scale", 5);
            var speed = Prop(n, "speed", 0);
            var pos = dimensions == 2 ? "float3(" + source + "*" + scale + ",0)" : "(" + p + "*" + scale + ")";
            var shifted = "(" + pos + (dimensions == 2 ? "+float3(1,.731,0)*" : "+float3(1,.731,.357)*") + time + "*" + speed + ")";
            string value;
            switch (n.Operation)
            {
                case "core.musgrave": value = "NX_Musgrave(" + shifted + "," + IntProp(n,"octaves",4,1,8) + "," + Prop(n,"lacunarity",2) + "," + Prop(n,"gain",.5) + "," + IntProp(n,"mode",0,0,2) + ")"; break;
                case "core.voronoi": value = "NX_Voronoi(" + shifted + "," + Prop(n,"randomness",1) + "," + dimensions + ")"; break;
                case "core.checker": value = "NX_Checker(" + shifted + "," + dimensions + ")"; break;
                default: value = "NX_Wave(" + pos + "," + IntProp(n,"mode",0,0,1) + "," + IntProp(n,"axis",0,0,2) + "," + time + "*" + speed + ")"; break;
            }
            return port == "color" ? "float4(" + value + "," + value + "," + value + ",1)" : value;
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

        void AddToonProperties(StringBuilder b, GraphNode surface, int passIndex)
        {
            if (surface.Operation != "core.toonSurface") return;
            var shell = passIndex > 0;
            foreach (var setting in new[] { "threshold", "softness", "shadowStrength" })
            {
                if (surface.Properties[setting + "ParameterId"] != null) continue;
                var symbol = ToonSymbol(setting, passIndex);
                b.AppendLine(symbol + " (\"" + (shell ? "Shell" + (passIndex == 1 ? "" : passIndex.ToString(CultureInfo.InvariantCulture)) + " " : "") + setting + "\", Range(0,1)) = " + Prop(surface,setting,setting == "threshold" ? .5 : setting == "softness" ? .05 : 1));
                properties.Add(new MaterialProperty { Name=symbol, DisplayName=setting, Type=GraphValueType.Float, Binding=GraphBindingKind.Material });
            }
        }
        static string ToonSymbol(string setting, int passIndex)
        {
            return "_NXSG_" + (passIndex == 0 ? "" : "Shell" + (passIndex == 1 ? "" : passIndex.ToString(CultureInfo.InvariantCulture))) + (setting == "threshold" ? "ToonThreshold" : setting == "softness" ? "ToonSoftness" : "ShadowStrength");
        }
        string ToonSetting(GraphNode surface,string setting,int passIndex)
        {
            if (surface.Operation != "core.toonSurface") return "0";
            var id=(string)surface.Properties[setting+"ParameterId"];
            if(id==null) return ToonSymbol(setting,passIndex);
            var parameter=graph.Parameters.FirstOrDefault(p=>p.Id==id);
            if(parameter==null || parameter.Type!=GraphValueType.Float) throw new InvalidOperationException("Missing scalar Toon parameter: "+id);
            return parameter.Binding==GraphBindingKind.Constant ? Literal(parameter.DefaultValue,"float") : ParameterName(id);
        }

        string Pass(GraphNode surface, int passIndex, string offset)
        {
            var displacement = Scalar(surface,"displacement",0,true);
            var shell = passIndex > 0;
            var color = Input(surface,"albedo","float4(1,1,1,1)","color");
            var emission = Input(surface,"emission","float4(0,0,0,1)","color");
            var opacity = Scalar(surface,"opacity",1);
            var normal = Input(surface,"normal","float3(0,0,1)","vector3");
            var metallic = Scalar(surface,"metallic",0);
            var roughness = Scalar(surface,"roughness",.5);
            var threshold=ToonSetting(surface,"threshold",passIndex);var softness=ToonSetting(surface,"softness",passIndex);var shadow=ToonSetting(surface,"shadowStrength",passIndex);
            var b=new StringBuilder("Pass {\nName \""+(shell?(passIndex == 1 ? "Shell" : "Shell" + passIndex.ToString(CultureInfo.InvariantCulture)):"ForwardBase")+"\"\nTags { \"LightMode\"=\""+(shell?"Always":"ForwardBase")+"\" }\nCull Back\n"+(shell?"ZWrite Off\nBlend SrcAlpha OneMinusSrcAlpha":"ZWrite On")+"\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vert\n#pragma fragment frag\n#pragma multi_compile_fwdbase\n#pragma multi_compile_instancing\n");
            b.AppendLine("NXInput vert(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*("+displacement+"+"+offset+"); NXInput o=NX_Make(v); o.originalLocal=input.originalLocal; o.originalWs=input.originalWs; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_SHADOW(o); return o; }");
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
        string ParticlePass(GraphNode surface)
        {
            var color = Input(surface, "albedo", "float4(1,1,1,1)", "color");
            var emission = Input(surface, "emission", "float4(0,0,0,1)", "color");
            var opacity = Scalar(surface, "opacity", 1);
            var softToken = surface.Properties["softDistance"];
            var soft = softToken != null && (softToken.Type == JTokenType.Integer || softToken.Type == JTokenType.Float) && (double)softToken > 0;
            var blendToken = surface.Properties["blendMode"];
            var additive = blendToken != null && (blendToken.Type == JTokenType.Integer || blendToken.Type == JTokenType.Float) && (double)blendToken == 1;
            var b = new StringBuilder("Pass {\nName \"Particle\"\nTags { \"LightMode\"=\"Always\" }\nCull Off\nZWrite Off\nBlend SrcAlpha ");
            b.AppendLine(additive ? "One" : "OneMinusSrcAlpha");
            b.AppendLine("CGPROGRAM\n#pragma target 3.5\n#pragma vertex vertParticle\n#pragma fragment fragParticle\n#pragma multi_compile_instancing");
            if (soft) b.AppendLine("UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture);");
            b.AppendLine("NXInput vertParticle(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput o=NX_Make(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);" + (soft ? " o.screenPos=ComputeScreenPos(o.pos); COMPUTE_EYEDEPTH(o.screenPos.z);" : "") + " return o; }");
            b.Append("float4 fragParticle(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float4 c=(").Append(color).Append(")*_Color*input.color; float3 e=(").Append(emission).Append(").rgb*input.color.rgb; float alpha=saturate(c.a*").Append(opacity).Append(");");
            if (soft)
            {
                b.AppendLine(" float rawDepth=UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture,input.screenPos.xy/input.screenPos.w).r; float sceneEyeDepth=LinearEyeDepth(rawDepth);");
                b.AppendLine("#if defined(UNITY_REVERSED_Z)\nrawDepth=1-rawDepth;\n#endif");
                b.AppendLine(" sceneEyeDepth=lerp(sceneEyeDepth,lerp(_ProjectionParams.y,_ProjectionParams.z,rawDepth),unity_OrthoParams.w); alpha*=saturate((sceneEyeDepth-input.screenPos.z)/max(.0001," + Prop(surface,"softDistance",0) + "));");
            }
            b.AppendLine(" return float4(c.rgb+e,alpha); }\nENDCG\n}");
            return b.ToString();
        }
        string Shadow(GraphNode surface, string offset)
        {
            var displacement=Scalar(surface,"displacement",0,true);
            var opacity=Scalar(surface,"opacity",1);
            var color=Input(surface,"albedo","float4(1,1,1,1)","color");
            return "Pass {\nName \"ShadowCaster\"\nTags { \"LightMode\"=\"ShadowCaster\" }\nZWrite On\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vertShadow\n#pragma fragment fragShadow\n#pragma multi_compile_shadowcaster\n#pragma multi_compile_instancing\nstruct NXShadow { V2F_SHADOW_CASTER; float2 uv:TEXCOORD1; float3 ws:TEXCOORD2; float3 normal:TEXCOORD3; float3 local:TEXCOORD4; float2 uv1:TEXCOORD5; float2 uv2:TEXCOORD6; float2 uv3:TEXCOORD7; float3 originalWs:TEXCOORD8; float3 originalLocal:TEXCOORD9; float4 color:TEXCOORD10; UNITY_VERTEX_OUTPUT_STEREO };\nNXShadow vertShadow(NXApp v){UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=(v.normal*("+displacement+"+"+offset+")); NXShadow o; UNITY_INITIALIZE_OUTPUT(NXShadow,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.uv=v.uv; o.uv1=v.uv1; o.uv2=v.uv2; o.uv3=v.uv3; o.originalWs=input.originalWs; o.originalLocal=input.originalLocal; o.color=input.color; o.ws=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); o.local=v.vertex.xyz; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o); return o;}\nfloat4 fragShadow(NXShadow i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); NXInput input=(NXInput)0; input.uv=i.uv; input.uv1=i.uv1; input.uv2=i.uv2; input.uv3=i.uv3; input.originalWs=i.originalWs; input.originalLocal=i.originalLocal; input.color=i.color; input.ws=i.ws; input.n=i.normal; input.local=i.local; clip(("+color+").a*_Color.a*("+opacity+")-"+Prop(surface,"cutoff",.001)+"); SHADOW_CASTER_FRAGMENT(i);}\nENDCG\n}\n";
        }
        const string Helpers=@"
struct NXApp { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD1; float2 uv2:TEXCOORD2; float2 uv3:TEXCOORD3; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct NXInput { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD7; float2 uv2:TEXCOORD8; float2 uv3:TEXCOORD9; float3 ws:TEXCOORD1; float3 n:TEXCOORD2; float3 local:TEXCOORD3; float3 originalWs:TEXCOORD10; float3 originalLocal:TEXCOORD11; float3 tangent:TEXCOORD4; float3 bitangent:TEXCOORD5; float4 color:TEXCOORD12; float4 screenPos:TEXCOORD13; float2 sourceUV:TEXCOORD14; SHADOW_COORDS(6) UNITY_VERTEX_OUTPUT_STEREO };
NXInput NX_Make(NXApp v){ NXInput o=(NXInput)0; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.uv1=v.uv1; o.uv2=v.uv2; o.uv3=v.uv3; o.local=v.vertex.xyz; o.ws=mul(unity_ObjectToWorld,v.vertex).xyz; o.originalLocal=o.local; o.originalWs=o.ws; o.color=v.color; o.n=UnityObjectToWorldNormal(v.normal); o.tangent=UnityObjectToWorldDir(v.tangent.xyz); o.bitangent=cross(o.n,o.tangent)*v.tangent.w*unity_WorldTransformParams.w; return o; }
float4 NX_Splat(float x){return float4(x,x,x,x);}
float NX_Div(float a,float b){return a/((b<0?-1:1)*max(abs(b),.00001));}
float4 NX_Div(float4 a,float4 b){return a/((step(0,b)*2-1)*max(abs(b),.00001));}
float2 NX_Rotate(float2 uv,float2 center,float degrees){float a=radians(degrees);float s=sin(a),c=cos(a);uv-=center;return float2(c*uv.x-s*uv.y,s*uv.x+c*uv.y)+center;}
float2 NX_Polar(float2 uv,float2 center,float radial,float angular){float2 p=uv-center;return float2(length(p)*2*radial,(dot(p,p)<1e-12?.5:atan2(p.y,p.x)/6.28318530718+.5)*angular);}
float NX_Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float NX_Noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(NX_Hash(i),NX_Hash(i+float2(1,0)),f.x),lerp(NX_Hash(i+float2(0,1)),NX_Hash(i+float2(1,1)),f.x),f.y);}
float2 NX_PanoUV(float3 d){return float2(atan2(d.x,d.z)/6.28318530718+.5,asin(clamp(d.y,-1,1))/3.14159265359+.5);}
float2 NX_MatcapUV(float3 view,float3 normal){float3 n=normalize(mul((float3x3)UNITY_MATRIX_V,normal));return n.xy*.5+.5;}
float2 NX_Flipbook(float2 uv,float frame,float cols,float rows){float count=cols*rows;frame=floor(frame);frame=frame-floor(frame/count)*count;return (frac(uv)+float2(fmod(frame,cols),rows-1-floor(frame/cols)))/float2(cols,rows);}
float2 NX_Distort(float2 uv,float strength,float scale,float time){return uv+(float2(NX_Noise(uv*scale+time),NX_Noise(uv*scale+time+17.2))-.5)*strength;}
float3 NX_Normal(float4 encoded,float strength){float3 n=UnpackNormal(encoded);n.xy*=strength;n.z=sqrt(saturate(1-dot(n.xy,n.xy)));return normalize(n);}
";
    }
}
