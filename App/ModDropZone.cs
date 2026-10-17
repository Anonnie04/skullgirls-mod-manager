using System.Drawing.Drawing2D;

namespace SkullgirlsModManager.Desktop;

internal sealed class ModDropZone : Panel
{
    public bool Highlight;
    public ModDropZone() { DoubleBuffered = true; Dock = DockStyle.Fill; Padding = new Padding(12, 6, 12, 6); }
    public bool SimulateFileDrop(string[] paths)
    {
        var data = new DataObject(DataFormats.FileDrop, paths);
        var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        OnDragEnter(args);
        if (args.Effect != DragDropEffects.Copy) return false;
        OnDragDrop(args); return true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Highlight ? Theme.Gold : Theme.Line) { DashStyle = DashStyle.Dash };
        e.Graphics.DrawRectangle(pen, 1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
        using var titleFont = new Font("Segoe UI Semibold", 10);
        TextRenderer.DrawText(e.Graphics, "DROP A REPLACEMENT FILE HERE", titleFont,
            new Rectangle(14, 10, Math.Max(0, Width - 175), 23), Theme.Gold, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, "The archive and asset path are filled in automatically.", Font,
            new Rectangle(14, 35, Math.Max(0, Width - 175), 23), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
