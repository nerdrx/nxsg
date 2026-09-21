using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        [SerializeField] string selectedParameterId;

        void AddParameterControls()
        {
            if (graph == null) return;
            if (graph.Parameters == null) graph.Parameters = new List<GraphParameter>();
            var section = new Foldout { text = "PARAMETERS", value = true };
            section.Add(new Button(() => AddParameter(GraphValueType.Float)) { text = "+ Float parameter" });
            section.Add(new Button(() => AddParameter(GraphValueType.Color)) { text = "+ Color parameter" });
            foreach (var parameter in graph.Parameters.Where(p => p != null).ToList()) AddParameterRow(section, parameter);
            inspector.Add(section);
            var node = selected == null ? null : graph.Nodes.FirstOrDefault(n => n.Id == selected && n.Operation == "core.parameter");
            if (node != null)
            {
                selectedParameterId = (string)node.Properties["parameterId"];
                var parameters = graph.Parameters.Where(p => p != null).ToList();
                if (parameters.Count > 0)
                {
                    var reference = new PopupField<string>("Reads", parameters.Select(p => p.Name).ToList(), Math.Max(0, parameters.FindIndex(p => p.Id == selectedParameterId)));
                    reference.RegisterValueChangedCallback(e => Edit("Reassign parameter", () => node.Properties["parameterId"] = parameters[reference.index].Id));
                    section.Add(reference);
                }
            }
            if (!string.IsNullOrEmpty(selectedParameterId))
            {
                var parameter = graph.Parameters.FirstOrDefault(p => p != null && p.Id == selectedParameterId);
                if (parameter != null) AddParameterEditor(section, parameter);
            }
        }

        void AddParameterRow(VisualElement parent, GraphParameter parameter)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
            var button = new Button(() => { selectedParameterId = parameter.Id; RebuildInspector(); })
            { text = parameter.Name + " · " + parameter.Type, style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };
            row.Add(button);
            row.Add(new Button(() => AddParameterNode(parameter.Id)) { text = "+ node", tooltip = "Add a node that reads this parameter." });
            parent.Add(row);
        }

        void AddParameterEditor(VisualElement parent, GraphParameter parameter)
        {
            var editor = new VisualElement { style = { marginTop = 6, paddingLeft = 6, paddingRight = 6, paddingBottom = 6, backgroundColor = new Color(.12f, .12f, .12f) } };
            editor.Add(new Label("EDIT PARAMETER") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var name = new TextField("Name") { value = parameter.Name, isDelayed = true };
            name.RegisterValueChangedCallback(e =>
            {
                var value = (e.newValue ?? "").Trim();
                if (value.Length == 0 || graph.Parameters.Any(p => p != parameter && string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase)))
                { name.SetValueWithoutNotify(parameter.Name); SetStatus("Parameter names must be unique and non-empty."); return; }
                Edit("Rename parameter", () => parameter.Name = value);
            });
            editor.Add(name);
            var types = new List<string> { "Float", "Color" };
            var type = new PopupField<string>("Type", types, parameter.Type == GraphValueType.Color ? 1 : 0);
            type.RegisterValueChangedCallback(e => Edit("Change parameter type", () =>
            {
                parameter.Type = e.newValue == "Color" ? GraphValueType.Color : GraphValueType.Float;
                parameter.DefaultValue = DefaultValue(parameter.Type);
            }));
            editor.Add(type);
            var bindings = new List<string> { "Constant", "Material", "AnimatedMaterial" };
            var binding = new PopupField<string>("Binding", bindings, Math.Max(0, bindings.IndexOf(parameter.Binding.ToString())));
            binding.RegisterValueChangedCallback(e => Edit("Change parameter binding", () => parameter.Binding = (GraphBindingKind)Enum.Parse(typeof(GraphBindingKind), e.newValue)));
            editor.Add(binding);
            var exposed = new Toggle("Exposed") { value = parameter.Exposed };
            exposed.RegisterValueChangedCallback(e => Edit("Change parameter exposure", () => parameter.Exposed = e.newValue));
            editor.Add(exposed);
            var group = new TextField("Material group") { value = MaterialGroup(parameter.Id), isDelayed = true,
                tooltip = "Optional inspector group. Stored by stable parameter ID in graph adapter metadata." };
            group.RegisterValueChangedCallback(e => EditMaterialGroup(parameter.Id, e.newValue));
            editor.Add(group);
            var symbol = new TextField("Shader reference") { value = GeneratedSymbol(parameter.Id), isReadOnly = true,
                tooltip = "Stable across display-name changes. Material/AnimatedMaterial bindings expose this shader property." };
            editor.Add(symbol);
            editor.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = GeneratedSymbol(parameter.Id)) { text = "Copy shader reference" });
            if (parameter.Type == GraphValueType.Color)
            {
                var field = new ColorField("Default") { value = ReadColor(parameter.DefaultValue) };
                field.RegisterValueChangedCallback(e => EditValue("Change parameter default", () => parameter.DefaultValue = ColorValue(e.newValue)));
                editor.Add(field);
            }
            else
            {
                var field = new FloatField("Default") { value = ReadFloat(parameter.DefaultValue), isDelayed = true };
                field.RegisterValueChangedCallback(e =>
                {
                    if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue))
                    { field.SetValueWithoutNotify(ReadFloat(parameter.DefaultValue)); SetStatus("Enter a finite parameter default."); return; }
                    EditValue("Change parameter default", () => parameter.DefaultValue = new JValue(e.newValue));
                });
                editor.Add(field);
            }
            var references = graph.Nodes.Count(n => n != null && n.Operation == "core.parameter" && (string)n.Properties["parameterId"] == parameter.Id);
            var delete = new Button(() => DeleteParameter(parameter)) { text = references == 0 ? "Delete parameter" : "Delete parameter (used by " + references + " node" + (references == 1 ? "" : "s") + ")" };
            editor.Add(delete);
            parent.Add(editor);
        }

        string NodeTitle(GraphNode node)
        {
            if (node != null && node.Operation == "core.parameter")
            {
                var id = node.Properties == null ? null : (string)node.Properties["parameterId"];
                var parameter = graph?.Parameters?.FirstOrDefault(p => p != null && p.Id == id);
                return parameter == null ? "Parameter (missing)" : "Parameter · " + parameter.Name;
            }
            var resourceId = (string)node?.Properties?["resourceId"];
            if (!string.IsNullOrEmpty(resourceId)) return TextureSlotLabels.DisplayName(graph, resourceId);
            return Title(node?.Operation);
        }

        static string GeneratedSymbol(string id) => string.IsNullOrEmpty(id) ? "—" : "_NXSG_P_" + id.Replace('-', '_');

        string MaterialGroup(string parameterId)
        {
            var groups = graph?.Adapter?["materialGroups"] as JObject;
            return groups == null ? string.Empty : (string)groups[parameterId] ?? string.Empty;
        }

        void EditMaterialGroup(string parameterId, string value)
        {
            var group = (value ?? string.Empty).Trim();
            Edit("Change material group", () =>
            {
                if (graph.Adapter == null) graph.Adapter = new JObject();
                var groups = graph.Adapter["materialGroups"] as JObject;
                if (groups == null) graph.Adapter["materialGroups"] = groups = new JObject();
                if (group.Length == 0) groups.Remove(parameterId); else groups[parameterId] = group;
            });
        }

        void AddParameter(GraphValueType type)
        {
            if (graph.Parameters == null) graph.Parameters = new List<GraphParameter>();
            var baseName = type == GraphValueType.Color ? "Color" : "Value";
            var name = baseName; var suffix = 2;
            while (graph.Parameters.Any(p => p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) name = baseName + suffix++;
            var parameter = new GraphParameter { Id = Guid.NewGuid().ToString("N"), Name = name, Type = type, Binding = GraphBindingKind.Material, DefaultValue = DefaultValue(type), Exposed = true };
            Edit("Add parameter", () => graph.Parameters.Add(parameter));
            selectedParameterId = parameter.Id;
            RebuildInspector();
        }

        void AddParameterNode(string parameterId)
        {
            if (graph.Parameters == null) return;
            var parameter = graph.Parameters.FirstOrDefault(p => p != null && p.Id == parameterId);
            if (parameter == null) return;
            Edit("Add parameter node", () =>
            {
                var node = new GraphNode { Id = Guid.NewGuid().ToString("N"), Operation = "core.parameter", Properties = new JObject { ["parameterId"] = parameterId } };
                graph.Nodes.Add(node); SetPosition(node.Id, new Vector2(82 + graph.Nodes.Count * 12, 112 + graph.Nodes.Count * 12));
                selection.Clear(); selection.Add(node.Id); selected = node.Id;
            });
        }

        void DeleteParameter(GraphParameter parameter)
        {
            if (graph.Nodes.Any(n => n != null && n.Operation == "core.parameter" && (string)n.Properties["parameterId"] == parameter.Id))
            { SetStatus("Delete or reassign its parameter nodes first."); return; }
            Edit("Delete parameter", () => graph.Parameters.Remove(parameter));
            selectedParameterId = null;
        }

        static JToken DefaultValue(GraphValueType type) => type == GraphValueType.Color ? ColorValue(Color.white) : new JValue(.5f);
        static JToken ColorValue(Color value) => new JArray(value.r, value.g, value.b, value.a);
        static float ReadFloat(JToken value) => value != null && (value.Type == JTokenType.Float || value.Type == JTokenType.Integer) ? (float)value : .5f;
        static Color ReadColor(JToken value)
        {
            var array = value as JArray;
            return array != null && array.Count == 4 ? new Color((float)array[0], (float)array[1], (float)array[2], (float)array[3]) : Color.white;
        }
    }
}
