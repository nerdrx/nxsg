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
        static readonly string[] Emission = { "_EmissionMap" };
        static readonly string[] Mask = { "_ClippingMask", "_AlphaMask" };
        static readonly string[] Metallic = { "_MetallicGlossMap", "_MetallicMap" };
        static readonly string[] Roughness = { "_RoughnessMap" };

        public static bool Supports(Material material)
        {
            var name = material != null && material.shader != null ? material.shader.name : "";
            return name.IndexOf("Poiyomi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static MaterialImportResult Import(Material source, bool pbr = false)
        {
            if (!Supports(source)) throw new InvalidOperationException("Select a Poiyomi or lilToon material.");
            var materialPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(materialPath) || !materialPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("The material must be an asset in this project's Assets folder.");
            var originalGuid = AssetDatabase.AssetPathToGUID(materialPath);

            var used = new HashSet<string>(StringComparer.Ordinal);
            var assignments = new Dictionary<TextureSetSlot, string>();
            var textureProperties = new Dictionary<TextureSetSlot, string>();
            AddTexture(source, Albedo, TextureSetSlot.Albedo, assignments, textureProperties, used);
            AddTexture(source, Normal, TextureSetSlot.Normal, assignments, textureProperties, used);
            AddTexture(source, Emission, TextureSetSlot.Emission, assignments, textureProperties, used);
            AddTexture(source, Mask, TextureSetSlot.Mask, assignments, textureProperties, used);
            if (pbr)
            {
                AddTexture(source, Metallic, TextureSetSlot.Metallic, assignments, textureProperties, used);
                AddTexture(source, Roughness, TextureSetSlot.Roughness, assignments, textureProperties, used);
            }

            var graph = TextureSetGraphBuilder.Build(assignments, pbr);
            ApplyTextureCoordinates(graph, source, assignments, textureProperties);
            var surface = graph.Nodes.First(n => n.Operation == (pbr ? "core.pbrSurface" : "core.toonSurface"));
            var output = graph.Nodes.First(n => n.Operation == "core.output");
            var baseColor = GetColor(source, used, Color.white, "_Color", "_BaseColor");
            ConnectColor(graph, surface, "albedo", baseColor, assignments.ContainsKey(TextureSetSlot.Albedo));

            if (assignments.ContainsKey(TextureSetSlot.Normal))
            {
                var normal = graph.Nodes.First(n => n.Operation == "core.normalMap");
                normal.Properties["strength"] = GetFloat(source, used, 1f, "_BumpScale");
            }

            var emissionColor = GetColor(source, used, assignments.ContainsKey(TextureSetSlot.Emission) ? Color.white : Color.black, "_EmissionColor");
            var emissionStrength = GetFloat(source, used, 1f, "_EmissionStrength");
            if (source.HasProperty("_UseEmission") && GetFloat(source, used, 0f, "_UseEmission") < .5f)
                emissionStrength = 0;
            if (assignments.ContainsKey(TextureSetSlot.Emission) || emissionColor.maxColorComponent > 0)
                ConnectColor(graph, surface, "emission", emissionColor * emissionStrength, assignments.ContainsKey(TextureSetSlot.Emission));

            if (pbr)
            {
                if (!assignments.ContainsKey(TextureSetSlot.Metallic)) surface.Properties["metallic"] = GetFloat(source, used, 0f, "_Metallic");
                if (!assignments.ContainsKey(TextureSetSlot.Roughness)) surface.Properties["roughness"] = 1f - GetFloat(source, used, .5f, "_Glossiness", "_Smoothness");
            }
            surface.Properties["cutoff"] = GetFloat(source, used, .5f, "_Cutoff");
            surface.Properties["useAlbedoAlpha"] = 1;
            output.Properties["renderMode"] = RenderMode(source);
            if (source.HasProperty("_Cull"))
            {
                var sourceCull = Mathf.RoundToInt(GetFloat(source, used, 2f, "_Cull"));
                output.Properties["cull"] = sourceCull == 0 ? 2 : sourceCull == 1 ? 1 : 0;
            }

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

            var unused = backup.shader.GetPropertyCount() == 0 ? new string[0] :
                Enumerable.Range(0, backup.shader.GetPropertyCount())
                    .Where(i => (backup.shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) == 0)
                    .Select(i => backup.shader.GetPropertyName(i))
                    .Where(name => !used.Contains(name))
                    .ToArray();
            File.WriteAllText(reportPath, "NXSG material import\nSource shader: " + backup.shader.name +
                "\nOriginal material: " + materialPath + "\nBackup: " + backupPath + "\nGraph: " + graphPath +
                "\n\nMapped properties: " + string.Join(", ", used.OrderBy(x => x)) +
                "\n\nReview manually: " + string.Join(", ", unused) +
                "\n\nAdvanced layers, masks, animation, and lighting may need manual graph work. Packed metallic/gloss maps may use different channels; inspect the result before upload.\n");
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

        static void ApplyTextureCoordinates(ShaderGraph graph, Material material, Dictionary<TextureSetSlot, string> assignments, Dictionary<TextureSetSlot, string> properties)
        {
            foreach (var pair in assignments)
            {
                var property = properties[pair.Key];
                var scale = material.GetTextureScale(property);
                var offset = material.GetTextureOffset(property);
                if (scale == Vector2.one && offset == Vector2.zero) continue;
                var resource = graph.Resources.FirstOrDefault(r => r.Uri == "project://" + pair.Value);
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

        static int RenderMode(Material material)
        {
            var tag = material.GetTag("RenderType", false, "");
            var shader = material.shader.name;
            if (tag.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0 || shader.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (tag.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 || material.renderQueue >= 3000) return 3;
            return 1;
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
        bool pbr;
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
            pbr = EditorGUILayout.Toggle("PBR surface", pbr);
            EditorGUILayout.HelpBox("Creates an editable .nxsg graph, builds its shader, saves a backup, then switches this same material asset. Avatar material slots keep their references. Review the conversion report and visual result before uploading.", MessageType.Info);
            if (material == null) return;
            if (!MaterialTranslator.Supports(material))
            {
                EditorGUILayout.HelpBox("Select a Poiyomi or lilToon material asset.", MessageType.Warning);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Source shader", material.shader.name);
            EditorGUILayout.LabelField("Asset", AssetDatabase.GetAssetPath(material));
            EditorGUILayout.HelpBox("Common color, texture, emission, normal, cutoff and render settings map automatically. Advanced shader effects need manual review. Original settings stay in the backup.", MessageType.None);
            EditorGUILayout.EndScrollView();
            if (!GUILayout.Button("Import and replace material", GUILayout.Height(32))) return;
            try
            {
                var result = MaterialTranslator.Import(material, pbr);
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
