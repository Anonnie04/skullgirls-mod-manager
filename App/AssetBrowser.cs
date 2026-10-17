using System.Drawing.Imaging;
using System.Media;
using System.Runtime.InteropServices;

namespace SkullgirlsModManager.Desktop;

internal sealed class AssetBrowser : Form
{
    private readonly ComboBox archives = new DecoComboBox { Dock = DockStyle.Fill, FormattingEnabled = true };
    private readonly ComboBox kind = new DecoComboBox { Dock = DockStyle.Fill };
    private readonly ComboBox character = new DecoComboBox { Dock = DockStyle.Fill, AccessibleName = "Character filter" };
    private sealed record CharacterChoice(string Name, string Label) { public override string ToString() => Label; }
    private Dictionary<string, IReadOnlyList<string>> characterNames = new(StringComparer.OrdinalIgnoreCase);
    private bool loadingCharacters;
    private readonly TextBox filter = new() { Dock = DockStyle.Fill, PlaceholderText = "Search internal asset paths…" };
    private readonly DataGridView files = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToResizeRows = false, BorderStyle = BorderStyle.None, RowTemplate = { Height = 32 }, ColumnHeadersHeight = 34 };
    private readonly Label count = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label assetName = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 12), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label metadata = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label hint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Panel viewport = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false };
    private readonly FlowLayoutPanel conversions = new() { Dock = DockStyle.Fill, WrapContents = false };
    private TableLayoutPanel? previewLayout;
    private readonly ImageCanvas canvas = new();
    private readonly TextBox text = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10) };
    private readonly WaveCanvas waveform = new();
    private readonly Label message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(30) };
    private const string WorkshopUrl = "https://github.com/Anonnie04/skullgirls-sprite-workshop";
    private readonly LinkLabel workshopLink = new() { Dock = DockStyle.Fill, Text = "Skullgirls Sprite Workshop\n" + WorkshopUrl,
        TextAlign = ContentAlignment.MiddleCenter, LinkColor = Theme.Gold, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Muted,
        Font = new Font("Segoe UI Semibold", 11), Padding = new Padding(20), TabStop = true };
    private readonly CheckBox loop = new() { Text = "Loop", AutoSize = true, Margin = new Padding(10, 10, 8, 3) };
    private readonly SemaphoreSlim decodeGate = new(1, 1);
    private CancellationTokenSource? request;
    private ArchiveIndex? index;
    private AudioPlayback? player;
    private Button? pauseButton;
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 50 };
    private DecodedAudio? currentAudio;
    private bool updating;
    private Task currentPreview = Task.CompletedTask;
    private readonly string initialArchive;
    private readonly string gameRoot;
    private readonly Library? library;
    private readonly ComboBox targetMods = new DecoComboBox { Dock = DockStyle.Fill };
    private readonly TextBox newModName = new() { Dock = DockStyle.Fill, Text = "My Skullgirls Mod", PlaceholderText = "Name your new mod" };
    private readonly ModDropZone dropZone = new();
    private readonly Label draftStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private bool showReplacement, staging, loadingTargets;
    private bool automatedDrop;
    private Task lastDrop = Task.CompletedTask;
    private sealed record ModChoice(Mod? Mod) { public override string ToString() => Mod?.Info.Name ?? "+ Create a new mod"; }
    public event Action<StagedAsset>? AssetStaged;
    public event Action<Mod>? ModCreated;
    public string PreviewKind { get; private set; } = "None";
    public string PreviewDescription { get; private set; } = "";

    public AssetBrowser(string archive, Library? library = null)
    {
        this.library = library;
        initialArchive = Path.GetFullPath(archive);
        gameRoot = Directory.GetParent(Path.GetDirectoryName(initialArchive)!)!.FullName;
        Text = "Skullgirls — Asset preview";
        ClientSize = new Size(1330, 930); MinimumSize = new Size(1100, 810);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
        Build(); Theme.Apply(this);
        playbackTimer.Tick += (_, _) => UpdatePlaybackVisual();
        waveform.SeekRequested += SeekAudio;
        workshopLink.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WorkshopUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open Sprite Workshop link"); }
        };
        LoadTargets();
        targetMods.SelectedIndexChanged += (_, _) => { if (loadingTargets) return; newModName.Enabled = SelectedMod == null; showReplacement = false; currentPreview = PreviewSelected(); };
        RegisterDrop(viewport); RegisterDrop(dropZone);
        archives.SelectedIndexChanged += async (_, _) => await LoadArchive();
        filter.TextChanged += (_, _) => Filter(); kind.SelectedIndexChanged += (_, _) => Filter();
        character.SelectedIndexChanged += (_, _) => { if (!loadingCharacters) Filter(); };
        files.SelectionChanged += (_, _) => { if (!updating) { showReplacement = false; currentPreview = PreviewSelected(); } };
        Shown += async (_, _) =>
        {
            archives.Items.AddRange(Directory.GetFiles(Path.GetDirectoryName(initialArchive)!, "*.gfs").Order().Cast<object>().ToArray());
            archives.SelectedItem = initialArchive;
            if (archives.SelectedIndex < 0) await LoadArchive();
        };
        FormClosed += (_, _) => { request?.Cancel(); StopAudio(); playbackTimer.Dispose(); };
        FormClosing += (_, e) => { if (staging) { e.Cancel = true; draftStatus.Text = "Please wait for the replacement to finish copying."; } };
    }

    private void Build()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 14, 22, 14) };
        layout.RowStyles.Add(new(SizeType.Absolute, 80)); layout.RowStyles.Add(new(SizeType.Absolute, 68)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 32));
        var header = new DecoHeader { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "THE ARCHIVES", Font = new Font("Georgia", 25, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label { Text = "ART  /  SOUND  /  SCRIPTS", AutoSize = true, Location = new Point(3, 42) });
        header.Controls.Add(new Label { Text = library == null ? "READ-ONLY ASSET PREVIEW" : "PREVIEW  /  DROP  /  BUILD YOUR MOD", Dock = DockStyle.Right, Width = 340, TextAlign = ContentAlignment.MiddleRight });
        layout.Controls.Add(header, 0, 0);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Enabled = library != null };
        composer.ColumnStyles.Add(new(SizeType.Absolute, 400)); composer.ColumnStyles.Add(new(SizeType.Percent, 100)); composer.ColumnStyles.Add(new(SizeType.Absolute, 155));
        composer.RowStyles.Add(new(SizeType.Absolute, 24)); composer.RowStyles.Add(new(SizeType.Percent, 100));
        composer.Controls.Add(new Label { Text = "SAVE REPLACEMENTS TO", AutoSize = true, Font = new Font("Segoe UI Semibold", 9) }, 0, 0);
        composer.Controls.Add(new Label { Text = "NEW MOD NAME", AutoSize = true, Font = new Font("Segoe UI Semibold", 9) }, 1, 0);
        composer.Controls.Add(targetMods, 0, 1); composer.Controls.Add(newModName, 1, 1);
        composer.Controls.Add(Theme.Button("Open mod folder", OpenModFolder), 2, 1); layout.Controls.Add(composer, 0, 1);
        var split = new SplitContainer { Size = new Size(1280, 700), Dock = DockStyle.Fill, SplitterDistance = 430, SplitterWidth = 12, Panel1MinSize = 320, Panel2MinSize = 500 };
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
        foreach (int h in new[] { 42, 38, 38, 38, 0, 30 }) left.RowStyles.Add(h == 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, h));
        archives.Format += (_, e) => e.Value = Path.GetFileName(e.ListItem?.ToString());
        kind.Items.AddRange(["All asset types", "Image", "Audio", "Text", "Binary"]); kind.SelectedIndex = 0;
        left.Controls.Add(archives, 0, 0); left.Controls.Add(filter, 0, 1); left.Controls.Add(kind, 0, 2);
        left.Controls.Add(character, 0, 3);
        files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Internal asset path", FillWeight = 58, SortMode = DataGridViewColumnSortMode.Automatic });
        files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Character", FillWeight = 25, SortMode = DataGridViewColumnSortMode.Automatic });
        files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", FillWeight = 17, SortMode = DataGridViewColumnSortMode.NotSortable });
        files.SortCompare += (_, e) => { e.SortResult = StringComparer.OrdinalIgnoreCase.Compare(AssetCharacters.NaturalKey(e.CellValue1?.ToString() ?? ""), AssetCharacters.NaturalKey(e.CellValue2?.ToString() ?? "")); if (e.SortResult == 0) e.SortResult = StringComparer.OrdinalIgnoreCase.Compare(AssetCharacters.NaturalKey(files.Rows[e.RowIndex1].Cells[0].Value?.ToString() ?? ""), AssetCharacters.NaturalKey(files.Rows[e.RowIndex2].Cells[0].Value?.ToString() ?? "")); e.Handled = true; };
        left.Controls.Add(files, 0, 4); left.Controls.Add(count, 0, 5); split.Panel1.Controls.Add(left);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(10, 0, 0, 0) };
        previewLayout = right;
        foreach (int h in new[] { 36, 34, -1, 48, 0, 34, 76, 36 }) right.RowStyles.Add(h == -1 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, h));
        right.Controls.Add(assetName, 0, 0); right.Controls.Add(metadata, 0, 1);
        viewport.Controls.AddRange([canvas, text, waveform, message, workshopLink]); right.Controls.Add(viewport, 0, 2);
        right.Controls.Add(actions, 0, 3); right.Controls.Add(conversions, 0, 4); right.Controls.Add(hint, 0, 5); split.Panel2.Controls.Add(right);
        var choose = Theme.Button("Choose file…", ChooseReplacement); choose.Dock = DockStyle.Right; dropZone.Controls.Add(choose);
        right.Controls.Add(dropZone, 0, 6); right.Controls.Add(draftStatus, 0, 7);
        draftStatus.Text = "Drop a file to stage a replacement. Apply it later from the main window.";
        layout.Controls.Add(split, 0, 2);
        layout.Controls.Add(new Label { Text = "Drops are saved to your mod library. Game archives change only when you Apply. Packed fighting-character frames use the Sprite Workshop.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9) }, 0, 3);
        Controls.Add(layout);
    }

    private async Task LoadArchive()
    {
        if (archives.SelectedItem is not string path) return;
        request?.Cancel(); StopAudio(); index = null;
        ShowMessage("Reading archive index…");
        try
        {
            var loaded = await Task.Run(() => Archives.Read(path));
            if (IsDisposed || archives.SelectedItem?.ToString() != path) return;
            index = loaded; LoadCharacters(); Filter();
        }
        catch (Exception ex) { if (!IsDisposed) ShowMessage(ex.Message); }
    }
    private void LoadCharacters()
    {
        string selected = (character.SelectedItem as CharacterChoice)?.Name ?? "";
        loadingCharacters = true;
        try
        {
            characterNames = index!.Entries.ToDictionary(e => e.Path, e => AssetCharacters.Names(e.Path), StringComparer.OrdinalIgnoreCase);
            character.Items.Clear();
            character.Items.Add(new CharacterChoice("", $"All characters ({index.Entries.Count:N0})"));
            foreach (var group in characterNames.Values.SelectMany(n => n).GroupBy(n => n).OrderBy(g => g.Key))
                character.Items.Add(new CharacterChoice(group.Key, $"{group.Key} ({group.Count():N0})"));
            int shared = characterNames.Values.Count(n => n.Count == 0);
            character.Items.Add(new CharacterChoice("~shared", $"Other / shared ({shared:N0})"));
            if (!character.Items.Cast<CharacterChoice>().Any(c => c.Name == selected) || (selected == "~shared" && shared == 0)) selected = "";
            character.SelectedItem = character.Items.Cast<CharacterChoice>().First(c => c.Name == selected);
        }
        finally { loadingCharacters = false; }
    }
    private void Filter()
    {
        updating = true; files.Rows.Clear();
        try
        {
            if (index == null) return;
            string selected = (character.SelectedItem as CharacterChoice)?.Name ?? "";
            foreach (var entry in index.Entries.Where(e => (e.Path.Contains(filter.Text, StringComparison.OrdinalIgnoreCase) || characterNames.GetValueOrDefault(e.Path, []).Any(n => n.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)))
                && (selected.Length == 0 || (selected == "~shared" ? characterNames[e.Path].Count == 0 : characterNames[e.Path].Contains(selected)))
                && (kind.SelectedIndex <= 0 || AssetPreviews.Kind(e.Path) == kind.SelectedItem?.ToString())).OrderBy(e => string.Join(", ", characterNames.GetValueOrDefault(e.Path, [])), StringComparer.OrdinalIgnoreCase).ThenBy(e => AssetCharacters.NaturalKey(e.Path), StringComparer.OrdinalIgnoreCase))
            {
                int n = files.Rows.Add(entry.Path, string.Join(", ", characterNames.GetValueOrDefault(entry.Path, [])), AssetPreviews.Kind(entry.Path)); files.Rows[n].Tag = entry;
            }
            count.Text = $"{files.Rows.Count:N0} / {index.Entries.Count:N0} assets";
        }
        finally { updating = false; showReplacement = false; currentPreview = PreviewSelected(); }
    }

    private sealed record PreviewData(string Kind, string Description, DecodedImage? Image = null, DecodedAudio? Audio = null, string? Text = null);
    private static PreviewData Decode(ArchiveIndex archive, ArchiveEntry entry)
    {
        if (AssetPreviews.WorkshopAsset(archive.FilePath, entry.Path))
            return new("Workshop", "Skullgirls Sprite Workshop on GitHub");
        string kind = AssetPreviews.Kind(entry.Path);
        int limit = kind == "Audio" ? 128 * 1024 * 1024 : kind == "Image" ? 64 * 1024 * 1024 : kind == "Text" ? 1024 * 1024 : 4096;
        if (entry.Size > limit && kind is "Image" or "Audio") return new("Binary", "Asset exceeds the bounded preview limit.", Text: AssetPreviews.Hex(AssetPreviews.Read(archive, entry, 4096)));
        byte[] data = AssetPreviews.Read(archive, entry, limit);
        string extension = Path.GetExtension(entry.Path).ToLowerInvariant();
        if (extension == ".dds") { var image = AssetPreviews.Dds(data); return new("Image", $"{image.Width:N0} × {image.Height:N0}  •  {image.Format}", Image: image); }
        if (extension == ".pcx") { var image = AssetPreviews.Pcx(data); return new("Image", $"{image.Width:N0} × {image.Height:N0}  •  {image.Format}", Image: image); }
        if (kind == "Audio") { var audio = AssetPreviews.Wav(data); return new("Audio", $"{(int)audio.Duration / 60}:{(int)audio.Duration % 60:00}  •  {audio.SampleRate:N0} Hz  •  {audio.Channels} channel(s)  •  {audio.Format}", Audio: audio); }
        if (kind == "Image")
        {
            using var stream = new MemoryStream(data); using var source = Image.FromStream(stream);
            if ((long)source.Width * source.Height > 32_000_000) throw new InvalidDataException("Image exceeds the preview limit.");
            using var converted = new Bitmap(source);
            var pixels = new int[converted.Width * converted.Height];
            for (int y = 0; y < converted.Height; y++) for (int x = 0; x < converted.Width; x++) pixels[y * converted.Width + x] = converted.GetPixel(x, y).ToArgb();
            return new("Image", $"{source.Width} × {source.Height}  •  {extension}", Image: new(converted.Width, converted.Height, pixels, extension));
        }
        if (kind == "Text") return new("Text", entry.Size > limit ? "Text preview · first 1 MB (truncated)" : "Text preview · read only", Text: AssetPreviews.Text(data));
        return new("Binary", $"No renderer for {extension} · first {data.Length:N0} bytes", Text: AssetPreviews.Hex(data));
    }
    private async Task PreviewSelected()
    {
        request?.Cancel(); request?.Dispose(); request = new(); var token = request.Token;
        StopAudio(); currentAudio = null; canvas.SetImage(null); waveform.Audio = null;
        foreach (Control control in conversions.Controls.Cast<Control>().ToArray()) { conversions.Controls.Remove(control); control.Dispose(); }
        if (previewLayout != null) previewLayout.RowStyles[4].Height = 0;
        foreach (Control control in actions.Controls.Cast<Control>().ToArray()) { actions.Controls.Remove(control); if (control != loop) control.Dispose(); }
        if (index is not { } archive || files.CurrentRow?.Tag is not ArchiveEntry entry)
        { assetName.Text = "Choose an asset"; metadata.Text = ""; ShowMessage(index != null && files.Rows.Count == 0 ? "No assets match these filters. Clear the search or choose All characters / All asset types." : "Select an asset from the list to preview it."); return; }
        string? replacement = StagedPath(archive, entry);
        if (replacement == null) showReplacement = false;
        var previewArchive = archive; var previewEntry = entry;
        if (showReplacement)
        {
            previewEntry = new(entry.Path, new FileInfo(replacement!).Length, 1, 0);
            previewArchive = new(replacement!, archive.Header, [previewEntry]);
        }
        assetName.Text = entry.Path; metadata.Text = $"{(showReplacement ? "STAGED REPLACEMENT" : "ORIGINAL GAME ASSET")}  •  {previewEntry.Size:N0} bytes  •  {Path.GetFileName(archive.FilePath)}"; hint.Text = "Loading preview…";
        ShowMessage("Loading preview…");
        try
        {
            await decodeGate.WaitAsync(token);
            PreviewData result;
            try { token.ThrowIfCancellationRequested(); result = await Task.Run(() => Decode(previewArchive, previewEntry), token); }
            finally { decodeGate.Release(); }
            if (IsDisposed || token.IsCancellationRequested) return;
            PreviewKind = result.Kind; PreviewDescription = result.Description;
            dropZone.Visible = result.Kind != "Workshop";
            hint.Text = result.Description;
            foreach (Control child in viewport.Controls) child.Visible = false;
            if (result.Image is { } image)
            {
                var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                var locked = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { Marshal.Copy(image.Pixels, 0, locked.Scan0, image.Pixels.Length); } finally { bitmap.UnlockBits(locked); }
                canvas.SetImage(bitmap); canvas.Visible = true;
                if (previewLayout != null) previewLayout.RowStyles[4].Height = 44;
                conversions.Controls.Add(Theme.Button("Save PNG…", () => SavePng(image)));
                if (Path.GetExtension(entry.Path).Equals(".dds", StringComparison.OrdinalIgnoreCase)) conversions.Controls.Add(Theme.Button("PNG → DDS…", ConvertPngToDds));
                actions.Controls.Add(Theme.Button("Fit", () => { canvas.ActualSize = false; canvas.Invalidate(); }));
                actions.Controls.Add(Theme.Button("100%", () => { canvas.ActualSize = true; canvas.Invalidate(); }));
                actions.Controls.Add(Theme.Button("Checker", () => { canvas.SolidBackground = null; canvas.Invalidate(); }));
                actions.Controls.Add(Theme.Button("Dark", () => { canvas.SolidBackground = Theme.Field; canvas.Invalidate(); }));
                actions.Controls.Add(Theme.Button("Light", () => { canvas.SolidBackground = Color.FromArgb(235, 229, 217); canvas.Invalidate(); }));
            }
            else if (result.Audio is { } audio)
            {
                currentAudio = audio; waveform.Audio = audio; waveform.Visible = true; waveform.Invalidate();
                actions.Controls.Add(Theme.Button("Play", Play));
                pauseButton = Theme.Button("Pause", PauseAudio); pauseButton.Enabled = false; actions.Controls.Add(pauseButton);
                actions.Controls.Add(Theme.Button("Stop", () => { StopAudio(); hint.Text = result.Description; })); actions.Controls.Add(loop);
                hint.Text = result.Description + "  •  Click the waveform to seek";
            }
            else if (result.Kind == "Workshop") workshopLink.Visible = true;
            else { text.Text = result.Text ?? "No preview available."; text.Visible = true; text.SelectionStart = 0; }
            if (result.Kind != "Workshop") actions.Controls.Add(Theme.Button("Save original…", SaveOriginal));
            if (replacement != null) actions.Controls.Add(Theme.Button(showReplacement ? "Show original" : "Show replacement", () => { showReplacement = !showReplacement; currentPreview = PreviewSelected(); }));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed || token.IsCancellationRequested) return;
            PreviewKind = "Unsupported"; PreviewDescription = ex.Message;
            text.Text = ex.Message + "\r\n\r\nRaw header (first 4 KB):\r\n";
            try { text.AppendText(AssetPreviews.Hex(AssetPreviews.Read(previewArchive, previewEntry, 4096))); } catch { }
            foreach (Control child in viewport.Controls) child.Visible = child == text;
            hint.Text = "This format could not be rendered. The game file was not changed.";
            actions.Controls.Add(Theme.Button("Save original…", SaveOriginal));
        }
    }
    private void ShowMessage(string value)
    {
        foreach (Control child in viewport.Controls) child.Visible = child == message;
        message.Text = value;
    }
    private void ValidateExportPath(string output)
    {
        if (Path.GetFullPath(output).StartsWith(gameRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a destination outside the game installation.");
    }
    private void SavePng(DecodedImage image)
    {
        string name = files.CurrentRow?.Tag is ArchiveEntry asset ? Path.GetFileNameWithoutExtension(asset.Path) : "texture";
        using var save = new SaveFileDialog { Title = "Save decoded image as PNG", FileName = name + ".png", Filter = "PNG image (*.png)|*.png", DefaultExt = "png", AddExtension = true };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try { ValidateExportPath(save.FileName); ReplacementPreparation.SavePng(image, save.FileName); draftStatus.Text = "Saved full-resolution PNG with transparency: " + save.FileName; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save PNG"); }
    }
    private async void ConvertPngToDds()
    {
        if (staging || index is not { } archive || files.CurrentRow?.Tag is not ArchiveEntry asset) return;
        using var input = new OpenFileDialog { Title = "Choose PNG to convert to the selected DDS format", Filter = "PNG image (*.png)|*.png" };
        if (input.ShowDialog(this) != DialogResult.OK) return;
        using var output = new SaveFileDialog { Title = "Save converted DDS", FileName = Path.GetFileName(asset.Path), Filter = "DDS texture (*.dds)|*.dds", DefaultExt = "dds", AddExtension = true };
        if (output.ShowDialog(this) != DialogResult.OK) return;
        staging = true; UseWaitCursor = true; string? temporary = null;
        try
        {
            ValidateExportPath(output.FileName);
            var target = TextureTarget.Read(AssetPreviews.Read(archive, asset, 128));
            var size = await Task.Run(() => ReplacementPreparation.ImageSize(input.FileName));
            bool resize = size.Width != target.Width || size.Height != target.Height;
            if (resize && MessageBox.Show(this, $"Resize {size.Width} × {size.Height} to {target.Width} × {target.Height}? The full image will be fitted and proportions may change.", "Fit PNG to DDS", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            temporary = Path.Combine(Path.GetDirectoryName(output.FileName)!, ".convert-" + Guid.NewGuid().ToString("N") + ".dds");
            draftStatus.Text = "Converting PNG to the selected texture's DDS format…";
            await Task.Run(() => ReplacementPreparation.ConvertImage(archive, asset, input.FileName, temporary, resize));
            Library.ValidateReplacement(archive, asset, temporary);
            File.Move(temporary, output.FileName, true); temporary = null;
            draftStatus.Text = "DDS saved. Drop it onto this asset to add it to your mod.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not convert PNG to DDS"); draftStatus.Text = "Conversion stopped: " + ex.Message; }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); staging = false; UseWaitCursor = false; }
    }
    private Mod? SelectedMod => (targetMods.SelectedItem as ModChoice)?.Mod;
    private void LoadTargets(string? selected = null)
    {
        loadingTargets = true;
        try
        {
            targetMods.Items.Clear(); targetMods.Items.Add(new ModChoice(null));
            if (library != null) foreach (var mod in library.Mods()) targetMods.Items.Add(new ModChoice(mod));
            targetMods.SelectedIndex = 0;
            for (int n = 1; n < targetMods.Items.Count; n++) if (((ModChoice)targetMods.Items[n]!).Mod?.Folder == selected) targetMods.SelectedIndex = n;
            newModName.Enabled = SelectedMod == null;
        }
        finally { loadingTargets = false; }
    }
    private string? StagedPath(ArchiveIndex archive, ArchiveEntry entry)
    {
        if (SelectedMod is not { } mod || AssetPreviews.WorkshopAsset(archive.FilePath, entry.Path)) return null;
        var packages = mod.Replacements();
        return packages.TryGetValue(Path.GetFileName(archive.FilePath), out var assets) && assets.TryGetValue(entry.Path, out var file) ? file : null;
    }
    private void OpenModFolder()
    {
        if (staging) { draftStatus.Text = "Wait for the replacement to finish saving, then open the folder."; return; }
        try
        {
            var mod = EnsureModFolder();
            FolderLauncher.Open(mod.Folder);
            draftStatus.Text = "Opened mod folder: " + mod.Folder;
        }
        catch (Exception ex)
        {
            draftStatus.Text = "Could not open mod folder: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Could not open mod folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private Mod EnsureModFolder()
    {
        if (SelectedMod is { } selected) return selected;
        if (library == null) throw new InvalidOperationException("Open this browser from the mod manager to create a mod.");
        var created = library.CreateDraft(newModName.Text);
        LoadTargets(created.Folder);
        ModCreated?.Invoke(created);
        return created;
    }
    private bool CanStage => !staging && library != null && index != null && files.CurrentRow?.Tag is ArchiveEntry asset && !AssetPreviews.WorkshopAsset(index.FilePath, asset.Path);
    private void RegisterDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
            bool valid = CanStage && paths?.Length == 1 && File.Exists(paths[0]);
            e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
            dropZone.Highlight = valid; dropZone.Invalidate();
        };
        control.DragLeave += (_, _) => { dropZone.Highlight = false; dropZone.Invalidate(); };
        control.DragDrop += (_, e) =>
        {
            dropZone.Highlight = false; dropZone.Invalidate();
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length == 1) lastDrop = StageDroppedFile(paths[0], !automatedDrop);
        };
        foreach (Control child in control.Controls) RegisterDrop(child);
    }
    private async void ChooseReplacement()
    {
        if (!CanStage) { draftStatus.Text = "Select a replaceable asset. Packed character frames use the Sprite Workshop."; return; }
        using var picker = new OpenFileDialog { Title = "Choose a replacement for " + ((ArchiveEntry)files.CurrentRow!.Tag!).Path, Filter = "Replacement file|*.*" };
        if (picker.ShowDialog(this) == DialogResult.OK) await StageDroppedFile(picker.FileName);
    }
    private async Task StageDroppedFile(string input, bool interactive = true)
    {
        if (!CanStage || index is not { } archive || files.CurrentRow?.Tag is not ArchiveEntry asset || library == null) return;
        var target = SelectedMod; string name = newModName.Text;
        if (target == null && string.IsNullOrWhiteSpace(name)) { if (interactive) MessageBox.Show(this, "Enter a name for the new mod.", "Name your mod"); return; }
        staging = true; StopAudio(); request?.Cancel();
        archives.Enabled = files.Enabled = filter.Enabled = kind.Enabled = targetMods.Enabled = newModName.Enabled = false;
        UseWaitCursor = true; string? prepared = null;
        try
        {
            string source = input;
            if (ReplacementPreparation.ConvertsImage(asset, input))
            {
                var format = TextureTarget.Read(AssetPreviews.Read(archive, asset, 128));
                var dimensions = await Task.Run(() => ReplacementPreparation.ImageSize(input));
                bool resize = dimensions.Width != format.Width || dimensions.Height != format.Height;
                if (resize && (!interactive || MessageBox.Show(this, $"This image is {dimensions.Width} × {dimensions.Height}.\nThe game texture is {format.Width} × {format.Height}.\n\nResize the full image to the game texture size? Its proportions may change.", "Fit replacement image", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK))
                { if (!interactive) throw new InvalidDataException("Fixture image dimensions do not match."); draftStatus.Text = "Nothing saved. Choose an image matching the original size."; return; }
                string tempRoot = Path.Combine(Path.GetDirectoryName(library.Root)!, "conversion-temp"); Directory.CreateDirectory(tempRoot);
                prepared = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".dds");
                draftStatus.Text = "Converting image to the original DDS format…";
                await Task.Run(() => ReplacementPreparation.ConvertImage(archive, asset, input, prepared, resize));
                source = prepared;
            }
            draftStatus.Text = "Validating and copying the replacement into your mod…";
            var saved = await Task.Run(() => library.StageAsset(archive, asset, source, target, name));
            LoadTargets(saved.Mod.Folder);
            AssetStaged?.Invoke(saved);
            draftStatus.Text = $"Saved to {saved.Mod.Info.Name}. {(saved.Mod.Enabled ? "Apply from the main window when ready." : "Enable the mod and Apply when ready.")}";
            if (saved.PreviousCopy != null) draftStatus.Text += " Previous copy kept in replacement-history.";
            showReplacement = true; currentPreview = PreviewSelected(); await currentPreview;
        }
        catch (Exception ex)
        {
            draftStatus.Text = "Nothing applied: " + ex.Message;
            if (interactive) MessageBox.Show(this, ex.Message, "Could not stage replacement", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else throw;
        }
        finally
        {
            if (prepared != null && File.Exists(prepared)) File.Delete(prepared);
            staging = false; archives.Enabled = files.Enabled = filter.Enabled = kind.Enabled = targetMods.Enabled = true; newModName.Enabled = SelectedMod == null; UseWaitCursor = false;
        }
    }
    private void Play()
    {
        if (currentAudio == null) return;
        try
        {
            if (player?.Paused == true) { player.Resume(); if (pauseButton != null) pauseButton.Text = "Pause"; }
            else { double start = waveform.Position >= currentAudio.Duration ? 0 : waveform.Position; StopAudio(); player = new AudioPlayback(currentAudio); player.Start(start); }
            if (pauseButton != null) pauseButton.Enabled = true;
            playbackTimer.Start(); UpdatePlaybackVisual();
        }
        catch (Exception ex) { StopAudio(); hint.Text = "Playback failed: " + ex.Message; }
    }
    private void StopAudio() { playbackTimer.Stop(); player?.Dispose(); player = null; waveform.Position = 0; if (pauseButton is { IsDisposed: false }) { pauseButton.Enabled = false; pauseButton.Text = "Pause"; } }
    private void PauseAudio()
    {
        if (player == null) return;
        try
        {
            if (player.Paused) player.Resume(); else player.Pause();
            if (pauseButton != null) pauseButton.Text = player.Paused ? "Resume" : "Pause";
            UpdatePlaybackVisual();
        }
        catch (Exception ex) { StopAudio(); hint.Text = "Playback failed: " + ex.Message; }
    }
    private void SeekAudio(double seconds)
    {
        if (currentAudio == null) return;
        try
        {
            if (player == null) { waveform.Position = seconds; hint.Text = $"Ready at {Time(seconds)}  •  Press Play"; return; }
            bool paused = player.Paused;
            player.Start(seconds);
            if (paused) player.Pause();
            UpdatePlaybackVisual();
        }
        catch (Exception ex) { StopAudio(); hint.Text = "Playback failed: " + ex.Message; }
    }
    private static string Time(double value) => $"{(int)value / 60}:{(int)value % 60:00}";
    private void UpdatePlaybackVisual()
    {
        if (currentAudio is not { } audio || audio.Duration <= 0 || player == null) return;
        try
        {
            double seconds = player.Position;
            if (seconds >= audio.Duration)
            {
                if (loop.Checked) { player.Start(0); seconds = 0; }
                else { StopAudio(); waveform.Position = audio.Duration; hint.Text = "Finished  •  " + PreviewDescription; return; }
            }
            waveform.Position = seconds;
            hint.Text = $"{(player.Paused ? "Paused" : "Playing")}  {Time(seconds)} / {Time(audio.Duration)}  •  Click waveform to seek";
        }
        catch (Exception ex) { StopAudio(); hint.Text = "Playback failed: " + ex.Message; }
    }
    private void SaveOriginal()
    {
        if (index is not { } archive || files.CurrentRow?.Tag is not ArchiveEntry entry || AssetPreviews.WorkshopAsset(archive.FilePath, entry.Path)) return;
        using var save = new SaveFileDialog { Title = "Save a copy of this asset", FileName = Path.GetFileName(entry.Path), Filter = "Original asset|*.*" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string output = Path.GetFullPath(save.FileName);
            if (output.StartsWith(gameRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a destination outside the game installation.");
            using var source = File.OpenRead(archive.FilePath); source.Position = entry.Offset;
            using var target = new FileStream(output, FileMode.Create, FileAccess.Write);
            var buffer = new byte[1024 * 1024]; long remaining = entry.Size;
            while (remaining > 0) { int n = source.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length)); if (n == 0) throw new EndOfStreamException(); target.Write(buffer, 0, n); remaining -= n; }
            hint.Text = "Saved a copy of the original asset.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save asset"); }
    }

    public async Task SmokeSelect(string path)
    {
        for (int n = 0; index == null && n < 100; n++) await Task.Delay(50);
        if (index == null) throw new InvalidOperationException("Asset browser did not load.");
        filter.Text = path;
        if (files.Rows.Count != 1) throw new InvalidOperationException("Asset browser filter failed.");
        files.CurrentCell = files.Rows[0].Cells[0];
        await currentPreview;
    }
    public async Task SmokeCharacterFilter(string name)
    {
        filter.Clear();
        character.SelectedItem = character.Items.Cast<CharacterChoice>().First(c => c.Name == name);
        if (files.Rows.Count == 0 || files.Rows.Cast<DataGridViewRow>().Any(r => !characterNames[((ArchiveEntry)r.Tag!).Path].Contains(name))) throw new InvalidOperationException("Character filter did not isolate the selected character.");
        await currentPreview;
    }
    public void VerifyAudioDevice()
    {
        if (currentAudio == null) throw new InvalidOperationException("No decoded audio.");
        // Load validates the PCM playback stream without playing sound during automated QA.
        using var stream = new MemoryStream(currentAudio.Wave()); using var audio = new SoundPlayer(stream); audio.Load();
    }
    public async Task VerifyPlaybackVisual()
    {
        if (currentAudio == null) throw new InvalidOperationException("No decoded audio.");
        // Exercise the real output device with silence, without playing music during QA.
        player = new AudioPlayback(new(currentAudio.SampleRate, currentAudio.Channels, new short[currentAudio.Samples.Length], "Silent transport fixture"));
        player.Start(0); playbackTimer.Start();
        await Task.Delay(220); UpdatePlaybackVisual();
        if (waveform.Position <= 0 || !hint.Text.StartsWith("Playing")) throw new InvalidOperationException("Playback visuals did not advance.");
        PauseAudio(); double paused = waveform.Position;
        await Task.Delay(180); UpdatePlaybackVisual();
        if (player?.Paused != true || Math.Abs(waveform.Position - paused) > .03) throw new InvalidOperationException("Pause did not hold the device position.");
        SeekAudio(currentAudio.Duration / 2);
        if (player?.Paused != true || Math.Abs(waveform.Position - currentAudio.Duration / 2) > .05) throw new InvalidOperationException("Seeking while paused failed.");
        PauseAudio(); await Task.Delay(180); UpdatePlaybackVisual();
        if (player?.Paused != false || waveform.Position <= currentAudio.Duration / 2) throw new InvalidOperationException("Resume did not advance device position.");
        loop.Checked = true; SeekAudio(currentAudio.Duration - .04);
        await Task.Delay(220); UpdatePlaybackVisual();
        if (player == null || waveform.Position > 1) throw new InvalidOperationException("Loop did not restart at the beginning.");
        loop.Checked = false; SeekAudio(currentAudio.Duration - .04);
        await Task.Delay(220); UpdatePlaybackVisual();
        if (player != null || !hint.Text.StartsWith("Finished")) throw new InvalidOperationException("Track completion failed.");
        StopAudio();
        if (waveform.Position != 0 || playbackTimer.Enabled) throw new InvalidOperationException("Stop did not reset playback visuals.");
        waveform.Position = currentAudio.Duration / 3;
        hint.Text = "Ready  •  Click waveform to seek";
    }
    public async Task SmokeArchiveFilterFallback()
    {
        filter.Clear();
        string music = archives.Items.Cast<string>().First(p => Path.GetFileName(p) == "music-win.gfs");
        // Annie has her own music, so select a character absent from this archive.
        var musicCharacters = Archives.Read(music).Entries.SelectMany(e => AssetCharacters.Names(e.Path)).ToHashSet();
        var absent = character.Items.Cast<CharacterChoice>().FirstOrDefault(c => c.Name.Length > 0 && c.Name != "~shared" && !musicCharacters.Contains(c.Name));
        if (absent == null) { absent = new CharacterChoice("Unavailable character fixture", "Unavailable character fixture"); character.Items.Add(absent); }
        character.SelectedItem = absent;
        archives.SelectedItem = music;
        for (int n = 0; (index == null || index.FilePath != music) && n < 100; n++) await Task.Delay(50);
        if (index?.FilePath != music || files.Rows.Count == 0 || (character.SelectedItem as CharacterChoice)?.Name != "") throw new InvalidOperationException("Unavailable character filter hid the music archive.");
        await currentPreview;
    }
    public async Task SmokeStage(string input)
    {
        File.WriteAllText(input, "; Drag-and-drop mod fixture\r\n; This file is staged only; the game is untouched.\r\n[preview]\r\nname=My replacement\r\n");
        targetMods.SelectedIndex = 0; newModName.Text = "Drop workflow example";
        var emptyDraft = EnsureModFolder();
        var folderCommand = FolderLauncher.Command(emptyDraft.Folder);
        if (emptyDraft.Enabled || !File.Exists(Path.Combine(emptyDraft.Folder, Mod.MODINFO_NAME)) || folderCommand.ArgumentList.Single() != emptyDraft.Folder
            || !folderCommand.FileName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) || EnsureModFolder().Folder != emptyDraft.Folder)
            throw new InvalidOperationException("Create/open folder before first drop failed.");
        automatedDrop = true;
        try
        {
            if (dropZone.SimulateFileDrop([input, input])) throw new InvalidOperationException("Multi-file drop should be rejected.");
            if (!dropZone.SimulateFileDrop([input])) throw new InvalidOperationException("Valid file drop was rejected.");
            await lastDrop;
        }
        finally { automatedDrop = false; }
        if (SelectedMod is not { } mod || mod.Enabled || index == null || files.CurrentRow?.Tag is not ArchiveEntry entry
            || StagedPath(index, entry) is not string path || File.ReadAllText(path) != File.ReadAllText(input) || !showReplacement || PreviewKind != "Text")
            throw new InvalidOperationException("Drop workflow smoke test failed.");
    }
    public async Task SmokeImageDrop(string input)
    {
        if (index == null || files.CurrentRow?.Tag is not ArchiveEntry entry) throw new InvalidOperationException("No texture selected.");
        var target = TextureTarget.Read(AssetPreviews.Read(index, entry, 128));
        using (var image = new Bitmap(target.Width, target.Height))
        {
            using var graphics = Graphics.FromImage(image); graphics.Clear(Color.FromArgb(180, 220, 40, 60));
            image.Save(input, ImageFormat.Png);
        }
        targetMods.SelectedIndex = 0; newModName.Text = "Image drop example"; automatedDrop = true;
        try { if (!dropZone.SimulateFileDrop([input])) throw new InvalidOperationException("PNG drop was rejected."); await lastDrop; }
        finally { automatedDrop = false; }
        if (StagedPath(index, entry) is not { } saved || TextureTarget.Read(File.ReadAllBytes(saved)) != target || PreviewKind != "Image" || !showReplacement)
            throw new InvalidOperationException("PNG conversion and staged image preview failed.");
        var decoded = AssetPreviews.Dds(File.ReadAllBytes(saved));
        string pngExport = input + ".export.png", ddsExport = input + ".roundtrip.dds";
        ReplacementPreparation.SavePng(decoded, pngExport);
        using (var png = new Bitmap(pngExport))
            if (png.Width != decoded.Width || png.Height != decoded.Height || png.GetPixel(0, 0).ToArgb() != decoded.Pixels[0])
                throw new InvalidOperationException("PNG export changed image dimensions or transparency.");
        ReplacementPreparation.ConvertImage(index, entry, pngExport, ddsExport, false);
        Library.ValidateReplacement(index, entry, ddsExport);
    }
}
