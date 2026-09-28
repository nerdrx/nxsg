using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;

internal static class FaceControlChecks
{
    public static void Run()
    {
        var face = FaceNodes.Create(FaceNodes.FrontFace);
        Require(face != null && face.Properties["flipBackfaceNormal"].Value<int>() == 1, "Front Face default must orient back normals");
        Require(FeatureNodes.IsKnown(FaceNodes.FrontFace), "Feature registry must include Front Face");
        Require(FaceNodes.PortType(FaceNodes.FrontFace, "isFront", true) == "float", "Front Face signal type changed");
        Require(FaceNodes.PortType(FaceNodes.FrontFace, "normalWorld", true) == "vector3", "Front Face normal type changed");
        Require(FaceNodes.PortType(FaceNodes.FrontFace, "normalWorld", false) == null, "Front Face has no inputs");
        Require(FaceNodes.IsFiniteInRange(JToken.FromObject(.5), 0, 1), "Finite in-range value rejected");
        Require(!FaceNodes.IsFiniteInRange(JToken.FromObject(double.NaN), 0, 1), "NaN must be rejected");
        Require(!FaceNodes.IsFiniteInRange(JToken.FromObject(double.PositiveInfinity), 0, 1), "Infinity must be rejected");
        Require(!FaceNodes.IsFiniteInRange(JToken.FromObject(1.1), 0, 1), "Out-of-range value must be rejected");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Face controls: " + message);
    }
}
