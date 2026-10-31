using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SkullgirlsModManager.Desktop;

internal sealed class DecoHeader : Control
{
    private static readonly Pen Gold = new(Brush.Parse("#D8B777"), 1);
    private static readonly Pen Faint = new(Brush.Parse("#4F3F3B"), 1);
    public DecoHeader() { Height = 142; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w = Bounds.Width, y = 124;
        context.DrawLine(Gold, new Point(0, y), new Point(w / 2 - 14, y));
        context.DrawLine(Gold, new Point(w / 2 + 14, y), new Point(w, y));
        context.DrawLine(Faint, new Point(20, y + 5), new Point(w / 2 - 20, y + 5));
        context.DrawLine(Faint, new Point(w / 2 + 20, y + 5), new Point(w - 20, y + 5));
        using (context.PushTransform(Matrix.CreateTranslation(w / 2, y)))
            context.DrawGeometry(null, Gold, Geometry.Parse("M 0,-8 L 8,0 L 0,8 L -8,0 Z"));
        for (int i = 0; i < 7; i++)
        {
            context.DrawLine(Faint, new Point(w - 6, 110), new Point(w - 25 - i * 14, 4));
            context.DrawLine(Faint, new Point(6, 110), new Point(25 + i * 14, 4));
        }
        using (context.PushTransform(Matrix.CreateTranslation(w / 2, 28)))
        {
            context.DrawGeometry(Brush.Parse("#231E25"), Gold,
                Geometry.Parse("M 0,-24 L 35,-4 L 35,18 L 20,18 L 20,24 L -20,24 L -20,18 L -35,18 L -35,-4 Z"));
            // Abstract skull crest, with angular wings and stepped jaw.
            context.DrawGeometry(Brush.Parse("#D8B777"), null,
                Geometry.Parse("M -12,-10 L -7,-15 L 7,-15 L 12,-10 L 12,1 L 8,5 L 8,12 L -8,12 L -8,5 L -12,1 Z"));
            context.DrawEllipse(Brush.Parse("#17141A"), null, new Point(-5, -3), 3.5, 4);
            context.DrawEllipse(Brush.Parse("#17141A"), null, new Point(5, -3), 3.5, 4);
            context.DrawGeometry(Brush.Parse("#17141A"), null, Geometry.Parse("M 0,0 L -2,5 L 2,5 Z"));
            for (int i = -4; i <= 4; i += 4) context.DrawLine(Faint, new Point(i, 8), new Point(i, 12));
            for (int i = 0; i < 3; i++)
            {
                context.DrawLine(Gold, new Point(-18, -5 + i * 7), new Point(-29, -10 + i * 7));
                context.DrawLine(Gold, new Point(18, -5 + i * 7), new Point(29, -10 + i * 7));
            }
        }
    }
}

internal sealed class DecoFrame : Decorator
{
    public DecoFrame(Control child) { Child = child; Padding = new Thickness(22, 8, 22, 20); Margin = new Thickness(14); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var gold = new Pen(Brush.Parse("#D8B777"), 1);
        var faint = new Pen(Brush.Parse("#4F3F3B"), 1);
        double w = Bounds.Width, h = Bounds.Height;
        context.DrawRectangle(null, faint, new Rect(0.5, .5, w - 1, h - 1));
        context.DrawRectangle(null, faint, new Rect(5.5, 5.5, w - 11, h - 11));
        foreach (var corner in new[] { (0d, 0d, 1d, 1d), (w, 0d, -1d, 1d), (0d, h, 1d, -1d), (w, h, -1d, -1d) })
        {
            var (x, y, sx, sy) = corner;
            context.DrawLine(gold, new Point(x + sx * 12, y + sy * 2), new Point(x + sx * 40, y + sy * 2));
            context.DrawLine(gold, new Point(x + sx * 2, y + sy * 12), new Point(x + sx * 2, y + sy * 40));
            context.DrawLine(gold, new Point(x + sx * 2, y + sy * 12), new Point(x + sx * 12, y + sy * 2));
        }
    }
}
