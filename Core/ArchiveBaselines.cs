using System.Text.Json;
using ThemModdingHerds.VelvetBeautifier.Utilities;

namespace SkullgirlsModManager;

public record ArchiveBaselineReview(string Archive, string CurrentHash, string? OriginalHash, string? LastHash, string StateHash);
public record ArchiveBaselineUpdate(string Archive, string History, string SnapshotHash, string NewHash, string? OldHash);

public partial class Manager
{
    private string BaselineJournal => Path.Combine(Profile, "baseline-update.json");
    public string BaselineHistory => Path.Combine(Profile, "baseline-history");

    public ArchiveBaselineReview ReviewArchiveUpdate(string name)
    {
        Ready();
        string target = Target(name);
        Archives.Read(target);
        var state = State();
        state.Archives.TryGetValue(name, out var known);
        return new(name, Crypto.Checksum(target), known?.OriginalHash, known?.LastHash, HashOrMissing(StateFile));
    }

    public string? UpdateArchiveBaseline(ArchiveBaselineReview reviewed)
    {
        Ready();
        using var guard = new FileStream(Path.Combine(Profile, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SafePaths.RejectLinks(Profile); SafePaths.RejectLinks(BackupFolder);
        using var currentGuard = new FileStream(Target(reviewed.Archive), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (ReviewArchiveUpdate(reviewed.Archive) != reviewed)
            throw new IOException("The archive or backup state changed after review. Review Update archive again.");
        if (reviewed.CurrentHash == reviewed.OriginalHash && reviewed.CurrentHash == reviewed.LastHash)
        { Log("Archive already matches its baseline; nothing to update."); return null; }
        string backup = SafePaths.Under(BackupFolder, reviewed.Archive);
        if (File.Exists(backup)) SafePaths.RejectLinks(backup);
        if (reviewed.OriginalHash == null && File.Exists(backup)) throw new InvalidDataException("An untracked backup already exists. Review the backup folder before updating.");
        if (reviewed.OriginalHash != null && Crypto.Checksum(backup) != reviewed.OriginalHash)
            throw new InvalidDataException("Previous backup checksum failed. The archive baseline was not changed.");
        Directory.CreateDirectory(BaselineHistory); SafePaths.RejectLinks(BaselineHistory);
        string token = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        string history = SafePaths.Under(BaselineHistory, token); Directory.CreateDirectory(history);
        string snapshot = Path.Combine(history, "previous-state.json");
        if (File.Exists(StateFile)) File.Copy(StateFile, snapshot);
        else Storage.Save(snapshot, State());
        if (reviewed.OriginalHash != null)
        {
            File.Copy(backup, Path.Combine(history, "previous-backup.gfs"));
            if (Crypto.Checksum(Path.Combine(history, "previous-backup.gfs")) != reviewed.OriginalHash) throw new IOException("History backup verification failed.");
        }
        File.Copy(Target(reviewed.Archive), Path.Combine(history, "current.gfs"));
        if (Crypto.Checksum(Path.Combine(history, "current.gfs")) != reviewed.CurrentHash || HashOrMissing(StateFile) != reviewed.StateHash)
            throw new IOException("Archive or state changed while preserving baseline history.");
        var update = new ArchiveBaselineUpdate(reviewed.Archive, token, Crypto.Checksum(snapshot), reviewed.CurrentHash, reviewed.OriginalHash);
        Storage.Save(Path.Combine(history, "update.json"), update);
        Storage.Save(BaselineJournal, update);
        CompleteBaselineUpdate();
        return history;
    }

    private void CompleteBaselineUpdate()
    {
        var update = JsonSerializer.Deserialize<ArchiveBaselineUpdate>(File.ReadAllText(BaselineJournal)) ?? throw new InvalidDataException("Invalid baseline update journal.");
        if (string.IsNullOrEmpty(update.History) || update.History.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Invalid baseline history path.");
        string target = Target(update.Archive), backup = SafePaths.Under(BackupFolder, update.Archive);
        SafePaths.RejectLinks(Profile); SafePaths.RejectLinks(BackupFolder); SafePaths.RejectLinks(BaselineHistory);
        string history = SafePaths.Under(BaselineHistory, update.History); SafePaths.RejectLinks(history);
        string snapshot = Path.Combine(history, "previous-state.json"), current = Path.Combine(history, "current.gfs");
        SafePaths.RejectLinks(snapshot); SafePaths.RejectLinks(current);
        if (File.Exists(backup)) SafePaths.RejectLinks(backup);
        if (Crypto.Checksum(snapshot) != update.SnapshotHash || Crypto.Checksum(current) != update.NewHash)
            throw new InvalidDataException("Baseline history checksum failed. Recovery stopped.");
        var before = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(snapshot)) ?? throw new InvalidDataException("Invalid saved baseline state.");
        before.Archives = new(before.Archives, StringComparer.OrdinalIgnoreCase);
        before.Archives.TryGetValue(update.Archive, out var old);
        if (old?.OriginalHash != update.OldHash) throw new InvalidDataException("Baseline history does not match its journal.");
        if (update.OldHash != null)
        {
            string previous = Path.Combine(history, "previous-backup.gfs"); SafePaths.RejectLinks(previous);
            if (Crypto.Checksum(previous) != update.OldHash) throw new InvalidDataException("Previous baseline history checksum failed.");
        }
        var next = JsonSerializer.Deserialize<InstallState>(JsonSerializer.Serialize(before))!;
        next.Archives = new(next.Archives, StringComparer.OrdinalIgnoreCase);
        next.Archives[update.Archive] = new() { OriginalHash = update.NewHash, LastHash = update.NewHash };
        string state = JsonSerializer.Serialize(State());
        if (state != JsonSerializer.Serialize(before) && state != JsonSerializer.Serialize(next)) throw new IOException("Backup state changed after the baseline update started. Recovery stopped.");
        string backupHash = HashOrMissing(backup);
        if (backupHash != (update.OldHash ?? "") && backupHash != update.NewHash) throw new IOException("Baseline backup changed after the update started. Recovery stopped.");
        using var currentGuard = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Crypto.Checksum(target) != update.NewHash) throw new IOException("The installed archive changed after the baseline update started. Recovery stopped.");
        Archives.Read(current);
        if (backupHash != update.NewHash)
        {
            string staged = backup + ".baseline-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(current, staged);
                if (Crypto.Checksum(staged) != update.NewHash) throw new IOException("New baseline backup verification failed.");
                if (File.Exists(backup)) File.Replace(staged, backup, null);
                else File.Move(staged, backup);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        Storage.Save(StateFile, next);
        File.Delete(BaselineJournal);
        Log($"Updated baseline for {update.Archive}. Game file unchanged. Previous baseline and state retained in {history}");
    }
}
