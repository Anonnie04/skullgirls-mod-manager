using System.Security.Cryptography;

namespace SkullgirlsModManager;

// One audited Linux build only. Never scan-and-patch unknown executable versions.
public static class NativeCompatibility
{
    public const string ExecutableName = "Skullgirls.x86_64-pc-linux-gnu";
    public const string OriginalHash = "98C47645B247917A9976E8EDC7EAF4425F245911C5236AB442325CD508077F73";
    public const string PatchedHash = "D3214419D07BF755624A03A2092CE319B5C25E6D93A806BA5B49EADBA0D07EB9";
    public const int PatchOffset = 0x19c3c1;
    public static bool IsNative(string game) => !File.Exists(Path.Combine(game, "SkullGirls.exe")) && File.Exists(Path.Combine(game, ExecutableName));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Backup(string profile) => Path.Combine(profile, "compatibility", ExecutableName + ".original");
    public static bool Installed(string game)
    {
        string file = Path.Combine(game, ExecutableName);
        if (!File.Exists(file)) return false;
        SafePaths.RejectLinks(file);
        return Hash(File.ReadAllBytes(file)) == PatchedHash;
    }
    public static byte[] Prepare(byte[] original)
    {
        if (Hash(original) != OriginalHash)
            throw new InvalidDataException("This native Linux game build is not supported. No executable was changed. A game update needs a new audited compatibility patch.");
        // At VA 0x59c3bf: test al,al; jne +0x1d. Make the successful
        // archive-verification branch unconditional; preserve archive reading.
        if (!original.AsSpan(PatchOffset - 2, 4).SequenceEqual(new byte[] { 0x84, 0xc0, 0x75, 0x1d }))
            throw new InvalidDataException("Native verification instruction does not match.");
        byte[] patched = (byte[])original.Clone(); patched[PatchOffset] = 0xeb;
        if (Hash(patched) != PatchedHash) throw new InvalidDataException("Native patch verification failed.");
        return patched;
    }
    public static void Install(string game, string profile)
    {
        if (!IsNative(game)) throw new InvalidOperationException("Select the supported native Linux game.");
        if (ArchiveCompatibility.GameRunning()) throw new InvalidOperationException("Close Skullgirls before setting up native mod support.");
        using var guard = new FileStream(Path.Combine(profile, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(Path.Combine(profile, "transaction.json")) || File.Exists(Path.Combine(profile, "baseline-update.json")))
            throw new InvalidOperationException("Recover the interrupted archive operation first.");
        string target = Path.Combine(game, ExecutableName), backup = Backup(profile);
        SafePaths.RejectLinks(game); SafePaths.RejectLinks(target);
        if (Installed(game))
        {
            if (!File.Exists(backup) || Hash(File.ReadAllBytes(backup)) != OriginalHash)
                throw new InvalidDataException("Native support is installed but its verified original backup is missing.");
            return;
        }
        byte[] original = File.ReadAllBytes(target), patched = Prepare(original);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        SafePaths.RejectLinks(Path.GetDirectoryName(backup)!);
        if (File.Exists(backup))
        {
            SafePaths.RejectLinks(backup);
            if (Hash(File.ReadAllBytes(backup)) != OriginalHash) throw new InvalidDataException("Native executable backup checksum failed.");
        }
        else WriteAtomic(backup, original, null);
        ReplaceChecked(target, original, patched);
    }
    public static void Restore(string game, string profile)
    {
        if (!IsNative(game)) return;
        string currentHash = Hash(File.ReadAllBytes(Path.Combine(game, ExecutableName)));
        if (currentHash == OriginalHash) return;
        if (currentHash != PatchedHash)
        {
            if (File.Exists(Backup(profile))) throw new IOException("The native executable changed outside the manager. It was not overwritten; review the game update before restoring support.");
            return;
        }
        if (ArchiveCompatibility.GameRunning()) throw new InvalidOperationException("Close Skullgirls before restoring native mod support.");
        using var guard = new FileStream(Path.Combine(profile, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string backup = Backup(profile); SafePaths.RejectLinks(backup);
        byte[] original = File.ReadAllBytes(backup);
        if (Hash(original) != OriginalHash) throw new InvalidDataException("Native executable backup checksum failed.");
        string target = Path.Combine(game, ExecutableName);
        ReplaceChecked(target, File.ReadAllBytes(target), original);
    }
    private static void ReplaceChecked(string target, byte[] expected, byte[] replacement)
    {
        SafePaths.RejectLinks(target);
        if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(expected)) throw new IOException("Game executable changed during setup.");
        UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(target);
        WriteAtomic(target, replacement, mode);
    }
    private static void WriteAtomic(string target, byte[] bytes, UnixFileMode? mode)
    {
        string staged = target + ".sgmm-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write)) { output.Write(bytes); output.Flush(true); }
            if (mode.HasValue && !OperatingSystem.IsWindows()) File.SetUnixFileMode(staged, mode.Value);
            if (Hash(File.ReadAllBytes(staged)) != Hash(bytes)) throw new IOException("Native executable staging verification failed.");
            File.Move(staged, target, true);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
