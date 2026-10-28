using System.Drawing.Drawing2D;

namespace SkullgirlsModManager.Desktop;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(23, 20, 26);
    public static readonly Color Panel = Color.FromArgb(35, 30, 37);
    public static readonly Color Field = Color.FromArgb(19, 18, 23);
    public static readonly Color Gold = Color.FromArgb(216, 183, 119);
    public static readonly Color Text = Color.FromArgb(239, 228, 207);
    public static readonly Color Muted = Color.FromArgb(177, 164, 155);
    public static readonly Color Burgundy = Color.FromArgb(107, 37, 54);
    public static readonly Color Selected = Color.FromArgb(82, 58, 52);
    public static readonly Color Line = Color.FromArgb(79, 63, 59);

    public static void Apply(Control root)
    {
        root.BackColor = root is TextBox or RichTextBox ? Field : root is TabPage ? Panel : Background;
        root.ForeColor = Text;
        if (root is Label label) label.ForeColor = label.Font.Size >= 17 ? Gold : label.Font.Size < 10 ? Muted : Text;
        if (root is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Equals(button.Tag, "primary") ? Burgundy : Panel;
            button.ForeColor = Gold;
            button.FlatAppearance.BorderColor = Gold;
            button.FlatAppearance.MouseOverBackColor = Selected;
            button.FlatAppearance.MouseDownBackColor = Burgundy;
        }
        if (root is ComboBox combo) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Panel; combo.ForeColor = Gold; }
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Field; grid.GridColor = Line; grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Burgundy;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Gold;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Burgundy;
            grid.DefaultCellStyle.BackColor = Panel; grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Selected; grid.DefaultCellStyle.SelectionForeColor = Gold;
            grid.RowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Panel, ForeColor = Text, SelectionBackColor = Selected, SelectionForeColor = Gold };
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 27, 33), ForeColor = Text, SelectionBackColor = Selected, SelectionForeColor = Gold };
            foreach (DataGridViewColumn column in grid.Columns) { column.DefaultCellStyle.SelectionBackColor = Selected; column.DefaultCellStyle.SelectionForeColor = Gold; }
        }
        if (root is TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += (_, e) =>
            {
                bool selected = tabs.SelectedIndex == e.Index;
                using var fill = new SolidBrush(selected ? Burgundy : Background);
                e.Graphics.FillRectangle(fill, e.Bounds);
                using var line = new Pen(selected ? Gold : Line);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? Gold : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
        foreach (Control child in root.Controls) Apply(child);
    }

    public static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(74, 34), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 3, 8, 3), FlatStyle = FlatStyle.Flat };
        button.Click += (_, _) => action(); Apply(button); return button;
    }
}

internal sealed class DecoComboBox : ComboBox
{
    public DecoComboBox() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; DropDownStyle = ComboBoxStyle.DropDownList; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var fill = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selected : Theme.Panel);
        e.Graphics.FillRectangle(fill, e.Bounds);
        string value = (e.Index >= 0 ? GetItemText(Items[e.Index]) : Text) ?? string.Empty;
        TextRenderer.DrawText(e.Graphics, value, e.Font ?? Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height), Theme.Gold,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg != 0x000F && m.Msg != 0x0317 && m.Msg != 0x0318) || !IsHandleCreated) return;
        using var graphics = m.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam);
        using var fill = new SolidBrush(Theme.Panel); using var pen = new Pen(Theme.Gold);
        int left = Width - SystemInformation.VerticalScrollBarWidth - 3;
        graphics.FillRectangle(fill, left, 1, Width - left - 1, Height - 2);
        int x = (left + Width) / 2, y = Height / 2;
        graphics.DrawLines(pen, [new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2)]);
        graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
}

internal sealed class WorkspacePage(string title) : Panel
{
    public string Title { get; } = title;
}

internal sealed class DecoTabs : Panel
{
    public sealed class PageCollection(DecoTabs owner) : List<WorkspacePage>
    {
        public new void AddRange(IEnumerable<WorkspacePage> pages)
        {
            foreach (var page in pages) { Add(page); page.Dock = DockStyle.Fill; owner.Controls.Add(page); }
            owner.SelectedIndex = 0;
        }
    }
    private int selected;
    public event EventHandler? SelectedIndexChanged;
    private readonly List<Rectangle> buttons = [];
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PageCollection TabPages { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selected;
        set { bool changed = selected != value; selected = value; for (int i = 0; i < TabPages.Count; i++) TabPages[i].Visible = i == selected; Invalidate(); if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    public DecoTabs() { TabPages = new(this); DoubleBuffered = true; Padding = new Padding(1, 42, 1, 1); TabStop = true; SetStyle(ControlStyles.Selectable, true); AccessibleName = "Workspace tabs"; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (TabPages.Count == 0) return;
        if (e.KeyCode == Keys.Left) SelectedIndex = (SelectedIndex + TabPages.Count - 1) % TabPages.Count;
        else if (e.KeyCode == Keys.Right) SelectedIndex = (SelectedIndex + 1) % TabPages.Count;
        else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
        else if (e.KeyCode == Keys.End) SelectedIndex = TabPages.Count - 1;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); buttons.Clear();
        using var line = new Pen(Theme.Gold);
        int x = 0;
        for (int i = 0; i < TabPages.Count; i++)
        {
            int width = TextRenderer.MeasureText(TabPages[i].Title, Font).Width + 28;
            var rect = new Rectangle(x, 0, width, 38); buttons.Add(rect);
            using var fill = new SolidBrush(i == selected ? Theme.Burgundy : Theme.Background);
            e.Graphics.FillRectangle(fill, rect);
            if (i == selected) e.Graphics.DrawRectangle(line, x, 0, width - 1, 37);
            TextRenderer.DrawText(e.Graphics, TabPages[i].Title, Font, rect, i == selected ? Theme.Gold : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x += width + 3;
        }
        using var frame = new Pen(Theme.Line); e.Graphics.DrawRectangle(frame, 0, 41, Math.Max(0, Width - 1), Math.Max(0, Height - 42));
    }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); for (int i = 0; i < buttons.Count; i++) if (buttons[i].Contains(e.Location)) { SelectedIndex = i; break; } }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}

