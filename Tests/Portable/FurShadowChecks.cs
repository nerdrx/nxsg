using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;

public static class FurShadowChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var defaults = NodeCatalog.Create("core.fur");
        assert(defaults != null && (int)defaults.Properties["receiveShadows"] == 1 &&
            (int)defaults.Properties["selfShadowQuality"] == 0 &&
            (double)defaults.Properties["selfShadowStrength"] == 1 &&
            Math.Abs((double)defaults.Properties["selfShadowBias"] - .03) < .000001,
            "fur shadow defaults");

        var valid = new ShaderGraph { GraphId = "fur-shadows-valid" };
        valid.Nodes.Add(defaults);
        defaults.Properties["selfShadowQuality"] = 3;
        defaults.Properties["selfShadowStrength"] = 4;
        defaults.Properties["selfShadowBias"] = .25;
        assert(GraphValidator.Validate(valid).IsValid, "fur shadow valid bounds");

        CheckInvalid(assert, "receiveShadows", new JValue(2));
        CheckInvalid(assert, "receiveShadows", new JValue("on"));
        CheckInvalid(assert, "selfShadowQuality", new JValue(4));
        CheckInvalid(assert, "selfShadowQuality", new JValue("high"));
        CheckInvalid(assert, "selfShadowStrength", new JValue(4.1));
        CheckInvalid(assert, "selfShadowBias", new JValue(.2501));
    }

    static void CheckInvalid(Action<bool, string> assert, string property, JToken value)
    {
        var graph = new ShaderGraph { GraphId = "fur-shadows-invalid-" + property };
        var node = NodeCatalog.Create("core.fur");
        node.Properties[property] = value;
        graph.Nodes.Add(node);
        assert(!GraphValidator.Validate(graph).IsValid, "fur shadow rejects " + property + "=" + value);
    }
}
