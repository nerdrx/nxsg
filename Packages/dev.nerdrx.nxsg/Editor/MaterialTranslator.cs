using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NXSG.Editor
{
    public sealed class MaterialImportResult
    {
        public Material SourceMaterial;
        public string GraphPath;
        public string BackupPath;
        public string ReportPath;
    }

    /// <summary>Converts common material inputs, then changes the existing material asset in place.</summary>
    public static class MaterialTranslator
    {
        static readonly string[] Albedo = { "_MainTex", "_BaseMap", "_BaseColorMap" };
        static readonly string[] Normal = { "_BumpMap", "_NormalMap" };
        static readonly string[] Mask = { "_ClippingMask", "_AlphaMask" };
        static readonly string[] Metallic = { "_MetallicGlossMap", "_MetallicMap" };
        static readonly string[] Roughness = { "_RoughnessMap" };

        public static bool Supports(Material material)
        {
            var name = material != null && material.shader != null ? material.shader.name : "";
            return name.IndexOf("Poiyomi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static MaterialImportResult Import(Material source)
        {
            if (!Supports(source)) throw new InvalidOperationException("Select a Poiyomi or lilToon material.");
            var materialPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(materialPath) || !materialPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("The material must be an asset in this project's Assets folder.");
            var originalGuid = AssetDatabase.AssetPathToGUID(materialPath);

            var used = new HashSet<string>(StringComparer.Ordinal);
            var isLilToon = source.shader.name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasCoat = !isLilToon && GetFloat(source, used, 0, "_ClearCoatBRDF") > .5f &&
                GetFloat(source, used, 0, "_ClearCoatStrength") > 0;
            var pbr = hasCoat || (!isLilToon && GetFloat(source, used, 5, "_LightingMode") == 6);
            var assignments = new Dictionary<TextureSetSlot, string>();
            var textureProperties = new Dictionary<TextureSetSlot, string>();
            AddTexture(source, Albedo, TextureSetSlot.Albedo, assignments, textureProperties, used);
            if (!isLilToon || GetFloat(source, used, 1, "_UseBumpMap") > .5f)
                AddTexture(source, Normal, TextureSetSlot.Normal, assignments, textureProperties, used);
            var forceOpaque = !isLilToon && GetFloat(source, used, 0, "_AlphaForceOpaque") > .5f;
            var maskMode = GetFloat(source, used, 1, isLilToon ? "_AlphaMaskMode" : "_MainAlphaMaskMode");
            if (!forceOpaque && maskMode > .5f) AddTexture(source, Mask, TextureSetSlot.Mask, assignments, textureProperties, used);
            if (pbr)
            {
                AddTexture(source, Metallic, TextureSetSlot.Metallic, assignments, textureProperties, used);
                AddTexture(source, Roughness, TextureSetSlot.Roughness, assignments, textureProperties, used);
            }

            // Clear coat needs the layered PBR surface; source settings choose the surface automatically.
            var graph = TextureSetGraphBuilder.Build(assignments, pbr);
            var namedResources = new HashSet<string>();
            foreach (var assignment in assignments.OrderBy(p => p.Key))
            {
                var resource = graph.Resources.FirstOrDefault(r => r.Uri == "project://" + assignment.Value && !namedResources.Contains(r.Id));
                if (resource != null) { resource.Name = TextureSlotLabels.Clean(assignment.Key.ToString()); namedResources.Add(resource.Id); }
            }
            ApplyTextureCoordinates(graph, source, assignments, textureProperties);
            var surface = graph.Nodes.First(n => n.Operation == (pbr ? "core.pbrSurface" : "core.toonSurface"));
            var output = graph.Nodes.First(n => n.Operation == "core.output");
            if (hasCoat)
            {
                surface.Operation = "core.layeredPbrSurface";
                surface.Properties["coat"] = GetFloat(source, used, 0, "_ClearCoatStrength");
                surface.Properties["coatRoughness"] = 1f - GetFloat(source, used, 1, "_ClearCoatSmoothness");
            }
            var baseColor = GetColor(source, used, Color.white, "_Color", "_BaseColor");
            ConnectColor(graph, surface, "albedo", baseColor, assignments.ContainsKey(TextureSetSlot.Albedo));

            if (assignments.ContainsKey(TextureSetSlot.Normal))
            {
                var normal = graph.Nodes.First(n => n.Operation == "core.normalMap");
                normal.Properties["strength"] = GetFloat(source, used, 1f, "_BumpScale");
            }
            BuildExtraNormal(graph, source, surface, isLilToon, used);

            BuildColorLayers(graph, source, surface, isLilToon, used);
            BuildMatcaps(graph, source, surface, isLilToon, used);
            BuildEmission(graph, source, surface, isLilToon, used);
            BuildGlitter(graph, source, surface, isLilToon, used);
            BuildOpacity(graph, source, surface, output, isLilToon, used);
            BuildShading(graph, source, surface, isLilToon, used);

            if (pbr)
            {
                if (!assignments.ContainsKey(TextureSetSlot.Metallic)) surface.Properties["metallic"] = GetFloat(source, used, 0f, "_Metallic");
                if (!assignments.ContainsKey(TextureSetSlot.Roughness)) surface.Properties["roughness"] = 1f - GetFloat(source, used, .5f, "_Glossiness", "_Smoothness");
                if (hasCoat)
                {
                    var coatMap = Texture(graph, source, "_ClearCoatMaps", used, "Clear coat mask", channel: "r");
                    if (coatMap != null)
                        Connect(graph, Combine(graph, "core.multiply", coatMap,
                            Scalar(graph, GetFloat(source, used, 0, "_ClearCoatStrength"), "Clear coat strength"), "Clear coat amount", true), surface, "coat");
                }
            }
            surface.Properties["cutoff"] = GetFloat(source, used, .5f, "_Cutoff");
            output.Properties["renderMode"] = RenderMode(source, isLilToon);
            surface.Properties["useAlbedoAlpha"] = (int)output.Properties["renderMode"] == 1 ? 0 : 1;
            if (source.HasProperty("_Cull"))
            {
                var sourceCull = Mathf.RoundToInt(GetFloat(source, used, 2f, "_Cull"));
                output.Properties["cull"] = sourceCull == 0 ? 2 : sourceCull == 1 ? 1 : 0;
            }

            BuildOutline(graph, source, surface, output, isLilToon, used);

            PlaceNewNodes(graph);
            BindTextures(graph);
            var directory = Path.GetDirectoryName(materialPath).Replace('\\', '/');
            var stem = Path.GetFileNameWithoutExtension(materialPath);
            var graphPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + stem + ".nxsg");
            var backupPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + stem + ".before-NXSG.mat");
            var reportPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + stem + ".NXSG-import.txt");
            File.WriteAllText(graphPath, GraphJson.Serialize(graph, true));
            AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);

            Material generated;
            try { generated = GraphBuild.Build(graph, Path.GetFullPath(graphPath)); }
            catch { AssetDatabase.DeleteAsset(graphPath); throw; }
            AssetDatabase.SaveAssetIfDirty(source);
            if (!AssetDatabase.CopyAsset(materialPath, backupPath))
                throw new IOException("Could not back up the original material. It was not changed.");
            AssetDatabase.ImportAsset(backupPath, ImportAssetOptions.ForceSynchronousImport);
            var backup = AssetDatabase.LoadAssetAtPath<Material>(backupPath);
            if (backup == null) throw new IOException("Could not verify the material backup. The original was not changed.");

            var review = ActiveUnmapped(backup, used);
            File.WriteAllText(reportPath, "NXSG material import\nSource shader: " + backup.shader.name +
                "\nOriginal material: " + materialPath + "\nBackup: " + backupPath + "\nGraph: " + graphPath +
                "\n\nMapped properties: " + string.Join(", ", used.OrderBy(x => x)) +
                "\n\nActive settings needing manual review:\n" + (review.Length == 0 ? "None detected" : string.Join("\n", review)) +
                "\n\nImport is best effort. AudioLink response, glitter shape, UV blends, shadow models and packed channels may differ. Compare the original backup before upload.\n");
            AssetDatabase.ImportAsset(reportPath);

            var oldName = source.name;
            try
            {
                Undo.RecordObject(source, "Import material to NXSG");
                source.shader = generated.shader;
                source.CopyPropertiesFromMaterial(generated);
                source.name = oldName;
                EditorUtility.SetDirty(source);
                AssetDatabase.SaveAssetIfDirty(source);
                if (AssetDatabase.AssetPathToGUID(materialPath) != originalGuid || AssetDatabase.GetAssetPath(source) != materialPath)
                    throw new IOException("Material asset identity changed during import.");
            }
            catch
            {
                EditorUtility.CopySerialized(backup, source);
                source.name = oldName;
                EditorUtility.SetDirty(source);
                AssetDatabase.SaveAssetIfDirty(source);
                throw;
            }

            return new MaterialImportResult { SourceMaterial = source, GraphPath = graphPath, BackupPath = backupPath, ReportPath = reportPath };
        }

        static void AddTexture(Material material, string[] properties, TextureSetSlot slot, Dictionary<TextureSetSlot, string> assignments, Dictionary<TextureSetSlot, string> textureProperties, HashSet<string> used)
        {
            foreach (var property in properties)
            {
                if (!material.HasProperty(property) || !(material.GetTexture(property) is Texture2D texture)) continue;
                var path = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(path) || !(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))) continue;
                assignments[slot] = path; textureProperties[slot] = property; used.Add(property); return;
            }
        }

        static string[] ActiveUnmapped(Material material, HashSet<string> used)
        {
            var shader = material.shader;
            var review = new List<string>();
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                if (used.Contains(name)) continue;
                var type = shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Texture)
                {
                    if (material.GetTexture(name) != null && AssetDatabase.Contains(material.GetTexture(name)))
                        review.Add(name + " (assigned texture)");
                    continue;
                }
                if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range) continue;
                if (!(name.StartsWith("_Enable", StringComparison.Ordinal) || name.StartsWith("_Use", StringComparison.Ordinal) ||
                    name.StartsWith("_Apply", StringComparison.Ordinal) || name.EndsWith("Enabled", StringComparison.Ordinal))) continue;
                var value = material.GetFloat(name);
                if (value > .5f) review.Add(name + " = " + value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture));
            }
            return review.Take(100).ToArray();
        }

        static void ApplyTextureCoordinates(ShaderGraph graph, Material material, Dictionary<TextureSetSlot, string> assignments, Dictionary<TextureSetSlot, string> properties)
        {
            var usedResources = new HashSet<string>();
            foreach (var pair in assignments.OrderBy(p => p.Key))
            {
                var property = properties[pair.Key];
                var scale = material.GetTextureScale(property);
                var offset = material.GetTextureOffset(property);
                var resource = graph.Resources.FirstOrDefault(r => r.Uri == "project://" + pair.Value && !usedResources.Contains(r.Id));
                if (resource != null) usedResources.Add(resource.Id);
                if (scale == Vector2.one && offset == Vector2.zero) continue;
                var texture = resource == null ? null : graph.Nodes.FirstOrDefault(n => (string)n.Properties["resourceId"] == resource.Id);
                if (texture == null) continue;
                var connection = graph.Connections.FirstOrDefault(c => c.To.NodeId == texture.Id && c.To.PortId == "uv");
                if (connection == null) continue;
                graph.Connections.Remove(connection);
                var transform = NodeCatalog.Create("core.uvTransform"); transform.Id = "uvTransform-" + graph.Nodes.Count;
                transform.Properties["tiling"] = new JArray(scale.x, scale.y);
                transform.Properties["offset"] = new JArray(offset.x, offset.y);
                graph.Nodes.Add(transform);
                graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = connection.From, To = new GraphPortRef { NodeId = transform.Id, PortId = "uv" } });
                Wire(graph, transform, "uv", texture, "uv");
            }
        }

        static void PlaceNewNodes(ShaderGraph graph)
        {
            var row = 0;
            foreach (var node in graph.Nodes)
            {
                if (graph.Layout.Nodes.ContainsKey(node.Id)) continue;
                graph.Layout.Nodes[node.Id] = new GraphNodeLayout { X = 420, Y = 80 + row++ * 180 };
            }
            var bounds = graph.Nodes.ToDictionary(n => n.Id, n =>
            {
                var position = graph.Layout.Nodes[n.Id];
                var height = 80 + 22 * (NodeCatalog.Ports(n.Operation, false).Length + NodeCatalog.Ports(n.Operation, true).Length);
                return new Rect((float)position.X, (float)position.Y, 175, height);
            });
            var arranged = GraphAutoLayout.Arrange(bounds, graph.Connections.Select(c => (from: c.From.NodeId, to: c.To.NodeId)));
            foreach (var pair in arranged)
                graph.Layout.Nodes[pair.Key] = new GraphNodeLayout { X = pair.Value.x, Y = pair.Value.y };
        }

        static Color GetColor(Material material, HashSet<string> used, Color fallback, params string[] properties)
        {
            foreach (var property in properties)
                if (material.HasProperty(property)) { used.Add(property); return material.GetColor(property); }
            return fallback;
        }

        static float GetFloat(Material material, HashSet<string> used, float fallback, params string[] properties)
        {
            foreach (var property in properties)
                if (material.HasProperty(property)) { used.Add(property); return material.GetFloat(property); }
            return fallback;
        }

        static int RenderMode(Material material, bool lilToon)
        {
            if (lilToon && material.HasProperty("_TransparentMode"))
            {
                var mode = Mathf.RoundToInt(material.GetFloat("_TransparentMode"));
                if (mode == 1 || mode == 5) return 2;
                if (mode == 2 || mode == 3 || mode == 4 || mode == 6) return 3;
            }
            if (material.HasProperty("_AlphaForceOpaque") && material.GetFloat("_AlphaForceOpaque") > .5f) return 1;
            var tag = material.GetTag("RenderType", false, "");
            var shader = material.shader.name;
            if (tag.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0 || shader.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (tag.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 || material.renderQueue >= 3000) return 3;
            return 1;
        }

        // A source is a typed output port. Reusing it keeps every imported feature wired to the final surface.
        static GraphPortRef Port(GraphNode node, string name) => new GraphPortRef { NodeId = node.Id, PortId = name };

        static GraphNode Add(ShaderGraph graph, string operation, string hint)
        {
            var node = NodeCatalog.Create(operation);
            node.Id = hint + "-" + graph.Nodes.Count;
            graph.Nodes.Add(node);
            return node;
        }

        static void Connect(ShaderGraph graph, GraphPortRef from, GraphNode to, string port)
        {
            graph.Connections.RemoveAll(c => c.To.NodeId == to.Id && c.To.PortId == port);
            graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = from, To = Port(to, port) });
        }

        static GraphPortRef Constant(ShaderGraph graph, Color color, string hint)
        {
            var node = Add(graph, "core.constant", hint);
            node.Properties["valueType"] = "color";
            node.Properties["value"] = new JArray(color.r, color.g, color.b, color.a);
            return Port(node, "value");
        }

        static GraphPortRef Scalar(ShaderGraph graph, float value, string hint)
        {
            var node = Add(graph, "core.constant", hint);
            node.Properties["valueType"] = "float";
            node.Properties["value"] = value;
            return Port(node, "value");
        }

        static GraphPortRef Combine(ShaderGraph graph, string operation, GraphPortRef a, GraphPortRef b, string hint, bool scalar = false)
        {
            var node = Add(graph, operation, hint);
            if (scalar) node.Properties["valueType"] = "float";
            Connect(graph, a, node, "a");
            Connect(graph, b, node, "b");
            return Port(node, "value");
        }

        static GraphPortRef Texture(ShaderGraph graph, Material material, string property, HashSet<string> used,
            string hint, int uvMode = 0, Vector2? pan = null, string channel = "color")
        {
            if (!material.HasProperty(property) || !(material.GetTexture(property) is Texture2D texture)) return null;
            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))) return null;
            used.Add(property);
            var sample = Add(graph, "core.texture2D", hint);
            var resource = new GraphResource { Id = "texture-" + sample.Id, Name = TextureSlotLabels.Clean(hint), Kind = "texture2D", Uri = "project://" + path };
            graph.Resources.Add(resource);
            sample.Properties["resourceId"] = resource.Id;
            var uv = Add(graph, "core.uv0", hint + "UV");
            uv.Properties["coordinateSource"] = Coordinate(uvMode);
            var uvSource = Port(uv, "uv");
            var scale = material.GetTextureScale(property);
            var offset = material.GetTextureOffset(property);
            if (scale != Vector2.one || offset != Vector2.zero)
            {
                var transform = Add(graph, "core.uvTransform", hint + "Tiling");
                transform.Properties["tiling"] = new JArray(scale.x, scale.y);
                transform.Properties["offset"] = new JArray(offset.x, offset.y);
                Connect(graph, uvSource, transform, "uv");
                uvSource = Port(transform, "uv");
            }
            if (pan.HasValue && pan.Value != Vector2.zero)
            {
                var scroll = Add(graph, "core.uvScroll", hint + "Pan");
                scroll.Properties["speed"] = new JArray(pan.Value.x, pan.Value.y);
                Connect(graph, uvSource, scroll, "uv");
                uvSource = Port(scroll, "uv");
            }
            Connect(graph, uvSource, sample, "uv");
            if (channel != "r") return Port(sample, channel);
            var split = Add(graph, "core.splitColor", hint + " channel");
            Connect(graph, Port(sample, "color"), split, "color");
            return Port(split, "r");
        }

        static Vector2 Pan(Material material, string property)
        {
            if (!material.HasProperty(property)) return Vector2.zero;
            var value = material.GetVector(property);
            return new Vector2(value.x, value.y);
        }

        static string Coordinate(int mode)
        {
            if (mode >= 0 && mode <= 3) return "uv" + mode;
            switch (mode)
            {
                case 4: return "panosphere";
                case 5: return "world";
                case 6: return "polar";
                case 8: return "object";
                case 9: return "matcap";
                default: return "uv0";
            }
        }

        static GraphPortRef Source(ShaderGraph graph, GraphNode target, string port)
        {
            return graph.Connections.FirstOrDefault(c => c.To.NodeId == target.Id && c.To.PortId == port)?.From;
        }

        static void BuildColorLayers(ShaderGraph graph, Material material, GraphNode surface, bool lilToon, HashSet<string> used)
        {
            if (!lilToon) return;
            foreach (var suffix in new[] { "2nd", "3rd" })
            {
                if (GetFloat(material, used, 0, "_UseMain" + suffix + "Tex") < .5f) continue;
                var overlay = Texture(graph, material, "_Main" + suffix + "Tex", used, "Main " + suffix);
                var tint = GetColor(material, used, Color.white, "_Color" + suffix);
                if (overlay == null) overlay = Constant(graph, tint, "Main " + suffix + " color");
                else if (tint != Color.white) overlay = Combine(graph, "core.multiply", overlay, Constant(graph, tint, "Main " + suffix + " tint"), "Main " + suffix + " tinted");
                var previous = Source(graph, surface, "albedo") ?? Constant(graph, GetColor(material, used, Color.white, "_Color"), "Base color");
                var layer = Add(graph, "core.layer", "Main " + suffix + " layer");
                Connect(graph, previous, layer, "base");
                Connect(graph, overlay, layer, "overlay");
                var mask = Texture(graph, material, "_Main" + suffix + "BlendMask", used, "Main " + suffix + " mask", channel: "r");
                if (mask != null) Connect(graph, mask, layer, "mask");
                Connect(graph, Port(layer, "color"), surface, "albedo");
            }
        }

        static void BuildExtraNormal(ShaderGraph graph, Material material, GraphNode surface, bool lilToon, HashSet<string> used)
        {
            if (!lilToon || GetFloat(material, used, 0, "_UseBump2ndMap") < .5f) return;
            var texture = Texture(graph, material, "_Bump2ndMap", used, "Second normal");
            if (texture == null) return;
            var normal = Add(graph, "core.normalMap", "Second normal map");
            normal.Properties["strength"] = GetFloat(material, used, 1, "_Bump2ndScale");
            Connect(graph, texture, normal, "color");
            var first = Source(graph, surface, "normal");
            if (first == null) { Connect(graph, Port(normal, "normal"), surface, "normal"); return; }
            var blend = Add(graph, "core.normalBlend", "Combined normals");
            Connect(graph, first, blend, "a");
            Connect(graph, Port(normal, "normal"), blend, "b");
            Connect(graph, Port(blend, "normal"), surface, "normal");
        }

        static void BuildMatcaps(ShaderGraph graph, Material material, GraphNode surface, bool lilToon, HashSet<string> used)
        {
            if (!lilToon) return;
            foreach (var suffix in new[] { "", "2nd" })
            {
                if (GetFloat(material, used, 0, "_UseMatCap" + suffix) < .5f) continue;
                var property = "_MatCap" + suffix + "Tex";
                if (!material.HasProperty(property) || !(material.GetTexture(property) is Texture2D texture)) continue;
                var path = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(path)) continue;
                used.Add(property);
                var matcap = Add(graph, "core.matcapTexture", "Matcap " + suffix);
                var resource = new GraphResource { Id = "texture-" + matcap.Id, Name = "Matcap " + (suffix == "" ? "1" : "2"), Kind = "texture2D", Uri = "project://" + path };
                graph.Resources.Add(resource); matcap.Properties["resourceId"] = resource.Id;
                var normal = Source(graph, surface, "normal");
                if (normal != null) Connect(graph, normal, matcap, "normal");
                var tint = GetColor(material, used, Color.white, "_MatCap" + suffix + "Color");
                GraphPortRef color = Port(matcap, "color");
                if (tint != Color.white) color = Combine(graph, "core.multiply", color, Constant(graph, tint, "Matcap tint"), "Tinted matcap");
                var baseColor = Source(graph, surface, "albedo");
                if (baseColor == null) baseColor = Constant(graph, Color.white, "Matcap base");
                var layer = Add(graph, "core.layer", "Matcap layer");
                Connect(graph, baseColor, layer, "base"); Connect(graph, color, layer, "overlay");
                var mask = Texture(graph, material, "_MatCap" + suffix + "BlendMask", used, "Matcap mask", channel: "r");
                if (mask != null) Connect(graph, mask, layer, "mask");
                else layer.Properties["mask"] = GetFloat(material, used, 1, "_MatCap" + suffix + "Blend");
                Connect(graph, Port(layer, "color"), surface, "albedo");
            }
        }

        static void BuildEmission(ShaderGraph graph, Material material, GraphNode surface, bool lilToon, HashSet<string> used)
        {
            var suffixes = lilToon ? new[] { "", "2nd" } : new[] { "", "1", "2", "3" };
            GraphPortRef sum = null;
            foreach (var suffix in suffixes)
            {
                var enabled = lilToon ? "_UseEmission" + suffix : "_EnableEmission" + suffix;
                // Locked shaders omit disabled layers entirely. Missing enable flags are not enabled layers.
                if (!material.HasProperty(enabled) || GetFloat(material, used, 0, enabled) < .5f) continue;
                var map = "_Emission" + suffix + (lilToon ? "Map" : "Map");
                if (!lilToon) map = "_EmissionMap" + suffix;
                var colorProperty = lilToon ? "_Emission" + suffix + "Color" : "_EmissionColor" + suffix;
                var strengthProperty = "_EmissionStrength" + suffix;
                var tint = GetColor(material, used, Color.white, colorProperty);
                var strength = lilToon ? 1f : GetFloat(material, used, 1f, strengthProperty);
                var uvMode = !lilToon ? Mathf.RoundToInt(GetFloat(material, used, 0, map + "UV")) : 0;
                var texture = Texture(graph, material, map, used, "Emission " + (suffix == "" ? "0" : suffix), uvMode,
                    lilToon ? (Vector2?)null : Pan(material, map + "Pan"));
                if (texture == null && tint.maxColorComponent <= 0) continue;
                GraphPortRef value = texture ?? Constant(graph, Color.white, "Emission " + suffix + " base");
                if (tint != Color.white) value = Combine(graph, "core.multiply", value, Constant(graph, tint, "Emission " + suffix + " tint"), "Emission " + suffix + " tinted");
                var maskProperty = lilToon ? "_Emission" + suffix + "BlendMask" : "_EmissionMask" + suffix;
                var maskUv = lilToon ? 0 : Mathf.RoundToInt(GetFloat(material, used, 0, maskProperty + "UV"));
                var mask = Texture(graph, material, maskProperty, used, "Emission " + suffix + " mask", maskUv,
                    lilToon ? (Vector2?)null : Pan(material, maskProperty + "Pan"), "r");
                if (mask != null) value = Combine(graph, "core.multiply", value, mask, "Emission " + suffix + " masked");
                var audioFlag = "_EmissionAL" + (suffix == "" ? "0" : suffix) + "Enabled";
                if (!lilToon && GetFloat(material, used, 0, "_EnableAudioLink") > .5f && GetFloat(material, used, 0, audioFlag) > .5f)
                {
                    var audio = Add(graph, "core.audioLink", "Emission " + suffix + " AudioLink");
                    // Poiyomi AudioLink modes are richer; this preserves the active audio-driven branch.
                    audio.Properties["fallback"] = 1;
                    value = Combine(graph, "core.multiply", value, Port(audio, "value"), "Emission " + suffix + " audio");
                }
                if (strength != 1f) value = Combine(graph, "core.multiply", value, Scalar(graph, strength, "Emission " + suffix + " strength"), "Emission " + suffix + " scaled");
                sum = sum == null ? value : Combine(graph, "core.add", sum, value, "Emission layers");
            }
            if (sum != null) Connect(graph, sum, surface, "emission");
        }

        static void BuildGlitter(ShaderGraph graph, Material material, GraphNode surface, bool lilToon, HashSet<string> used)
        {
            var enabled = lilToon ? "_UseGlitter" : "_GlitterEnable";
            if (GetFloat(material, used, 0, enabled) < .5f) return;
            var glitter = Add(graph, "core.glitter", "Glitter");
            if (lilToon && material.HasProperty("_GlitterParams1"))
            {
                var parameters = material.GetVector("_GlitterParams1"); used.Add("_GlitterParams1");
                glitter.Properties["scale"] = Mathf.Max(1, (parameters.x + parameters.y) * .5f);
                glitter.Properties["size"] = parameters.z;
                glitter.Properties["sharpness"] = parameters.w;
                if (material.HasProperty("_GlitterParams2"))
                {
                    glitter.Properties["speed"] = material.GetVector("_GlitterParams2").x;
                    used.Add("_GlitterParams2");
                }
            }
            else if (!lilToon)
            {
                glitter.Properties["scale"] = GetFloat(material, used, 60, "_GlitterFrequency");
                glitter.Properties["size"] = GetFloat(material, used, .16f, "_GlitterSize");
                glitter.Properties["brightness"] = GetFloat(material, used, 2f, "_GlitterBrightness");
                glitter.Properties["speed"] = GetFloat(material, used, 1f, "_GlitterSpeed");
                // Poiyomi applies contrast after its glint calculation. NXSG uses a view-angle exponent.
                glitter.Properties["sharpness"] = Mathf.Pow(Mathf.Max(1, GetFloat(material, used, 32f, "_GlitterContrast")), 1f / 3f);
                glitter.Properties["density"] = Mathf.Clamp01(.4f * GetFloat(material, used, 1, "_GlitterLayers"));
                if (material.HasProperty("_GlitterTexture") && material.GetTexture("_GlitterTexture") is Texture2D shape)
                {
                    var shapeName = shape.name.ToLowerInvariant();
                    glitter.Properties["shape"] = shapeName.Contains("cross") ? 2 : shapeName.Contains("star") ? 3 : shapeName.Contains("square") ? 1 : 0;
                    // NXSG's cross has long arms relative to the cell; keep imported texture stamps small.
                    if (shapeName.Contains("cross")) glitter.Properties["size"] = (float)glitter.Properties["size"] * .16f;
                    // Keep the source texture in the review report: the built-in shape is an approximation.
                }
            }
            var tint = GetColor(material, used, Color.white, "_GlitterColor");
            GraphPortRef glitterColor = Constant(graph, tint, "Glitter color");
            if (lilToon)
            {
                var colorMap = Texture(graph, material, "_GlitterColorTex", used, "Glitter color map");
                if (colorMap != null) glitterColor = Combine(graph, "core.multiply", glitterColor, colorMap, "Glitter textured color");
            }
            Connect(graph, glitterColor, glitter, "color");
            if (!lilToon)
            {
                var maskUv = Mathf.RoundToInt(GetFloat(material, used, 0, "_GlitterMaskUV"));
                var mask = Texture(graph, material, "_GlitterMask", used, "Glitter mask", maskUv, Pan(material, "_GlitterMaskPan"), "r");
                if (mask != null)
                {
                    if (GetFloat(material, used, 0, "_GlitterMaskInvert") > .5f)
                        mask = Combine(graph, "core.subtract", Scalar(graph, 1, "One"), mask, "Inverted glitter mask", true);
                    Connect(graph, mask, glitter, "mask");
                }
            }
            var uv = Add(graph, "core.uv0", "Glitter UV");
            if (lilToon && GetFloat(material, used, 0, "_GlitterUVMode") > .5f) uv.Properties["coordinateSource"] = "uv1";
            if (!lilToon) uv.Properties["coordinateSource"] = Coordinate(Mathf.RoundToInt(GetFloat(material, used, 0, "_GlitterUV")));
            GraphPortRef uvSource = Port(uv, "uv");
            var pan = Pan(material, "_GlitterUVPanning");
            if (pan != Vector2.zero)
            {
                var scroll = Add(graph, "core.uvScroll", "Glitter pan");
                scroll.Properties["speed"] = new JArray(pan.x, pan.y);
                Connect(graph, uvSource, scroll, "uv"); uvSource = Port(scroll, "uv");
            }
            Connect(graph, uvSource, glitter, "uv");
            GraphPortRef sparkle = Port(glitter, "color");
            if (!lilToon && GetFloat(material, used, 0, "_EnableAudioLink") > .5f && GetFloat(material, used, 0, "_GlitterALEnabled") > .5f)
            {
                var audio = Add(graph, "core.audioLink", "Glitter AudioLink");
                var clock = Add(graph, "core.time", "Glitter clock");
                Connect(graph, Combine(graph, "core.add", Port(clock, "value"),
                    Combine(graph, "core.multiply", Port(audio, "value"), Scalar(graph, 2, "Audio motion"), "Audio rotation speed", true),
                    "Glitter audio motion", true), glitter, "time");
            }
            var previous = Source(graph, surface, "emission");
            Connect(graph, previous == null ? sparkle : Combine(graph, "core.add", previous, sparkle, "Emission and glitter"), surface, "emission");
        }

        static void BuildOpacity(ShaderGraph graph, Material material, GraphNode surface, GraphNode output, bool lilToon, HashSet<string> used)
        {
            if (!lilToon && GetFloat(material, used, 0, "_AlphaForceOpaque") > .5f) return;
            var opacity = Source(graph, surface, "opacity");
            if (opacity == null) return;
            if (!lilToon && GetFloat(material, used, 0, "_AlphaMaskInvert") > .5f)
            {
                opacity = Combine(graph, "core.subtract", Scalar(graph, 1, "One"), opacity, "Inverted alpha", true);
                Connect(graph, opacity, surface, "opacity");
            }
            if (!lilToon)
            {
                var mod = GetFloat(material, used, 0, "_AlphaMod");
                if (Mathf.Abs(mod) > 0.0001f)
                    Connect(graph, Combine(graph, "core.add", opacity, Scalar(graph, mod, "Alpha adjustment"), "Adjusted alpha", true), surface, "opacity");
            }
        }

        static void BuildShading(ShaderGraph graph, Material material, GraphNode surface, bool lilToon, HashSet<string> used)
        {
            if (surface.Operation != "core.toonSurface") return;
            if (lilToon && GetFloat(material, used, 0, "_UseShadow") > .5f)
            {
                foreach (var suffix in new[] { "", "2nd", "3rd" })
                {
                    var index = suffix == "" ? "" : suffix == "2nd" ? "2" : "3";
                    var color = GetColor(material, used, Color.white, "_Shadow" + suffix + "Color");
                    Connect(graph, Constant(graph, color, "Shadow " + index + " color"), surface, "shadeColor" + index);
                    surface.Properties["threshold" + index] = GetFloat(material, used, .5f, "_Shadow" + suffix + "Border");
                    surface.Properties["softness" + index] = GetFloat(material, used, .1f, "_Shadow" + suffix + "Blur");
                }
            }
            if (GetFloat(material, used, 0, lilToon ? "_UseRim" : "_RimEnabled") > .5f)
            {
                var rim = GetColor(material, used, Color.white, "_RimColor");
                surface.Properties["rimColor"] = new JArray(rim.r, rim.g, rim.b, rim.a);
                surface.Properties["rimStrength"] = 1;
                surface.Properties["rimWidth"] = GetFloat(material, used, .2f, "_RimBorder");
                surface.Properties["rimSoftness"] = GetFloat(material, used, .05f, "_RimBlur");
            }
            if (!lilToon && material.HasProperty("_LightingMinLightBrightness"))
                surface.Properties["lightingMin"] = GetFloat(material, used, 0, "_LightingMinLightBrightness");
        }

        static void BuildOutline(ShaderGraph graph, Material material, GraphNode surface, GraphNode output, bool lilToon, HashSet<string> used)
        {
            var enabled = lilToon ? "_UseOutline" : "_EnableOutlines";
            if (GetFloat(material, used, 0, enabled) < .5f) return;
            var outline = Add(graph, "core.outline", "Outline");
            outline.Properties["width"] = GetFloat(material, used, .003f, lilToon ? "_OutlineWidth" : "_OutlineWidth");
            var color = GetColor(material, used, Color.black, "_OutlineColor");
            outline.Properties["color"] = new JArray(color.r, color.g, color.b, color.a);
            Connect(graph, Port(surface, "surface"), outline, "base");
            Connect(graph, Port(outline, "surface"), output, "surface");
        }

        static void ConnectColor(ShaderGraph graph, GraphNode surface, string port, Color color, bool hasTexture)
        {
            if (color == Color.white && hasTexture) return;
            if (color == Color.black && !hasTexture && port == "emission") return;
            var constant = NodeCatalog.Create("core.constant"); constant.Id = port + "Color-" + graph.Nodes.Count;
            constant.Properties["valueType"] = "color";
            constant.Properties["value"] = new JArray(color.r, color.g, color.b, color.a);
            graph.Nodes.Add(constant);
            if (!hasTexture) { Wire(graph, constant, "value", surface, port); return; }
            var previous = graph.Connections.First(c => c.To.NodeId == surface.Id && c.To.PortId == port);
            graph.Connections.Remove(previous);
            var multiply = NodeCatalog.Create("core.multiply"); multiply.Id = port + "Tint-" + graph.Nodes.Count;
            graph.Nodes.Add(multiply);
            graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = previous.From, To = new GraphPortRef { NodeId = multiply.Id, PortId = "a" } });
            Wire(graph, constant, "value", multiply, "b"); Wire(graph, multiply, "value", surface, port);
        }

        static void Wire(ShaderGraph graph, GraphNode from, string fromPort, GraphNode to, string toPort)
        {
            graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = new GraphPortRef { NodeId = from.Id, PortId = fromPort }, To = new GraphPortRef { NodeId = to.Id, PortId = toPort } });
        }

        static void BindTextures(ShaderGraph graph)
        {
            var textures = new JObject();
            foreach (var resource in graph.Resources)
            {
                var path = resource.Uri.StartsWith("project://", StringComparison.Ordinal) ? resource.Uri.Substring(10) : "";
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) textures[resource.Id] = guid;
            }
            graph.Adapter = new JObject { ["textures"] = textures };
        }
    }

    public sealed class MaterialTranslatorWindow : EditorWindow
    {
        Material material;
        Vector2 scroll;

        [MenuItem("Tools/NXSG/Import Poiyomi or lilToon material…")]
        public static void Open()
        {
            var window = GetWindow<MaterialTranslatorWindow>(true, "Import material to NXSG", true);
            window.material = Selection.activeObject as Material;
            window.minSize = new Vector2(480, 260);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Import material to NXSG", EditorStyles.boldLabel);
            material = EditorGUILayout.ObjectField("Material", material, typeof(Material), false) as Material;
            EditorGUILayout.HelpBox("Detects the source material's surface and enabled effects, builds an editable .nxsg graph, saves a backup, then switches this same material asset. Avatar material slots keep their references.", MessageType.Info);
            if (material == null) return;
            if (!MaterialTranslator.Supports(material))
            {
                EditorGUILayout.HelpBox("Select a Poiyomi or lilToon material asset.", MessageType.Warning);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Source shader", material.shader.name);
            EditorGUILayout.LabelField("Asset", AssetDatabase.GetAssetPath(material));
            EditorGUILayout.HelpBox("Active texture layers, emission, glitter, clear coat, AudioLink and outlines map where NXSG supports them. The report names active settings that still need review. Compare with the original before uploading.", MessageType.None);
            EditorGUILayout.EndScrollView();
            if (!GUILayout.Button("Import and replace material", GUILayout.Height(32))) return;
            try
            {
                var result = MaterialTranslator.Import(material);
                Selection.activeObject = result.SourceMaterial;
                GraphWindow.Open(result.GraphPath, result.SourceMaterial);
                EditorUtility.DisplayDialog("NXSG import complete", "Graph: " + result.GraphPath + "\nBackup: " + result.BackupPath + "\nReport: " + result.ReportPath, "Open graph");
                Close();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("NXSG import failed", error.Message + "\nThe original material was kept or restored from its backup.", "OK");
            }
        }
    }
}
