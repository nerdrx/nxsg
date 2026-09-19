using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NXSG.Core;

namespace NXSG.Editor
{
    // Local project cache only. Snapshots are data, never executable editor state.
    internal static class GraphRecovery
    {
        internal static string Folder = Path.GetFullPath("Library/NXSG/Recovery");
        internal static string Write(ShaderGraph graph, string name, bool checkpoint)
        {
            var text = GraphJson.Serialize(graph, true);
            if (Encoding.UTF8.GetByteCount(text) > 4 * 1024 * 1024) throw new IOException("Recovery graph exceeds 4 MiB.");
            Directory.CreateDirectory(Folder);
            string key;
            using(var hash=SHA256.Create()) key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(graph.GraphId ?? "new"))).Replace("-", "").Substring(0,16);
            name = new string((name ?? "Untitled").Where(c=>char.IsLetterOrDigit(c)||c=='-'||c==' ').Take(30).ToArray());
            var path=Path.Combine(Folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"_"+key+"_"+(checkpoint?"Checkpoint":"Auto")+"_"+name+".nxsg");
            var temp=path+".tmp";
            try { File.WriteAllText(temp,text); File.Move(temp,path); }
            finally { if(File.Exists(temp))File.Delete(temp); }
            // Keep manual checkpoints separate so routine autosaves cannot evict them.
            foreach(var stale in Files().Where(p=>Path.GetFileName(p).Contains(checkpoint?"_Checkpoint_":"_Auto_")).Skip(30)) File.Delete(stale);
            return path;
        }
        internal static string[] Files() => Directory.Exists(Folder) ? Directory.GetFiles(Folder,"*.nxsg").OrderByDescending(Path.GetFileName,StringComparer.Ordinal).ToArray() : new string[0];
        internal static ShaderGraph Read(string path)
        {
            if(Path.GetDirectoryName(Path.GetFullPath(path))!=Folder)throw new IOException("Invalid recovery location.");
            if(new FileInfo(path).Length>4*1024*1024)throw new IOException("Recovery graph exceeds 4 MiB.");
            return GraphJson.Parse(File.ReadAllText(path));
        }
    }
}
