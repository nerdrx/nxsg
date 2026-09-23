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
        bool volumeEnabled;
        bool wireframeEnabled;
        bool ltcgiEnabled;
        bool lightingControlsEnabled;
        int depth;

        sealed class SurfacePass
        {
            public GraphNode Surface;
            public string Offset;
        }

        public static bool IsAdvanced(ShaderGraph graph)
        {
            if (graph?.Nodes == null) return false;
            var ops = new HashSet<string> { "core.volumeSurface", "core.rayPosition", "core.sdfSphere", "core.sdfBox", "core.sdfTorus", "core.sdfBlend", "core.unlitSurface", "core.pbrSurface", "core.particleSurface", "core.particleColor", "core.particleInfo", "core.surfaceParticles", "core.fur", "core.tessellation", "core.fresnel", "core.colorRamp", "core.layer", "core.sticker", "core.dissolve", "core.flipbook", "core.uvDistort", "core.vertexMotion", "core.audioLink", "core.ltcgi", "core.darknessGlow", "core.shell", "core.normalMap", "core.previewVector", "core.musgrave", "core.voronoi", "core.checker", "core.wave", "core.gradient", "core.uvTile", "core.posterize", "core.absolute", "core.power", "core.sqrt", "core.sine", "core.cosine", "core.fraction", "core.floor", "core.ceil", "core.round", "core.step", "core.smoothstep", "core.remap", "core.pingPong", "core.splitColor", "core.combineColor", "core.luminance", "core.contrast", "core.saturation", "core.hueShift", "core.colorAdjust", "core.splitUV", "core.combineUV", "core.position", "core.normalDirection", "core.viewDirection", "core.vertexColor", "core.cameraDistance", "core.screenUV", "core.circleMask", "core.boxMask", "core.polygonMask", "core.starMask", "core.radialRays", "core.spiral", "core.brick", "core.hexGrid", "core.triplanarTexture", "core.matcapTexture", "core.rimGlow", "core.heightMask", "core.slopeMask", "core.distanceFade", "core.wireframe" };
            // Only reachable effects select the extended lowering; disconnected nodes never change shading.
            var connected = new HashSet<string>();
            var queue = new Queue<string>(graph.Nodes.Where(n => n?.Operation == "core.output").Select(n => n.Id));
            var incoming = (graph.Connections ?? new List<GraphConnection>()).Where(e => e?.From?.NodeId != null && e.To?.NodeId != null).ToLookup(e => e.To.NodeId);
            while (queue.Count > 0) { var id = queue.Dequeue(); if (!connected.Add(id)) continue; foreach (var edge in incoming[id]) queue.Enqueue(edge.From.NodeId); }
            var live = graph.Nodes.Where(n => n != null && connected.Contains(n.Id)).ToArray();
            var inferred = GraphTypes.Infer(graph);
            var liveById = live.ToDictionary(n => n.Id);
            var hasColorScalarEdge = (graph.Connections ?? new List<GraphConnection>()).Any(edge =>
                edge?.From != null && edge.To != null && connected.Contains(edge.From.NodeId) && connected.Contains(edge.To.NodeId) &&
                GraphTypes.PortType(graph, liveById[edge.From.NodeId], edge.From.PortId, inferred) == "color" &&
                GraphTypes.PortType(graph, liveById[edge.To.NodeId], edge.To.PortId, inferred) == "float");
            var hasTextureAlphaEdge = (graph.Connections ?? new List<GraphConnection>()).Any(edge =>
                edge?.From != null && edge.To != null && connected.Contains(edge.From.NodeId) &&
                liveById.TryGetValue(edge.From.NodeId, out var textureNode) && textureNode.Operation == "core.texture2D" && edge.From.PortId == "alpha");
            return live.Any(n => ops.Contains(n.Operation) || FeatureNodes.IsKnown(n.Operation)) || live.Any(n => (n.Operation == "core.noise" || n.Operation == "core.uv0" || n.Operation == "core.polarUV" || n.Operation == "core.texture2D") && IsAdvancedCoordinates(n, incoming[n.Id])) || live.Count(n => n.Operation == "core.texture2D") > 1 ||
                hasColorScalarEdge ||
                hasTextureAlphaEdge ||
                live.Any(n => n.Operation == "core.toonSurface" && (n.Properties?["useAlbedoAlpha"] != null || n.Properties?["opacity"] != null || n.Properties?["displacement"] != null || incoming[n.Id].Any(e => e.To.PortId == "opacity" || e.To.PortId == "displacement" || e.To.PortId == "normal") || HasLightingControls(n)));
        }

        static bool HasLightingControls(GraphNode node)
        {
            if (node == null || (node.Operation != "core.toonSurface" && node.Operation != "core.pbrSurface")) return false;
            return LightingValueDiffers(node, "lightingMin", 0) || LightingValueDiffers(node, "lightingMax", 0) || LightingValueDiffers(node, "lightingSaturation", 1);
        }

        static bool LightingValueDiffers(GraphNode node, string key, double defaultValue)
        {
            var value = node.Properties?[key];
            if (value == null) return false;
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) return true;
            return Math.Abs((double)value - defaultValue) > 0;
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
            volumeEnabled = root.Operation == "core.volumeSurface";
            var particle = root.Operation == "core.particleSurface" || volumeEnabled;
            var surfaceParticles = root.Operation == "core.surfaceParticles";
            ValidateParticleInfo(root, particle, surfaceParticles);
            var tessNode = root.Operation == "core.tessellation" ? root : null;
            if (surfaceParticles && Source(root, "base")?.Operation == "core.tessellation") throw new InvalidOperationException("Tessellation and Surface Particles cannot be combined in this version. Use a regular surface as Base.");
            if (tessNode != null && Source(tessNode, "base")?.Operation == "core.fur") throw new InvalidOperationException("Tessellation cannot wrap Fur; connect Tessellation to Toon, Unlit or PBR.");
            GraphNode furNode = root.Operation == "core.fur" ? root : null;
            var baseRoot = tessNode != null ? Source(tessNode, "base") : root.Operation == "core.fur" ? Source(root, "base") : surfaceParticles ? Source(root, "base") : root;
            if (surfaceParticles && baseRoot != null && baseRoot.Operation == "core.fur") { furNode = baseRoot; baseRoot = Source(baseRoot, "base"); }
            if (surfaceParticles && baseRoot == null) throw new InvalidOperationException("Connect a surface to Surface Particles Base.");
            if (furNode != null && baseRoot == null) throw new InvalidOperationException("Connect a surface to Fur Base.");
            var name = options.ShaderName;
            if (string.IsNullOrEmpty(name) || name.Length > 180 || name.Any(c => char.IsControl(c) || c == '"' || c == '\\')) throw new InvalidOperationException("Invalid shader name.");
            var fallback = options.IncludeVrcFallback ? options.VrcFallbackTag : null;
            if (fallback != null && !new[] { "toonstandard", "standard", "unlit", "toon", "hidden" }.Contains(fallback)) throw new InvalidOperationException("Unsupported fallback tag.");
            if (particle && fallback == "toonstandard") fallback = "Particle";
            var live = new HashSet<string>();
            Visit(root, live);
            if (!volumeEnabled && live.Any(id => nodes[id].Operation == "core.rayPosition")) throw new InvalidOperationException("Ray Position requires Volume Surface connected directly to Output.");
            if (volumeEnabled && live.Any(id => new[]{"core.particleInfo", "core.ltcgi", "core.wireframe", "core.refraction", "core.parallaxOcclusion"}.Contains(nodes[id].Operation))) throw new InvalidOperationException("Volume Surface cannot evaluate particle data, lighting, wireframe, refraction or parallax branches inside its march loop.");
            var refracts = live.Any(id => nodes[id].Operation == "core.refraction");
            if(refracts && (particle || surfaceParticles)) throw new InvalidOperationException("Screen refraction currently supports regular mesh surfaces, not particle surfaces.");
            var wireNode = live.Select(id => nodes[id]).FirstOrDefault(n => n.Operation == "core.wireframe");
            wireframeEnabled = wireNode != null;
            if (wireframeEnabled && particle) throw new InvalidOperationException("Wireframe currently supports Toon, Unlit, PBR and Shell surfaces; use those for mesh edges.");
            if (wireframeEnabled && surfaceParticles)
            {
                var particleInputs = new HashSet<string>();
                foreach (var port in new[]{"albedo","emission","opacity","mask","time"})
                { var source = Source(root,port); if(source != null) Visit(source,particleInputs); }
                if(particleInputs.Any(id=>nodes[id].Operation=="core.wireframe"))
                    throw new InvalidOperationException("Connect Wireframe to the Base surface, not the generated particle inputs.");
            }
            foreach (var node in live.Select(id => nodes[id]).OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                if (node.Version != 1) throw new InvalidOperationException("Unsupported node version: " + node.Id);
                if (node.Operation != "core.texture2D" && node.Operation != "core.sticker" && node.Operation != "core.triplanarTexture" && node.Operation != "core.matcapTexture" && node.Operation != "core.parallaxOcclusion" && node.Operation != "core.chromaticTexture" && node.Operation != "core.interiorMapping" && node.Operation != "core.textureBomb") continue;
                var id = (string)node.Properties["resourceId"];
                var resource = (graph.Resources ?? new List<GraphResource>()).FirstOrDefault(r => r.Id == id);
                if (resource == null || resource.Kind != "texture2D") throw new InvalidOperationException("Missing texture resource: " + id);
                if (textureNames.ContainsKey(id)) continue;
                var symbol = textureNames.Count == 0 ? "_MainTex" : "_NXSG_Tex_" + Hash(id);
                textureNames.Add(id, symbol);
                properties.Add(new MaterialProperty { Name = symbol, DisplayName = TextureSlotLabels.DisplayName(graph, id), Type = GraphValueType.Texture2D, Binding = GraphBindingKind.Material, ResourceId = id, ResourceUri = resource.Uri });
            }
            if(live.Any(id=>nodes[id].Operation=="core.avatarMotion"))
                foreach(var axis in new[]{"Speed","X","Y","Z"})
                    properties.Add(new MaterialProperty { Name="_NXSG_Motion"+axis, DisplayName="Motion "+axis, Type=GraphValueType.Float, Binding=GraphBindingKind.AnimatedMaterial });
            ltcgiEnabled = live.Any(id => nodes[id].Operation == "core.ltcgi");
            lightingControlsEnabled = live.Select(id => nodes[id]).Any(HasLightingControls);
            if (ltcgiEnabled && !options.LtcgiAvailable)
                throw new InvalidOperationException("LTCGI Lighting requires the optional at.pimaker.ltcgi package. Install LTCGI from https://ltcgi.dev, then rebuild the graph.");
            var passes = new List<SurfacePass>();
            if (!particle) FlattenSurfaces(baseRoot, "0", 0, passes);
            var b = new StringBuilder();
            b.AppendLine("Shader \"" + name + "\" {\nProperties {");
            foreach (var prop in properties.Where(p => p.Type == GraphValueType.Texture2D)) b.AppendLine(prop.Name + " (\"" + prop.DisplayName + "\", 2D) = \"white\" {}");
            b.AppendLine("_Color (\"Tint\", Color) = (1,1,1,1)");
            b.AppendLine(PreviewClock.Properties);
            if(live.Any(id=>nodes[id].Operation=="core.avatarMotion"))
                b.AppendLine("[Header(Avatar Motion Driver)] _NXSG_MotionSpeed (\"Motion speed (m/s)\", Float) = 0\n_NXSG_MotionX (\"Sideways speed (m/s)\", Float) = 0\n_NXSG_MotionY (\"Vertical speed (m/s)\", Float) = 0\n_NXSG_MotionZ (\"Forward speed (m/s)\", Float) = 0");
            b.AppendLine("[HideInInspector] _NXSG_AudioLinkPreview (\"Preview audio\", Float) = 0\n[HideInInspector] _NXSG_AudioLinkValue (\"Preview value\", Float) = 0");
            var symbols = new HashSet<string>(properties.Select(p => p.Name));
            string lastHeader = null;
            foreach (var parameter in (graph.Parameters ?? new List<GraphParameter>()).OrderBy(p=>MaterialGroups.HeaderFor(graph,p.Id)??""))
            {
                if (parameter.Binding == GraphBindingKind.Constant) continue;
                if (parameter.Binding != GraphBindingKind.Material && parameter.Binding != GraphBindingKind.AnimatedMaterial) throw new InvalidOperationException("Use the AudioLink node for audio; only constant/material/animated bindings are supported here.");
                var header = MaterialGroups.HeaderFor(graph,parameter.Id);
                if(header!=null&&header!=lastHeader){b.AppendLine(header);lastHeader=header;}
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
            if (volumeEnabled) passCode.Append(VolumePass(root));
            else if (particle) passCode.Append(ParticlePass(root));
            else for (var i = 0; i < passes.Count; i++) passCode.Append(Pass(passes[i].Surface, i, passes[i].Offset, tessNode));
            bool cardsOnly = furNode != null && IntProp(furNode, "cardsOnly", 0, 0, 1) == 1;
            if (furNode != null) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "lighting.forwardAdd", furNode.Id, "Additional pixel lights affect the lit base surface only; fur overlays use the main light and ambient lighting."));
            if (furNode != null && !cardsOnly)
            {
                passCode.Append(FurShader.Pass(
                    Input(furNode, "rootColor", "float4(.2,.1,.05,1)", "color"), Input(furNode, "tipColor", "float4(.8,.6,.3,1)", "color"), Scalar(furNode, "length", .04, true), Scalar(furNode, "density", 100), Scalar(furNode, "thickness", .35), Scalar(furNode, "mask", 1), Input(furNode, "groom", "float3(0,0,0)", "vector3", true), Input(furNode, "time", "NXSG_Time()", "float", true), IntProp(furNode, "layers", 16, 4, 32), double.Parse(RawProp(furNode, "taper", 1), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "gravity", .1), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "windStrength", .1), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "windSpeed", 1), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "windScale", 2), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "rimStrength", .25), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "lodNear", 5), CultureInfo.InvariantCulture), double.Parse(RawProp(furNode, "lodFar", 15), CultureInfo.InvariantCulture), IntProp(furNode, "minLayers", 4, 1, 32), IntProp(furNode,"receiveShadows",1,0,1)==1));
            }
            if (furNode != null && (cardsOnly || IntProp(furNode, "fins", 0, 0, 1) == 1))
            {
                passCode.Append(FurFinShader.Pass(Input(furNode,"rootColor","float4(.2,.1,.05,1)","color"),Input(furNode,"tipColor","float4(.8,.6,.3,1)","color"),Scalar(furNode,"length",.04,true),Scalar(furNode,"density",100,true),Scalar(furNode,"thickness",.35),Scalar(furNode,"mask",1),Input(furNode,"groom","float3(0,0,0)","vector3",true),Input(furNode,"time","NXSG_Time()","float",true),double.Parse(RawProp(furNode,"taper",1),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"gravity",.1),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"windStrength",.1),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"windSpeed",1),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"windScale",2),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"rimStrength",.25),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"finOpacity",.7),CultureInfo.InvariantCulture), IntProp(furNode,"receiveShadows",1,0,1)==1, cardsOnly));
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,"cost.furFins",furNode.Id,cardsOnly ? "Fur cards use one geometry pass with three cards per selected triangle, no shells. Shared edges duplicate cards; mesh density controls coverage and cost. Expand renderer bounds; transparent sorting still applies." : "Fur fins add one geometry pass with three edge strips per source triangle. Grazing opacity approximates silhouettes without mesh adjacency; bounds and transparent sorting still apply."));
            }
            if (surfaceParticles) passCode.Append(SurfaceParticleShader.Pass(
                Scalar(root,"mask",1,true), Input(root,"albedo","float4(1,1,1,1)","color"), Input(root,"emission","float4(0,0,0,1)","color"), Scalar(root,"opacity",1), Input(root,"time","NXSG_Time()","float",true),
                Scalar(root,"density",.1,true), Input(root,"emissionRate",root.Properties["emissionRate"] == null ? "1.0/max(" + Scalar(root,"lifetime",2,true) + ",0.0001)" : Prop(root,"emissionRate",0),"float",true), Scalar(root,"size",.03,true), Scalar(root,"lifetime",2,true), Scalar(root,"speed",.2,true), Scalar(root,"gravity",0,true), Scalar(root,"spread",.05,true), IntProp(root,"blendMode",1,0,1), IntProp(root,"sourceUV",0,0,1) == 1, Scalar(root,"edgeSharpness",0), Source(root,"emissionRate") != null || Source(root,"lifetime") != null,
                ParticleCurve(root, "sizeCurve", false, "1"), ParticleCurve(root, "colorCurve", true, "float4(1,1,1,1)"), ParticleCurve(root, "opacityCurve", false, "1")));
            var screenDependentShadow = !particle && options.IncludeShadowCaster &&
                ((IntProp(passes[0].Surface,"useAlbedoAlpha",1,0,1)==1 && ContainsScreenDependentOperation(passes[0].Surface, "albedo")) || ContainsScreenDependentOperation(passes[0].Surface, "opacity"));
            if (screenDependentShadow)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "shadow.screenDependent", passes[0].Surface.Id, "View-dependent opacity or enabled albedo alpha disables the shadow/depth pass. Disable Use albedo alpha when only the surface color depends on the camera."));
            var shadowPass = !particle && options.IncludeShadowCaster && !screenDependentShadow ? Shadow(passes[0].Surface, passes[0].Offset, tessNode) : "";
            b.AppendLine("}\nSubShader {\nTags { \"RenderType\"=\"" + (particle || refracts ? "Transparent" : "Opaque") + "\" \"Queue\"=\"" + (particle || refracts ? "Transparent" : "Geometry") + "\"" + (surfaceParticles || volumeEnabled ? " \"DisableBatching\"=\"True\"" : "") + (ltcgiEnabled ? " \"LTCGI\"=\"ALWAYS\"" : "") + (fallback == null ? "" : " \"VRCFallback\"=\"" + fallback + "\"") + " }");
            if(refracts) b.AppendLine("GrabPass { \"_NXSG_GrabTexture\" }");
            b.AppendLine("CGINCLUDE\n#include \"UnityCG.cginc\"\n#include \"Lighting.cginc\"\n#include \"AutoLight.cginc\"\n#include \"UnityPBSLighting.cginc\"");
            b.AppendLine("float4 _Color;");
            foreach (var prop in properties) b.AppendLine(prop.Type == GraphValueType.Texture2D ? "sampler2D " + prop.Name + "; float4 " + prop.Name + "_ST;" : (prop.Type == GraphValueType.Float ? "float " : "float4 ") + prop.Name + ";");
            if (live.Any(id => nodes[id].Operation == "core.audioLink")) b.AppendLine(AudioLinkShader.Hlsl);
            b.AppendLine("#ifndef SHADOW_COORDS\n#define SHADOW_COORDS(index)\n#endif");
            b.AppendLine(PreviewClock.Hlsl);

            b.AppendLine(Helpers);
            b.AppendLine(VolumeShader.Helpers);
            if (lightingControlsEnabled) b.AppendLine(LightingHelpers);
            if(furNode!=null) {
                int quality=IntProp(furNode,"selfShadowQuality",0,0,3);
                b.AppendLine(FurLighting.Helpers(quality,double.Parse(RawProp(furNode,"selfShadowStrength",1),CultureInfo.InvariantCulture),double.Parse(RawProp(furNode,"selfShadowBias",.03),CultureInfo.InvariantCulture)));
                if(quality>0)diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,"cost.furSelfShadow",furNode.Id,"Fur self-shadowing samples the local strand volume "+(quality==1?4:quality==2?8:16)+" times per shaded fur fragment. Cost grows with shell layers and screen coverage; it approximates local straight fur, not shadows across separate body parts."));
            }
            if (ltcgiEnabled) b.AppendLine(LtcgiShader.Hlsl);
            if(refracts) b.AppendLine("sampler2D _NXSG_GrabTexture; float4 _NXSG_GrabTexture_TexelSize;");
            b.AppendLine(FeatureShader.Helpers);
            if (live.Any(id => nodes[id].Operation == "core.glitter")) b.AppendLine(GlitterShader.Hlsl);
            b.AppendLine(ProceduralShader.Hlsl);
            b.AppendLine(DistortionShader.Hlsl);
            b.AppendLine(code.ToString());
            b.AppendLine("ENDCG\n" + passCode + shadowPass + "}\nCustomEditor \"NXSG.Editor.NXSGMaterialShaderGUI\"\nFallback Off\n}");
            if (refracts) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,"cost.refraction","$","Refraction copies the framebuffer into a shared named GrabPass texture and uses the transparent queue. It cannot refract off-screen objects; overlapping transparent materials and stereo need validation."));
            if (live.Any(id => nodes[id].Operation == "core.textureBomb")) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,"cost.textureBomb","$","Texture Bomb blends four transformed texture samples, plus plain sampling when Blend is not constant one."));
            if (passes.Count > 1) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.shell", root.Id, (passes.Count - 1) + " extra transparent mesh pass" + (passes.Count == 2 ? "" : "es") + " per view; normal offset does not expand renderer bounds. Overlapping transparent objects can sort imperfectly."));
            if (passes.Count > 1) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "lighting.forwardAdd", root.Id, "Shell passes use LightMode Always and do not receive additional per-pixel lights; the base lit surface does."));
            if (live.Any(id => nodes[id].Operation == "core.musgrave" || nodes[id].Operation == "core.voronoi" || (nodes[id].Operation == "core.noise" && (int?)nodes[id].Properties["dimensions"] == 4)))
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.procedural", "$", "Fractal, cellular and 4D patterns cost more than 2D noise; start with few detail layers, especially across shells."));
            if (live.Any(id => nodes[id].Operation == "core.vertexMotion")) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "bounds.displacement", "$", "Vertex displacement requires mesh/SkinnedMeshRenderer bounds large enough for the motion."));
            if (surfaceParticles) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.surfaceParticles", root.Id, "An extra PC geometry pass processes every source triangle; density controls selected triangles, not geometry work. Four particles are emitted per generated subtriangle; high rates use tessellation up to level 64 and approximate the requested source-triangle rate. Particles follow the current pose; expand renderer bounds for outward motion. Stereo and VRChat client behavior need validation."));
            if (tessNode != null) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.tessellation", tessNode.Id, "Adaptive fractional-odd tessellation is PC-only. Detail factor is capped at 63; triangle cost grows roughly quadratically. Expand renderer bounds for height displacement; mobile, Fur composition and Surface Particles Base are unsupported."));
            if (furNode != null && !cardsOnly) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.fur", furNode.Id, "Fur emits " + IntProp(furNode, "layers", 16, 4, 32) + " transparent shell passes per view. Distance LOD reduces active shell coverage but does not remove draw calls; expand renderer bounds for strand length."));
            if (particle && root.Properties["softDistance"] != null && (double)root.Properties["softDistance"] > 0)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.particleDepth", root.Id, "Soft intersections require a camera depth texture. Set soft distance to 0 when unavailable; transparent overdraw and depth sampling add cost."));
            // NXInput can exceed SM4's 16 vertex outputs once light/shadow and stereo fields are present.
            // Declare the actual varying budget in every pass, including geometry and tessellation passes.
            return b.ToString().Replace("CGPROGRAM\n", "CGPROGRAM\n#pragma require interpolators32\n");
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
        bool ContainsScreenDependentOperation(GraphNode root, string port)
        {
            var operations = new HashSet<string> { "core.refraction", "core.screenUV", "core.cameraDistance", "core.viewDirection", "core.fresnel", "core.rimGlow", "core.matcapTexture", "core.interiorMapping" };
            var seen = new HashSet<string>();
            var stack = new Stack<GraphNode>();
            var first = Source(root, port);
            if (first != null) stack.Push(first);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!seen.Add(node.Id)) continue;
                if (operations.Contains(node.Operation)) return true;
                foreach (var input in NodeCatalog.Ports(node.Operation, false))
                {
                    var source = Source(node, input);
                    if (source != null) stack.Push(source);
                }
            }
            return false;
        }
        static string Key(string a, string b) { return a + ":" + b; }
        static string Hash(string value) { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2"))); }
        static string ParameterName(string id) { if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,96}$")) throw new InvalidOperationException("Unsupported parameter ID."); return "_NXSG_P_" + id.Replace('-', '_'); }
        static string Num(double value) { if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue) throw new InvalidOperationException("Shader numbers must be finite floats."); return ((float)value).ToString("R", CultureInfo.InvariantCulture); }
        // Keep ShaderLab defaults and numeric parsing raw; HLSL operands need token boundaries.
        static string NumberOperand(string literal) { return literal.StartsWith("-", StringComparison.Ordinal) ? "(" + literal + ")" : literal; }
        internal static string NumExpr(double value) { return NumberOperand(Num(value)); }
        string Prop(GraphNode n, string key, double fallback) { return NumberOperand(RawProp(n, key, fallback)); }
        string RawProp(GraphNode n, string key, double fallback) { var t = n.Properties[key]; if (t == null) return Num(fallback); if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new InvalidOperationException("Expected a number: " + n.Id + "." + key); return Num((double)t); }
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
            if (actual == "color" && expected == "float") return "dot((" + value + ").rgb,float3(.2126,.7152,.0722))";
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
            string M(string p, double def) { var value = Prop(n,p,def); return P(p,type == "color" ? "NX_Splat(" + value + ")" : value); }
            string uv = DefaultUV(n, vertex);
            switch (n.Operation)
            {
                case "core.previewVector": body = Source(n,"normal") != null ? "float4(" + P("normal","float3(0,0,1)","vector3") + "*.5+.5,1)" : "float4(" + P("uv",uv,"vector2") + ",0,1)"; break;
                case "core.particleColor": body = port == "alpha" ? "input.color.a" : "input.color"; break;
                case "core.particleInfo":
                    if (port != "age" && port != "random") throw new InvalidOperationException("Particle Info output must be age or random.");
                    body = port == "age" ? "input.particleAge" : "input.particleRandom";
                    break;
                case "core.rayPosition": body = "input.local"; break;
                case "core.sdfSphere": body = "(length("+P("position","input.local","vector3")+")-max(.00001,"+S("radius",.3)+"))"; break;
                case "core.sdfBox": body = "NX_SdfBox("+P("position","input.local","vector3")+","+P("size",n.Properties["size"] == null ? "float3(.3,.3,.3)" : Literal(n.Properties["size"],"vector3"),"vector3")+")"; break;
                case "core.sdfTorus": body = "NX_SdfTorus("+P("position","input.local","vector3")+",max(.00001,"+S("radius",.3)+"),max(.00001,"+S("thickness",.08)+"))"; break;
                case "core.sdfBlend": body = "NX_SdfBlend("+S("a",1)+","+S("b",1)+",max(0,"+S("smoothing",.1)+"),"+IntProp(n,"mode",0,0,2)+")"; break;
                case "core.value": body = Prop(n, "value", 0); break;
                case "core.constant": body = Literal(n.Properties["value"], type); break;
                case "core.parameter":
                    var parameter = graph.Parameters.Single(p => p.Id == (string)n.Properties["parameterId"]);
                    body = parameter.Binding == GraphBindingKind.Constant ? Literal(parameter.DefaultValue, type) : ParameterName(parameter.Id); break;
                case "core.time": body = "(NXSG_Time()*" + Prop(n, "speed", 1) + "+" + Prop(n, "offset", 0) + ")"; break;
                case "core.uv0": body = uv; break;
                case "core.objectUV": body = "input.local.xz"; break;
                case "core.worldUV": body = "input.ws.xz"; break;
                case "core.uvTransform": body = "(" + P("uv", uv) + "*" + Vec(n, "tiling", 1, 1) + "+" + Vec(n, "offset", 0, 0) + ")"; break;
                case "core.uvScroll": body = "(" + P("uv", uv) + "+" + Vec(n, "speed", .1, 0) + "*" + P("time", "NXSG_Time()", "float") + ")"; break;
                case "core.uvRotate": body = "NX_Rotate(" + P("uv", uv) + "," + Vec(n, "center", .5, .5) + "," + S("angle", 0) + ")"; break;
                case "core.polarUV": body = "NX_Polar(" + P("uv", uv) + "," + Vec(n, "center", .5, .5) + "," + Prop(n, "radialScale", 1) + "," + Prop(n, "angleScale", 1) + ")"; break;
                case "core.texture2D": body = port == "alpha" ? "(" + Sample(n, P("uv", uv, "vector2"), vertex) + ").a" : Sample(n, P("uv", uv, "vector2"), vertex); break;
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
                case "core.absolute": body = "abs(" + M("a", .5) + ")"; break;
                case "core.power": body = "pow(max(abs(" + M("a", .5) + "),.00001)," + M("b", 2) + ")"; break;
                case "core.sqrt": body = "sqrt(max(0," + M("a", .5) + "))"; break;
                case "core.sine": body = "sin(" + M("a", .5) + ")"; break;
                case "core.cosine": body = "cos(" + M("a", .5) + ")"; break;
                case "core.fraction": body = "frac(" + M("a", .5) + ")"; break;
                case "core.floor": body = "floor(" + M("a", .5) + ")"; break;
                case "core.ceil": body = "ceil(" + M("a", .5) + ")"; break;
                case "core.round": body = "floor(" + M("a", .5) + "+.5)"; break;
                case "core.step": body = "step(" + S("a", .5) + "," + S("b", 0) + ")"; break;
                case "core.smoothstep": body = "float t=saturate(NX_Div(" + S("value", 0) + "-" + S("low", 0) + "," + S("high", 1) + "-" + S("low", 0) + ")); return t*t*(3-2*t);"; break;
                case "core.remap": body = "(" + S("outMin", 0) + "+NX_Div(" + S("value", 0) + "-" + S("inMin", 0) + "," + S("inMax", 1) + "-" + S("inMin", 0) + ")*(" + S("outMax", 1) + "-" + S("outMin", 0) + "))"; break;
                case "core.pingPong": body = "(abs(frac(" + S("value", 0) + "/(2*max(abs(" + S("length", 1) + "),.00001))+.5)*2-1)*max(abs(" + S("length", 1) + "),.00001))"; break;
                case "core.splitColor": body = "(" + P("color", "float4(.5,.5,.5,.5)", "color") + ")." + port; break;
                case "core.combineColor": body = "float4(" + S("r", 0) + "," + S("g", 0) + "," + S("b", 0) + "," + S("a", 1) + ")"; break;
                case "core.luminance": body = "dot((" + P("color", "float4(.5,.5,.5,1)", "color") + ").rgb,float3(.2126,.7152,.0722))"; break;
                case "core.contrast": body = "float4 c=" + P("color", "float4(.5,.5,.5,1)", "color") + "; return float4((c.rgb-" + S("pivot", .5) + ")*" + S("amount", 1) + "+" + S("pivot", .5) + ",c.a);"; break;
                case "core.colorAdjust":
                    bool Changed(string setting, double neutral) => edges.ContainsKey(Key(n.Id, setting)) || (double?)n.Properties[setting] != null && (double)n.Properties[setting] != neutral;
                    body = "float4 c=" + P("color", "float4(.5,.5,.5,1)", "color") + ";";
                    if (Changed("hue", 0)) body += "c=NX_HueShift(c," + S("hue", 0) + ");";
                    if (Changed("saturation", 1)) body += "c.rgb=lerp(dot(c.rgb,float3(.2126,.7152,.0722)),c.rgb," + S("saturation", 1) + ");";
                    if (Changed("lift", 0)) body += "c.rgb=c.rgb+(1-c.rgb)*" + S("lift", 0) + ";";
                    if (Changed("gamma", 1)) body += "c.rgb=sign(c.rgb)*pow(abs(c.rgb),1/max(" + S("gamma", 1) + ",.0001));";
                    if (Changed("gain", 1)) body += "c.rgb*=" + S("gain", 1) + ";";
                    if (Changed("contrast", 1)) body += "c.rgb=(c.rgb-.5)*" + S("contrast", 1) + "+.5;";
                    if (Changed("exposure", 0)) body += "c.rgb*=exp2(" + S("exposure", 0) + ");";
                    body += "return c;";
                    break;
                case "core.hueShift": body = "NX_HueShift(" + P("color", "float4(1,0,0,1)", "color") + "," + S("hue", 0) + ")"; break;
                case "core.saturation": body = "float4(lerp(dot((" + P("color", "float4(.5,.5,.5,1)", "color") + ").rgb,float3(.2126,.7152,.0722)),(" + P("color", "float4(.5,.5,.5,1)", "color") + ").rgb," + S("amount", 1) + "),(" + P("color", "float4(.5,.5,.5,1)", "color") + ").a)"; break;
                case "core.splitUV": body = "(" + P("uv", uv, "vector2") + ")." + (port == "u" ? "x" : "y"); break;
                case "core.combineUV": body = "float2(" + S("u", 0) + "," + S("v", 0) + ")"; break;
                case "core.position": body = IntProp(n, "space", 0, 0, 1) == 1 ? "input.ws" : "input.local"; break;
                case "core.normalDirection": body = IntProp(n, "space", 1, 0, 1) == 1 ? "normalize(input.n)" : "normalize(mul(normalize(input.n),(float3x3)unity_ObjectToWorld))"; break;
                case "core.viewDirection": body = "normalize(_WorldSpaceCameraPos-input.ws)"; break;
                case "core.vertexColor": body = port == "alpha" ? "input.color.a" : "input.color"; break;
                case "core.cameraDistance": body = "distance(_WorldSpaceCameraPos,input.ws)"; break;
                case "core.screenUV": body = "(ComputeScreenPos(UnityWorldToClipPos(input.ws)).xy/ComputeScreenPos(UnityWorldToClipPos(input.ws)).w)"; break;
                case "core.wireframe":
                    if (vertex) throw new InvalidOperationException("Wireframe is a pixel mask; connect it to color, emission or opacity, not displacement or emitter mask.");
                    body = "NX_Wire(input.wireBary," + Prop(n,"width",1) + "," + Prop(n,"softness",1) + ")"; break;
                case "core.circleMask": body = "NX_ShapeEdge(length("+P("uv",uv,"vector2")+"-.5)-"+Prop(n,"radius",.4)+","+Prop(n,"softness",.02)+")"; break;
                case "core.boxMask": body = "float2 q=abs("+P("uv",uv,"vector2")+"-.5)-float2("+Prop(n,"width",.7)+","+Prop(n,"height",.7)+")*.5; return NX_ShapeEdge(max(q.x,q.y),"+Prop(n,"softness",.02)+");"; break;
                case "core.polygonMask": body = "NX_Polygon("+P("uv",uv,"vector2")+","+IntProp(n,"sides",6,3,32)+","+Prop(n,"radius",.4)+","+Prop(n,"rotation",0)+","+Prop(n,"softness",.02)+")"; break;
                case "core.starMask": body = "NX_Star("+P("uv",uv,"vector2")+","+IntProp(n,"points",5,3,32)+","+Prop(n,"inner",.2)+","+Prop(n,"outer",.45)+","+Prop(n,"rotation",0)+","+Prop(n,"softness",.02)+")"; break;
                case "core.radialRays": body = "float2 q="+P("uv",uv,"vector2")+"-.5; float wave=cos((atan2(q.y,q.x)+radians("+Prop(n,"rotation",0)+"))*"+IntProp(n,"count",12,1,128)+"); return smoothstep(-max(.0001,"+Prop(n,"softness",.02)+"),max(.0001,"+Prop(n,"softness",.02)+"),wave);"; break;
                case "core.spiral": body = "float2 q="+P("uv",uv,"vector2")+"-.5; float phase=frac(atan2(q.y,q.x)/6.2831853+length(q)*"+Prop(n,"turns",3)+"+"+Prop(n,"rotation",0)+"/360); return NX_ShapeEdge(abs(phase-.5)-max(0,"+Prop(n,"width",.2)+")*.5,.005);"; break;
                case "core.brick": body = "float2 p="+P("uv",uv,"vector2")+"*float2("+Prop(n,"tilingX",5)+","+Prop(n,"tilingY",8)+"); p.x+=floor(p.y)*.5; float2 edge=min(frac(p),1-frac(p)); return step(max(0,"+Prop(n,"mortar",.08)+")*.5,min(edge.x,edge.y));"; break;
                case "core.hexGrid": body = "NX_Hex("+P("uv",uv,"vector2")+","+Prop(n,"scale",8)+","+Prop(n,"width",.05)+")"; break;
                case "core.triplanarTexture":
                    var triPosition=P("position","input.local","vector3");
                    var triNormal=P("normal","normalize(mul(input.n,(float3x3)unity_ObjectToWorld))","vector3");
                    var triScale=Prop(n,"scale",1);
                    body="float3 p="+triPosition+"*"+triScale+"; float3 w=pow(abs("+triNormal+"),max(.001,"+Prop(n,"sharpness",4)+")); w/=max(dot(w,float3(1,1,1)),.00001); return "+Sample(n,"p.yz",vertex)+"*w.x+"+Sample(n,"p.xz",vertex)+"*w.y+"+Sample(n,"p.xy",vertex)+"*w.z;"; break;
                case "core.matcapTexture":
                    body=Sample(n,"(normalize(mul((float3x3)UNITY_MATRIX_V,"+P("normal","input.n","vector3")+")).xy*.5+.5)",vertex); break;
                case "core.rimGlow": body = "float4(" + P("color", "float4(1,1,1,1)", "color") + ".rgb*pow(saturate(1-dot(normalize(input.n),normalize(_WorldSpaceCameraPos-input.ws))),max(.0001," + S("power", 3) + "))," + P("color", "float4(1,1,1,1)", "color") + ".a)"; break;
                case "core.heightMask":
                    var heightPosition = P("position", "input.local", "vector3");
                    var heightAxis = IntProp(n, "axis", 1, 0, 2);
                    var heightComponent = heightAxis == 0 ? ".x" : heightAxis == 2 ? ".z" : ".y";
                    body = "saturate(NX_Div(" + heightPosition + heightComponent + "-" + Prop(n, "low", 0) + "," + Prop(n, "high", 1) + "-" + Prop(n, "low", 0) + "))"; break;
                case "core.slopeMask": body = "saturate(NX_Div(normalize(" + P("normal", "input.n", "vector3") + ").y-" + Prop(n, "low", 0) + "," + Prop(n, "high", 1) + "-" + Prop(n, "low", 0) + "))"; break;
                case "core.distanceFade": body = "saturate(NX_Div(" + Prop(n, "far", 10) + "-distance(_WorldSpaceCameraPos,input.ws)," + Prop(n, "far", 10) + "-" + Prop(n, "near", 0) + "))"; break;
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
                case "core.flipbook": body = "NX_Flipbook(" + P("uv", uv) + "," + P("time", "NXSG_Time()", "float") + "*" + Prop(n, "speed", 1) + "," + Prop(n, "columns", 1) + "," + Prop(n, "rows", 1) + ")"; break;
                case "core.uvDistort":
                    var baseUV = P("uv", uv, "vector2");
                    body = "NX_Warp(" + baseUV + "," + IntProp(n,"mode",0,0,6) + ",(" + S("strength",.05) + "*saturate(" + S("mask",1) + "))," + Prop(n,"scale",5) + "," + P("time","NXSG_Time()","float") + "*" + Prop(n,"speed",1) + "," + Vec(n,"center",.5,.5) + "," + Vec(n,"direction",1,1) + "," + Vec(n,"axes",1,1) + "," + Prop(n,"radius",.5) + "," + Prop(n,"falloff",1) + "," + IntProp(n,"detail",1,1,6) + "," + "(" + P("flow","float4(.5,.5,0,1)","color") + ").rg" + ")";
                    if (port == "offset") body = "(" + body + "-" + baseUV + ")";
                    break;
                case "core.gradient":
                    body = "NX_Gradient(" + P("uv",uv,"vector2") + "," + IntProp(n,"mode",0,0,2) + "," + Vec(n,"center",.5,.5) + "," + Prop(n,"angle",0) + "," + Prop(n,"radius",.5) + ")";
                    if (port == "color") body = "float v=" + body + "; return float4(v,v,v,1);";
                    break;
                case "core.uvTile": body = "NX_TileUV(" + P("uv",uv,"vector2") + "," + IntProp(n,"mode",0,0,2) + "," + Vec(n,"tiling",1,1) + "," + Vec(n,"offset",0,0) + ")"; break;
                case "core.posterize": body = "NX_Posterize(" + S("value",0) + "," + S("levels",4) + ")"; break;
                case "core.vertexMotion": body = "(sin(" + P("time", "NXSG_Time()", "float") + "*" + Prop(n, "speed", 1) + "+input.local.y*" + Prop(n, "frequency", 2) + ")*" + S("strength", .02) + ")"; break;
                case "core.avatarMotion":
                    body=port=="speed"?"max(0,_NXSG_MotionSpeed)":port=="sideways"?"_NXSG_MotionX":port=="vertical"?"_NXSG_MotionY":port=="forward"?"_NXSG_MotionZ":"float3(_NXSG_MotionX,_NXSG_MotionY,_NXSG_MotionZ)";break;
                case "core.darknessGlow":
                    if(vertex)throw new InvalidOperationException("Darkness Glow is a fragment lighting effect. Connect it to Emission.");
                    body="NX_DarkGlow(input,"+P("color","float4(1,1,1,1)","color")+","+S("strength",1)+","+S("threshold",.4)+","+S("softness",.2)+")"; break;
                case "core.ltcgi":
                    if (vertex) throw new InvalidOperationException("LTCGI Lighting is fragment-only; do not connect it to displacement, tessellation height or particle emitter inputs.");
                    body = "NX_Ltcgi(input," + P("albedo", "float4(1,1,1,1)", "color") + "," + P("normal", "float3(0,0,1)", "vector3") + "," + S("roughness", .5) + "," + S("metallic", 0) + "," + S("strength", 1) + ")"; break;
                case "core.audioLink": body = "NXSG_Audio(" + Prop(n, "band", 0) + "," + Prop(n, "gain", 1) + "," + Prop(n, "smoothing", .5) + "," + Prop(n, "fallback", 0) + "," + IntProp(n, "rangeEnabled", 0, 0, 1) + "," + Prop(n, "min", 0) + "," + Prop(n, "max", 1) + ")"; break;
                case "core.glitter":
                    if(vertex)throw new InvalidOperationException("Glitter needs fragment derivatives and view direction. Use it for color, emission or opacity, not vertex displacement.");
                    var glitterMask = port == "color" ? Eval(n,"value",false) : null;
                    body = port == "color"
                        ? "float4(("+P("color",n.Properties["color"]==null?"float4(1,1,1,1)":Literal(n.Properties["color"],"color"),"color")+").rgb*"+glitterMask+"*"+Prop(n,"brightness",2)+","+glitterMask+")"
                        : "NX_Glitter(input,"+P("uv",uv,"vector2")+","+Prop(n,"scale",60)+","+Prop(n,"density",.6)+","+Prop(n,"size",.16)+","+Prop(n,"sharpness",32)+","+Prop(n,"viewStrength",1)+","+P("time","NXSG_Time()","float")+","+Prop(n,"speed",1)+","+Prop(n,"twinkle",.3)+","+Prop(n,"seed",0)+","+S("mask",1)+")";
                    break;
                case "core.normalMap": body = "NX_Normal(" + P("color", "float4(.5,.5,1,1)", "color") + "," + Prop(n, "strength", 1) + ")*float3(1," + (IntProp(n,"flipGreen",0,0,1)==1 ? "-1" : "1") + ",1)"; break;
                default:
                    if (!FeatureNodes.IsKnown(n.Operation)) throw new InvalidOperationException("Unsupported value operation: " + n.Operation);
                    var resourceId=(string)n.Properties["resourceId"];
                    body=FeatureShader.Body(n,uv,vertex,P,S,(property,fallback)=>Prop(n,property,fallback),(coords,isVertex)=>Sample(n,coords,isVertex),resourceId!=null && textureNames.TryGetValue(resourceId,out var sampler)?sampler:null);
                    break;
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
            return vertex || volumeEnabled ? "tex2Dlod(" + tex + ",float4(" + transformed + ",0,0))" : "tex2D(" + tex + "," + transformed + ")";
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
        string DefaultUV(GraphNode n, bool vertex)
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
                case "panosphere": return (vertex ? "NX_PanoUV" : "NX_PanoFilteredUV") + "(normalize(input.originalWs-_WorldSpaceCameraPos))";
                case "matcap": return "NX_MatcapUV(normalize(input.originalWs-_WorldSpaceCameraPos),normalize(input.n))";
                default: return "input.uv";
            }
        }
        string NoiseBody(GraphNode n, string selectedUV, bool vertex)
        {
            var dimensions = IntProp(n, "dimensions", 2, 1, 4);
            var time = Input(n, "time", "NXSG_Time()", "float", vertex);
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
            var time = Input(n, "time", "NXSG_Time()", "float", vertex);
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
                b.Append("if(x<="+Num((double)z[0])+"){float t=saturate((x-"+NumExpr((double)a[0])+")/"+Num((double)z[0]-(double)a[0])+"); ");
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
                b.AppendLine(symbol + " (\"" + (shell ? "Shell" + (passIndex == 1 ? "" : passIndex.ToString(CultureInfo.InvariantCulture)) + " " : "") + setting + "\", Range(0,1)) = " + RawProp(surface,setting,setting == "threshold" ? .5 : setting == "softness" ? .05 : 1));
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

        const string WireGeometry = "[maxvertexcount(3)] void geomWire(triangle NXInput tri[3], inout TriangleStream<NXInput> stream) { NXInput o=tri[0]; o.wireBary=float3(1,0,0); stream.Append(o); o=tri[1]; o.wireBary=float3(0,1,0); stream.Append(o); o=tri[2]; o.wireBary=float3(0,0,1); stream.Append(o); stream.RestartStrip(); }";

        string Pass(GraphNode surface, int passIndex, string offset, GraphNode tessellation)
        {
            var displacement = Scalar(surface,"displacement",0,true);
            var tessHeight = tessellation == null ? "0" : "(" + Scalar(tessellation, "height", .5, true) + "-" + Prop(tessellation, "reference", .5) + ")*" + Prop(tessellation, "strength", .1);
            var shell = passIndex > 0;
            var color = Input(surface,"albedo","float4(1,1,1,1)","color");
            var emission = Input(surface,"emission","float4(0,0,0,1)","color");
            var opacity = Scalar(surface,"opacity",1);
            var normal = Input(surface,"normal","float3(0,0,1)","vector3");
            var metallic = Scalar(surface,"metallic",0);
            var roughness = Scalar(surface,"roughness",.5);
            var threshold=ToonSetting(surface,"threshold",passIndex);var softness=ToonSetting(surface,"softness",passIndex);var shadow=ToonSetting(surface,"shadowStrength",passIndex);
            var lighting = HasLightingControls(surface);
            var lightingMin = lighting ? Prop(surface, "lightingMin", 0) : null;
            var lightingMax = lighting ? Prop(surface, "lightingMax", 0) : null;
            var lightingSaturation = lighting ? Prop(surface, "lightingSaturation", 1) : null;
            var tess = tessellation != null;
            var b=new StringBuilder("Pass {\nName \""+(shell?(passIndex == 1 ? "Shell" : "Shell" + passIndex.ToString(CultureInfo.InvariantCulture)):"ForwardBase")+"\"\nTags { \"LightMode\"=\""+(shell?"Always":"ForwardBase")+"\" }\nCull Back\n"+(shell?"ZWrite Off\nBlend SrcAlpha OneMinusSrcAlpha":"ZWrite On")+"\nCGPROGRAM\n#pragma target "+(tess?"4.6":wireframeEnabled?"4.0":"3.5")+"\n#pragma vertex "+(tess?"vertTess":"vert")+"\n"+(tess?"#pragma hull hullTess\n#pragma domain domainTess\n":"")+"#pragma fragment frag\n"+(wireframeEnabled?"#pragma geometry geomWire\n":"")+"#pragma multi_compile_fwdbase\n#pragma multi_compile_instancing\n");
            b.AppendLine("NXInput vert(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*("+displacement+"+"+offset+"+"+tessHeight+"); NXInput o=NX_Make(v); o.originalLocal=input.originalLocal; o.originalWs=input.originalWs; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_VERTEX_TO_FRAGMENT(o); return o; }");
            if (tess) b.AppendLine(TessellationShader.Forward(Prop(tessellation,"factor",8), Prop(tessellation,"minFactor",1), Prop(tessellation,"nearDistance",2), Prop(tessellation,"farDistance",15), Prop(tessellation,"smoothing",0)));
            if (wireframeEnabled) b.AppendLine(WireGeometry);
            b.AppendLine("float4 frag(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float4 c="+color+"*_Color; float3 emission=("+emission+").rgb; float alpha=saturate("+(IntProp(surface,"useAlbedoAlpha",1,0,1)==1 ? "c.a" : "_Color.a")+"*"+opacity+");");
            if(!shell)b.AppendLine("clip(alpha-"+Prop(surface,"cutoff",.001)+");");
            if(surface.Operation=="core.unlitSurface")b.AppendLine("return float4(c.rgb+emission,alpha);");
            else
            {
                b.AppendLine("float3 tn="+normal+"; float3 n=normalize(normalize(input.tangent)*tn.x+normalize(input.bitangent)*tn.y+normalize(input.n)*tn.z); float3 view=normalize(_WorldSpaceCameraPos-input.ws); float3 lightDir=normalize(UnityWorldSpaceLightDir(input.ws)); UNITY_LIGHT_ATTENUATION(atten,input,input.ws);");
                if(surface.Operation=="core.pbrSurface")
                {
                    if (!lighting) b.AppendLine("half3 spec; half reflectivity; half3 diffuse=DiffuseAndSpecularFromMetallic(c.rgb,saturate("+metallic+"),spec,reflectivity); UnityLight light; light.color=_LightColor0.rgb*atten; light.dir=lightDir; light.ndotl=saturate(dot(n,lightDir)); UnityIndirect indirect; indirect.diffuse=max(0,ShadeSH9(float4(n,1))); half rough=saturate("+roughness+"); half4 env=UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflect(-view,n),rough*6); indirect.specular=DecodeHDR(env,unity_SpecCube0_HDR); return float4(UNITY_BRDF_PBS(diffuse,spec,reflectivity,1-rough,n,view,light,indirect).rgb+emission,alpha);");
                    else b.AppendLine("half3 spec; half reflectivity; half3 diffuse=DiffuseAndSpecularFromMetallic(c.rgb,saturate("+metallic+"),spec,reflectivity); UnityLight light; light.color=NX_LightingContribution(_LightColor0.rgb*atten,"+lightingSaturation+","+lightingMax+"); light.dir=lightDir; light.ndotl=saturate(dot(n,lightDir)); UnityIndirect indirect; indirect.diffuse=NX_LightingBase(NX_LightingContribution(max(0,ShadeSH9(float4(n,1))),"+lightingSaturation+","+lightingMax+"),"+lightingMin+","+lightingMax+"); half rough=saturate("+roughness+"); half4 env=UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflect(-view,n),rough*6); indirect.specular=NX_LightingBase(NX_LightingContribution(DecodeHDR(env,unity_SpecCube0_HDR),"+lightingSaturation+","+lightingMax+"),"+lightingMin+","+lightingMax+"); return float4(UNITY_BRDF_PBS(diffuse,spec,reflectivity,1-rough,n,view,light,indirect).rgb+emission,alpha);");
                }
                else if (!lighting) b.AppendLine("float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),dot(n,lightDir)*.5+.5); return float4(c.rgb*(max(0,ShadeSH9(float4(n,1)))+_LightColor0.rgb*atten*lerp(1-saturate("+shadow+"),1,lit))+emission,alpha);");
                else b.AppendLine("float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),dot(n,lightDir)*.5+.5); float3 ambient=NX_LightingContribution(max(0,ShadeSH9(float4(n,1))),"+lightingSaturation+","+lightingMax+"); float3 direct=NX_LightingContribution(_LightColor0.rgb*atten*lerp(1-saturate("+shadow+"),1,lit),"+lightingSaturation+","+lightingMax+"); return float4(c.rgb*NX_LightingBase(ambient+direct,"+lightingMin+","+lightingMax+")+emission,alpha);");
            }
            var result = b.AppendLine("}\nENDCG\n}").ToString().Replace("#pragma target 3.5", wireframeEnabled ? "#pragma target 4.0" : "#pragma target 3.5");
            if (!shell && (surface.Operation == "core.toonSurface" || surface.Operation == "core.pbrSurface"))
                result += ForwardAddPass(surface, passIndex, offset, tessellation);
            return result;
        }
        string ForwardAddPass(GraphNode surface, int passIndex, string offset, GraphNode tessellation)
        {
            var displacement = Scalar(surface,"displacement",0,true);
            var tessHeight = tessellation == null ? "0" : "(" + Scalar(tessellation,"height",.5,true) + "-" + Prop(tessellation,"reference",.5) + ")*" + Prop(tessellation,"strength",.1);
            var color = Input(surface,"albedo","float4(1,1,1,1)","color");
            var opacity = Scalar(surface,"opacity",1);
            var normal = Input(surface,"normal","float3(0,0,1)","vector3");
            var metallic = Scalar(surface,"metallic",0);
            var roughness = Scalar(surface,"roughness",.5);
            var threshold=ToonSetting(surface,"threshold",passIndex); var softness=ToonSetting(surface,"softness",passIndex); var shadow=ToonSetting(surface,"shadowStrength",passIndex);
            var lighting = HasLightingControls(surface);
            var lightingMax = lighting ? Prop(surface, "lightingMax", 0) : null;
            var lightingSaturation = lighting ? Prop(surface, "lightingSaturation", 1) : null;
            var tess = tessellation != null;
            var b = new StringBuilder("\nPass {\nName \"ForwardAdd\"\nTags { \"LightMode\"=\"ForwardAdd\" }\nBlend One One\nColorMask RGB\nZWrite Off\nCGPROGRAM\n#pragma target "+(tess?"4.6":wireframeEnabled?"4.0":"3.5")+"\n#pragma vertex "+(tess?"vertTessAdd":"vertAdd")+"\n"+(tess?"#pragma hull hullTessAdd\n#pragma domain domainTessAdd\n":"")+"#pragma fragment fragAdd\n"+(wireframeEnabled?"#pragma geometry geomWireAdd\n":"")+"#pragma multi_compile_fwdadd_fullshadows\n#pragma multi_compile_instancing\n");
            b.AppendLine("NXInput vertAdd(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*("+displacement+"+"+offset+"+"+tessHeight+"); NXInput o=NX_Make(v); o.originalLocal=input.originalLocal; o.originalWs=input.originalWs; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_VERTEX_TO_FRAGMENT(o); return o; }");
            if (tess) b.AppendLine(TessellationShader.Forward(Prop(tessellation,"factor",8),Prop(tessellation,"minFactor",1),Prop(tessellation,"nearDistance",2),Prop(tessellation,"farDistance",15),Prop(tessellation,"smoothing",0)).Replace("vertTess", "vertTessAdd").Replace("hullTess", "hullTessAdd").Replace("domainTess", "domainTessAdd").Replace("return vert(a)", "return vertAdd(a)"));
            if (wireframeEnabled) b.AppendLine(WireGeometry.Replace("geomWire", "geomWireAdd"));
            b.AppendLine("float4 fragAdd(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float4 c="+color+"*_Color; float alpha=saturate("+(IntProp(surface,"useAlbedoAlpha",1,0,1)==1 ? "c.a" : "_Color.a")+"*"+opacity+"); clip(alpha-"+Prop(surface,"cutoff",.001)+"); float3 tn="+normal+"; float3 n=normalize(normalize(input.tangent)*tn.x+normalize(input.bitangent)*tn.y+normalize(input.n)*tn.z); float3 view=normalize(_WorldSpaceCameraPos-input.ws); float3 lightDir=normalize(UnityWorldSpaceLightDir(input.ws)); UNITY_LIGHT_ATTENUATION(atten,input,input.ws);");
            if (surface.Operation == "core.pbrSurface")
                b.AppendLine(lighting ? "half3 spec; half reflectivity; half3 diffuse=DiffuseAndSpecularFromMetallic(c.rgb,saturate("+metallic+"),spec,reflectivity); UnityLight light; light.color=NX_LightingContribution(_LightColor0.rgb*atten,"+lightingSaturation+","+lightingMax+"); light.dir=lightDir; light.ndotl=saturate(dot(n,lightDir)); UnityIndirect indirect; indirect.diffuse=0; indirect.specular=0; half rough=saturate("+roughness+"); return float4(UNITY_BRDF_PBS(diffuse,spec,reflectivity,1-rough,n,view,light,indirect).rgb,0);" : "half3 spec; half reflectivity; half3 diffuse=DiffuseAndSpecularFromMetallic(c.rgb,saturate("+metallic+"),spec,reflectivity); UnityLight light; light.color=_LightColor0.rgb*atten; light.dir=lightDir; light.ndotl=saturate(dot(n,lightDir)); UnityIndirect indirect; indirect.diffuse=0; indirect.specular=0; half rough=saturate("+roughness+"); return float4(UNITY_BRDF_PBS(diffuse,spec,reflectivity,1-rough,n,view,light,indirect).rgb,0);");
            else
                b.AppendLine(lighting ? "float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),dot(n,lightDir)*.5+.5); return float4(c.rgb*NX_LightingContribution(_LightColor0.rgb*atten*lerp(1-saturate("+shadow+"),1,lit),"+lightingSaturation+","+lightingMax+"),0);" : "float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),dot(n,lightDir)*.5+.5); return float4(c.rgb*_LightColor0.rgb*atten*lerp(1-saturate("+shadow+"),1,lit),0);");
            return b.AppendLine("}\nENDCG\n}").ToString();
        }
        void ValidateParticleInfo(GraphNode root, bool particle, bool surfaceParticles)
        {
            var infos = nodes.Values.Where(n => n.Operation == "core.particleInfo").ToArray();
            if (infos.Length == 0) return;
            var live = new HashSet<string>();
            var pending = new Stack<string>(); pending.Push(root.Id);
            while (pending.Count > 0) { var id = pending.Pop(); if (!live.Add(id)) continue; foreach (var edge in graph.Connections.Where(e => e.To.NodeId == id)) pending.Push(edge.From.NodeId); }
            infos = infos.Where(info => live.Contains(info.Id)).ToArray();
            if (infos.Length == 0) return;
            if (!surfaceParticles)
                throw new InvalidOperationException("Particle Info is only valid inside Surface Particles; particle surface has no particle lifetime context.");
            var forbidden = new HashSet<string>(new[] { "base", "mask", "time", "density", "emissionRate", "lifetime" }, StringComparer.Ordinal);
            foreach (var info in infos)
            {
                var queue = new Queue<GraphConnection>(); var seen = new HashSet<GraphConnection>();
                foreach (var edge in graph.Connections.Where(e => e.From.NodeId == info.Id)) queue.Enqueue(edge);
                while (queue.Count > 0)
                {
                    var edge = queue.Dequeue();
                    if (!seen.Add(edge)) continue;
                    if (edge.To.NodeId == root.Id && forbidden.Contains(edge.To.PortId))
                        throw new InvalidOperationException("Particle Info age/random cannot drive Surface Particles " + edge.To.PortId + "; use it for particle appearance only.");
                    foreach (var next in graph.Connections.Where(e => e.From.NodeId == edge.To.NodeId)) queue.Enqueue(next);
                }
            }
        }

        string ParticleCurve(GraphNode node, string primary, bool color, string fallback)
        {
            var token = node.Properties[primary];
            return SurfaceParticleShader.Curve(token, "normalizedAge", color, fallback);
        }

        string VolumePass(GraphNode surface)
        {
            var density = Scalar(surface, "density", 1);
            var color = Input(surface, "color", surface.Properties["color"] == null ? "float4(.4,.2,1,1)" : Literal(surface.Properties["color"], "color"), "color");
            var emission = Input(surface, "emission", surface.Properties["emission"] == null ? "float4(0,0,0,1)" : Literal(surface.Properties["emission"], "color"), "color");
            var distance = Input(surface, "distance", "-1", "float");
            var bounds = surface.Properties["bounds"] == null ? "float3(.5,.5,.5)" : Literal(surface.Properties["bounds"], "vector3");
            int steps = IntProp(surface, "steps", 32, 8, 128);
            bool depthClip = IntProp(surface, "depthClip", 0, 0, 1) == 1;
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.volume", surface.Id, "Volume raymarching evaluates density, distance, color and emission up to " + steps + " times per pixel per eye. Use a closed box proxy with matching local bounds; it does not infer an avatar's interior or cast self-shadows. Transparent volumes can sort imperfectly. Texture samples inside volumes use mip level zero."));
            if (depthClip) diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "volume.depth", surface.Id, "Depth clipping requires a valid camera depth texture; disable it in worlds or mirrors without one. Transparent surfaces usually do not contribute to that texture."));
            bool solid = IntProp(surface, "mode", 0, 0, 1) == 1;
            if (solid && Source(surface, "distance") == null) throw new InvalidOperationException("Solid SDF mode needs a shape connected to Distance.");
            return VolumeShader.Pass(density, color, emission, distance, bounds, steps, RawProp(surface, "maxDistance", 4), depthClip, solid);
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
        string Shadow(GraphNode surface, string offset, GraphNode tessellation)
        {
            var displacement=Scalar(surface,"displacement",0,true);
            var tessHeight = tessellation == null ? "0" : "(" + Scalar(tessellation, "height", .5, true) + "-" + Prop(tessellation, "reference", .5) + ")*" + Prop(tessellation, "strength", .1);
            var opacity=Scalar(surface,"opacity",1);
            var color=Input(surface,"albedo","float4(1,1,1,1)","color");
            var shader = "Pass {\nName \"ShadowCaster\"\nTags { \"LightMode\"=\"ShadowCaster\" }\nZWrite On\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vertShadow\n#pragma fragment fragShadow\n#pragma multi_compile_shadowcaster\n#pragma multi_compile_instancing\nstruct NXShadow { V2F_SHADOW_CASTER; float2 uv:TEXCOORD1; float3 ws:TEXCOORD2; float3 normal:TEXCOORD3; float3 local:TEXCOORD4; float2 uv1:TEXCOORD5; float2 uv2:TEXCOORD6; float2 uv3:TEXCOORD7; float3 originalWs:TEXCOORD8; float3 originalLocal:TEXCOORD9; float4 color:TEXCOORD10; float3 tangent:TEXCOORD11; float3 bitangent:TEXCOORD12; float2 sourceUV:TEXCOORD13; UNITY_VERTEX_OUTPUT_STEREO };\nNXShadow vertShadow(NXApp v){UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=(v.normal*("+displacement+"+"+offset+")); NXShadow o; UNITY_INITIALIZE_OUTPUT(NXShadow,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.uv=v.uv; o.uv1=v.uv1; o.uv2=v.uv2; o.uv3=v.uv3; o.originalWs=input.originalWs; o.originalLocal=input.originalLocal; o.color=input.color; o.tangent=input.tangent; o.bitangent=input.bitangent; o.sourceUV=input.sourceUV; o.ws=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); o.local=v.vertex.xyz; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o); return o;}\nfloat4 fragShadow(NXShadow i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); NXInput input=(NXInput)0; input.uv=i.uv; input.uv1=i.uv1; input.uv2=i.uv2; input.uv3=i.uv3; input.originalWs=i.originalWs; input.originalLocal=i.originalLocal; input.color=i.color; input.ws=i.ws; input.n=i.normal; input.local=i.local; input.tangent=i.tangent; input.bitangent=i.bitangent; input.sourceUV=i.sourceUV; clip("+(IntProp(surface,"useAlbedoAlpha",1,0,1)==1 ? "("+color+").a*" : "")+"_Color.a*("+opacity+")-"+Prop(surface,"cutoff",.001)+"); SHADOW_CASTER_FRAGMENT(i);}\nENDCG\n}\n";
            if (tessellation != null)
            {
                shader = shader.Replace("#pragma target 3.5", "#pragma target 4.6\n#pragma hull hullTess\n#pragma domain domainTess")
                    .Replace("#pragma vertex vertShadow", "#pragma vertex vertTess")
                    .Replace("v.normal*(" + displacement + "+" + offset + ")", "v.normal*(" + displacement + "+" + offset + "+" + tessHeight + ")");
                shader = shader.Insert(shader.IndexOf("float4 fragShadow", StringComparison.Ordinal), TessellationShader.Shadow(Prop(tessellation,"factor",8), Prop(tessellation,"minFactor",1), Prop(tessellation,"nearDistance",2), Prop(tessellation,"farDistance",15), Prop(tessellation,"smoothing",0)));
            }
            if (wireframeEnabled)
                shader = shader.Replace("#pragma target 3.5", "#pragma target 4.0").Replace("#pragma fragment fragShadow", "#pragma fragment fragShadow\n#pragma geometry geomWireShadow")
                    .Replace("float4 color:TEXCOORD10;", "float4 color:TEXCOORD10; float3 wireBary:TEXCOORD14;")
                    .Replace("float4 fragShadow", WireGeometry.Replace("NXInput", "NXShadow").Replace("geomWire", "geomWireShadow") + "\nfloat4 fragShadow")
                    .Replace("input.uv=i.uv;", "input.wireBary=i.wireBary; input.uv=i.uv;");
            return shader;
        }
        const string Helpers=@"
