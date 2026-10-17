using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SkullgirlsModManager.Desktop;

internal static class ReplacementPreparation
{
    public static bool ConvertsImage(ArchiveEntry asset, string input) => Path.GetExtension(asset.Path).Equals(".dds", StringComparison.OrdinalIgnoreCase)
        && new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(input).ToLowerInvariant());

    public static (int Width, int Height) ImageSize(string input)
    {
        if (new FileInfo(input).Length > 64 * 1024 * 1024) throw new InvalidDataException("Image exceeds the 64 MB input limit.");
        SafePaths.RejectLinks(input);
        using var image = Image.FromFile(input);
        if ((long)image.Width * image.Height > 32_000_000) throw new InvalidDataException("Image exceeds the 32 megapixel limit.");
        return (image.Width, image.Height);
    }

    public static void ConvertImage(ArchiveIndex archive, ArchiveEntry asset, string input, string output, bool resize)
    {
        var target = TextureTarget.Read(AssetPreviews.Read(archive, asset, 128));
        ImageSize(input);
        using var source = Image.FromFile(input);
        if (!resize && (source.Width != target.Width || source.Height != target.Height))
            throw new InvalidDataException($"Image must be {target.Width} × {target.Height} pixels.");
        using var bitmap = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        }
        int[] pixels = new int[target.Width * target.Height];
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length); } finally { bitmap.UnlockBits(locked); }
        File.WriteAllBytes(output, TextureEncoding.EncodeLike(new(target.Width, target.Height, pixels, "BGRA"), AssetPreviews.Read(archive, asset, 128)));
    }
    public static void SavePng(DecodedImage image, string output)
    {
        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(image.Pixels, 0, locked.Scan0, image.Pixels.Length); } finally { bitmap.UnlockBits(locked); }
        bitmap.Save(output, ImageFormat.Png);
    }
}
