using NXSG.Core;

namespace NXSG.Editor
{
    public partial class GraphWindow
    {
        // Root editor integration: call for core.frontFace and from AddOutputControls.
        void AddFrontFaceControls(GraphNode node)
        {
            AddInspectorSection("FACES");
            AddIndexedChoice(node, "flipBackfaceNormal", "Flip back normal", new[] { "Off", "On" }, 1);
            FeatureNote("Is Front is 1 for front faces and 0 for back faces. Normal World is a world-space geometric normal; surface normal-map inputs use tangent space.");
        }

        void AddFaceOutputControls(GraphNode node)
        {
            AddInspectorSection("FACE RENDERING");
            AddIndexedChoice(node, "flipBackfaceNormals", "Flip back-face normals", new[] { "Off", "On" });
            AddIndexedChoice(node, "alphaToCoverage", "Alpha to coverage", new[] { "Off", "On" });
            AddBoundedNumber(node, "alphaEdgeSharpness", "Alpha edge sharpness", 0, 1, 0);
            FeatureNote("Alpha to coverage maps fragment alpha to MSAA sample coverage. It needs multisample anti-aliasing. Edge sharpness hardens the alpha transition.");
            AddIndexedChoice(node, "twoPassTransparency", "Two-sided transparency", new[] { "Off", "Back then front" });
            if ((int?)node.Properties["twoPassTransparency"] != 1) return;
            FurSection("Back-face pass", () => AddTransparencyPassControls(node, "back"));
            FurSection("Front-face pass", () => AddTransparencyPassControls(node, "front"));
            FeatureNote("Each pass draws one face orientation. Pass order is back first, then front; transparent object sorting still applies.");
        }

        void AddTransparencyPassControls(GraphNode node, string face)
        {
            AddIndexedChoice(node, face + "PassBlend", "Blend", new[] { "Follow Output", "Alpha", "Additive", "Premultiplied" });
            AddIndexedChoice(node, face + "PassZWrite", "Depth write", new[] { "Automatic", "On", "Off" });
        }
    }
}
