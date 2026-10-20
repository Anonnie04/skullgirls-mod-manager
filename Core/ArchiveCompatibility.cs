using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;

namespace SkullgirlsModManager;

public static class ArchiveCompatibility
{
    // Supported user-supplied compatibility component. Never redistributed by SGMM.
    public const string LoaderSha256 = "014bfb8c48fa7afbcd585a34d814011b96b289c742482a01376f56aafa1e11d3";
    public static readonly string[] ProtectedArchives = ["core.gfs", "characters-foits.gfs", "ui.gfs"];
    public static bool Required(string game, IEnumerable<string> archives) => archives.Any(a => ProtectedArchives.Contains(a, StringComparer.OrdinalIgnoreCase))
        && File.ReadAllBytes(Path.Combine(game,"SkullGirls.exe")).AsSpan().IndexOf(Encoding.ASCII.GetBytes("Package %s.gfs not verified.")) >= 0;
    public static bool Installed(string game)
    {
        string path = Path.Combine(game,"CoreMessaging.dll");
        if (!File.Exists(path)) return false;
        SafePaths.RejectLinks(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(LoaderSha256,StringComparison.OrdinalIgnoreCase);
    }
    public static string SavedLoader(string profile) => Path.Combine(profile, "compatibility", "CoreMessaging.dll");
    private static bool Verified(string path)
    {
        if (!File.Exists(path)) return false;
        SafePaths.RejectLinks(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(LoaderSha256, StringComparison.OrdinalIgnoreCase);
    }
    public static bool Saved(string profile) => Verified(SavedLoader(profile));
    public static bool RestoreSaved(string game, string profile)
    {
        if (Installed(game)) return true;
        if (!Saved(profile)) return false;
        Install(game, profile, SavedLoader(profile));
        return true;
    }
    private static void Remember(string profile, byte[] bytes)
    {
        string path = SavedLoader(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafePaths.RejectLinks(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) SafePaths.RejectLinks(path);
        string staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(staged, bytes); File.Move(staged, path, true); }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
    public static void Require(string game, IEnumerable<string> archives)
    {
        if (Required(game,archives) && !Installed(game)) throw new InvalidOperationException("These mods change archives verified by Skullgirls. Open Backups & tools → Set up mod support. This is a separate, one-time setup, not a mod ZIP import. After setup, the manager saves support locally and restores it automatically after a game reinstall.");
    }
    public static void Install(string game, string profile, string source)
    {
        if (Process.GetProcessesByName("SkullGirls").Length != 0) throw new InvalidOperationException("Close Skullgirls, including its error dialog, before installing compatibility.");
        using var guard = new FileStream(Path.Combine(profile,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(Path.Combine(profile,"transaction.json"))) throw new InvalidOperationException("Recover the interrupted installation first.");
        SafePaths.RejectLinks(game); SafePaths.RejectLinks(source);
        byte[] bytes;
        if (source.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(source);
            var entries=zip.Entries.Where(e=>e.FullName.Replace('\\','/').Split('/').Last().Equals("CoreMessaging.dll",StringComparison.OrdinalIgnoreCase)).ToList();
            if(entries.Count!=1 || entries[0].Length!=949248) throw new InvalidDataException("Select a ZIP containing exactly one supported CoreMessaging.dll.");
            using var input=entries[0].Open(); bytes=new byte[949248]; input.ReadExactly(bytes); if(input.ReadByte()!=-1) throw new InvalidDataException("Loader ZIP entry size mismatch.");
        }
        else
        {
            if(new FileInfo(source).Length!=949248) throw new InvalidDataException("Unsupported compatibility loader.");
            bytes=File.ReadAllBytes(source);
        }
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(LoaderSha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Compatibility loader checksum does not match the supported loader.");
        string target=Path.Combine(game,"CoreMessaging.dll");
        if(File.Exists(target)) { if(Installed(game)) { Remember(profile, bytes); return; } throw new IOException("A different CoreMessaging.dll already exists; it was not overwritten."); }
        if(Directory.Exists(target)) throw new IOException("A directory occupies the loader path.");
        foreach(string proxy in new[]{"d3d9.dll","dbghelp.dll"}) if(File.Exists(Path.Combine(game,proxy))) throw new IOException("Another proxy loader exists: "+proxy);
        Remember(profile, bytes);
        string staged=target+".sgmm-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(staged,bytes); File.Move(staged,target);
            try { if(!Installed(game)) throw new IOException("Installed loader verification failed."); Storage.Save(Path.Combine(profile,"archive-compatibility.json"),new{source=Path.GetFileName(source),sha256=LoaderSha256,installedUtc=DateTime.UtcNow,installedByManager=true}); }
            catch { File.Delete(target); throw; }
        }
        finally { if(File.Exists(staged)) File.Delete(staged); }
    }
}
