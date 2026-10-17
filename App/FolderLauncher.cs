using System.Diagnostics;

namespace SkullgirlsModManager.Desktop;

internal static class FolderLauncher
{
    public static ProcessStartInfo Command(string folder)
    {
        string full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"The folder no longer exists: {full}");
        var command = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
        command.ArgumentList.Add(full);
        return command;
    }
    public static void Open(string folder)
    {
        using var process = Process.Start(Command(folder));
    }
}
