using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    public sealed class GraphPerformanceReport
    {
        public IReadOnlyList<GraphNode> ReachableNodes { get; internal set; }
        public IReadOnlyList<GraphCostItem> HotSpots { get; internal set; }
        public int TextureSampleSites { get; internal set; }
        public int StaticPassBudget { get; internal set; }
        public string PassNote { get; internal set; }
        public IReadOnlyList<string> LoopBudgets { get; internal set; }
        public IReadOnlyList<string> DynamicUnknowns { get; internal set; }
    }

    public sealed class GraphCostItem
    {
        public string NodeId { get; internal set; }
        public string Label { get; internal set; }
        public string Estimate { get; internal set; }
        public string Explanation { get; internal set; }
    }

    /// <summary>Reports static graph facts and backend budgets; it does not predict GPU time.</summary>
    public static class GraphPerformance
    {
        public static GraphPerformanceReport Analyze(ShaderGraph graph)
        {
            var nodes = graph?.Nodes ?? new List<GraphNode>();
            var edges = graph?.Connections ?? new List<GraphConnection>();
            var byId = nodes.Where(n => n != null && !string.IsNullOrEmpty(n.Id)).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
            var incoming = edges.Where(e => e?.To != null && e.From != null && e.To.NodeId != null && e.From.NodeId != null)
                .ToLookup(e => e.To.NodeId);
            var output = nodes.FirstOrDefault(n => n?.Operation == "core.output");
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            if (output != null) queue.Enqueue(output.Id);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (id == null || !reached.Add(id)) continue;
                byId.TryGetValue(id, out var current);
                foreach (var edge in GraphDependencies.ActiveIncoming(current, incoming[id])) queue.Enqueue(edge.From.NodeId);
            }

            var live = nodes.Where(n => n != null && n.Id != null && reached.Contains(n.Id)).ToList();
            var items = new List<GraphCostItem>();
            var loops = new List<string>();
            var unknown = new List<string>();
            var textureSites = 0;
            foreach (var node in live)
            {
                var op = node.Operation ?? string.Empty;
                var count = TextureSites(node, incoming);
                textureSites += count;
                if (count > 0) items.Add(Item(node, count + " texture sample site" + (count == 1 ? "" : "s"), "Each site may run in several passes or stages. Runtime sample count depends on the rendered path."));

                if (op == "core.parallaxOcclusion")
                {
                    var steps = Int(node, "steps", 16, 4, 64);
                    loops.Add("Parallax Occlusion: up to " + steps.ToString(CultureInfo.InvariantCulture) + " loop evaluations per fragment, plus an initial height read. The loop may exit early.");
                    items.Add(Item(node, "1 height sample site", "That site runs once before the loop and once per executed iteration; actual reads range from 1 to " + (steps + 1) + " per fragment."));
                }
                else if (op == "core.volumeSurface")
                {
                    var steps = Int(node, "steps", 32, 8, 128);
                    loops.Add("Volume march: up to " + steps.ToString(CultureInfo.InvariantCulture) + " iterations per pixel per eye.");
                    items.Add(Item(node, "Up to " + steps + " volume iterations per pixel", "Each iteration evaluates the connected volume fields; actual work depends on early termination and pixel coverage."));
                    if (Int(node, "mode", 0, 0, 1) == 1) loops.Add("Solid SDF: a hit adds six distance evaluations to estimate its surface normal.");
                }
                else if (op == "core.fur")
                {
                    if (Int(node, "cardsOnly", 0, 0, 1) == 1)
                        items.Add(Item(node, "3 cards per selected source triangle", "Cards use one geometry pass. Triangle count and density determine generated geometry."));
                    else
                    {
                        var layers = Int(node, "layers", 16, 4, 32);
                        items.Add(Item(node, layers + " fur shell passes", "Distance LOD can reduce active fragments but does not remove shell draw calls."));
                        if (Int(node, "selfShadowQuality", 0, 0, 3) > 0)
                            loops.Add("Fur self-shadow: " + new[] { 0, 4, 8, 16 }[Int(node, "selfShadowQuality", 0, 0, 3)] + " local samples per shaded fur fragment, multiplied across active shells.");
                    }
                    if (Connected(node, "density", incoming) || Connected(node, "mask", incoming)) unknown.Add("Fur coverage depends on connected density or mask values and screen coverage.");
                }
                else if (op == "core.surfaceParticles")
                {
                    var rate = Connected(node, "emissionRate", incoming) || Connected(node, "lifetime", incoming);
                    if (rate)
                    {
                        items.Add(Item(node, "Adaptive tessellation, up to factor 64", "Connected Rate or Lifetime changes topology per primitive. Each microtriangle runs a geometry shader with up to 4 particle slots (16 emitted vertices); mesh size multiplies this work."));
                        loops.Add("Surface Particles tessellation factor varies from 1 to 64; microtriangles per source triangle follow (3 × factor² − factor mod 2) / 2. Up to 4 slots run per microtriangle.");
                        unknown.Add("Particle tessellation and emission budget are dynamic because Rate or Lifetime is connected.");
                    }
                    else
                    {
                        var life = Math.Max(.0001, Number(node, "lifetime", 2));
                        // The generator's static tessellation parser sees a missing rate as zero (its runtime shader default remains 1/lifetime).
                        var rateValue = Number(node, "emissionRate", 0);
                        var level = (int)Math.Max(1, Math.Min(64, Math.Ceiling(Math.Sqrt(Math.Max(0, rateValue) * life / 4))));
                        var triangles = level == 1 ? 1 : (3 * level * level - level % 2) / 2;
                        items.Add(Item(node, "Tessellation factor " + level + " · " + triangles + " microtriangle" + (triangles == 1 ? "" : "s") + " per source triangle",
                            "Geometry shader runs up to 4 particle slots per microtriangle (16 vertices). Mesh triangle count multiplies the estimate; density or mask can hide particles without reducing generated vertices."));
                        loops.Add("Surface Particles: factor " + level + " gives " + triangles + " estimated microtriangles per source triangle; up to 4 slots per microtriangle. Default requested rate is 1 / max(lifetime, 0.0001).");
                    }
                }
                else if (op == "core.tessellation")
                {
                    items.Add(Item(node, "Adaptive tessellation", "Triangle count grows roughly quadratically with tessellation factor and varies with distance."));
                    loops.Add("Tessellation: factor " + Int(node,"minFactor",1,1,63) + " to " + Int(node,"factor",8,1,63) + " with fractional-odd spacing; camera distance affects topology.");
                }
                else if (op == "core.layeredPbrSurface")
                {
                    items.Add(Item(node, "Lit surface pass", "The base uses ForwardBase and ForwardAdd per additional pixel light. When used as a shell layer, this surface adds one overlay pass instead."));
                    if (ActiveLayer(node, "coat", incoming)) items.Add(Item(node, "Clear coat lobe", "Adds one reflection probe sample in ForwardBase or a shell overlay, plus direct-light work. ForwardAdd does not sample the probe."));
                    if (ActiveLayer(node, "sheen", incoming)) items.Add(Item(node, "Sheen lobe", "Adds direct-light and grazing ambient calculations; no additional texture sample."));
                }
                else if (op == "core.toonSurface" || op == "core.pbrSurface")
                    items.Add(Item(node, "Lit surface pass", "The base uses ForwardBase and ForwardAdd per additional pixel light. When used as a shell layer, this surface adds one overlay pass instead."));
                else if (op == "core.refraction") items.Add(Item(node, "Screen GrabPass", "Captures the screen once before surface passes; capture cost depends on camera and scene."));
                else if (IsUnmodeledExpensive(op)) unknown.Add(NodeCatalog.Title(op) + " has backend work that depends on shader stage or inputs; no numeric estimate is available.");
            }

            foreach (var item in items)
            {
                var resource = byId[item.NodeId].Properties?["resourceId"];
                if (resource?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)resource))
                    item.Label = TextureSlotLabels.DisplayName(graph, (string)resource);
            }
            var root = output == null ? null : incoming[output.Id].Where(e => e.To.PortId == "surface").Select(e => byId.ContainsKey(e.From.NodeId) ? byId[e.From.NodeId] : null).FirstOrDefault(n => n != null);
            var passBudget = PassBudget(root, byId, incoming, out var passNote);
            return new GraphPerformanceReport
            {
                ReachableNodes = live,
                HotSpots = items.OrderByDescending(i => Severity(i.Estimate)).ToList(),
                TextureSampleSites = textureSites,
                StaticPassBudget = passBudget,
                PassNote = passNote,
                LoopBudgets = loops,
                DynamicUnknowns = unknown.Distinct().ToList()
            };
        }

        static int PassBudget(GraphNode root, IDictionary<string, GraphNode> nodes, ILookup<string, GraphConnection> incoming, out string note)
        {
            if (root == null) { note = "Connect a surface to Output to calculate pass budget."; return 0; }
            GraphNode fur = root.Operation == "core.fur" ? root : null;
            GraphNode baseRoot = root;
            if (root.Operation == "core.tessellation") baseRoot = Source(root, "base", nodes, incoming);
            if (root.Operation == "core.fur") baseRoot = Source(root, "base", nodes, incoming);
            if (root.Operation == "core.surfaceParticles") baseRoot = Source(root, "base", nodes, incoming);
            if (root.Operation == "core.surfaceParticles" && baseRoot?.Operation == "core.fur") { fur = baseRoot; baseRoot = Source(fur, "base", nodes, incoming); }
            if (root.Operation == "core.volumeSurface" || root.Operation == "core.particleSurface")
            { note = "One specialized pass. Unity variants are not counted."; return 1; }
            if (baseRoot == null) { note = "Base surface is not connected."; return 0; }
            var leafSurfaces = new List<GraphNode>();
            var truncated = false;
            Flatten(baseRoot, nodes, incoming, new HashSet<string>(StringComparer.Ordinal), leafSurfaces, 0, ref truncated);
            var total = 0;
            for (var i = 0; i < leafSurfaces.Count; i++)
            {
                var surface = leafSurfaces[i];
                total += 1;
                if (i == 0 && (surface.Operation == "core.toonSurface" || surface.Operation == "core.pbrSurface" || surface.Operation == "core.layeredPbrSurface")) total++;
            }
            if (leafSurfaces.Count > 0 && HasShadowPass(leafSurfaces[0], nodes, incoming)) total++;
            if (root.Operation == "core.surfaceParticles") total++; // specialized particle pass follows the base surface
            if (fur != null)
            {
                if (Int(fur, "cardsOnly", 0, 0, 1) == 1) total++;
                else
                {
                    total += Int(fur, "layers", 16, 4, 32);
                    if (Int(fur, "fins", 0, 0, 1) == 1) total++;
                }
            }
            note = "Static pass declarations, including the shadow caster only when its inputs allow one. ForwardAdd runs once per additional pixel light; Unity variants are not counted.";
            if (truncated) note += " Shell traversal reached the backend surface cap; reported count is bounded.";
            return total;
        }

        static void Flatten(GraphNode node, IDictionary<string, GraphNode> nodes, ILookup<string, GraphConnection> incoming, HashSet<string> path, List<GraphNode> result, int depth, ref bool truncated)
        {
            if (node == null) return;
            if (depth > 64 || result.Count >= 9) { truncated = true; return; }
            if (!path.Add(node.Id)) { truncated = true; return; }
            if (node.Operation != "core.shell") { result.Add(node); path.Remove(node.Id); return; }
            Flatten(Source(node, "base", nodes, incoming), nodes, incoming, path, result, depth + 1, ref truncated);
            Flatten(Source(node, "layer", nodes, incoming), nodes, incoming, path, result, depth + 1, ref truncated);
            path.Remove(node.Id);
        }

        static bool HasShadowPass(GraphNode firstSurface, IDictionary<string, GraphNode> nodes, ILookup<string, GraphConnection> incoming)
        {
            var followsAlpha = Int(firstSurface, "useAlbedoAlpha", 1, 0, 1) == 1;
            var pending = new Stack<GraphNode>();
            if (followsAlpha) PushSources(firstSurface, "albedo", nodes, incoming, pending);
            PushSources(firstSurface, "opacity", nodes, incoming, pending);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var screenOps = new HashSet<string>(new[] { "core.refraction", "core.screenUV", "core.cameraDistance", "core.viewDirection", "core.fresnel", "core.rimGlow", "core.matcapTexture", "core.interiorMapping" }, StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!seen.Add(node.Id)) continue;
                if (screenOps.Contains(node.Operation)) return false;
                foreach (var port in NodeCatalog.Ports(node.Operation, false))
                    foreach (var edge in incoming[node.Id].Where(e => e.To.PortId == port))
                    { GraphNode source; if (nodes.TryGetValue(edge.From.NodeId, out source)) pending.Push(source); }
            }
            return true;
        }

        static void PushSources(GraphNode node, string port, IDictionary<string, GraphNode> nodes, ILookup<string, GraphConnection> incoming, Stack<GraphNode> pending)
        {
            foreach (var edge in incoming[node.Id].Where(e => e.To.PortId == port))
            { GraphNode source; if (nodes.TryGetValue(edge.From.NodeId, out source)) pending.Push(source); }
        }

        static GraphNode Source(GraphNode node, string port, IDictionary<string, GraphNode> nodes, ILookup<string, GraphConnection> incoming)
        {
            if (node == null) return null;
            var edge = incoming[node.Id].FirstOrDefault(e => e.To.PortId == port);
            GraphNode value;
            return edge != null && nodes.TryGetValue(edge.From.NodeId, out value) ? value : null;
        }

        static int TextureSites(GraphNode n, ILookup<string, GraphConnection> incoming)
        {
            switch (n.Operation)
            {
                case "core.texture2D": case "core.sticker": case "core.matcapTexture": case "core.interiorMapping": return 1;
                case "core.chromaticTexture": return 3;
                case "core.triplanarTexture": return 3;
                case "core.parallaxOcclusion": return 1; // one sample site inside the loop; iteration budget is reported separately
                case "core.textureBomb":
                    var blend = n.Properties?["blend"];
                    var constantOne = blend == null || ((blend.Type == JTokenType.Float || blend.Type == JTokenType.Integer) && (double)blend == 1);
                    return constantOne && !Connected(n, "blend", incoming) ? 4 : 5;
                default: return 0;
            }
        }

        static bool ActiveLayer(GraphNode n, string port, ILookup<string, GraphConnection> incoming)
        {
            return GraphDependencies.IsLayerActive(n, port, Connected(n, port, incoming));
        }
        static bool Connected(GraphNode n, string port, ILookup<string, GraphConnection> incoming) => incoming[n.Id].Any(e => e.To.PortId == port);
        static int Int(GraphNode n, string key, int fallback, int min, int max)
        {
            var t = n.Properties?[key];
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return fallback;
            var value = (double)t;
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return (int)Math.Max(min, Math.Min(max, value));
        }
        static double Number(GraphNode n, string key, double fallback)
        {
            var t = n.Properties?[key];
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return fallback;
            var value = (double)t;
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        }
        static bool IsUnmodeledExpensive(string op) => op == "core.musgrave" || op == "core.voronoi" || op == "core.volumeSurface";
        static int Severity(string estimate)
        {
            var text = estimate.ToLowerInvariant();
            return text.Contains("shell") || text.Contains("iteration") || text.Contains("tessellation") ? 3 : text.Contains("sample") || text.Contains("read") ? 2 : 1;
        }
        static GraphCostItem Item(GraphNode node, string estimate, string explanation) => new GraphCostItem { NodeId = node.Id, Label = NodeCatalog.Title(node.Operation), Estimate = estimate, Explanation = explanation };
    }
}
