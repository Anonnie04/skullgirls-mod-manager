using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ThemModdingHerds.GFS;
using ThemModdingHerds.IO.Binary;

namespace SkullgirlsModManager;

public record ArchiveEntry(string Path, long Size, int Alignment, long Offset);
public record ArchiveIndex(string FilePath, RevergePackageHeader Header, List<ArchiveEntry> Entries);

public static class SafePaths
{
    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.StartsWith('\\'))
            throw new InvalidDataException($"Invalid relative path: {relative}");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(fullRoot, (OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new InvalidDataException($"Path escapes its folder: {relative}");
        return result;
    }

    public static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Linked files and folders are unsupported: {path}");
    }
}

public static class Archives
{
    // Read only the index. Upstream Open() loads every payload into RAM, including multi-GB art packs.
    public static ArchiveIndex Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        int I32() => BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
        long I64() => BinaryPrimitives.ReadInt64BigEndian(reader.ReadBytes(8));
        string Text()
        {
            long count = I64();
            if (count < 0 || count > 32768 || count > stream.Length - stream.Position) throw new InvalidDataException("Invalid GFS string.");
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)count));
        }
        int dataOffset = I32();
        string id = Text(), version = Text();
        long count = I64();
        var header = new RevergePackageHeader(dataOffset, count, id, version);
        if (!header.Verify() || count < 0 || count > 1_000_000 || dataOffset > stream.Length)
            throw new InvalidDataException($"Unsupported GFS header: {Path.GetFileName(path)}");
        var entries = new List<ArchiveEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long position = dataOffset;
        for (long n = 0; n < count; n++)
        {
            string name = Text();
            SafePaths.Under(Path.GetTempPath(), name);
            long size = I64();
            int alignment = I32();
            if (size < 0 || alignment <= 0 || alignment > 1_048_576 || !names.Add(name))
                throw new InvalidDataException("Invalid or duplicate GFS entry.");
            position = checked(position + (alignment - position % alignment) % alignment);
            if (position > stream.Length || size > stream.Length - position) throw new InvalidDataException("GFS payload is truncated.");
            entries.Add(new(name, size, alignment, position));
            position = checked(position + size);
        }
        if (stream.Position > dataOffset) throw new InvalidDataException("GFS file table overlaps payload.");
        return new(path, header, entries);
    }

    public static void Extract(string input, string output)
    {
        var index = Read(input);
        Directory.CreateDirectory(output);
        using var source = File.OpenRead(input);
        foreach (var entry in index.Entries)
        {
            string target = SafePaths.Under(output, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // Refuse overwrites, including pre-existing links in a selected directory.
            using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            source.Position = entry.Offset;
            Copy(source, destination, entry.Size);
        }
    }

    public static void Build(string original, string output, IReadOnlyDictionary<string, string> replacements)
    {
        var index = Read(original);
        var entries = index.Entries.Select(e => new RevergePackageEntry(e.Path,
            replacements.TryGetValue(e.Path, out var p) ? new FileInfo(p).Length : e.Size, e.Alignment)).ToList();
        var names = entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in replacements.Where(p => !names.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            SafePaths.Under(Path.GetTempPath(), pair.Key);
            entries.Add(new(pair.Key, new FileInfo(pair.Value).Length, 1));
        }
        // Preserve upstream models/serialization, but calculate the COMPLETE table before data offsets.
        var header = new RevergePackageHeader(0, entries.Count, index.Header.Identifier, index.Header.Version);
        header.DataOffset = checked(header.DataSize() + entries.Sum(e => e.DataSize()));
        if (entries.Any(e => e.Path.Any(c => c > 127))) throw new InvalidDataException("Non-ASCII GFS paths are unsupported by the upstream writer.");
        using (var writer = new Writer(output))
        {
            writer.Write(header);
            foreach (var entry in entries)
            {
                writer.WritePascal64String(entry.Path);
                writer.Write(entry.Size);
                writer.Write(entry.Alignment);
            }
        }
        using var destination = new FileStream(output, FileMode.Open, FileAccess.Write);
        using var source = File.OpenRead(original);
        var lookup = index.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        long offset = header.DataOffset;
        foreach (var entry in entries)
        {
            offset = checked(offset + (entry.Alignment - offset % entry.Alignment) % entry.Alignment);
            destination.Position = offset;
            if (replacements.TryGetValue(entry.Path, out string? replacement))
            {
                using var input = File.OpenRead(replacement);
                Copy(input, destination, entry.Size);
            }
            else
            {
                source.Position = lookup[entry.Path].Offset;
                Copy(source, destination, entry.Size);
            }
            offset = checked(offset + entry.Size);
        }
        destination.SetLength(offset);
        destination.Flush(true);
    }

    public static void ValidateBuild(string original, string built, IReadOnlyDictionary<string, string> replacements)
    {
        var before = Read(original);
        var after = Read(built);
        var lookup = after.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        using var source = File.OpenRead(original);
        using var target = File.OpenRead(built);
        foreach (var entry in before.Entries)
        {
            if (!lookup.TryGetValue(entry.Path, out var result)) throw new InvalidDataException("Repacked archive lost an entry.");
            if (replacements.ContainsKey(entry.Path)) continue;
            if (entry.Size != result.Size || !Digest(source, entry).SequenceEqual(Digest(target, result)))
                throw new InvalidDataException($"Unchanged payload differs: {entry.Path}");
        }
        foreach (var pair in replacements)
        {
            if (!lookup.TryGetValue(pair.Key, out var result)) throw new InvalidDataException("Replacement missing.");
            using var replacement = File.OpenRead(pair.Value);
            if (replacement.Length != result.Size || !SHA256.HashData(replacement).SequenceEqual(Digest(target, result)))
                throw new InvalidDataException($"Replacement differs: {pair.Key}");
        }
    }

    private static byte[] Digest(Stream stream, ArchiveEntry entry)
    {
        stream.Position = entry.Offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long remaining = entry.Size;
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (read == 0) throw new EndOfStreamException();
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return hash.GetHashAndReset();
    }

    private static void Copy(Stream source, Stream target, long length)
    {
        var buffer = new byte[1024 * 1024];
        while (length > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) throw new EndOfStreamException();
            target.Write(buffer, 0, read);
            length -= read;
        }
    }
}
