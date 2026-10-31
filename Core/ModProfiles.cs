using System.Text.Json;

namespace SkullgirlsModManager;

public record ProfileMod(string Folder, string Id, string Name, string Version, bool Enabled)
{
    public Dictionary<string, bool>? OptionalPaths { get; init; }
}
public record ModProfile(string Id, string Name, IReadOnlyList<ProfileMod> Mods)
{
    public override string ToString() => Name;
}
public record ProfileSelection(string Folder, bool Enabled)
{
    public Dictionary<string, bool>? OptionalPaths { get; init; }
}
public record ProfileReviewRow(string Name, string? Folder, int? Order, bool Enabled, string SavedVersion, string CurrentVersion, string Note)
{
    public Dictionary<string, bool>? OptionalPaths { get; init; }
}
public sealed class ProfileReview
{
    public ModProfile Profile { get; }
    public IReadOnlyList<ProfileReviewRow> Rows { get; }
    internal string Root { get; }
    internal string Before { get; }
    internal ProfileReview(string root, ModProfile profile, List<ProfileReviewRow> rows, string before)
    { Root = root; Profile = profile; Rows = rows.AsReadOnly(); Before = before; }
}

public partial class Library
{
    private string ProfilesFile => Path.Combine(Root, ".mod-profiles.json");
    private string ProfileSwitchJournal => Path.Combine(Root, ".profile-switch.json");
    private record SelectionJournal(List<ProfileSelection> Before, string? Order);

