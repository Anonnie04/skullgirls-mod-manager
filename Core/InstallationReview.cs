using ThemModdingHerds.VelvetBeautifier.Utilities;

namespace SkullgirlsModManager;

public record AssetOutcome(string Archive, string Asset, IReadOnlyList<Change> Contributors, string Explanation)
{
    public Change Winner => Contributors[^1];
    public bool Conflict => Contributors.Count > 1;
}

public record RecoveryIssue(string Target, string Problem, string Action);
public record InstallationStatus(bool PendingTransaction, bool PendingBaselineUpdate, int TrackedArchives,
    int TrackedLooseFiles, int HistoryCount, IReadOnlyList<RecoveryIssue> Issues)
{
    public bool NeedsRecovery => PendingTransaction || PendingBaselineUpdate;
}

public partial class Manager
{
    private static List<AssetOutcome> ExplainOutcomes(Plan plan)
    {
        return plan.Changes.Where(c => c.ModFolder != null)
            .GroupBy(c => c.Archive + "\0" + c.Asset, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var contributors = group.ToList(); var winner = contributors[^1];
                string explanation = contributors.Count > 1
                    ? $"{winner.Mod} wins at load-order position {winner.LoadOrder}: its complete file replaces all earlier contributions to this path."
                    : $"{winner.Mod} supplies this complete file at load-order position {winner.LoadOrder}.";
                explanation += " Files are replaced, not merged. Successful archive packing does not establish game compatibility.";
                if (winner.Archive.Equals("characters-foits.gfs", StringComparison.OrdinalIgnoreCase)
                    && AssetPreviews.WorkshopAsset(winner.Archive, winner.Asset))
                {
                    string source = plan.Replacements[winner.Archive][winner.Asset];
                    using var stream = File.OpenRead(source);
                    byte[] bytes = new byte[(int)Math.Min(stream.Length, 1024 * 1024)]; stream.ReadExactly(bytes);
                    explanation += SpriteStorage.RecognizesMetadata(bytes, winner.Asset)
                        ? " Recognized VRSN 9 character metadata includes a palette-name table and art memory sizes. Replacing the whole metadata file can override palette definitions and affect associated art. Check the winning metadata against the enabled palette and art packages."
                        : " Character metadata format is not recognized by the supported VRSN 9 checks. Only file-level precedence is known; palette and art compatibility has not been established.";
                }
                return new AssetOutcome(winner.Archive, winner.Asset, contributors, explanation);
            }).ToList();
    }

    // Read-only inspection; recovery and baseline adoption remain explicit existing operations.
    public InstallationStatus InspectInstallation()
    {
        bool transaction = File.Exists(Journal), baselineUpdate = File.Exists(BaselineJournal);
        var issues = new List<RecoveryIssue>(); int historyCount = 0;
        if (transaction || baselineUpdate)
            issues.Add(new(Path.GetFileName(DataFolder), transaction && baselineUpdate ? "Two recovery journals are present." :
                transaction ? "Interrupted installation transaction." : "Interrupted archive baseline update.",
                "Use Recover interrupted operation. Recovery verifies saved files before proceeding."));
        try { if (Directory.Exists(BaselineHistory)) { SafePaths.RejectLinks(BaselineHistory); historyCount = Directory.GetDirectories(BaselineHistory).Length; } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { issues.Add(new("Baseline history", ex.Message, "Inspect the profile's baseline-history folder.")); }
        InstallState state;
        try { state = State(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            issues.Add(new("Backup state", ex.Message, "Inspect the backup folder and saved state before applying."));
            return new(transaction, baselineUpdate, 0, 0, historyCount, issues);
        }
        void Inspect(string name, ArchiveState known, Func<string> target, string backup, bool archive)
        {
            if (known == null) { issues.Add(new(name, "Invalid saved file state.", "Inspect saved backup state before applying.")); return; }
            try
            {
                string current = CheckedHash(target());
                if (!transaction && !baselineUpdate && current != known.LastHash && (!archive || current != known.OriginalHash))
                    issues.Add(new(name, current.Length == 0 ? "Installed file is missing." : "Installed file changed outside the manager.",
                        archive ? current.Length == 0 ? "Restore the missing game archive before applying; inspect the verified backup and baseline history first."
                            : "Review this archive in Game archives. Update archive adopts its current contents as the baseline; review backups first."
                            : "Review the changed artwork and backup before applying. External edits are protected."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { issues.Add(new(name, "Cannot inspect installed file: " + ex.Message, "Check the file and access permissions before applying.")); }
            try
            {
                string saved = CheckedHash(backup);
                if (saved != known.OriginalHash)
                    issues.Add(new(name, saved.Length == 0 ? "Original backup is missing." : "Original backup checksum failed.",
                        "Inspect the backup folder and baseline history. Apply and Restore refuse damaged backups."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { issues.Add(new(name, "Cannot verify backup: " + ex.Message, "Inspect the backup folder before applying or restoring.")); }
        }
        foreach (var entry in state.Archives)
        {
            try
            {
                if (Path.GetFileName(entry.Key) != entry.Key || !entry.Key.EndsWith(".gfs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid archive name in backup state.");
                Inspect(entry.Key, entry.Value, () => SafePaths.Under(DataFolder, entry.Key), SafePaths.Under(BackupFolder, entry.Key), true);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            { issues.Add(new(entry.Key, ex.Message, "Inspect saved backup state.")); }
        }
        foreach (var entry in state.LooseFiles)
        {
            try
            {
                ValidateLooseName(entry.Key);
                Inspect(entry.Key, entry.Value, () => LooseTarget(entry.Key), SafePaths.Under(BackupFolder, "loose/" + entry.Key), false);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            { issues.Add(new(entry.Key, ex.Message, "Inspect saved backup state.")); }
        }
        return new(transaction, baselineUpdate, state.Archives.Count, state.LooseFiles.Count, historyCount, issues);
    }

    private static string CheckedHash(string path)
    {
        for (string? parent = path; parent != null; parent = Path.GetDirectoryName(parent))
            if (File.Exists(parent) || Directory.Exists(parent)) SafePaths.RejectLinks(parent);
        if (Directory.Exists(path)) throw new InvalidDataException("A directory occupies the file path.");
        return HashOrMissing(path);
    }
}