float4 NX_HueShift(float4 color, float turns) {
    float hi = max(color.r, max(color.g, color.b));
    float lo = min(color.r, min(color.g, color.b));
    float chroma = hi - lo;
    if (chroma <= 0) return color;
    float hue;
    if (hi == color.r) hue = (color.g - color.b) / chroma;
    else if (hi == color.g) hue = 2 + (color.b - color.r) / chroma;
    else hue = 4 + (color.r - color.g) / chroma;
    hue = frac(hue / 6 + frac(turns));
    float3 wheel = saturate(abs(frac(hue + float3(0, 2.0/3.0, 1.0/3.0)) * 6 - 3) - 1);
    return float4(lo + chroma * wheel, color.a);
}

struct NXApp { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD1; float2 uv2:TEXCOORD2; float2 uv3:TEXCOORD3; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct NXInput { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD7; float2 uv2:TEXCOORD8; float2 uv3:TEXCOORD9; float3 ws:TEXCOORD1; float3 n:TEXCOORD2; float3 local:TEXCOORD3; float3 originalWs:TEXCOORD10; float3 originalLocal:TEXCOORD11; float3 tangent:TEXCOORD4; float3 bitangent:TEXCOORD5; float4 color:TEXCOORD12; float4 screenPos:TEXCOORD13; float2 sourceUV:TEXCOORD14; float3 wireBary:TEXCOORD15; float particleAlpha:TEXCOORD20; float particleAge:TEXCOORD21; float particleRandom:TEXCOORD22; LIGHTING_COORDS(16,17) UNITY_VERTEX_OUTPUT_STEREO };
NXInput NX_Make(NXApp v){ NXInput o=(NXInput)0; o.pos=UnityObjectToClipPos(v.vertex); o.screenPos=ComputeGrabScreenPos(o.pos); o.uv=v.uv; o.uv1=v.uv1; o.uv2=v.uv2; o.uv3=v.uv3; o.local=v.vertex.xyz; o.ws=mul(unity_ObjectToWorld,v.vertex).xyz; o.originalLocal=o.local; o.originalWs=o.ws; o.color=v.color; o.n=UnityObjectToWorldNormal(v.normal); o.tangent=UnityObjectToWorldDir(v.tangent.xyz); o.bitangent=cross(o.n,o.tangent)*v.tangent.w*unity_WorldTransformParams.w; return o; }
float4 NX_Splat(float x){return float4(x,x,x,x);}

float NX_ShapeEdge(float d,float softness){return 1-smoothstep(-max(abs(softness),.00001),max(abs(softness),.00001),d);}
float NX_Polygon(float2 uv,float sides,float radius,float rotation,float softness){float2 p=uv-.5;float sector=6.2831853/sides;float a=atan2(p.y,p.x)+radians(rotation);float folded=(frac(a/sector+.5)-.5)*sector;return NX_ShapeEdge(length(p)*cos(folded)-max(0,radius)*cos(sector*.5),softness);}
float NX_Star(float2 uv,float points,float inner,float outer,float rotation,float softness){float2 p=uv-.5;float a=atan2(p.y,p.x)+radians(rotation);float sector=6.2831853/points;float angle=abs((frac(a/sector+.5)-.5)*sector);float halfSector=sector*.5;float denominator=max(.00001,inner*sin(halfSector-angle)+outer*sin(angle));float radius=max(0,outer)*max(0,inner)*sin(halfSector)/denominator;return NX_ShapeEdge(length(p)-radius,softness);}
float NX_Hex(float2 uv,float scale,float width){float2 p=uv*scale;float2 period=float2(1.7320508,3);float2 a=p-period*floor(p/period+.5);float2 b=p-period*floor((p-float2(.8660254,1.5))/period+.5)-float2(.8660254,1.5);float2 q=dot(a,a)<dot(b,b)?a:b;q=abs(q);float edge=.8660254-max(q.x,dot(q,float2(.5,.8660254)));return 1-smoothstep(max(0,width),max(0,width)+.01,edge);}
float NX_Wire(float3 bary,float width,float softness){float3 aa=max(fwidth(bary),float3(.00001,.00001,.00001));float3 coverage=smoothstep(aa*max(0,width),aa*(max(0,width)+max(.001,softness)),bary);return 1-min(coverage.x,min(coverage.y,coverage.z));}

float NX_Div(float a,float b){return a/((b<0?-1:1)*max(abs(b),.00001));}
float4 NX_Div(float4 a,float4 b){return a/((step(0,b)*2-1)*max(abs(b),.00001));}
float2 NX_Rotate(float2 uv,float2 center,float degrees){float a=radians(degrees);float s=sin(a),c=cos(a);uv-=center;return float2(c*uv.x-s*uv.y,s*uv.x+c*uv.y)+center;}
float2 NX_Polar(float2 uv,float2 center,float radial,float angular){float2 p=uv-center;return float2(length(p)*2*radial,(dot(p,p)<1e-12?.5:atan2(p.y,p.x)/6.28318530718+.5)*angular);}
float NX_Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float NX_Noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(NX_Hash(i),NX_Hash(i+float2(1,0)),f.x),lerp(NX_Hash(i+float2(0,1)),NX_Hash(i+float2(1,1)),f.x),f.y);}
float2 NX_PanoUV(float3 d){return float2(atan2(d.x,d.z)/6.28318530718+.5,asin(clamp(d.y,-1,1))/3.14159265359+.5);}
// Only switch longitude charts at a real wrap. Bias prevents roundoff from changing charts in smooth regions.
// Panosphere seam handling reference: Poiyomi (see docs/PANOSPHERE.md). Vertex evaluation keeps raw UVs.
float2 NX_PanoFilteredUV(float3 d){float2 uv=NX_PanoUV(d);float alternate=frac(uv.x+.5)-.5;uv.x=fwidth(uv.x)-.0001<fwidth(alternate)?uv.x:alternate;return uv;}
float2 NX_MatcapUV(float3 view,float3 normal){float3 n=normalize(mul((float3x3)UNITY_MATRIX_V,normal));return n.xy*.5+.5;}
float2 NX_Flipbook(float2 uv,float frame,float cols,float rows){float count=cols*rows;frame=floor(frame);frame=frame-floor(frame/count)*count;return (frac(uv)+float2(fmod(frame,cols),rows-1-floor(frame/cols)))/float2(cols,rows);}
float2 NX_Distort(float2 uv,float strength,float scale,float time){return uv+(float2(NX_Noise(uv*scale+time),NX_Noise(uv*scale+time+17.2))-.5)*strength;}
float4 NX_DarkGlow(NXInput input,float4 color,float strength,float threshold,float softness){float3 n=normalize(input.n); float3 lighting=max(0,ShadeSH9(float4(n,1))); UNITY_LIGHT_ATTENUATION(atten,input,input.ws); lighting+=_LightColor0.rgb*atten*saturate(dot(n,normalize(UnityWorldSpaceLightDir(input.ws)))); float level=dot(lighting,float3(.2126,.7152,.0722)); float mask=1-smoothstep(max(0,threshold)-max(.001,softness),max(0,threshold)+max(.001,softness),level); return float4(color.rgb*max(0,strength)*mask,color.a);}
float3 NX_Normal(float4 encoded,float strength){float3 n=UnpackNormal(encoded);n.xy*=strength;n.z=sqrt(saturate(1-dot(n.xy,n.xy)));return normalize(n);}
";

        const string LightingHelpers = @"
float3 NX_LightingSaturate(float3 value,float saturation){float gray=dot(value,float3(.2126,.7152,.0722));return lerp(float3(gray,gray,gray),value,max(0,saturation));}
float3 NX_LightingContribution(float3 value,float saturation,float maximum){value=max(float3(0,0,0),NX_LightingSaturate(value,saturation));return maximum>0?min(value,float3(maximum,maximum,maximum)):value;}
float3 NX_LightingBase(float3 value,float minimum,float maximum){value=max(float3(0,0,0),value);if(maximum>0)value=min(value,float3(maximum,maximum,maximum));float floorValue=min(minimum,maximum>0?maximum:minimum);return max(value,float3(floorValue,floorValue,floorValue));}
";
    }
}