    public List<ModProfile> Profiles()
    {
        var profiles = File.Exists(ProfilesFile) ? JsonSerializer.Deserialize<List<ModProfile>>(File.ReadAllText(ProfilesFile))
            ?? throw new InvalidDataException("Invalid saved mod profiles.") : [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name)
                || profile.Mods == null || !ids.Add(profile.Id) || !names.Add(profile.Name))
                throw new InvalidDataException("Invalid or duplicate saved mod profile.");
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in profile.Mods)
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || !folders.Add(mod.Folder))
                    throw new InvalidDataException("Invalid or duplicate profile mod reference.");
                ProfileFolder(mod.Folder);
            }
        }
        return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ModProfile SaveProfile(string name, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new InvalidDataException("Enter a profile name (1–80 characters).");
        name = name.Trim(); var profiles = Profiles();
        var existing = profiles.SingleOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null && !overwrite) throw new InvalidOperationException("A profile with this name exists. Confirm replacing its saved selection.");
        var saved = new ModProfile(existing?.Id ?? Guid.NewGuid().ToString("N"), name, Snapshot(Mods()).AsReadOnly());
        if (existing != null) profiles.Remove(existing);
        profiles.Add(saved); Storage.Save(ProfilesFile, profiles); return saved;
    }

    private static List<ProfileMod> Snapshot(IEnumerable<Mod> mods) => mods.Select(m =>
        new ProfileMod(Path.GetFileName(m.Folder), m.Info.Id, m.Info.Name, m.Info.Version.ToString(), m.Enabled) { OptionalPaths = m.OptionalSelections() }).ToList();

    public ProfileReview ReviewProfile(string id)
    {
        var profile = Profiles().SingleOrDefault(p => p.Id == id) ?? throw new InvalidDataException("Choose an existing mod profile.");
        var current = Mods(); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ProfileReviewRow>();
        foreach (var saved in profile.Mods)
        {
            var match = current.SingleOrDefault(m => Path.GetFileName(m.Folder).Equals(saved.Folder, StringComparison.OrdinalIgnoreCase));
            // A reimport has a new folder; resolve the same ID only when unambiguous.
            var matches = current.Where(m => m.Info.Id == saved.Id).ToList();
            if (match == null && matches.Count == 1) match = matches[0];
            if (match == null || !used.Add(match.Folder))
            {
                rows.Add(new(saved.Name, null, null, saved.Enabled, saved.Version, "Missing", "Missing or ambiguous library entry; omitted from the selection."));
                continue;
            }
            var notes = new List<string>();
            if (match.Info.Id != saved.Id) notes.Add($"Mod ID changed: {saved.Id} → {match.Info.Id}.");
            if (match.Info.Version.ToString() != saved.Version) notes.Add("Version changed since this profile was saved.");
            if (!Path.GetFileName(match.Folder).Equals(saved.Folder, StringComparison.OrdinalIgnoreCase)) notes.Add("Resolved reimport by mod ID.");
            if (match.Info.OptionalPaths.Count > 0)
            {
                var options = saved.OptionalPaths ?? new Dictionary<string, bool>();
                notes.Add("Optional components: " + string.Join(", ", match.Info.OptionalPaths.Select(o => o.Name + " " + (options.GetValueOrDefault(o.Id, o.DefaultEnabled) ? "on" : "off"))) + ".");
            }
            rows.Add(new(match.Info.Name, match.Folder, used.Count, saved.Enabled, saved.Version, match.Info.Version.ToString(), string.Join(" ", notes)) { OptionalPaths = saved.OptionalPaths });
        }
        int nextOrder = used.Count + 1;
        foreach (var mod in current.Where(m => !used.Contains(m.Folder)))
            rows.Add(new(mod.Info.Name, mod.Folder, nextOrder++,
                false, "—", mod.Info.Version.ToString(), "Not saved in this profile; will be disabled."));
        return new(Root, profile, rows, JsonSerializer.Serialize(Snapshot(current)));
    }

    public void SwitchProfile(ProfileReview reviewed)
    {
        var current = Mods();
        if (!Root.Equals(reviewed.Root, StringComparison.OrdinalIgnoreCase)
            || JsonSerializer.Serialize(Snapshot(current)) != reviewed.Before
            || JsonSerializer.Serialize(Profiles().SingleOrDefault(p => p.Id == reviewed.Profile.Id)) != JsonSerializer.Serialize(reviewed.Profile))
            throw new InvalidOperationException("The library or saved profile changed after review. Preview the profile again.");
        var selection = reviewed.Rows.Where(r => r.Folder != null).ToList();
        foreach (var mod in current) CheckSelectionPath(Path.GetFileName(mod.Folder));
        Storage.Save(ProfileSwitchJournal, new SelectionJournal(current.Select(m => new ProfileSelection(Path.GetFileName(m.Folder), m.Enabled) { OptionalPaths = m.OptionalSelections() }).ToList(),
            File.Exists(OrderFile) ? File.ReadAllText(OrderFile) : null));
        try
        {
            foreach (var row in selection)
            {
                var mod = current.Single(m => m.Folder.Equals(row.Folder, StringComparison.OrdinalIgnoreCase));
                if (mod.Enabled != row.Enabled) mod.SetEnabled(row.Enabled);
                mod.SetOptionalSelections(row.OptionalPaths ?? new Dictionary<string, bool>());
            }
            SetOrder(selection.Select(r => current.Single(m => m.Folder.Equals(r.Folder, StringComparison.OrdinalIgnoreCase))));
            File.Delete(ProfileSwitchJournal);
        }
        catch { RecoverProfileSwitch(); throw; }
    }

    private string ProfileFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith('.') || Path.GetFileName(folder) != folder)
            throw new InvalidDataException("Invalid library folder in mod profile.");
        return SafePaths.Under(Root, folder);
    }
    private void CheckSelectionPath(string folder)
    {
        SafePaths.RejectLinks(Root); string path = ProfileFolder(folder);
        if (Directory.Exists(path)) SafePaths.RejectLinks(path);
        string marker = Path.Combine(path, Mod.MOD_ENABLED_NAME);
        if (File.Exists(marker) || Directory.Exists(marker)) SafePaths.RejectLinks(marker);
        if (Directory.Exists(marker)) throw new InvalidDataException("A directory occupies the enabled marker.");
    }
    private void RecoverProfileSwitch()
    {
        if (!File.Exists(ProfileSwitchJournal)) return;
        var journal = JsonSerializer.Deserialize<SelectionJournal>(File.ReadAllText(ProfileSwitchJournal))
            ?? throw new InvalidDataException("Invalid profile switch journal.");
        if (journal.Before == null || journal.Before.Any(p => p == null)
            || journal.Before.Select(p => p.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Before.Count)
            throw new InvalidDataException("Invalid profile switch recovery selection.");
        foreach (var saved in journal.Before) CheckSelectionPath(saved.Folder);
        foreach (var saved in journal.Before)
        {
            string folder = ProfileFolder(saved.Folder);
            if (!Directory.Exists(folder)) continue;
            new Mod(folder).SetOptionalSelections(saved.OptionalPaths ?? new Dictionary<string, bool>());
            string marker = Path.Combine(folder, Mod.MOD_ENABLED_NAME);
            if (saved.Enabled) { if (!File.Exists(marker)) File.WriteAllText(marker, ""); }
            else if (File.Exists(marker)) File.Delete(marker);
        }
        if (journal.Order == null) { if (File.Exists(OrderFile)) File.Delete(OrderFile); }
        else if (!File.Exists(OrderFile) || File.ReadAllText(OrderFile) != journal.Order)
        {
            string temp = OrderFile + ".profile-recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, journal.Order); File.Move(temp, OrderFile, true);
        }
        File.Delete(ProfileSwitchJournal);
    }
}