internal sealed class DecoHeader : Panel
{
    public DecoHeader() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Gold, 1);
        using var faint = new Pen(Theme.Line, 1);
        float y = Height - 10, center = Width / 2f;
        g.DrawLine(pen, 0, y, center - 12, y); g.DrawLine(pen, center + 12, y, Width, y);
        g.DrawLine(faint, 20, y + 4, center - 17, y + 4); g.DrawLine(faint, center + 17, y + 4, Width - 20, y + 4);
        g.DrawPolygon(pen, [new PointF(center, y - 6), new PointF(center + 6, y), new PointF(center, y + 6), new PointF(center - 6, y)]);
        for (int i = 0; i < 7; i++)
        {
            float x = Width - 42 - i * 12;
            g.DrawLine(faint, Width - 18, 54, x, 5);
        }
    }
}

internal sealed class ImageCanvas : Control
{
    private Bitmap? image;
    private Point pan, last;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ActualSize { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color? SolidBackground { get; set; }
    public ImageCanvas() { DoubleBuffered = true; Dock = DockStyle.Fill; }
    public void SetImage(Bitmap? value) { var old = image; image = value; old?.Dispose(); pan = Point.Empty; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && ActualSize) { last = e.Location; Capture = true; Cursor = Cursors.SizeAll; } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture && ActualSize) { pan.Offset(e.X - last.X, e.Y - last.Y); last = e.Location; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; Cursor = Cursors.Default; }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (SolidBackground is { } color) e.Graphics.Clear(color);
        else
        {
            using var a = new SolidBrush(Color.FromArgb(43, 40, 46)); using var b = new SolidBrush(Color.FromArgb(58, 53, 59));
            for (int y = 0; y < Height; y += 16) for (int x = 0; x < Width; x += 16) e.Graphics.FillRectangle(((x / 16 + y / 16) & 1) == 0 ? a : b, x, y, 16, 16);
        }
        if (image == null) return;
        float scale = ActualSize ? 1 : Math.Min((Width - 24f) / image.Width, (Height - 24f) / image.Height);
        scale = Math.Max(0.001f, scale);
        float width = image.Width * scale, height = image.Height * scale;
        e.Graphics.InterpolationMode = ActualSize ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, (Width - width) / 2 + (ActualSize ? pan.X : 0), (Height - height) / 2 + (ActualSize ? pan.Y : 0), width, height);
    }
    protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
}

internal sealed class WaveCanvas : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public SkullgirlsModManager.DecodedAudio? Audio { get => audio; set { audio = value; peaks = null; Position = 0; Invalidate(); } }
    private SkullgirlsModManager.DecodedAudio? audio;
    private (short Min, short Max)[]? peaks;
    private double position;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Position { get => position; set { position = Math.Max(0, value); Invalidate(); } }
    public event Action<double>? SeekRequested;
    public WaveCanvas() { Dock = DockStyle.Fill; DoubleBuffered = true; Cursor = Cursors.Hand; }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Audio is { } audio) SeekRequested?.Invoke(Math.Clamp((e.X - 16d) / Math.Max(1, Width - 32), 0, 1) * audio.Duration);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(Theme.Field);
        if (Audio is not { } audio || audio.Samples.Length == 0) return;
        using var pen = new Pen(Theme.Gold); using var line = new Pen(Theme.Line);
        float center = Height / 2f, amplitude = Height * .4f;
        e.Graphics.DrawLine(line, 16, center, Width - 16, center);
        int width = Math.Max(1, Width - 32);
        if (peaks == null || peaks.Length != width)
        {
            peaks = new (short, short)[width];
            for (int x = 0; x < width; x++)
            {
                int start = (int)((long)x * audio.Samples.Length / width), end = (int)((long)(x + 1) * audio.Samples.Length / width);
                short min = 0, max = 0;
                for (int i = start; i < Math.Min(audio.Samples.Length, Math.Max(start + 1, end)); i++) { min = Math.Min(min, audio.Samples[i]); max = Math.Max(max, audio.Samples[i]); }
                peaks[x] = (min, max);
            }
        }
        float head = 16 + (float)Math.Clamp(Position / Math.Max(.001, audio.Duration), 0, 1) * width;
        using var played = new SolidBrush(Color.FromArgb(35, Theme.Gold));
        e.Graphics.FillRectangle(played, 16, 16, head - 16, Math.Max(0, Height - 32));
        for (int x = 0; x < width; x++) e.Graphics.DrawLine(pen, x + 16, center - peaks[x].Max / 32768f * amplitude, x + 16, center - peaks[x].Min / 32768f * amplitude);
        using var cursor = new Pen(Theme.Text, 2);
        e.Graphics.DrawLine(cursor, head, 16, head, Height - 16);
    }
}
