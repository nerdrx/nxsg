using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class PerformanceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var graph = new ShaderGraph { GraphId = "performance-reachable" };
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.pbrSurface" });
        graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.triplanarTexture" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        graph.Nodes.Add(new GraphNode { Id = "unused", Operation = "core.fur", Properties = new JObject { ["layers"] = 32 } });
        Connect(graph, "albedo", "texture", "color", "surface", "albedo");
        Connect(graph, "out", "surface", "surface", "output", "surface");
        var report = GraphPerformance.Analyze(graph);
        check(report.ReachableNodes.Count == 3 && report.TextureSampleSites == 3, "only output-reachable texture sample sites counted");
        check(report.StaticPassBudget == 3, "PBR base/add/shadow pass declarations counted");
        var pbrBuild = ShaderEmitter.Emit(CreatePbrGraph());
        check(pbrBuild.Succeeded && Regex.Matches(pbrBuild.ShaderSource, @"\bPass\s*\{").Count == report.StaticPassBudget,
            "PBR static pass count matches emitted shader declarations");

        var volume = new ShaderGraph { GraphId = "performance-volume" };
        volume.Nodes.Add(new GraphNode { Id = "volume", Operation = "core.volumeSurface", Properties = new JObject { ["steps"] = 96 } });
        volume.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(volume, "volume-out", "volume", "surface", "out", "surface");
        var volumeReport = GraphPerformance.Analyze(volume);
        check(volumeReport.StaticPassBudget == 1 && volumeReport.LoopBudgets.Any(s => s.Contains("96 iterations")), "volume steps and specialized pass budget reported");

        var parallax = new ShaderGraph { GraphId = "performance-pom" };
        parallax.Nodes.Add(new GraphNode { Id = "pom", Operation = "core.parallaxOcclusion", Properties = new JObject { ["steps"] = 48 } });
        parallax.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" });
        parallax.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(parallax, "pom-color", "pom", "color", "surface", "albedo");
        Connect(parallax, "surface-out", "surface", "surface", "out", "surface");
        var pomReport = GraphPerformance.Analyze(parallax);
        check(pomReport.TextureSampleSites == 1 && pomReport.LoopBudgets.Any(s => s.Contains("up to 48")), "POM site count separated from its loop evaluation budget");
        check(pomReport.StaticPassBudget == 2, "unlit surface still counts its shadow caster");

        var particles = new ShaderGraph { GraphId = "performance-particles" };
        particles.Nodes.Add(new GraphNode { Id = "pbr", Operation = "core.pbrSurface" });
        particles.Nodes.Add(new GraphNode { Id = "particles", Operation = "core.surfaceParticles" });
        particles.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(particles, "base", "pbr", "surface", "particles", "base");
        Connect(particles, "particles-out", "particles", "surface", "out", "surface");
        var particleReport = GraphPerformance.Analyze(particles);
        check(particleReport.StaticPassBudget == 4, "surface particles include base, ForwardAdd, shadow and particle passes");
        var particleBuild = ShaderEmitter.Emit(particles);
        check(particleBuild.Succeeded && Regex.Matches(particleBuild.ShaderSource, @"\bPass\s*\{").Count == particleReport.StaticPassBudget,
            "Surface Particles static pass count matches emitted shader declarations");

        var shell = new ShaderGraph { GraphId = "performance-shared-shell" };
        shell.Nodes.Add(new GraphNode { Id = "pbr", Operation = "core.pbrSurface" });
        shell.Nodes.Add(new GraphNode { Id = "shell", Operation = "core.shell" });
        shell.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(shell, "shared-base", "pbr", "surface", "shell", "base");
        Connect(shell, "shared-layer", "pbr", "surface", "shell", "layer");
        Connect(shell, "shell-out", "shell", "surface", "out", "surface");
        var shellReport = GraphPerformance.Analyze(shell);
        var shellBuild = ShaderEmitter.Emit(shell);
        check(shellBuild.Succeeded && Regex.Matches(shellBuild.ShaderSource, @"\bPass\s*\{").Count == shellReport.StaticPassBudget && shellReport.StaticPassBudget == 4,
            "shared nested shell leaves count once per emitted surface plus base add and shadow passes");

        particles.Nodes.Single(n=>n.Id=="particles").Properties["emissionRate"] = 1e20;
        check(GraphPerformance.Analyze(particles).HotSpots.Any(item=>item.Estimate.Contains("factor 64")), "Very large particle rates retain the maximum cost estimate without integer overflow");

        var layered = new ShaderGraph { GraphId = "performance-layered" };
        layered.Nodes.Add(new GraphNode { Id = "layered", Operation = "core.layeredPbrSurface", Properties = new JObject { ["coat"] = 0, ["sheen"] = 1 } });
        layered.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(layered, "layered-out", "layered", "surface", "out", "surface");
        var layeredReport = GraphPerformance.Analyze(layered);
        check(layeredReport.HotSpots.Any(i => i.NodeId == "layered" && i.Estimate.Contains("Sheen")) && !layeredReport.HotSpots.Any(i => i.Estimate.Contains("Clear coat")), "layered PBR inactive lobes are excluded");
    }

    public static void CheckExamples(Action<bool,string> check, string fixtures)
    {
        var root = Path.GetFullPath(Path.Combine(fixtures,"../../Packages/dev.nerdrx.nxsg/Samples~"));
        foreach(var path in Directory.GetFiles(root,"*.nxsg"))
        {
            var graph=GraphJson.Parse(File.ReadAllText(path));
            var result=ShaderEmitter.Emit(graph, new EmitterOptions { LtcgiAvailable = true });
            if(!result.Succeeded) { check(false,"Example fails emission: "+Path.GetFileName(path)); continue; }
            var report=GraphPerformance.Analyze(graph);
            check(Regex.Matches(result.ShaderSource,@"\bPass\s*\{").Count==report.StaticPassBudget,
                "Performance pass estimate matches example: "+Path.GetFileName(path));
        }
        var inactive=NodeCatalog.Create("core.layeredPbrSurface");inactive.Id="inactive";
        var texture=NodeCatalog.Create("core.texture2D");texture.Id="texture";
        var output=NodeCatalog.Create("core.output");output.Id="out";
        var graph2=new ShaderGraph {GraphId="inactive-estimate"};graph2.Nodes.Add(inactive);graph2.Nodes.Add(texture);graph2.Nodes.Add(output);
        Connect(graph2,"surface","inactive","surface","out","surface");Connect(graph2,"unused","texture","color","inactive","sheenColor");
        check(GraphPerformance.Analyze(graph2).TextureSampleSites==0,"Disabled sheen does not report unused texture cost");
    }

    static void Connect(ShaderGraph graph, string id, string fromNode, string fromPort, string toNode, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = fromNode, PortId = fromPort }, To = new GraphPortRef { NodeId = toNode, PortId = toPort } });
    }

    static ShaderGraph CreatePbrGraph()
    {
        var graph = new ShaderGraph { GraphId = "performance-pbr-build" };
        graph.Nodes.Add(new GraphNode { Id = "pbr", Operation = "core.pbrSurface" });
        graph.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(graph, "pbr-out", "pbr", "surface", "out", "surface");
        return graph;
    }
}
