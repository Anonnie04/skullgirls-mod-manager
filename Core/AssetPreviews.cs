using System.Buffers.Binary;
using System.Text;

namespace SkullgirlsModManager;

public record DecodedImage(int Width, int Height, int[] Pixels, string Format);
public record DecodedAudio(int SampleRate, int Channels, short[] Samples, string Format)
{
    public double Duration => Samples.Length / (double)(SampleRate * Channels);
    public byte[] Wave()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + Samples.Length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)Channels); writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * 2); writer.Write((short)(Channels * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(Samples.Length * 2);
        foreach (short sample in Samples) writer.Write(sample);
        return stream.ToArray();
    }
}

public static class AssetPreviews
{
    public static string Kind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".dds" or ".pcx" or ".png" or ".jpg" or ".jpeg" or ".bmp" => "Image",
        ".wav" => "Audio",
        ".ini" or ".txt" or ".lua" or ".json" or ".fx" or ".glsl" or ".xml" or ".csv" => "Text",
        _ => "Binary"
    };
    // Matches the sibling Workshop's decoder.NAMES, art banks and legacy temp aliases.
    // sprites.gfs supplies its palettes; it is NOT an archive of Workshop-only assets.
    private static readonly HashSet<string> WorkshopCharacters = new(StringComparer.OrdinalIgnoreCase)
    {
        "annie", "beowulf", "bigband", "blackdahlia", "bossmarie", "cerebella", "double", "eliza", "filia", "fukua",
        "marie", "msfortune", "painwheel", "parasoul", "peacock", "robofortune", "squigly", "umbrella", "valentine"
    };
    public static bool WorkshopAsset(string archive, string path)
    {
        string extension = Path.GetFileName(archive).ToLowerInvariant() switch
        {
            "characters-art.gfs" => ".art",
            "characters-art-pt.gfs" => ".art-pt",
            "characters-foits.gfs" => ".foit",
            _ => ""
        };
        if (extension.Length == 0) return false;
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        if (slash < 0) return false;
        string folder = normalized[..slash];
        if (!folder.Equals("characters", StringComparison.OrdinalIgnoreCase) && !folder.Equals("temp", StringComparison.OrdinalIgnoreCase)) return false;
        string file = normalized[(slash + 1)..];
        if (!file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;
        string character = file[..^extension.Length];
        if (character.Equals("boss_marie", StringComparison.OrdinalIgnoreCase)) character = "bossmarie";
        return WorkshopCharacters.Contains(character);
    }

    public static byte[] Read(ArchiveIndex archive, ArchiveEntry entry, int limit)
    {
        if (limit < 0 || limit > 128 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(limit));
        using var stream = File.OpenRead(archive.FilePath);
        if (entry.Offset < 0 || entry.Size < 0 || entry.Offset > stream.Length || entry.Size > stream.Length - entry.Offset)
            throw new InvalidDataException("The archive changed. Reopen it to refresh its index.");
        stream.Position = entry.Offset;
        byte[] data = new byte[(int)Math.Min(entry.Size, limit)];
        stream.ReadExactly(data);
        return data;
    }

    public static string Text(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\0", "·");
    }
    public static string Hex(byte[] data)
    {
        var text = new StringBuilder();
        for (int offset = 0; offset < data.Length; offset += 16)
        {
            var row = data.AsSpan(offset, Math.Min(16, data.Length - offset));
            text.Append(offset.ToString("X8")).Append("  ");
            foreach (byte b in row) text.Append(b.ToString("X2")).Append(' ');
            text.Append(' ', (16 - row.Length) * 3).Append(" | ");
            foreach (byte b in row) text.Append(b >= 32 && b < 127 ? (char)b : '.');
            text.AppendLine();
        }
        return text.ToString();
    }

    private static int U16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset, 2));
    private static int I32(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
    private static int S16(byte[] b, int offset) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(offset, 2));
    private static void Dimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > 32_000_000)
            throw new InvalidDataException("Image exceeds the 32 megapixel preview limit or has invalid dimensions.");
    }

    public static DecodedImage Dds(byte[] bytes)
    {
        if (bytes.Length < 128 || !bytes.AsSpan(0, 4).SequenceEqual("DDS "u8) || I32(bytes, 4) != 124)
            throw new InvalidDataException("Invalid DDS header.");
        int width = I32(bytes, 16), height = I32(bytes, 12);
        Dimensions(width, height);
        string format = Encoding.ASCII.GetString(bytes, 84, 4);
        var pixels = new int[width * height];
        if (format is not ("DXT1" or "DXT3" or "DXT5"))
        {
            int bits = I32(bytes, 88), flags = I32(bytes, 80);
            if ((flags & 0x40) == 0 || bits is not (16 or 24 or 32)) throw new NotSupportedException($"DDS format {format.Trim('\0')} is not supported by this preview.");
            int stride = (I32(bytes, 8) & 8) != 0 ? I32(bytes, 20) : width * (bits / 8);
            if (stride < width * (bits / 8) || 128L + (long)stride * height > bytes.Length) throw new InvalidDataException("Truncated DDS pixels.");
            uint rMask = (uint)I32(bytes, 92), gMask = (uint)I32(bytes, 96), bMask = (uint)I32(bytes, 100), aMask = (uint)I32(bytes, 104);
            static int Channel(uint value, uint mask, int fallback)
            {
                if (mask == 0) return fallback;
                int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
                uint max = mask >> shift;
                return (int)(((value & mask) >> shift) * 255UL / max);
            }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int p = 128 + y * stride + x * (bits / 8);
                uint value = bits == 16 ? (uint)U16(bytes, p) : bits == 32 ? (uint)I32(bytes, p) : (uint)(bytes[p] | bytes[p + 1] << 8 | bytes[p + 2] << 16);
                pixels[y * width + x] = Channel(value, aMask, 255) << 24 | Channel(value, rMask, 0) << 16 | Channel(value, gMask, 0) << 8 | Channel(value, bMask, 0);
            }
            return new(width, height, pixels, $"DDS RGB{bits}");
        }
        int blockSize = format == "DXT1" ? 8 : 16, position = 128;
        if (128L + (long)((width + 3) / 4) * ((height + 3) / 4) * blockSize > bytes.Length) throw new InvalidDataException("Truncated DDS blocks.");
        var colors = new int[4];
        var alpha = new int[8];
        static int Color565(int value) => ((value >> 11) * 255 / 31) << 16 | (((value >> 5) & 63) * 255 / 63) << 8 | (value & 31) * 255 / 31;
        static int Mix(int a, int b, int n, int d)
        {
            int r = (((a >> 16) & 255) * n + ((b >> 16) & 255) * (d - n)) / d;
            int g = (((a >> 8) & 255) * n + ((b >> 8) & 255) * (d - n)) / d;
            int blue = ((a & 255) * n + (b & 255) * (d - n)) / d;
            return r << 16 | g << 8 | blue;
        }
        for (int by = 0; by < height; by += 4) for (int bx = 0; bx < width; bx += 4)
        {
            int colorPosition = position + (format == "DXT1" ? 0 : 8);
            int c0 = U16(bytes, colorPosition), c1 = U16(bytes, colorPosition + 2);
            colors[0] = Color565(c0); colors[1] = Color565(c1);
            bool transparent = format == "DXT1" && c0 <= c1;
            colors[2] = transparent ? Mix(colors[0], colors[1], 1, 2) : Mix(colors[0], colors[1], 2, 3);
            colors[3] = transparent ? 0 : Mix(colors[0], colors[1], 1, 3);
            uint indices = (uint)I32(bytes, colorPosition + 4);
            ulong alphaBits = 0;
            if (format == "DXT5")
            {
                alpha[0] = bytes[position]; alpha[1] = bytes[position + 1];
                if (alpha[0] > alpha[1]) for (int i = 2; i < 8; i++) alpha[i] = ((8 - i) * alpha[0] + (i - 1) * alpha[1]) / 7;
                else
                {
                    for (int i = 2; i < 6; i++) alpha[i] = ((6 - i) * alpha[0] + (i - 1) * alpha[1]) / 5;
                    alpha[6] = 0; alpha[7] = 255;
                }
                for (int i = 0; i < 6; i++) alphaBits |= (ulong)bytes[position + 2 + i] << (i * 8);
            }
            else if (format == "DXT3") alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position, 8));
            for (int p = 0; p < 16; p++)
            {
                int x = bx + p % 4, y = by + p / 4, index = (int)((indices >> (p * 2)) & 3);
                int opacity = format == "DXT5" ? alpha[(int)((alphaBits >> (p * 3)) & 7)] : format == "DXT3" ? (int)((alphaBits >> (p * 4)) & 15) * 17 : transparent && index == 3 ? 0 : 255;
                if (x < width && y < height) pixels[y * width + x] = opacity << 24 | colors[index];
            }
            position += blockSize;
        }
        return new(width, height, pixels, "DDS " + format + " · first mip level");
    }

    public static DecodedImage Pcx(byte[] bytes)
    {
        if (bytes.Length < 128 || bytes[0] != 10 || bytes[2] != 1 || bytes[3] != 8) throw new NotSupportedException("Only 8-bit RLE PCX images are previewed.");
        int width = U16(bytes, 8) - U16(bytes, 4) + 1, height = U16(bytes, 10) - U16(bytes, 6) + 1;
        Dimensions(width, height);
        int planes = bytes[65], stride = U16(bytes, 66);
        if (planes is not (1 or 3) || stride < width) throw new NotSupportedException("Unsupported PCX planes or stride.");
        int palette = bytes.Length - 768;
        if (planes == 1 && (palette < 129 || bytes[palette - 1] != 12)) throw new InvalidDataException("PCX palette missing.");
        int end = planes == 1 ? palette - 1 : bytes.Length;
        int position = 128, run = 0, value = 0;
        var row = new byte[stride * planes]; var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < row.Length; x++)
            {
                if (run == 0)
                {
                    if (position >= end) throw new InvalidDataException("Truncated PCX pixels.");
                    int token = bytes[position++]; run = (token & 192) == 192 ? token & 63 : 1;
                    if (run == 0 || ((token & 192) == 192 && position >= end)) throw new InvalidDataException("Invalid PCX run.");
                    value = (token & 192) == 192 ? bytes[position++] : token;
                }
                row[x] = (byte)value; run--;
            }
            for (int x = 0; x < width; x++)
            {
                int p = planes == 1 ? palette + row[x] * 3 : 0;
                int r = planes == 1 ? bytes[p] : row[x], g = planes == 1 ? bytes[p + 1] : row[stride + x], b = planes == 1 ? bytes[p + 2] : row[stride * 2 + x];
                pixels[y * width + x] = unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
            }
        }
        return new(width, height, pixels, planes == 1 ? "PCX · indexed palette" : "PCX · RGB");
    }

    public static DecodedAudio Wav(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("Invalid WAV header.");
        int fmt = -1, fmtSize = 0, data = -1, dataSize = 0, fact = 0;
        for (long p = 12; p + 8 <= bytes.Length;)
        {
            int size = I32(bytes, (int)p + 4); long next = p + 8 + size;
            if (size < 0 || next > bytes.Length) throw new InvalidDataException("Truncated WAV chunk.");
            string id = Encoding.ASCII.GetString(bytes, (int)p, 4);
            if (id == "fmt ") { fmt = (int)p + 8; fmtSize = size; }
            if (id == "data") { data = (int)p + 8; dataSize = size; }
            if (id == "fact" && size >= 4) fact = I32(bytes, (int)p + 8);
            p = next + (size & 1);
        }
        if (fmt < 0 || fmtSize < 16 || data < 0) throw new InvalidDataException("WAV format or data chunk missing.");
        int format = U16(bytes, fmt), channels = U16(bytes, fmt + 2), rate = I32(bytes, fmt + 4), block = U16(bytes, fmt + 12), bits = U16(bytes, fmt + 14);
        if (channels is not (1 or 2) || rate < 1000 || rate > 192000 || block <= 0) throw new NotSupportedException("Unsupported WAV channels, sample rate or block size.");
        long maxSamples = Math.Min(32_000_000, (long)rate * channels * 600);
        if (format == 1 && bits is 8 or 16)
        {
            int count = dataSize / (bits / 8);
            if (count > maxSamples) throw new InvalidDataException("Audio exceeds the 10 minute / 32 million sample preview limit.");
            var samples = new short[count];
            for (int i = 0; i < count; i++) samples[i] = bits == 16 ? (short)S16(bytes, data + i * 2) : (short)((bytes[data + i] - 128) << 8);
            return new(rate, channels, samples, $"WAV PCM {bits}-bit");
        }
        if (format != 2 || bits != 4 || fmtSize < 22) throw new NotSupportedException($"WAV codec {format} is not supported; PCM and Microsoft ADPCM are previewed.");
        int samplesPerBlock = U16(bytes, fmt + 18), coefficients = U16(bytes, fmt + 20);
        if (coefficients < 1 || coefficients > 256 || fmtSize < 22 + coefficients * 4 || block < channels * 7 || samplesPerBlock < 2
            || samplesPerBlock > 2 + (block - channels * 7) * 2 / channels) throw new InvalidDataException("Invalid Microsoft ADPCM format.");
        long capacity = ((dataSize + (long)block - 1) / block) * samplesPerBlock * channels;
        if (capacity > maxSamples) throw new InvalidDataException("Audio exceeds the 10 minute / 32 million sample preview limit.");
        var output = new List<short>((int)capacity);
        int[] adaptation = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
        var predictor = new int[channels]; var delta = new int[channels]; var s1 = new int[channels]; var s2 = new int[channels];
        for (int start = data; start < data + dataSize; start += block)
        {
            int end = Math.Min(start + block, data + dataSize), p = start;
            if (end - start < channels * 7) throw new InvalidDataException("Truncated ADPCM block header.");
            for (int c = 0; c < channels; c++) { predictor[c] = bytes[p++]; if (predictor[c] >= coefficients) throw new InvalidDataException("Invalid ADPCM predictor."); }
            for (int c = 0; c < channels; c++) { delta[c] = Math.Max(16, U16(bytes, p)); p += 2; }
            for (int c = 0; c < channels; c++) { s1[c] = S16(bytes, p); p += 2; }
            for (int c = 0; c < channels; c++) { s2[c] = S16(bytes, p); p += 2; output.Add((short)s2[c]); }
            for (int c = 0; c < channels; c++) output.Add((short)s1[c]);
            int emitted = 2 * channels;
            while (p < end && emitted < samplesPerBlock * channels)
            {
                int value = bytes[p++];
                for (int half = 0; half < 2 && emitted < samplesPerBlock * channels; half++)
                {
                    int c = channels == 2 ? half : 0, nibble = half == 0 ? value >> 4 : value & 15, signed = nibble >= 8 ? nibble - 16 : nibble;
                    int coef = fmt + 22 + predictor[c] * 4;
                    long predicted = ((long)s1[c] * S16(bytes, coef) + (long)s2[c] * S16(bytes, coef + 2)) / 256 + (long)signed * delta[c];
                    int sample = (int)Math.Clamp(predicted, short.MinValue, short.MaxValue);
                    s2[c] = s1[c]; s1[c] = sample; output.Add((short)sample); emitted++;
                    delta[c] = (int)Math.Clamp((long)adaptation[nibble] * delta[c] / 256, 16, int.MaxValue);
                }
            }
        }
        if (fact > 0 && (long)fact * channels < output.Count) output.RemoveRange(fact * channels, output.Count - fact * channels);
        return new(rate, channels, output.ToArray(), "WAV Microsoft ADPCM → PCM16");
    }
}
