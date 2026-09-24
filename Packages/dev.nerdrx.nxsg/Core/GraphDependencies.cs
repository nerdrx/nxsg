using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    // Runtime dependency pruning only. Validation must still inspect every authored edge.
    public static class GraphDependencies
    {
        public static bool IsLayerActive(GraphNode node, string layer, bool connected)
        {
            if (node?.Operation != "core.layeredPbrSurface") return false;
            if (connected) return true;
            var value = node.Properties?[layer];
            // Only a known, disconnected zero can make an input inactive.
            return value != null && (value.Type != JTokenType.Float && value.Type != JTokenType.Integer || (double)value != 0);
        }

        public static IEnumerable<GraphConnection> ActiveIncoming(GraphNode node, IEnumerable<GraphConnection> incoming)
        {
            if (node?.Operation != "core.layeredPbrSurface") return incoming;
            var edges = incoming.ToArray();
            var coat = IsLayerActive(node, "coat", edges.Any(e => e.To.PortId == "coat"));
            var sheen = IsLayerActive(node, "sheen", edges.Any(e => e.To.PortId == "sheen"));
            return edges.Where(e =>
                (coat || e.To.PortId != "coatNormal" && e.To.PortId != "coatRoughness") &&
                (sheen || e.To.PortId != "sheenColor" && e.To.PortId != "sheenRoughness"));
        }
    }
}
