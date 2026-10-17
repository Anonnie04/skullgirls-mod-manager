using System.Text.Json;

// Compatibility adapter for Velvet's copied ModInfo model.
namespace ThemModdingHerds.VelvetBeautifier.Utilities;
public static class FileSystem
{
    public static T? ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    public static bool WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
