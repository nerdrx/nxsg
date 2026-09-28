using System;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        // Root emitter integration: call from Eval's operation switch for core.frontFace.
        // The fragment setup must populate NX_FrontFace from the VFACE semantic
        // whenever this node, Output normal flipping, or two-pass transparency is live.
        string FrontFaceExpression(GraphNode node, string port)
        {
            if (port == "isFront") return "saturate(NX_FrontFace)";
            if (port == "normalWorld")
            {
                var nodeFlip = FaceFlip(node) == 1;
                var globalFlip = renderState != null && renderState.FlipBackfaceNormals;
                var flip = globalFlip ? (nodeFlip ? "1" : "(NX_FrontFace>0?1:-1)") : (nodeFlip ? "(NX_FrontFace>0?1:-1)" : "1");
                return "normalize(input.n)*" + flip;
            }
            throw new InvalidOperationException("Front Face output must be Is Front or Normal World.");
        }

        static int FaceFlip(GraphNode node)
        {
            var token = node?.Properties?["flipBackfaceNormal"];
            return token == null ? 1 : Math.Max(0, Math.Min(1, (int?)token ?? 1));
        }
    }
}
