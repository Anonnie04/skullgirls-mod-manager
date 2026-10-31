using System.Security.Cryptography;
using System.Text.Json;
using ThemModdingHerds.VelvetBeautifier.Modding;

namespace SkullgirlsModManager;

public partial class Library
{
    private static string? ConvertStandalone(string staging)
    {
        if (Directory.GetFiles(staging, Mod.MODINFO_NAME, SearchOption.AllDirectories).Length > 0) return null;
        var manifests = Directory.GetFiles(staging, "package.json", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(Path.GetDirectoryName(p)) == "assets"
                && File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(p))!, "assets", "AOTC.lua"))).ToList();
        if (manifests.Count == 0) return null;
        if (manifests.Count != 1) throw new InvalidDataException("Import one standalone package at a time.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifests[0]));
        var package = document.RootElement;
        if (package.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Unsupported AOTC package schema.");
        string source = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(manifests[0]))!, "content");
        string target = Path.Combine(staging, ".converted");
        Directory.CreateDirectory(target);
        var info = new ModInfo { Id = "aotc.restorations", Name = "AOTC", Author = "Ahad of the Curve",
            Description = "Ahad of the Curve restorations", Version = Version.Parse(package.GetProperty("release").GetString()!) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in package.GetProperty("modules").EnumerateArray())
        {
            var paths = new List<string>();
            void Copy(JsonElement file, string relative)
            {
                if (!seen.Add(relative)) throw new InvalidDataException("Duplicate standalone replacement: " + relative);
                string input = SafePaths.Under(source, relative), output = SafePaths.Under(target, relative);
                using (var stream = File.OpenRead(input))
                    if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Standalone replacement checksum failed: " + relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(input, output); paths.Add(relative);
            }
            foreach (var file in module.GetProperty("files").EnumerateArray())
                Copy(file, file.GetProperty("archive").GetString() + ".gfs/" + file.GetProperty("entry").GetString());
            if (module.TryGetProperty("loose", out var loose))
                foreach (var file in loose.EnumerateArray()) Copy(file, "loose/" + file.GetProperty("path").GetString());
            if (module.GetProperty("optional").GetBoolean())
                info.OptionalPaths.Add(new() { Id = module.GetProperty("id").GetString()!, Name = module.GetProperty("title").GetString()!, Paths = paths });
        }
        info.Write(Path.Combine(target, Mod.MODINFO_NAME));
        return target;
    }
}
