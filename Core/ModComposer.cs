using System.Buffers.Binary;
using System.Text;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using ThemModdingHerds.VelvetBeautifier.Modding;
using ThemModdingHerds.VelvetBeautifier.Utilities;

namespace SkullgirlsModManager;

public record TextureTarget(int Width, int Height, string FourCc, int MipLevels)
{
    public static TextureTarget Read(byte[] header)
    {
        if (header.Length < 128 || !header.AsSpan(0, 4).SequenceEqual("DDS "u8)) throw new InvalidDataException("Invalid DDS texture.");
        int I(int offset) => BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset, 4));
        int width = I(16), height = I(12), mips = Math.Max(1, I(28));
        if (width <= 0 || height <= 0 || (long)width * height > 32_000_000 || mips > 16) throw new InvalidDataException("Texture exceeds the editor limits.");
        return new(width, height, Encoding.ASCII.GetString(header, 84, 4), mips);
    }
}

public static class TextureEncoding
{
    public static byte[] EncodeLike(DecodedImage image, byte[] originalHeader, bool fast = false)
    {
        var target = TextureTarget.Read(originalHeader);
        if (target.FourCc is "DXT1" or "DXT3" or "DXT5") return Encode(image, target, fast);
        int I(int offset) => BinaryPrimitives.ReadInt32LittleEndian(originalHeader.AsSpan(offset, 4));
        int bits = I(88), flags = I(80);
        if ((flags & 64) == 0 || bits is not (16 or 24 or 32) || I(112) != 0)
            throw new NotSupportedException("PNG conversion supports DXT1/3/5 and RGB16/24/32 2D textures.");
        if (image.Width != target.Width || image.Height != target.Height || image.Pixels.Length != image.Width * image.Height)
            throw new InvalidDataException($"Replacement image must be {target.Width} × {target.Height} pixels.");
        var header = originalHeader[..128].ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), (I(8) | 8) & ~0x80000);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20, 4), target.Width * (bits / 8));
        using var output = new MemoryStream(); output.Write(header);
        using var writer = new BinaryWriter(output);
        uint[] masks = [(uint)I(92), (uint)I(96), (uint)I(100), (uint)I(104)];
        int width = image.Width, height = image.Height; int[] pixels = image.Pixels;
        static uint Pack(int channel, uint mask)
        {
            if (mask == 0) return 0;
            int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
            return (uint)(((ulong)channel * (mask >> shift) + 127) / 255 << shift) & mask;
        }
        for (int level = 0; level < target.MipLevels; level++)
        {
            foreach (int pixel in pixels)
            {
                uint value = Pack((pixel >> 16) & 255, masks[0]) | Pack((pixel >> 8) & 255, masks[1]) | Pack(pixel & 255, masks[2]) | Pack((pixel >> 24) & 255, masks[3]);
                writer.Write((byte)value); writer.Write((byte)(value >> 8));
                if (bits >= 24) writer.Write((byte)(value >> 16));
                if (bits == 32) writer.Write((byte)(value >> 24));
            }
            if (level + 1 >= target.MipLevels) break;
            int nextWidth = Math.Max(1, width / 2), nextHeight = Math.Max(1, height / 2); var next = new int[nextWidth * nextHeight];
            for (int y = 0; y < nextHeight; y++) for (int x = 0; x < nextWidth; x++)
            {
                int a = 0, r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                { int p = pixels[Math.Min(height - 1, y * 2 + dy) * width + Math.Min(width - 1, x * 2 + dx)]; a += (p >> 24) & 255; r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255; }
                next[y * nextWidth + x] = (a / 4) << 24 | (r / 4) << 16 | (g / 4) << 8 | b / 4;
            }
            pixels = next; width = nextWidth; height = nextHeight;
        }
        return output.ToArray();
    }

    public static byte[] Encode(DecodedImage image, TextureTarget target, bool fast = false)
    {
        if (image.Width != target.Width || image.Height != target.Height || image.Pixels.Length != image.Width * image.Height)
            throw new InvalidDataException($"Replacement image must be {target.Width} × {target.Height} pixels.");
        var format = target.FourCc switch { "DXT1" => CompressionFormat.Bc1WithAlpha, "DXT3" => CompressionFormat.Bc2, "DXT5" => CompressionFormat.Bc3,
            _ => throw new NotSupportedException("Automatic image conversion supports DXT1, DXT3 and DXT5 targets. Drop a matching DDS for other formats.") };
        byte[] bgra = new byte[image.Pixels.Length * 4];
        Buffer.BlockCopy(image.Pixels, 0, bgra, 0, bgra.Length);
        var encoder = new BcEncoder();
        encoder.OutputOptions.Format = format;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;
        encoder.OutputOptions.Quality = fast ? CompressionQuality.Fast : CompressionQuality.Balanced;
        encoder.OutputOptions.GenerateMipMaps = target.MipLevels > 1;
        encoder.OutputOptions.MaxMipMapLevel = target.MipLevels;
        encoder.OutputOptions.DdsPreferDxt10Header = false;
        using var output = new MemoryStream();
        encoder.EncodeToStream(bgra, image.Width, image.Height, BCnEncoder.Encoder.PixelFormat.Bgra32, output);
        byte[] result = output.ToArray();
        if (TextureTarget.Read(result) != target) throw new InvalidDataException("Converted texture does not match the original format or mip count.");
        return result;
    }
}

