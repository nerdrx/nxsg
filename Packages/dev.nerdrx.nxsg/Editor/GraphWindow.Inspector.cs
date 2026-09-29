using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        [SerializeField] List<string> openInspectorSections = new List<string>();
        [SerializeField] List<string> closedInspectorSections = new List<string>();
        readonly Dictionary<string, JObject> inspectorDefaults = new Dictionary<string, JObject>();
        readonly List<InspectorProperty> inspectorProperties = new List<InspectorProperty>();
        readonly List<Action> inspectorSectionUpdates = new List<Action>();

        sealed class InspectorProperty
        {
            public VisualElement element;
            public Func<bool> changed;
            public Action<bool> update;
        }

        void InspectorSection(GraphNode node, string key, string title, Action controls, bool expanded = false)
        {
            var stateKey = graph.GraphId + ":" + node.Id + ":" + key;
            var section = new Foldout { name = "inspector-section-" + key, text = title,
                value = openInspectorSections.Contains(stateKey) || expanded && !closedInspectorSections.Contains(stateKey) };
            section.AddToClassList("nxsg-inspector-section");
            section.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != section) return;
                openInspectorSections.Remove(stateKey); closedInspectorSections.Remove(stateKey);
                (evt.newValue ? openInspectorSections : closedInspectorSections).Add(stateKey);
            });
            inspector.Add(section);
            var previous = inspector;
            try { inspector = section; controls(); }
            finally { inspector = previous; }
            inspectorSectionUpdates.Add(() =>
            {
                var changed = inspectorProperties.Any(binding => section.Contains(binding.element) && binding.changed());
                section.text = title + (changed ? " *" : "");
                section.tooltip = changed ? "Contains settings changed from the node's defaults." : "Expand to show " + title.ToLowerInvariant() + ".";
            });
        }

        void TrackProperty<T>(GraphNode node, string property, BaseField<T> field, JToken fallback = null)
        {
            var label = field.label;
            TrackProperty(node, property, field, changed => field.label = label + (changed ? " *" : ""), fallback);
        }

        void TrackProperty(GraphNode node, string property, VisualElement element, Action<bool> update, JToken fallback = null)
        {
            if (!inspectorDefaults.TryGetValue(node.Operation, out var defaults))
                inspectorDefaults[node.Operation] = defaults = NodeCatalog.Create(node.Operation).Properties;
            var baseline = defaults[property] ?? fallback;
            if (string.IsNullOrEmpty(element.name)) element.name = "node-property-" + property;
            var binding = new InspectorProperty { element = element,
                changed = () => !SameInspectorValue(node.Properties[property] ?? fallback ?? baseline, baseline), update = update };
            inspectorProperties.Add(binding);
            binding.update(binding.changed());
        }

        static bool SameInspectorValue(JToken a, JToken b)
        {
            if (a == null || b == null) return a == b;
            bool Numeric(JToken token) => token.Type == JTokenType.Float || token.Type == JTokenType.Integer;
            // Fields edit floats; JSON may store the same value as an integer or double.
            if (Numeric(a) && Numeric(b)) return (float)a == (float)b;
            if (a is JArray aa && b is JArray ba)
                return aa.Count == ba.Count && aa.Zip(ba, SameInspectorValue).All(equal => equal);
            if (a is JObject ao && b is JObject bo)
                return ao.Count == bo.Count && ao.Properties().All(p => SameInspectorValue(p.Value, bo[p.Name]));
            return JToken.DeepEquals(a, b);
        }

        void RefreshInspectorMarkers()
        {
            foreach (var binding in inspectorProperties) binding.update(binding.changed());
            foreach (var update in inspectorSectionUpdates) update();
        }

        static void StackVectorField<T>(BaseField<T> field)
        {
            field.AddToClassList("nxsg-vector-field");
            field.style.flexDirection = FlexDirection.Column;
            field.style.alignItems = Align.Stretch;
            field.labelElement.style.width = Length.Percent(100);
            field.labelElement.style.marginBottom = 3;
            foreach (var component in field.Query<FloatField>().ToList())
            {
                component.style.minWidth = 0;
                component.style.flexGrow = 1;
                component.labelElement.style.width = 14;
                component.labelElement.style.minWidth = 14;
                component.labelElement.style.whiteSpace = WhiteSpace.NoWrap;
            }
        }

        void AddSurfaceBasics(GraphNode node, bool pbr = false)
        {
            if (pbr) InspectorSection(node, "surface", "Surface", () =>
            {
                AddBoundedNumber(node, "metallic", "Metallic", 0, 1, 0, "metallic");
                AddBoundedNumber(node, "roughness", "Roughness", 0, 1, .5f, "roughness");
                AddBoundedNumber(node, "specularAa", "Specular anti-aliasing", 0, 1, 0, "specularAa");
            }, true);
            InspectorSection(node, "transparency", "Transparency", () =>
            {
                AddAlbedoAlphaToggle(node);
                AddBoundedNumber(node, "opacity", "Opacity", 0, 1, 1, "opacity");
                AddNumber(node, "cutoff", "Alpha cutoff", .001f, help: "Pixels below this opacity are discarded. Output controls the rendering mode.");
            });
            InspectorSection(node, "displacement", "Displacement", () =>
                AddNumber(node, "displacement", "Distance (object units)", 0, "displacement"));
        }

        void AddSurfaceParticleControls(GraphNode node)
        {
            InspectorSection(node, "emission", "Emission", () =>
            {
                AddBoundedNumber(node, "density", "Triangle density", 0, 1, .1f, "density", "Fraction of source triangles used as emitters. 1 selects all triangles.");
                AddBoundedNumber(node, "emissionRate", "Rate per triangle / second", 0, 4, 1 / Mathf.Max(.001f, (float?)node.Properties["lifetime"] ?? 2), "emissionRate");
                AddBoundedNumber(node, "lifetime", "Lifetime (seconds)", .05f, 30, 2, "lifetime");
                AddBoundedNumber(node, "mask", "Emitter mask", 0, 1, 1, "mask", "Samples the emitter mesh UVs. 0 blocks emission; 1 allows it.");
                AddIndexedChoice(node, "perArea", "Emission distribution", new[] { "Per triangle", "Per surface area" });
                AddNumber(node, "referenceArea", "Reference area (m²)", .01f, help: "Rate applies to this much mesh area; Density remains a selection fraction.");
            }, true);
            InspectorSection(node, "appearance", "Appearance", () =>
            {
                var sourceUv = new Toggle("Color from mesh UVs") { value = (int?)node.Properties["sourceUV"] == 1,
                    tooltip = "On: Albedo and Emission sample the spawn point on mesh UV0. Off: each particle uses sprite UVs. Opacity always uses sprite UVs." };
                sourceUv.RegisterValueChangedCallback(evt => Edit("Change particle color UVs", () => node.Properties["sourceUV"] = evt.newValue ? 1 : 0));
                TrackProperty(node, "sourceUV", sourceUv, 0); inspector.Add(sourceUv);
                AddIndexedChoice(node, "blendMode", "Blending", new[] { "Alpha", "Additive" }, 1);
                AddBoundedNumber(node, "size", "Size", .0001f, 1, .03f, "size");
                AddBoundedNumber(node, "edgeSharpness", "Edge sharpness", 0, 1, 0, "edgeSharpness", "0 = soft puff, 1 = crisp circle. Opacity and lifetime fading still apply.");
                AddIndexedChoice(node, "shape", "Particle shape", new[] { "Soft circle", "Texture / rectangle", "Square", "Cross" });
                AddIntegerField(node, "atlasColumns", "Atlas columns", 1, 16, 1);
                AddIntegerField(node, "atlasRows", "Atlas rows", 1, 16, 1);
                AddNumber(node, "rotation", "Rotation (degrees)", 0);
                AddNumber(node, "randomRotation", "Random rotation (degrees)", 0);
                AddBoundedNumber(node, "opacity", "Opacity", 0, 1, 1, "opacity");
            }, true);
            InspectorSection(node, "lifetime-curves", "Lifetime curves", () => AddParticleCurves(node));
            InspectorSection(node, "motion", "Motion", () =>
            {
                AddNumber(node, "speed", "Outward speed", .2f, "speed");
                AddNumber(node, "gravity", "Gravity (local Y)", 0, "gravity");
                AddBoundedNumber(node, "spread", "Velocity randomness", 0, 5, .05f, "spread");
            });
            InspectorSection(node, "particle-help", "Setup and limits", () =>
            {
                FeatureNote("Connect your mesh surface to Base, then this node to Output. Particles follow the current mesh pose.");
                FeatureNote("Rate and Lifetime drive adaptive tessellation up to level 64. High values cost more. Changing either retimes procedural particles; there is no stored simulation.");
                FeatureNote("Expand renderer bounds if particles disappear near screen edges.");
            });
        }
    }
}
