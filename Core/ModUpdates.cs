using System.Text.Json;
using ThemModdingHerds.VelvetBeautifier.Utilities;

namespace SkullgirlsModManager;

public record ModAssetChange(string Path, string Kind);

public sealed class ModUpdate : IDisposable
{
    public Mod Current { get; }
    public Mod Replacement { get; }
    public IReadOnlyList<ModAssetChange> Changes { get; }
    internal Dictionary<string, string> Before { get; }
    internal Dictionary<string, string> After { get; }
    internal ModUpdate(Mod current, Mod replacement)
    {
        Current = current; Replacement = replacement;
        Before = Library.PackageHashes(current); After = Library.PackageHashes(replacement);
        var oldAssets = Assets(current); var newAssets = Assets(replacement);
        Changes = oldAssets.Keys.Union(newAssets.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ModAssetChange(p, !oldAssets.ContainsKey(p) ? "Added" : !newAssets.ContainsKey(p) ? "Removed" :
                Crypto.Checksum(oldAssets[p]) != Crypto.Checksum(newAssets[p]) ? "Changed" : "Unchanged"))
            .Where(c => c.Kind != "Unchanged").ToList();
    }
    private static Dictionary<string, string> Assets(Mod mod)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archive in mod.Replacements()) foreach (var asset in archive.Value) result.Add(archive.Key + "/" + asset.Key, asset.Value);
        foreach (var asset in mod.LooseFiles()) result.Add("loose/" + asset.Key, asset.Value);
        return result;
    }
    public void Dispose() { if (Directory.Exists(Replacement.Folder)) Directory.Delete(Replacement.Folder, true); }
}

public partial class Library
{
    private string UpdateJournal => Path.Combine(Root, ".mod-update.json");
    private record UpdateMove(string Destination, string Previous);
    private Mod LibraryEntry(Mod selected) => Mods().SingleOrDefault(m =>
        string.Equals(m.Folder, selected.Folder, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException("Select a mod from this library.");

    internal static Dictionary<string, string> PackageHashes(Mod mod) => Walk(mod.Folder)
        .ToDictionary(p => Path.GetRelativePath(mod.Folder, p), Crypto.Checksum, StringComparer.OrdinalIgnoreCase);

    public ModUpdate PrepareUpdate(Mod selected, string input) => PrepareUpdate(selected, input, false);

    private ModUpdate PrepareUpdate(Mod selected, string input, bool restoreSettings)
    {
        var current = LibraryEntry(selected);
        return StagePackage(input, replacement =>
        {
            string staged = Path.Combine(Root, ".prepared-" + Guid.NewGuid().ToString("N"));
            if (!restoreSettings) replacement.SetEnabled(current.Enabled);
            Directory.Move(replacement.Folder, staged);
            try { return new ModUpdate(current, new Mod(staged)); }
            catch { Directory.Delete(staged, true); throw; }
        });
    }

    public ModUpdate PrepareRollback(Mod selected)
    {
        var current = LibraryEntry(selected);
        string history = Path.Combine(Root, ".versions", Path.GetFileName(current.Folder));
        string? previous = Directory.Exists(history) ? Directory.GetDirectories(history).OrderDescending(StringComparer.Ordinal).FirstOrDefault() : null;
        if (previous == null) throw new InvalidOperationException("This mod has no previous package to restore.");
        return PrepareUpdate(current, previous, true);
    }

    public Mod CommitUpdate(ModUpdate update, bool allowDifferentId = false)
    {
        var current = LibraryEntry(update.Current);
        if (!Path.GetDirectoryName(update.Replacement.Folder)!.Equals(Root, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(update.Replacement.Folder).StartsWith(".prepared-", StringComparison.Ordinal))
            throw new InvalidDataException("Prepare this update in the same library.");
        if (current.Info.Id != update.Replacement.Info.Id && !allowDifferentId)
            throw new InvalidDataException("The replacement has a different mod ID. Confirm an explicit mapping to this library entry.");
        if (Mods().Any(m => m.Folder != current.Folder && m.Info.Id == update.Replacement.Info.Id))
            throw new InvalidDataException("The replacement ID belongs to another library entry.");
        static bool Matches(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var hash) && hash == p.Value);
        if (!Matches(update.Before, PackageHashes(current)) || !Matches(update.After, PackageHashes(update.Replacement)))
            throw new InvalidOperationException("The package changed after preview. Review a fresh update.");
        // The folder name is the library identity, so order.json never changes.
        string previous = Path.Combine(Root, ".versions", Path.GetFileName(current.Folder), DateTime.UtcNow.Ticks.ToString("D19") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        Storage.Save(UpdateJournal, new UpdateMove(Path.GetFileName(current.Folder), Path.GetRelativePath(Root, previous)));
        try
        {
            Directory.Move(current.Folder, previous);
            Directory.Move(update.Replacement.Folder, current.Folder);
        }
        catch { RecoverUpdate(); throw; }
        File.Delete(UpdateJournal);
        return new Mod(current.Folder);
    }

    private void RecoverUpdate()
    {
        if (!File.Exists(UpdateJournal)) return;
        var move = JsonSerializer.Deserialize<UpdateMove>(File.ReadAllText(UpdateJournal)) ?? throw new InvalidDataException("Invalid mod update journal.");
        string destination = SafePaths.Under(Root, move.Destination), previous = SafePaths.Under(Root, move.Previous);
        if (Path.GetDirectoryName(destination) != Root || !previous.StartsWith(Path.Combine(Root, ".versions") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid mod update recovery paths.");
        if (!Directory.Exists(destination)) Directory.Move(previous, destination);
        File.Delete(UpdateJournal);
    }
}
