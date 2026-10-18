using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ThemModdingHerds.VelvetBeautifier.Utilities;

namespace SkullgirlsModManager;

public record Change(string Archive, string Asset, string Mod, string Action, bool Conflict);
public class ArchiveState
{
    public string OriginalHash { get; set; } = "";
    public string LastHash { get; set; } = "";
}
public class InstallState
{
    public Dictionary<string, ArchiveState> Archives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ArchiveState> LooseFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public record TransactionFile(string Target, string Staged, string Rollback, string OldHash, string NewHash);
public record Transaction(InstallState Before, List<TransactionFile> Files);
public record Plan(List<Change> Changes, Dictionary<string, Dictionary<string, string>> Replacements, List<string> Restore)
{
    public Dictionary<string, string> LooseFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> LooseRestore { get; init; } = [];
}

public partial class Manager
{
    public string GameFolder { get; }
    public string DataFolder => Path.Combine(GameFolder, "data01");
    public string Profile { get; }
    public string BackupFolder => Path.Combine(Profile, "backups");
    private string StateFile => Path.Combine(Profile, "state.json");
    private string Journal => Path.Combine(Profile, "transaction.json");
    public Action<string> Log { get; set; } = _ => { };
    public bool NeedsRecovery => File.Exists(Journal);

    public Manager(string game, string dataRoot)
    {
        GameFolder = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(GameFolder, "SkullGirls.exe")) || !Directory.Exists(DataFolder))
            throw new InvalidDataException("Select the Skullgirls folder containing SkullGirls.exe and data01.");
        SafePaths.RejectLinks(GameFolder);
        SafePaths.RejectLinks(DataFolder);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GameFolder.ToUpperInvariant())))[..20];
        Profile = Path.Combine(dataRoot, "profiles", key);
        Directory.CreateDirectory(BackupFolder);
    }

    public List<string> ArchiveNames() => Directory.GetFiles(DataFolder, "*.gfs").Select(Path.GetFileName).Cast<string>().Order().ToList();
    private InstallState State()
    {
        var state = File.Exists(StateFile) ? JsonSerializer.Deserialize<InstallState>(File.ReadAllText(StateFile)) ?? throw new InvalidDataException("Invalid backup state.") : new();
        state.Archives = new(state.Archives, StringComparer.OrdinalIgnoreCase);
        state.LooseFiles = new(state.LooseFiles, StringComparer.OrdinalIgnoreCase);
        return state;
    }
    private string Target(string name)
    {
        if (Path.GetFileName(name) != name || !name.EndsWith(".gfs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid archive name.");
        string path = SafePaths.Under(DataFolder, name);
        SafePaths.RejectLinks(path);
        return path;
    }
    private string Baseline(string name, InstallState state)
    {
        if (!state.Archives.ContainsKey(name)) return Target(name);
        string backup = SafePaths.Under(BackupFolder, name);
        if (!File.Exists(backup)) throw new InvalidDataException($"Backup missing: {name}");
        return backup;
    }
    private void Ready()
    {
        if (Process.GetProcessesByName("SkullGirls").Length != 0) throw new InvalidOperationException("Close Skullgirls before changing game files.");
        if (NeedsRecovery) throw new InvalidOperationException("An interrupted operation needs recovery. Use Recover interrupted operation first.");
    }

    public Plan Preview(IEnumerable<Mod> mods)
    {
        if (NeedsRecovery) throw new InvalidOperationException("Recover the interrupted operation first.");
        var state = State();
        mods = mods.ToList();
        var changes = new List<Change>();
        var replacements = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.Where(m => m.Enabled))
        foreach (var archive in mod.Replacements())
        {
            if (!entries.TryGetValue(archive.Key, out var names))
            {
                string target = Target(archive.Key);
                names = Archives.Read(Baseline(archive.Key, state)).Entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                entries[archive.Key] = names;
                replacements[archive.Key] = new(StringComparer.OrdinalIgnoreCase);
            }
            foreach (var asset in archive.Value)
            {
                bool conflict = replacements[archive.Key].ContainsKey(asset.Key);
                changes.Add(new(archive.Key, asset.Key, mod.Info.Name, names.Contains(asset.Key) ? "Replace" : "Add", conflict));
                replacements[archive.Key][asset.Key] = asset.Value;
            }
        }
        var restore = state.Archives.Where(p => p.Value.LastHash != p.Value.OriginalHash && !replacements.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        foreach (string archive in restore) changes.Add(new(archive, "(whole archive)", "Original backup", "Restore", false));
        return PlanLoose(mods, state, new(changes, replacements, restore));
    }

    public void Apply(IEnumerable<Mod> mods, bool restoreAll = false)
    {
        Ready();
        using var guard = new FileStream(Path.Combine(Profile, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var plan = restoreAll ? Preview([]) : Preview(mods);
        ArchiveCompatibility.Require(GameFolder, plan.Replacements.Keys);
        var state = State();
        var targets = plan.Replacements.Keys.Concat(plan.Restore).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0 && plan.LooseFiles.Count == 0 && plan.LooseRestore.Count == 0) { Log("No changes to apply."); return; }
        var next = JsonSerializer.Deserialize<InstallState>(JsonSerializer.Serialize(state))!;
        next.Archives = new(next.Archives, StringComparer.OrdinalIgnoreCase);
        next.LooseFiles = new(next.LooseFiles, StringComparer.OrdinalIgnoreCase);
        var files = new List<TransactionFile>();
        var temporary = new List<string>();
        try
        {
            foreach (string name in targets)
            {
                string target = Target(name);
                Log($"Checking {name}...");
                string currentHash = Crypto.Checksum(target);
                if (state.Archives.TryGetValue(name, out var known) && currentHash != known.LastHash)
                    throw new InvalidOperationException($"{name} changed outside this manager (possibly a game update). No files were applied. Use a fresh profile after reviewing your backups.");
                string baseline = Baseline(name, state);
                if (known != null && Crypto.Checksum(baseline) != known.OriginalHash) throw new InvalidDataException($"Backup checksum failed: {name}");
                if (known == null)
                {
                    Archives.Read(target);
                    baseline = SafePaths.Under(BackupFolder, name);
                    if (File.Exists(baseline))
                    {
                        if (Crypto.Checksum(baseline) != currentHash) throw new InvalidDataException($"Untracked backup differs: {name}");
                    }
                    else
                    {
                        Log($"Backing up {name}...");
                        string tempBackup = baseline + ".tmp";
                        File.Copy(target, tempBackup, true);
                        if (Crypto.Checksum(tempBackup) != currentHash) throw new IOException("Backup verification failed.");
                        File.Move(tempBackup, baseline);
                    }
                    known = new() { OriginalHash = currentHash, LastHash = currentHash };
                    next.Archives[name] = known;
                }
                string token = Guid.NewGuid().ToString("N");
                string staged = target + ".sgmm-" + token + ".tmp";
                string rollback = target + ".sgmm-" + token + ".rollback";
                temporary.Add(staged);
                if (plan.Replacements.TryGetValue(name, out var replacements))
                {
                    Log($"Repacking {name} ({replacements.Count} assets)...");
                    Archives.Build(baseline, staged, replacements);
                    Log($"Verifying all payloads in {name}...");
                    Archives.ValidateBuild(baseline, staged, replacements);
                }
                else { Log($"Preparing restore of {name}..."); File.Copy(baseline, staged); }
                string newHash = Crypto.Checksum(staged);
                next.Archives[name].LastHash = newHash;
                files.Add(new(target, staged, rollback, currentHash, newHash));
            }
            PrepareLoose(plan, state, next, files, temporary);
            // Recheck after lengthy builds so external changes cannot silently be overwritten.
            foreach (var file in files)
                if (HashOrMissing(file.Target) != file.OldHash) throw new IOException("A game file changed during preparation.");
            Storage.Save(Journal, new Transaction(state, files));
            foreach (var file in files)
            {
                Log($"Installing {Path.GetFileName(file.Target)}...");
                if (file.NewHash.Length == 0) File.Move(file.Target, file.Rollback);
                else if (file.OldHash.Length == 0) File.Move(file.Staged, file.Target);
                else File.Replace(file.Staged, file.Target, file.Rollback);
            }
            Storage.Save(StateFile, next);
            // Once the journal is gone, this transaction is committed.
            File.Delete(Journal);
            foreach (var file in files) if (File.Exists(file.Rollback)) File.Delete(file.Rollback);
            Log(restoreAll ? "Original files restored; mod-added files removed." : "Enabled mods applied. Backups retained.");
        }
        catch
        {
            if (NeedsRecovery) { Log("Rolling back interrupted operation..."); RecoverInternal(); }
            throw;
        }
        finally
        {
            if (!NeedsRecovery)
                foreach (string temp in temporary) if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Recover()
    {
        if (Process.GetProcessesByName("SkullGirls").Length != 0) throw new InvalidOperationException("Close Skullgirls first.");
        using var guard = new FileStream(Path.Combine(Profile, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverInternal();
    }
    private void RecoverInternal()
    {
        if (!NeedsRecovery) { Log("No interrupted operation found."); return; }
        var transaction = JsonSerializer.Deserialize<Transaction>(File.ReadAllText(Journal)) ?? throw new InvalidDataException("Invalid transaction journal.");
        // Validate everything before restoring any archive.
        foreach (var file in transaction.Files)
        {
            if (File.Exists(file.Staged)) SafePaths.RejectLinks(file.Staged);
            if (File.Exists(file.Rollback)) SafePaths.RejectLinks(file.Rollback);
            bool Auxiliary(string path) => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetDirectoryName(Path.GetFullPath(file.Target)), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).StartsWith(Path.GetFileName(file.Target) + ".sgmm-", StringComparison.OrdinalIgnoreCase);
            string expected = string.Equals(Path.GetDirectoryName(Path.GetFullPath(file.Target)), DataFolder, StringComparison.OrdinalIgnoreCase)
                ? Target(Path.GetFileName(file.Target)) : LooseTarget(Path.GetRelativePath(GameFolder, file.Target).Replace('\\', '/'));
            if (!string.Equals(expected, Path.GetFullPath(file.Target), StringComparison.OrdinalIgnoreCase)
                || !Auxiliary(file.Rollback) || !Auxiliary(file.Staged)) throw new InvalidDataException("Invalid journal path.");
            string current = HashOrMissing(file.Target);
            if (current != file.NewHash && current != file.OldHash) throw new IOException("An archive changed after interruption; recovery stopped to preserve it.");
            if (File.Exists(file.Rollback) && Crypto.Checksum(file.Rollback) != file.OldHash) throw new InvalidDataException("Rollback checksum failed.");
            if (file.OldHash.Length > 0 && !File.Exists(file.Rollback) && current != file.OldHash) throw new IOException("Rollback file missing.");
        }
        foreach (var file in transaction.Files)
            if (file.OldHash.Length == 0) { if (File.Exists(file.Target)) File.Delete(file.Target); }
            else if (File.Exists(file.Rollback))
            {
                File.Copy(file.Rollback, file.Staged, true);
                File.Move(file.Staged, file.Target, true);
            }
        Storage.Save(StateFile, transaction.Before);
        File.Delete(Journal);
        foreach (var file in transaction.Files)
        {
            if (File.Exists(file.Staged)) File.Delete(file.Staged);
            if (File.Exists(file.Rollback)) File.Delete(file.Rollback);
        }
        Log("Interrupted operation rolled back successfully.");
    }
}
