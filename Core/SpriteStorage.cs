using System.Buffers.Binary;
using System.Text;

namespace SkullgirlsModManager;

public record SpriteStorageRepair(string Asset, string Art, int Before, int After);

public static class SpriteStorage
{
    public const string Receipt = "sprite-storage-repairs.json";

    // VRSN 9 FOITs advertise art memory sizes before their palette-name table.
    // Relocated Workshop exports can retain the old sizes after appending art.
    public static List<SpriteStorageRepair> Repair(Mod mod)
    {
        var result = new List<SpriteStorageRepair>();
        var replacements = mod.Replacements();
        if (!replacements.TryGetValue("characters-foits.gfs", out var metadata)) return result;
        foreach (var asset in metadata.Where(p => p.Key.EndsWith(".foit", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileNameWithoutExtension(asset.Key).Replace("boss_marie", "bossmarie");
            byte[] bytes = File.ReadAllBytes(asset.Value);
            int table = SizeTable(bytes, name);
            if (table < 0) continue;
            bool changed = false;
            foreach (var (archive, suffix, field) in new[] { ("characters-art.gfs", ".art", 2), ("characters-art-pt.gfs", ".art-pt", 3) })
            {
                string path = asset.Key[..^5] + suffix;
                if (!replacements.TryGetValue(archive, out var art) || !art.TryGetValue(path, out string? file)) continue;
                int size = checked((int)new FileInfo(file).Length);
                int offset = table + field * 4;
                int advertised = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
                // Keep larger existing reservations; only grow to cover supplied art.
                if (size <= advertised) continue;
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), size);
                result.Add(new(asset.Key, archive + "/" + path, advertised, size));
                changed = true;
            }
            if (changed) File.WriteAllBytes(asset.Value, bytes);
        }
        if (result.Count > 0) Storage.Save(Path.Combine(mod.Folder, Receipt), result);
        return result;
    }

    public static bool RecognizesMetadata(byte[] bytes, string asset) =>
        SizeTable(bytes, Path.GetFileNameWithoutExtension(asset).Replace("boss_marie", "bossmarie")) >= 0;

    private static int SizeTable(byte[] bytes, string name)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(4, 4).SequenceEqual("VRSN"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)) != 9) return -1;
        byte[] label = Encoding.ASCII.GetBytes(name + "_1p.pal");
        if (label.Length > 255) return -1;
        byte[] marker = [(byte)label.Length, .. label];
        int at = bytes.AsSpan().IndexOf(marker);
        if (at < 34 || bytes.AsSpan(at + marker.Length).IndexOf(marker) >= 0) return -1;
        int table = at - 22;
        for (int i = 0; i < 5; i++)
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(table + i * 4, 4)) < 0) return -1;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at - 2, 2));
        if (count is < 1 or > 256) return -1;
        int cursor = at;
        for (int i = 0; i < count; i++)
        {
            if (cursor >= bytes.Length) return -1;
            int length = bytes[cursor++];
            if (length < 5 || length > bytes.Length - cursor) return -1;
            var value = bytes.AsSpan(cursor, length);
            if (!value.EndsWith(".pal"u8)) return -1;
            foreach (byte ch in value) if (ch is < 32 or > 126) return -1;
            cursor += length;
        }
        return table;
    }
}
