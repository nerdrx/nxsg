using System;
using NXSG.Backend;
using NXSG.Core;

public static class UtilityChecks
{
    public static void Run(Action<bool, string> assert)
    {
        const uint period = 1000;
        assert(NetworkClockMath.Phase(1250, period, 0) == .25f, "network clock phase uses integer milliseconds");
        assert(NetworkClockMath.Phase(uint.MaxValue - 249, period, 500) == .25f, "network clock phase handles uint wrap and signed offset");
        assert(NetworkClockMath.Phase(999, 0, 0) == 0, "zero millisecond period is safely clamped");
        assert(NetworkClockMath.Cycle(2501, period, 0) == 2, "network clock cycle index is integral");
        assert(NumericTextMath.Format(-12.34, 4, 2) == "-  12.34", "numeric SDF format handles sign and fractional digits");
        assert(NumericTextMath.Format(12.345, 3, 2) == "12.35", "numeric SDF format rounds at requested precision");
        assert(NumericTextMath.Format(0.04, 3, 2) == "0.04", "numeric SDF format keeps fractional zeroes");
        assert(UtilityShader.ClockExpression("phase", "1", "period", "offset") == "NXSG_UtilityClockPhase(1,period,offset)", "clock expression preserves period/offset ports");
        var msdf = UtilityShader.MsdfExpression("uv", "_NXSG_P_a", "_NXSG_P_a_TexelSize", "tint", "outline", "width", "softness", "range");
        assert(msdf.Contains("_NXSG_P_a") && msdf.Contains("_NXSG_P_a_TexelSize") && !msdf.Contains("_NXSG_MSDFTex"), "MSDF expression binds the node's own texture resource");
        var numeric = UtilityShader.NumberColorExpression("uv", "value", "_NXSG_P_b", "tint", "scale", "spacing", 4, 2);
        assert(numeric.Contains("_NXSG_P_b") && numeric.Contains(",4,2)"), "numeric text expression uses the selected atlas and format");
    }
}