public record StagedAsset(Mod Mod, string Destination, string? PreviousCopy, bool CreatedMod);

public partial class Library
{
    public Mod CreateDraft(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw new InvalidDataException("Enter a mod name (1–120 characters).");
        string temporary = Path.Combine(Root, ".draft-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);
            new ModInfo { Id = "local." + Guid.NewGuid().ToString("N"), Name = name.Trim(), Description = "Created from asset replacements", Version = new Version(1, 0) }.Write(Path.Combine(temporary, Mod.MODINFO_NAME));
            string final = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.Move(temporary, final);
            return new Mod(final);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    public static void ValidateReplacement(ArchiveIndex archive, ArchiveEntry asset, string input)
    {
        if (!archive.Entries.Contains(asset)) throw new InvalidDataException("Select an asset from this archive first.");
        if (AssetPreviews.WorkshopAsset(archive.FilePath, asset.Path)) throw new InvalidOperationException("Use the separate Sprite Workshop for packed fighting-character frames.");
        SafePaths.RejectLinks(input);
        if (new FileInfo(input).Length > 512L * 1024 * 1024) throw new InvalidDataException("The replacement exceeds the 512 MB staging limit.");
        string extension = Path.GetExtension(asset.Path).ToLowerInvariant();
        if (!Path.GetExtension(input).Equals(extension, StringComparison.OrdinalIgnoreCase)
            && !(AssetPreviews.Kind(asset.Path) == "Text" && AssetPreviews.Kind(input) == "Text"))
            throw new InvalidDataException($"This asset needs a {extension} file. Images can be converted when dropped onto a DDS asset in the preview window.");
        if (extension == ".dds")
        {
            if (new FileInfo(input).Length > 64 * 1024 * 1024) throw new InvalidDataException("DDS replacement exceeds the 64 MB limit.");
            byte[] bytes = File.ReadAllBytes(input);
            var expected = TextureTarget.Read(AssetPreviews.Read(archive, asset, 128));
            var incoming = TextureTarget.Read(bytes);
            if (incoming != expected) throw new InvalidDataException($"DDS must match {expected.Width} × {expected.Height}, {expected.FourCc}, {expected.MipLevels} mip level(s).");
            AssetPreviews.Dds(bytes);
            int width = incoming.Width, height = incoming.Height; long size = 128;
            if (incoming.FourCc is "DXT1" or "DXT3" or "DXT5")
            {
                for (int n = 0; n < incoming.MipLevels; n++) { size += (long)((width + 3) / 4) * ((height + 3) / 4) * (incoming.FourCc == "DXT1" ? 8 : 16); width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); }
                if (bytes.Length < size) throw new InvalidDataException("DDS replacement is missing mipmap data.");
            }
        }
        if (extension == ".wav")
        {
            if (new FileInfo(input).Length > 128 * 1024 * 1024 || asset.Size > 128 * 1024 * 1024) throw new InvalidDataException("WAV exceeds the 128 MB validation limit.");
            var original = AssetPreviews.Wav(AssetPreviews.Read(archive, asset, 128 * 1024 * 1024));
            var replacement = AssetPreviews.Wav(File.ReadAllBytes(input));
            if (replacement.SampleRate != original.SampleRate || replacement.Channels != original.Channels)
                throw new InvalidDataException($"WAV must use {original.SampleRate:N0} Hz and {original.Channels} channel(s). Duration may differ.");
        }
        if (extension == ".pcx")
        {
            if (asset.Size > 64 * 1024 * 1024 || new FileInfo(input).Length > 64 * 1024 * 1024) throw new InvalidDataException("PCX exceeds the validation limit.");
            var original = AssetPreviews.Pcx(AssetPreviews.Read(archive, asset, 64 * 1024 * 1024)); var replacement = AssetPreviews.Pcx(File.ReadAllBytes(input));
            if (original.Width != replacement.Width || original.Height != replacement.Height) throw new InvalidDataException($"PCX must be {original.Width} × {original.Height} pixels.");
        }
    }

    public StagedAsset StageAsset(ArchiveIndex archive, ArchiveEntry asset, string input, Mod? existing, string newName)
    {
        using var inputGuard = File.OpenRead(input);
        ValidateReplacement(archive, asset, input);
        string archiveName = Path.GetFileName(archive.FilePath);
        if (!archiveName.EndsWith(".gfs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select a GFS archive.");
        string? draft = null, temp = null, previous = null;
        bool created = existing == null;
        Mod mod;
        if (existing == null)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName.Trim().Length > 120) throw new InvalidDataException("Enter a mod name (1–120 characters).");
            draft = Path.Combine(Root, ".draft-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(draft);
            new ModInfo { Id = "local." + Guid.NewGuid().ToString("N"), Name = newName.Trim(), Description = "Created from asset replacements", Version = new Version(1, 0) }.Write(Path.Combine(draft, Mod.MODINFO_NAME));
            mod = new(draft);
        }
        else
        {
            mod = Mods().SingleOrDefault(m => string.Equals(m.Folder, existing.Folder, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Choose a mod from this library.");
        }
        try
        {
            string archiveRoot = SafePaths.Under(mod.Folder, archiveName);
            string nested = SafePaths.Under(mod.Folder, "data01/" + archiveName);
            if (!Directory.Exists(archiveRoot) && Directory.Exists(nested)) archiveRoot = nested;
            string destination = SafePaths.Under(archiveRoot, asset.Path);
            // Check each existing ancestor, including a mod's manually edited directories.
            for (string? path = Path.GetDirectoryName(destination); path != null && path.StartsWith(Root, StringComparison.OrdinalIgnoreCase); path = Path.GetDirectoryName(path))
                if (Directory.Exists(path)) SafePaths.RejectLinks(path);
            if (File.Exists(destination)) SafePaths.RejectLinks(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(input, temp);
            if (Crypto.Checksum(input) != Crypto.Checksum(temp)) throw new IOException("Replacement copy did not verify.");
            if (File.Exists(destination))
            {
                string history = Path.Combine(Path.GetDirectoryName(Root)!, "replacement-history", Path.GetFileName(mod.Folder), Guid.NewGuid().ToString("N"));
                previous = SafePaths.Under(history, archiveName + "/" + asset.Path); Directory.CreateDirectory(Path.GetDirectoryName(previous)!); File.Copy(destination, previous);
            }
            File.Move(temp, destination, true); temp = null;
            if (created)
            {
                string final = Path.Combine(Root, Guid.NewGuid().ToString("N"));
                Directory.Move(draft!, final); draft = null; mod = new(final);
                destination = SafePaths.Under(Path.Combine(final, archiveName), asset.Path);
            }
            return new(mod, destination, previous, created);
        }
        finally
        {
            if (temp != null && File.Exists(temp)) File.Delete(temp);
            if (draft != null && Directory.Exists(draft)) Directory.Delete(draft, true);
        }
    }
}
