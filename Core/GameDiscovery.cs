using System.Text.RegularExpressions;

namespace SkullgirlsModManager;

public static class GameDiscovery
{
    public static string? Executable(string folder) => new[] { "SkullGirls.exe", "Skullgirls.x86_64-pc-linux-gnu", "Skullgirls.x86", "Skullgirls.x86_64" }
        .Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

    public static bool IsGameFolder(string? folder) => !string.IsNullOrWhiteSpace(folder)
        && Executable(folder) != null && Directory.Exists(Path.Combine(folder, "data01"));

    public static string Find(string? saved = null)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Find(saved, Environment.GetEnvironmentVariable("SKULLGIRLS_GAME_ROOT"), [
            Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".steam", "root"),
            Path.Combine(local, "Steam"), Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".steam", "steam")]);
    }

    public static string Find(string? saved, string? configured, IEnumerable<string> steamRoots)
    {
        if (IsGameFolder(configured)) return Path.GetFullPath(configured!);
        if (IsGameFolder(saved)) return Path.GetFullPath(saved!);
        var roots = new HashSet<string>(steamRoots, StringComparer.Ordinal);
        foreach (string root in roots.ToArray())
        foreach (string relative in new[] { "steamapps/libraryfolders.vdf", "config/libraryfolders.vdf" })
        {
            string content = Read(Path.Combine(root, relative));
            foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
                roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            // Legacy libraryfolders.vdf used numeric keys directly for paths.
            foreach (Match match in Regex.Matches(content, "\"[0-9]+\"\\s*\"([^\"]+)\""))
                roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (string root in roots)
        {
            string apps = Path.Combine(root, "steamapps");
            var install = Regex.Match(Read(Path.Combine(apps, "appmanifest_245170.acf")), "\"installdir\"\\s*\"([^\"]+)\"");
            if (install.Success)
            {
                string path = Path.Combine(apps, "common", install.Groups[1].Value);
                if (IsGameFolder(path)) return Path.GetFullPath(path);
            }
            string fallback = Path.Combine(apps, "common", "Skullgirls");
            if (IsGameFolder(fallback)) return Path.GetFullPath(fallback);
        }
        return "";
    }

    private static string Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }
}
