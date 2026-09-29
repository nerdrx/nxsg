using System;
using System.Collections.Generic;
using UnityEngine;

namespace NXSG.Editor
{
    internal static class GraphAutoLayout
    {
        private const float HorizontalGap = 80f;
        private const float VerticalGap = 36f;
        private const float FallbackWidth = 160f;
        private const float FallbackHeight = 80f;
        private const int BarycentricSweeps = 4;

        internal static Dictionary<string, Vector2> Arrange(
            IReadOnlyDictionary<string, Rect> bounds,
            IEnumerable<(string from, string to)> edges)
        {
            var result = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            if (bounds == null || bounds.Count == 0)
                return result;

            var ids = new List<string>(bounds.Keys);
            ids.Sort(StringComparer.Ordinal);

            int count = ids.Count;
            var indexById = new Dictionary<string, int>(count, StringComparer.Ordinal);
            var initialY = new float[count];
            var widths = new float[count];
            var heights = new float[count];
            float anchorX = float.PositiveInfinity;
            float anchorY = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                string id = ids[i];
                Rect rect = bounds[id];
                float x = IsFinite(rect.position.x) ? rect.position.x : 0f;
                float y = IsFinite(rect.position.y) ? rect.position.y : 0f;
                initialY[i] = y;
                widths[i] = PositiveOrFallback(rect.width, FallbackWidth);
                heights[i] = PositiveOrFallback(rect.height, FallbackHeight);
                anchorX = Mathf.Min(anchorX, x);
                anchorY = Mathf.Min(anchorY, y);
                indexById.Add(id, i);
            }

            var edgeKeys = new List<ulong>();
            var seenEdges = new HashSet<ulong>();
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    int from;
                    int to;
                    if (edge.from == null || edge.to == null ||
                        !indexById.TryGetValue(edge.from, out from) ||
                        !indexById.TryGetValue(edge.to, out to))
                        continue;

