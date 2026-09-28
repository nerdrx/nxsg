using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Portable definitions for richer AudioLink data inputs.</summary>
    public sealed class AudioDataNodeDefinition
    {
        public readonly string Title, Category, Description;
        public readonly Dictionary<string, string> Inputs, Outputs;
        public readonly JObject Defaults;

        internal AudioDataNodeDefinition(string title, string description, string inputs, string outputs, JObject defaults)
        {
            Title = title;
            Category = "Inputs";
            Description = description;
            Inputs = ParsePorts(inputs);
            Outputs = ParsePorts(outputs);
            Defaults = defaults;
        }

        static Dictionary<string, string> ParsePorts(string ports)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(ports)) return result;
            foreach (var item in ports.Split(','))
            {
                var parts = item.Split(':');
                result.Add(parts[0], parts[1]);
            }
            return result;
        }
    }

    /// <summary>AudioLink DFT, chronotensity, and theme-color node catalog.</summary>
    public static class AudioDataNodes
    {
        static readonly Dictionary<string, AudioDataNodeDefinition> Items = new Dictionary<string, AudioDataNodeDefinition>(StringComparer.Ordinal)
        {
            ["core.audioVisualizer"] = D("Audio Spectrum Bars", "Draw an AudioLink spectrum as horizontal bars or a radial display. Frequencies are spaced logarithmically. Connect Value to emission, opacity or a Color Ramp.", "uv:vector2,gain:float", "value:float", new JObject{{"bars",32},{"radial",0},{"minFrequency",40},{"maxFrequency",14000},{"gain",1},{"gap",.12}}),
            ["core.audioSpectrum"] = D("Audio Spectrum", "Sample AudioLink's DFT magnitude at a frequency in hertz. Valid AudioLink DFT range is 13.75–14080 Hz. Channel 0 is raw magnitude, 1 is EQ magnitude, and 2 is the ColorChord-filtered magnitude.", "frequency:float", "value:float", new JObject {{"frequency",440},{"channel",0},{"gain",1},{"fallback",0}}),
            ["core.audioSpectrumBin"] = D("Audio Spectrum Bin", "Sample AudioLink's DFT at one of its 240 chromatic bins. Bin 0 starts at 13.75 Hz; 24 bins span one octave. Fractional bins interpolate between adjacent samples.", "bin:float", "value:float", new JObject {{"bin",48},{"channel",0},{"gain",1},{"fallback",0}}),
            ["core.audioChronotensity"] = D("AudioLink Chronotensity", "Audio-reactive accumulated time. Index selects one of AudioLink's eight chronotensity modes; band selects bass, low-mid, high-mid, or treble. Normalized mode wraps output to 0–1.", "speed:float", "value:float", new JObject {{"index",0},{"band",0},{"speed",1},{"normalized",0},{"fallback",0}}),
            ["core.audioThemeColor"] = D("AudioLink Theme Color", "Read one of AudioLink's four world-selected theme colors. The fallback color is used when a compatible AudioLink texture is unavailable.", "", "color:color", new JObject {{"index",0},{"fallback",new JArray(1,1,1,1)}})
        };

        static AudioDataNodeDefinition D(string title, string description, string inputs, string outputs, JObject defaults)
        {
            return new AudioDataNodeDefinition(title, description, inputs, outputs, defaults);
        }

        public static IEnumerable<string> All { get { return Items.Keys; } }
        public static bool IsKnown(string operation) { return operation != null && Items.ContainsKey(operation); }
        public static bool TryGet(string operation, out AudioDataNodeDefinition definition) { return Items.TryGetValue(operation, out definition); }
        public static string[] Ports(string operation, bool output)
        {
            if (!Items.TryGetValue(operation ?? string.Empty, out var definition)) return new string[0];
            return new List<string>(output ? definition.Outputs.Keys : definition.Inputs.Keys).ToArray();
        }
        public static string PortType(string operation, string port, bool output)
        {
            if (!Items.TryGetValue(operation ?? string.Empty, out var definition)) return null;
            var ports = output ? definition.Outputs : definition.Inputs;
            return ports.TryGetValue(port ?? string.Empty, out var type) ? type : null;
        }
        public static GraphNode Create(string operation)
        {
            if (!Items.TryGetValue(operation ?? string.Empty, out var definition)) return null;
            return new GraphNode { Id = Guid.NewGuid().ToString("N"), Operation = operation, Properties = (JObject)definition.Defaults.DeepClone() };
        }
        public static IEnumerable<string> Numeric(string operation)
        {
            if (!Items.TryGetValue(operation ?? string.Empty, out var definition)) yield break;
            foreach (var property in definition.Defaults.Properties())
                if (property.Value.Type == JTokenType.Integer || property.Value.Type == JTokenType.Float)
                    yield return property.Name;
        }
    }
}
