using ThemModdingHerds.VelvetBeautifier.Utilities;

namespace SkullgirlsModManager;

public partial class Manager
{
    public static void ValidateLooseName(string name)
    {
        if (!name.StartsWith("Digital Art Compendium/", StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || name.Contains('\\') || name.Split('/').Any(s => s.Length == 0 || s.EndsWith('.') || s.EndsWith(' ')))
            throw new InvalidDataException("Loose mods support only PNG files under Digital Art Compendium/.");
        SafePaths.Under(Path.GetTempPath(), name);
    }
    private string LooseTarget(string name)
    {
        ValidateLooseName(name);
        string path = SafePaths.Under(GameFolder, name);
        for (string? current = path; current != null && !current.Equals(GameFolder, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current)) SafePaths.RejectLinks(current);
        if (Directory.Exists(path)) throw new InvalidDataException("A directory occupies the loose-file target: " + name);
        return path;
    }
    private static string HashOrMissing(string path) => File.Exists(path) ? Crypto.Checksum(path) : "";
    private Plan PlanLoose(IEnumerable<Mod> mods, InstallState state, Plan plan)
    {
        foreach (var mod in mods.Where(m => m.Enabled))
        foreach (var file in mod.LooseFiles())
        {
            string target = LooseTarget(file.Key);
            plan.Changes.Add(new("Loose files", file.Key, mod.Info.Name, File.Exists(target) ? "Replace" : "Add", plan.LooseFiles.ContainsKey(file.Key)));
            plan.LooseFiles[file.Key] = file.Value;
        }
        foreach (var known in state.LooseFiles.Where(p => p.Value.LastHash != p.Value.OriginalHash && !plan.LooseFiles.ContainsKey(p.Key)))
        {
            LooseTarget(known.Key);
            plan.LooseRestore.Add(known.Key);
            plan.Changes.Add(new("Loose files", known.Key, "Original backup", known.Value.OriginalHash.Length == 0 ? "Remove added file" : "Restore", false));
        }
        return plan;
    }
    private void PrepareLoose(Plan plan, InstallState state, InstallState next, List<TransactionFile> files, List<string> temporary)
    {
        foreach (string name in plan.LooseFiles.Keys.Concat(plan.LooseRestore).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            string target = LooseTarget(name), current = HashOrMissing(target);
            string backup = SafePaths.Under(BackupFolder, "loose/" + name);
            if (state.LooseFiles.TryGetValue(name, out var known) && current != known.LastHash)
                throw new IOException("Loose file changed outside this manager: " + name);
            for (string? currentPath = backup; currentPath != null && !currentPath.Equals(BackupFolder, StringComparison.OrdinalIgnoreCase); currentPath = Path.GetDirectoryName(currentPath))
                if (File.Exists(currentPath) || Directory.Exists(currentPath)) SafePaths.RejectLinks(currentPath);
            if (known != null && HashOrMissing(backup) != known.OriginalHash) throw new InvalidDataException("Loose backup checksum failed: " + name);
            if (known == null)
            {
                if (File.Exists(backup) && HashOrMissing(backup) != current) throw new InvalidDataException("Untracked loose backup differs: " + name);
                if (current.Length > 0 && !File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    string temp = backup + "." + Guid.NewGuid().ToString("N") + ".tmp"; temporary.Add(temp);
                    File.Copy(target, temp); if (HashOrMissing(temp) != current) throw new IOException("Loose backup verification failed.");
                    File.Move(temp, backup);
                }
                known = new() { OriginalHash = current, LastHash = current };
                next.LooseFiles[name] = known;
            }
            string? source = plan.LooseFiles.GetValueOrDefault(name) ?? (known.OriginalHash.Length > 0 ? backup : null);
            if (source != null) SafePaths.RejectLinks(source);
            if (plan.LooseFiles.ContainsKey(name)) { using var input = File.OpenRead(source!); byte[] signature = new byte[8]; input.ReadExactly(signature); if (!signature.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) throw new InvalidDataException("Loose artwork is not a PNG: " + name); }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string token = Guid.NewGuid().ToString("N"), staged = target + ".sgmm-" + token + ".tmp", rollback = target + ".sgmm-" + token + ".rollback";
            string newHash = "";
            if (source != null) { temporary.Add(staged); File.Copy(source, staged); newHash = HashOrMissing(staged); if (newHash != HashOrMissing(source)) throw new IOException("Loose staging verification failed."); }
            next.LooseFiles[name].LastHash = newHash;
            if (newHash != current) files.Add(new(target, staged, rollback, current, newHash));
        }
    }
}
