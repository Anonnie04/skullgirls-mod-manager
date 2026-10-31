using System.Text.Json;

namespace SkullgirlsModManager;

public partial class Mod
{
    private string OptionalFile => Path.Combine(Folder, ".optional-paths.json");
    public Dictionary<string, bool> OptionalSelections()
    {
        var saved = File.Exists(OptionalFile)
            ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(OptionalFile)) ?? throw new InvalidDataException("Invalid optional path selections.")
            : new Dictionary<string, bool>();
        return Info.OptionalPaths.ToDictionary(o => o.Id, o => saved.GetValueOrDefault(o.Id, o.DefaultEnabled));
    }
    public void SetOptionalSelections(IReadOnlyDictionary<string, bool> selections)
    {
        SafePaths.RejectLinks(Folder);
        if (File.Exists(OptionalFile) || Directory.Exists(OptionalFile)) SafePaths.RejectLinks(OptionalFile);
        Storage.Save(OptionalFile, Info.OptionalPaths.ToDictionary(o => o.Id, o => selections.GetValueOrDefault(o.Id, o.DefaultEnabled)));
    }
    public void ResetOptionalPaths() => SetOptionalSelections(new Dictionary<string, bool>());
    public void SetOptionalPath(string id, bool enabled)
    {
        var selections = OptionalSelections();
        if (!selections.ContainsKey(id)) throw new InvalidDataException("Unknown optional path: " + id);
        selections[id] = enabled; SetOptionalSelections(selections);
    }
    private static bool MatchesPath(string path, string prefix) => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    private bool Includes(string path)
    {
        var option = Info.OptionalPaths.SingleOrDefault(o => o.Paths.Any(p => MatchesPath(path, p)));
        return option == null || OptionalSelections()[option.Id];
    }
    public void ValidateOptionalPaths()
    {
        if (Info.OptionalPaths == null) throw new InvalidDataException("Invalid optional paths.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        var assets = Replacements(true).SelectMany(a => a.Value.Keys.Select(k => a.Key + "/" + k))
            .Concat(LooseFiles(true).Keys.Select(k => "loose/" + k)).ToList();
        foreach (var option in Info.OptionalPaths)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.Id) || string.IsNullOrWhiteSpace(option.Name)
                || !ids.Add(option.Id) || option.Paths == null || option.Paths.Count == 0)
                throw new InvalidDataException("Optional paths need unique IDs, names and replacement paths.");
            foreach (string path in option.Paths)
            {
                if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.EndsWith('/') || path.Split('/').Any(p => p is "" or "." or ".."))
                    throw new InvalidDataException("Invalid optional replacement path.");
                SafePaths.Under(Folder, path);
                if (!assets.Any(a => MatchesPath(a, path))) throw new InvalidDataException("Optional path has no replacements: " + path);
                if (paths.Any(p => MatchesPath(p, path) || MatchesPath(path, p))) throw new InvalidDataException("Optional paths must not overlap: " + path);
                paths.Add(path);
            }
        }
    }
}
