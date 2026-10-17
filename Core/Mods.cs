using System.IO.Compression;
using System.Text.Json;
using ThemModdingHerds.VelvetBeautifier.Modding;

namespace SkullgirlsModManager;

// Adapted from Velvet's Mod / ModDB: mod.json, enabled marker, and *.gfs folders.
public class Mod(string folder)
{
    public const string MOD_ENABLED_NAME = "enabled";
    public const string MODINFO_NAME = "mod.json";
    public string Folder { get; } = folder;
    public ModInfo Info { get; } = ModInfo.Read(Path.Combine(folder, MODINFO_NAME)) ?? throw new InvalidDataException("Invalid mod.json.");
    public bool Enabled => File.Exists(Path.Combine(Folder, MOD_ENABLED_NAME));
    public void SetEnabled(bool enabled)
    {
        string path = Path.Combine(Folder, MOD_ENABLED_NAME);
        if (enabled) File.WriteAllText(path, "");
        else if (File.Exists(path)) File.Delete(path);
    }

    public Dictionary<string, Dictionary<string, string>> Replacements()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var roots = Directory.GetDirectories(Folder, "*.gfs").ToList();
        string data = Path.Combine(Folder, "data01");
        if (Directory.Exists(data)) roots.AddRange(Directory.GetDirectories(data, "*.gfs"));
        foreach (string root in roots)
        {
            string name = Path.GetFileName(root);
            if (result.ContainsKey(name)) throw new InvalidDataException($"Archive appears twice in {Info.Name}: {name}");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Library.Walk(root))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!files.TryAdd(relative, file)) throw new InvalidDataException($"Duplicate asset path: {relative}");
            }
            if (files.Count > 0) result.Add(name, files);
        }
        return result;
    }
}

public partial class Library
{
    public string Root { get; }
    private string OrderFile => Path.Combine(Root, "order.json");
    public Library(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); }
    public List<Mod> Mods()
    {
        var order = File.Exists(OrderFile) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(OrderFile)) ?? [] : [];
        return Directory.GetDirectories(Root).Where(p => !Path.GetFileName(p).StartsWith('.') && File.Exists(Path.Combine(p, Mod.MODINFO_NAME)))
            .Select(p => new Mod(p)).OrderBy(m => { int n = order.IndexOf(Path.GetFileName(m.Folder)); return n < 0 ? int.MaxValue : n; })
            .ThenBy(m => Path.GetFileName(m.Folder), StringComparer.OrdinalIgnoreCase).ToList();
    }
    public void SetOrder(IEnumerable<Mod> mods) => Storage.Save(OrderFile, mods.Select(m => Path.GetFileName(m.Folder)).ToList());

    public Mod Import(string input)
    {
        string staging = Path.Combine(Root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            if (Directory.Exists(input))
            {
                if (Path.GetFullPath(input).TrimEnd('\\').Equals(Root, StringComparison.OrdinalIgnoreCase)
                    || Root.StartsWith(Path.GetFullPath(input).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Choose a mod folder outside the library.");
                foreach (string file in Walk(input))
                {
                    string target = SafePaths.Under(staging, Path.GetRelativePath(input, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
            else
            {
                using var archive = ZipFile.OpenRead(input);
                long size = 0;
                if (archive.Entries.Count > 100000) throw new InvalidDataException("ZIP contains too many files.");
                foreach (var entry in archive.Entries)
                {
                    size = checked(size + entry.Length);
                    if (size > 20L * 1024 * 1024 * 1024) throw new InvalidDataException("ZIP exceeds the 20 GB import limit.");
                    string target = SafePaths.Under(staging, entry.FullName);
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var output = new FileStream(target, FileMode.CreateNew);
                    using var source = entry.Open();
                    source.CopyTo(output);
                }
            }
            string content = staging;
            if (!File.Exists(Path.Combine(content, Mod.MODINFO_NAME)))
            {
                var candidates = Directory.GetFiles(staging, Mod.MODINFO_NAME, SearchOption.AllDirectories);
                if (candidates.Length == 1) content = Path.GetDirectoryName(candidates[0])!;
                else if (candidates.Length > 1) throw new InvalidDataException("Import one mod at a time.");
                else
                {
                    new ModInfo { Id = "local." + Guid.NewGuid().ToString("N"), Name = Path.GetFileNameWithoutExtension(input),
                        Description = "Imported local replacement mod", Version = new Version(1, 0) }.Write(Path.Combine(content, Mod.MODINFO_NAME));
                }
            }
            var mod = new Mod(content);
            if (string.IsNullOrWhiteSpace(mod.Info.Id) || string.IsNullOrWhiteSpace(mod.Info.Name)) throw new InvalidDataException("A mod needs an id and name.");
            if (Mods().Any(m => m.Info.Id == mod.Info.Id)) throw new InvalidDataException("A mod with this ID is already installed. Use a different ID for a new version.");
            if (mod.Replacements().Count == 0) throw new InvalidDataException("No replacement folders found. Expected archive-name.gfs/asset/path inside the mod.");
            mod.SetEnabled(false);
            string destination = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.Move(content, destination);
            return new Mod(destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static IEnumerable<string> Walk(string root)
    {
        SafePaths.RejectLinks(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            SafePaths.RejectLinks(entry);
            if (Directory.Exists(entry)) { foreach (var file in Walk(entry)) yield return file; }
            else yield return entry;
        }
    }
}

public static class Storage
{
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
        {
            JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
}