                    ulong key = ((ulong)(uint)from << 32) | (uint)to;
                    if (seenEdges.Add(key))
                        edgeKeys.Add(key);
                }
            }
            edgeKeys.Sort();

            var edgeFrom = new List<int>(edgeKeys.Count);
            var edgeTo = new List<int>(edgeKeys.Count);
            var outgoing = CreateLists(count);
            var incoming = CreateLists(count);
            var undirected = CreateLists(count);
            foreach (ulong key in edgeKeys)
            {
                int from = (int)(key >> 32);
                int to = (int)(uint)key;
                int edgeIndex = edgeFrom.Count;
                edgeFrom.Add(from);
                edgeTo.Add(to);
                outgoing[from].Add(edgeIndex);
                incoming[to].Add(edgeIndex);
                undirected[from].Add(to);
                undirected[to].Add(from);
            }

            int[] indegree = new int[count];
            for (int i = 0; i < count; i++)
                indegree[i] = incoming[i].Count;

            var comparer = new InitialOrderComparer(initialY, ids);
            var ready = new SortedSet<int>(comparer);
            for (int i = 0; i < count; i++)
                if (indegree[i] == 0)
                    ready.Add(i);

            var active = new bool[edgeFrom.Count];
            for (int i = 0; i < active.Length; i++)
                active[i] = true;

            var processed = new bool[count];
            var ranks = new int[count];
            var topologicalOrder = new List<int>(count);
            int processedCount = 0;
            while (processedCount < count)
            {
                if (ready.Count == 0)
                {
                    // Break one remaining cycle by dropping its incoming edges.
                    int breaker = -1;
                    for (int i = 0; i < count; i++)
                        if (!processed[i] && (breaker < 0 || comparer.Compare(i, breaker) < 0))
                            breaker = i;

                    foreach (int edgeIndex in incoming[breaker])
                    {
                        if (!active[edgeIndex] || processed[edgeFrom[edgeIndex]])
                            continue;
                        active[edgeIndex] = false;
                        indegree[breaker]--;
                    }
                    ready.Add(breaker);
                }

                int current = ready.Min;
                ready.Remove(current);
                if (processed[current])
                    continue;

                processed[current] = true;
                processedCount++;
                topologicalOrder.Add(current);
                foreach (int edgeIndex in outgoing[current])
                {
                    if (!active[edgeIndex])
                        continue;
                    int next = edgeTo[edgeIndex];
                    if (--indegree[next] == 0)
                        ready.Add(next);
                }
            }

            var components = FindComponents(undirected, comparer);
            var depthToSink = new int[count];
            for (int orderIndex = topologicalOrder.Count - 1; orderIndex >= 0; orderIndex--)
            {
                int node = topologicalOrder[orderIndex];
                foreach (int edgeIndex in outgoing[node])
                    if (active[edgeIndex])
                        depthToSink[node] = Math.Max(depthToSink[node], depthToSink[edgeTo[edgeIndex]] + 1);
            }

            foreach (var component in components)
            {
                int componentDepth = 0;
                foreach (int node in component)
                    componentDepth = Math.Max(componentDepth, depthToSink[node]);
                foreach (int node in component)
                    ranks[node] = componentDepth - depthToSink[node];
            }

            int layerCount = 1;
            for (int i = 0; i < count; i++)
                layerCount = Math.Max(layerCount, ranks[i] + 1);

            var layers = CreateLists(layerCount);
            for (int i = 0; i < count; i++)
                layers[ranks[i]].Add(i);
            for (int i = 0; i < layerCount; i++)
                layers[i].Sort(comparer);

            ReduceCrossings(layers, outgoing, incoming, edgeTo, edgeFrom, active, ranks, comparer);

            var componentByNode = new int[count];
            var componentLayers = new List<int>[components.Count][];
            var componentHeights = new float[components.Count];
            for (int component = 0; component < components.Count; component++)
            {
                int componentLayerCount = 1;
                foreach (int node in components[component])
                {
                    componentByNode[node] = component;
                    componentLayerCount = Math.Max(componentLayerCount, ranks[node] + 1);
                }

                componentLayers[component] = CreateLists(componentLayerCount);
            }

            for (int layer = 0; layer < layers.Length; layer++)
                foreach (int node in layers[layer])
                    componentLayers[componentByNode[node]][layer].Add(node);

            for (int component = 0; component < components.Count; component++)
            {
                int componentLayerCount = componentLayers[component].Length;
                for (int layer = 0; layer < componentLayerCount; layer++)
                {
                    float height = RequiredHeight(componentLayers[component][layer], heights);
                    componentHeights[component] = Mathf.Max(componentHeights[component], height);
                }
            }

            var positions = new Vector2[count];
            float bandTop = anchorY;
            for (int component = 0; component < components.Count; component++)
            {
                ArrangeVertical(
                    componentLayers[component], outgoing, incoming, edgeTo, edgeFrom,
                    active, ranks, heights, bandTop, componentHeights[component], positions);
                bandTop += componentHeights[component] + VerticalGap;
            }

            float columnX = anchorX;
            for (int layer = 0; layer < layerCount; layer++)
            {
                float layerWidth = 0f;
                foreach (int node in layers[layer])
                {
                    positions[node].x = columnX;
                    layerWidth = Mathf.Max(layerWidth, widths[node]);
                }
                columnX += layerWidth + HorizontalGap;
            }

            for (int i = 0; i < count; i++)
                result.Add(ids[i], positions[i]);
            return result;
        }

        private static List<List<int>> FindComponents(
            List<int>[] undirected,
            IComparer<int> initialComparer)
        {
            var visited = new bool[undirected.Length];
            var components = new List<List<int>>();
            var pending = new Stack<int>();

            for (int start = 0; start < undirected.Length; start++)
            {
                if (visited[start])
                    continue;

                var component = new List<int>();
                visited[start] = true;
                pending.Push(start);
                while (pending.Count > 0)
                {
                    int node = pending.Pop();
                    component.Add(node);
                    foreach (int neighbor in undirected[node])
                    {
                        if (visited[neighbor])
                            continue;
                        visited[neighbor] = true;
                        pending.Push(neighbor);
                    }
                }

                component.Sort(initialComparer);
                components.Add(component);
            }

            components.Sort((a, b) => initialComparer.Compare(a[0], b[0]));
            return components;
        }

        private static float RequiredHeight(List<int> nodes, float[] heights)
        {
            if (nodes.Count == 0)
                return 0f;

            float total = VerticalGap * (nodes.Count - 1);
            foreach (int node in nodes)
                total += heights[node];
            return total;
        }

        private static void ArrangeVertical(
            List<int>[] layers,
            List<int>[] outgoing,
            List<int>[] incoming,
            List<int> edgeTo,
            List<int> edgeFrom,
            bool[] active,
            int[] ranks,
            float[] heights,
            float bandTop,
            float bandHeight,
            Vector2[] positions)
        {
            for (int layer = 0; layer < layers.Length; layer++)
            {
                float y = bandTop;
                foreach (int node in layers[layer])
                {
                    positions[node].y = y;
                    y += heights[node] + VerticalGap;
                }
            }

            for (int sweep = 0; sweep < BarycentricSweeps; sweep++)
            {
                for (int layer = 1; layer < layers.Length; layer++)
                    AlignLayer(layer, true);
                for (int layer = layers.Length - 2; layer >= 0; layer--)
                    AlignLayer(layer, false);
            }

            void AlignLayer(int layer, bool useIncoming)
            {
                List<int> nodes = layers[layer];
                if (nodes.Count == 0)
                    return;

                var desiredCenters = new float[nodes.Count];
                float requiredHeight = RequiredHeight(nodes, heights);
                float slack = Mathf.Max(0f, bandHeight - requiredHeight);
                float offset = 0f;
                float previousOffset = 0f;
                for (int i = 0; i < nodes.Count; i++)
                {
                    int node = nodes[i];
                    List<int> edges = useIncoming ? incoming[node] : outgoing[node];
                    float sum = 0f;
                    int neighbors = 0;
                    foreach (int edgeIndex in edges)
                    {
                        if (!active[edgeIndex])
                            continue;
                        int other = useIncoming ? edgeFrom[edgeIndex] : edgeTo[edgeIndex];
                        if ((useIncoming && ranks[other] < layer) ||
                            (!useIncoming && ranks[other] > layer))
                        {
                            sum += positions[other].y + heights[other] * 0.5f;
                            neighbors++;
                        }
                    }

                    desiredCenters[i] = neighbors > 0
                        ? sum / neighbors
                        : positions[node].y + heights[node] * 0.5f;
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    int node = nodes[i];
                    float localOffset = Mathf.Clamp(
                        desiredCenters[i] - heights[node] * 0.5f - bandTop - offset,
                        0f,
                        slack);
                    localOffset = Mathf.Max(localOffset, previousOffset);
                    positions[node].y = bandTop + offset + localOffset;
                    previousOffset = localOffset;
                    offset += heights[node] + VerticalGap;
                }
            }
        }

        private static void ReduceCrossings(
            List<int>[] layers,
            List<int>[] outgoing,
            List<int>[] incoming,
            List<int> edgeTo,
            List<int> edgeFrom,
            bool[] active,
            int[] ranks,
            IComparer<int> initialComparer)
        {
            var order = new int[ranks.Length];
            var barycenter = new double[ranks.Length];

            for (int sweep = 0; sweep < BarycentricSweeps; sweep++)
            {
                for (int layer = 0; layer < layers.Length; layer++)
                    UpdateOrder(layer);
                for (int layer = 1; layer < layers.Length; layer++)
                    SortByNeighbors(layer, true);
                for (int layer = layers.Length - 2; layer >= 0; layer--)
                    SortByNeighbors(layer, false);
            }

            void SortByNeighbors(int layer, bool useIncoming)
            {
                for (int i = 0; i < layers[layer].Count; i++)
                    order[layers[layer][i]] = i;

                foreach (int node in layers[layer])
                {
                    List<int> edges = useIncoming ? incoming[node] : outgoing[node];
                    double sum = 0d;
                    int neighbors = 0;
                    foreach (int edgeIndex in edges)
                    {
                        if (!active[edgeIndex])
                            continue;
                        int other = useIncoming ? edgeFrom[edgeIndex] : edgeTo[edgeIndex];
                        if ((useIncoming && ranks[other] < layer) ||
                            (!useIncoming && ranks[other] > layer))
                        {
                            sum += order[other];
                            neighbors++;
                        }
                    }

                    barycenter[node] = neighbors > 0 ? sum / neighbors : order[node];
                }

                layers[layer].Sort((a, b) =>
                {
                    int compare = barycenter[a].CompareTo(barycenter[b]);
                    return compare != 0 ? compare : initialComparer.Compare(a, b);
                });
                UpdateOrder(layer);
            }

            void UpdateOrder(int layer)
            {
                for (int i = 0; i < layers[layer].Count; i++)
                    order[layers[layer][i]] = i;
            }
        }

        private static List<int>[] CreateLists(int count)
        {
            var lists = new List<int>[count];
            for (int i = 0; i < count; i++)
                lists[i] = new List<int>();
            return lists;
        }

        private static float PositiveOrFallback(float value, float fallback)
        {
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class InitialOrderComparer : IComparer<int>
        {
            private readonly float[] _initialY;
            private readonly List<string> _ids;

            internal InitialOrderComparer(float[] initialY, List<string> ids)
            {
                _initialY = initialY;
                _ids = ids;
            }

            public int Compare(int a, int b)
            {
                if (a == b)
                    return 0;
                int yCompare = _initialY[a].CompareTo(_initialY[b]);
                return yCompare != 0 ? yCompare : StringComparer.Ordinal.Compare(_ids[a], _ids[b]);
            }
        }
    }
}
