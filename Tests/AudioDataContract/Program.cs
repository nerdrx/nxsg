using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class Program
{
    static int Main()
    {
        Check(AudioDataNodes.All.Count() == 5, "AudioLink data and spectrum visualization node definitions");
        Check(AudioDataNodes.PortType("core.audioSpectrum", "frequency", false) == "float", "spectrum frequency input type");
        Check(AudioDataNodes.PortType("core.audioSpectrumBin", "value", true) == "float", "spectrum bin output type");
        Check(AudioDataNodes.PortType("core.audioThemeColor", "color", true) == "color", "theme color output type");

        var first = AudioDataNodes.Create("core.audioThemeColor");
        var second = AudioDataNodes.Create("core.audioThemeColor");
        first.Properties["fallback"][0] = .25;
        Check((double)second.Properties["fallback"][0] == 1, "node defaults are deep-cloned");
        Check((int)AudioDataNodes.Create("core.audioChronotensity").Properties["normalized"] == 0, "chronotensity defaults to continuous time");

        var frequency = AudioDataShader.SpectrumFrequency("440.0", "0", "1.0", "0.0");
        Check(frequency == "NXSG_AudioSpectrumFrequency(440.0,0,1.0,0.0)", "frequency call builder");
        Check(AudioDataShader.SpectrumBin("48.0", "2", "1.0", "0.0").StartsWith("NXSG_AudioSpectrumBin(", StringComparison.Ordinal), "bin call builder");
        Check(AudioDataShader.Chronotensity("4", "0", "1.0", "1", "0.0").Contains(",1,0.0)"), "chronotensity call builder");
        Check(AudioDataShader.ThemeColor("0", "float4(1,1,1,1)") == "NXSG_AudioThemeColor(0,float4(1,1,1,1))", "theme call builder");

        Check(AudioDataShader.PreviewProperties.Contains("_NXSG_AudioDataPreview") && AudioDataShader.PreviewProperties.Contains("_NXSG_AudioThemePreview"), "preview properties");
        Check(AudioDataShader.Hlsl.Contains("int3(16 + clamp(index, 0, 7), 28 + clamp(band, 0, 3), 0)"), "chronotensity texture coordinates and bounds");
        Check(AudioDataShader.Hlsl.Contains("int3(clamp(index, 0, 3), 23, 0)"), "theme texture coordinates and bounds");
        Check(AudioDataShader.Hlsl.Contains("NXSG_AudioDataAvailable(128, 6)"), "spectrum provider fallback guard");
        Check(AudioDataShader.Hlsl.Contains("NXSG_AudioDataAvailable(24, 32)"), "chronotensity provider fallback guard");
        Check(AudioDataShader.Hlsl.Contains("NXSG_AudioDataAvailable(4, 24)"), "theme provider fallback guard");
        Check(AudioDataShader.Hlsl.Contains("channels.x + channels.y * 1024u + channels.z * 1048576u + channels.w * 1073741824u"), "AudioLink packed uint reconstruction");

        Console.WriteLine("AudioData contract checks passed.");
        return 0;
    }

    static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
