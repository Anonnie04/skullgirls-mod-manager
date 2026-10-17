using System.Text.RegularExpressions;

namespace SkullgirlsModManager;

public static class AssetCharacters
{
    private static readonly (string Name, string[] Aliases)[] Roster =
    [
        ("Annie", ["annie"]), ("Beowulf", ["beowulf"]), ("Big Band", ["bigband", "big_band"]),
        ("Black Dahlia", ["blackdahlia", "black_dahlia", "dahlia"]), ("Boss Marie", ["bossmarie", "boss_marie"]),
        ("Cerebella", ["cerebella"]), ("Double", ["double"]), ("Eliza", ["eliza"]), ("Filia", ["filia"]),
        ("Fukua", ["fukua"]), ("Marie", ["marie"]), ("Ms. Fortune", ["msfortune", "ms_fortune"]),
        ("Painwheel", ["painwheel"]), ("Parasoul", ["parasoul"]), ("Peacock", ["peacock"]),
        ("Robo-Fortune", ["robofortune", "robo_fortune"]), ("Squigly", ["squigly"]),
        ("Umbrella", ["umbrella"]), ("Valentine", ["valentine"])
    ];
    private static readonly (string Name, Regex Pattern)[] Patterns = Roster.Select(c =>
        (c.Name, new Regex(@"(?:^|[/_\-. ])(?:" + string.Join("|", c.Aliases.Select(Regex.Escape)) + @")(?=$|[/_\-. \d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))).ToArray();

    public static IReadOnlyList<string> Names(string path)
    {
        string normalized = path.Replace('\\', '/');
        // Boss Marie is a separate bank from playable Marie.
        normalized = Regex.Replace(normalized, "boss[_-]?marie", "bossmarie", RegexOptions.IgnoreCase);
        var names = Patterns.Where(c => c.Pattern.IsMatch(normalized)).Select(c => c.Name).ToList();
        if (names.Count == 0 && Path.GetFileNameWithoutExtension(normalized).StartsWith("anniestarpower", StringComparison.OrdinalIgnoreCase)) names.Add("Annie");
        return names;
    }

    public static string NaturalKey(string path) => Regex.Replace(path, @"\d+", m => m.Value.PadLeft(20, '0'));
}
