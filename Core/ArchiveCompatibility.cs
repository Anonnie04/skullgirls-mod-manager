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
    public static bool GameRunning()
    {
        // Proton can expose the .exe suffix in the Unix process name.
        foreach (string name in new[] { "SkullGirls", "SkullGirls.exe", "Skullgirls", "Skullgirls.x86_64", "Skullgirls.x86_64-pc-linux-gnu", "Skullgirls.x86", "Skullgirls.x86_" })
        {
            var processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (running) return true;
        }
        return false;
    }
    public static bool Required(string game, IEnumerable<string> archives) => archives.Any(a => ProtectedArchives.Contains(a, StringComparer.OrdinalIgnoreCase))
        && File.ReadAllBytes(GameDiscovery.Executable(game) ?? throw new InvalidDataException("Skullgirls executable not found.")).AsSpan().IndexOf(Encoding.ASCII.GetBytes("Package %s.gfs not verified.")) >= 0;
    // Local Release Win32 build of SkullLoader commit 93798f8 for Steam build 26588330.
    private const string Game3720Sha256 = "C18918F3592EA0BFBBEE3405E0777A5DDE04CBC190564762F1F88BEDB3428509";
    private const string Loader3720Sha256 = "739FFB5FF5CBA2EDBC82D4D01807049611AEE26E201F7F28C3526CEF4A395C42";

    private static (string Hash, int Size) SupportedLoader(string game) =>
        Verified(Path.Combine(game, "SkullGirls.exe"), Game3720Sha256)
            ? (Loader3720Sha256, 946688)
            : (LoaderSha256, 949248);

    public static bool Installed(string game)
    {
        string path = Path.Combine(game, "CoreMessaging.dll");
        return File.Exists(path) && Verified(path, SupportedLoader(game).Hash);
    }
    public static string SavedLoader(string profile) => Path.Combine(profile, "compatibility", "CoreMessaging.dll");
    private static bool Verified(string path, params string[] hashes)
    {
        if (!File.Exists(path)) return false;
        SafePaths.RejectLinks(path);
        using var stream = File.OpenRead(path);
        return hashes.Contains(Convert.ToHexString(SHA256.HashData(stream)), StringComparer.OrdinalIgnoreCase);
    }
    public static bool Saved(string profile) => Verified(SavedLoader(profile), LoaderSha256, Loader3720Sha256);
    public static bool RestoreSaved(string game, string profile)
    {
        if (Installed(game)) return true;
        string source = SavedLoader(profile);
        if (!Verified(source, SupportedLoader(game).Hash)) return false;
        Install(game, profile, source);
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
        if (NativeCompatibility.IsNative(game))
        {
            if (archives.Any(a => ProtectedArchives.Contains(a, StringComparer.OrdinalIgnoreCase)) && !NativeCompatibility.Installed(game))
                throw new InvalidOperationException("Set up native Linux mod support before applying protected archive replacements. The manager will verify this exact game build and back up its original executable.");
            return;
        }
        if (Required(game,archives) && !Installed(game)) throw new InvalidOperationException("These mods change archives verified by Skullgirls. Open Backups & tools → Set up mod support. This is a separate, one-time setup, not a mod ZIP import. After setup, the manager saves support locally and restores it automatically after a game reinstall.");
    }
    public static void Install(string game, string profile, string source)
    {
        if (!File.Exists(Path.Combine(game, "SkullGirls.exe")))
            throw new InvalidOperationException("The DLL compatibility loader requires the Windows game through Proton; it cannot be installed for the native Linux game.");
        if (ArchiveCompatibility.GameRunning()) throw new InvalidOperationException("Close Skullgirls, including its error dialog, before installing compatibility.");
        using var guard = new FileStream(Path.Combine(profile,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(Path.Combine(profile,"transaction.json"))) throw new InvalidOperationException("Recover the interrupted installation first.");
        SafePaths.RejectLinks(game); SafePaths.RejectLinks(source);
        var (loaderHash, loaderSize) = SupportedLoader(game);
        byte[] bytes;
        if (source.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(source);
            var entries=zip.Entries.Where(e=>e.FullName.Replace('\\','/').Split('/').Last().Equals("CoreMessaging.dll",StringComparison.OrdinalIgnoreCase)).ToList();
            if(entries.Count!=1 || entries[0].Length!=loaderSize) throw new InvalidDataException("Select a ZIP containing exactly one CoreMessaging.dll supported by this Skullgirls build.");
            using var input=entries[0].Open(); bytes=new byte[loaderSize]; input.ReadExactly(bytes); if(input.ReadByte()!=-1) throw new InvalidDataException("Loader ZIP entry size mismatch.");
        }
        else
        {
            if(new FileInfo(source).Length!=loaderSize) throw new InvalidDataException("Unsupported compatibility loader for this Skullgirls build.");
            bytes=File.ReadAllBytes(source);
        }
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(loaderHash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Compatibility loader checksum does not match the supported loader for this Skullgirls build.");
        string target=Path.Combine(game,"CoreMessaging.dll");
        if(File.Exists(target)) { if(Installed(game)) { Remember(profile, bytes); return; } throw new IOException("A different CoreMessaging.dll already exists; it was not overwritten."); }
        if(Directory.Exists(target)) throw new IOException("A directory occupies the loader path.");
        foreach(string proxy in new[]{"d3d9.dll","dbghelp.dll"}) if(File.Exists(Path.Combine(game,proxy))) throw new IOException("Another proxy loader exists: "+proxy);
        Remember(profile, bytes);
        string staged=target+".sgmm-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(staged,bytes); File.Move(staged,target);
            try { if(!Installed(game)) throw new IOException("Installed loader verification failed."); Storage.Save(Path.Combine(profile,"archive-compatibility.json"),new{source=Path.GetFileName(source),sha256=loaderHash,installedUtc=DateTime.UtcNow,installedByManager=true}); }
            catch { File.Delete(target); throw; }
        }
        finally { if(File.Exists(staged)) File.Delete(staged); }
    }
}
