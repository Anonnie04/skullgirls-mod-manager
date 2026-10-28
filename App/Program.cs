using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using SkullgirlsModManager;
using ThemModdingHerds.VelvetBeautifier.Modding;

namespace SkullgirlsModManager.Desktop;

internal static class Program
{
    public static string ProjectRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null)
            {
                if (Directory.Exists(Path.Combine(folder.FullName, "Core")) && Directory.Exists(Path.Combine(folder.FullName, "App"))) return folder.FullName;
                folder = folder.Parent;
            }
            return AppContext.BaseDirectory;
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool smoke = args.Contains("--smoke-ui") || args.Contains("--smoke-review");
        using var mutex = new Mutex(true, "SkullgirlsModManager.Desktop" + (smoke ? ".Smoke." + Guid.NewGuid().ToString("N") : ""), out bool first);
        if (!first) { MessageBox.Show("Skullgirls Mod Manager is already open."); return; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Skullgirls Mod Manager");
        var form = new MainForm(smoke ? Path.Combine(ProjectRoot, "UiSmokeData", "run-" + Guid.NewGuid().ToString("N")) : Path.Combine(ProjectRoot, "Data"), smoke ? null : Path.Combine(ProjectRoot, "Mods"), smoke);
        if (smoke)
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    await form.ConnectForSmoke();
                    await form.SmokePreview();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(ProjectRoot, "ui-preview.png"));
                    await form.SmokeReview();
                    await form.SmokeProfiles();
                    if (args.Contains("--smoke-ui")) await form.SmokeAssets();
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(ProjectRoot, "ui-smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                finally { form.Close(); }
            };
        }
        Application.Run(form);
    }
}

public class Settings
{
    public string GameFolder { get; set; } = GameLocation.Find();
}

public partial class MainForm : Form
{
    private readonly string dataRoot;
    private readonly Library library;
    private Manager? manager;
    private readonly Settings settings;
    private List<Mod> mods = [];
    private bool refreshing;
    private bool busy;
    private readonly bool smokeTest;
    private readonly Color ink = Theme.Text;
    private readonly Color accent = Theme.Gold;
    private readonly TextBox gamePath = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Label summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label modSummary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(235, 238, 236) };
    private readonly DataGridView modGrid = Grid();
    private readonly DataGridView changeGrid = Grid();
    private readonly DataGridView archiveGrid = Grid();
    private readonly DataGridView contributorGrid = Grid();
    private readonly TextBox conflictExplanation = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView recoveryGrid = Grid();
    private readonly Label recoverySummary = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Connect an installation to inspect recovery status." };
    private readonly ProgressBar progress = new() { Width = 120, Height = 12, Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 30 };
    private readonly DecoTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
    private readonly FlowLayoutPanel footerButtons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Right };

    public MainForm(string root, string? modsFolder = null, bool smokeTest = false)
    {
        this.smokeTest = smokeTest;
        dataRoot = root;
        Directory.CreateDirectory(root);
        string legacyMods = Path.Combine(root, "mods");
        if (modsFolder != null && !Directory.Exists(modsFolder) && Directory.Exists(legacyMods)) Directory.Move(legacyMods, modsFolder);
        library = new(modsFolder ?? legacyMods);
        string config = Path.Combine(root, "settings.json");
        try { settings = File.Exists(config) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(config)) ?? new() : new(); }
        catch { settings = new(); }
        Text = "Skullgirls Mod Manager";
        ClientSize = new Size(1240, 850);
        MinimumSize = new Size(1090, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        ForeColor = ink;
        BackColor = Color.FromArgb(246, 246, 240);
        AutoScaleMode = AutoScaleMode.Dpi;
        BuildLayout();
        Theme.Apply(this);
        summary.ForeColor = Theme.Gold;
        gamePath.Text = settings.GameFolder;
        ReloadMods();
        if (!smokeTest) Shown += async (_, _) => await Run(() => LoadGame(gamePath.Text));
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; AppendLog("Wait for the current operation to finish before closing."); } };
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Theme.Field, BorderStyle = BorderStyle.None,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.Line,
        EnableHeadersVisualStyles = false, ColumnHeadersHeight = 38, RowTemplate = { Height = 38 },
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Burgundy, ForeColor = Theme.Gold, SelectionBackColor = Theme.Burgundy, Font = new Font("Segoe UI Semibold", 9) },
        DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, ForeColor = Theme.Text, Padding = new Padding(6, 3, 6, 3), SelectionBackColor = Theme.Selected, SelectionForeColor = Theme.Gold, Font = new Font("Segoe UI", 9.5f) }
    };

    private Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9), Height = 36, MinimumSize = new Size(70, 36), Padding = new Padding(8, 3, 8, 3),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Theme.Burgundy : Theme.Panel, ForeColor = Theme.Gold, Tag = primary ? "primary" : null, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 8, 4) };
        button.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(207, 215, 207);
        button.Click += (_, _) => action();
        return button;
    }
    private static FlowLayoutPanel Toolbar() => new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Margin = Padding.Empty };
    private static void Column(DataGridView grid, string name, string title, int weight, bool readOnly = true)
        => grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, FillWeight = weight, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable });

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(24, 14, 24, 12) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (float size in new float[] { 80, 60, 40, 0, 58, 25, 90 }) layout.RowStyles.Add(size == 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, size));
        Controls.Add(layout);
        var header = new DecoHeader { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "SKULLGIRLS  /  MOD WORKSPACE", Font = new Font("Segoe UI Semibold", 9), ForeColor = accent, AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label { Text = "MOD MANAGER", Font = new Font("Georgia", 25, FontStyle.Bold), AutoSize = true, Location = new Point(-3, 18) });
        header.Controls.Add(new Label { Text = "Built on Velvet Beautifier\nLocal asset replacements for Skullgirls", ForeColor = Color.DimGray, Width = 390, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight });
        layout.Controls.Add(header, 0, 0);

        var gameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        gameRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int width in new[] { 105, 95, 135 }) gameRow.ColumnStyles.Add(new(SizeType.Absolute, width));
        gameRow.RowStyles.Add(new(SizeType.Absolute, 20)); gameRow.RowStyles.Add(new(SizeType.Percent, 100));
        gameRow.Controls.Add(new Label { Text = "GAME INSTALLATION", ForeColor = Color.DimGray, Font = new Font("Segoe UI Semibold", 8), AutoSize = true }, 0, 0);
        gamePath.Margin = new Padding(0, 5, 12, 0);
        gameRow.Controls.Add(gamePath, 0, 1);
        gameRow.Controls.Add(Button("Browse…", BrowseGame), 1, 1);
        gameRow.Controls.Add(Button("Connect", async () => await Run(() => LoadGame(gamePath.Text))), 2, 1);
        gameRow.Controls.Add(Button("Launch game", Launch), 3, 1);
        layout.Controls.Add(gameRow, 0, 1);
        summary.Text = "Connect your Skullgirls installation to begin.";
        summary.ForeColor = accent;
        layout.Controls.Add(summary, 0, 2);

        body.ColumnStyles.Add(new(SizeType.Absolute, 370)); body.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.Controls.Add(body, 0, 3);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0, 0, 16, 0) };
        foreach (float height in new float[] { 28, 52, 0, 48, 48, 30 }) left.RowStyles.Add(height == 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, height));
        var modsLink = new LinkLabel { Text = "YOUR MODS  •  OPEN FOLDER", Font = new Font("Segoe UI Semibold", 10), AutoSize = true, LinkColor = Theme.Gold, ActiveLinkColor = Theme.Text };
        modsLink.LinkClicked += (_, _) => OpenFolder(library.Root);
        left.Controls.Add(modsLink, 0, 0);
        var imports = Toolbar();
        imports.Controls.Add(Button("Import folder", async () => await ImportFolder()));
        imports.Controls.Add(Button("Import ZIP", async () => await ImportZip()));
        imports.Controls.Add(Button("New mod", CreateMod));
        var versions = new ContextMenuStrip();
        versions.Items.Add("Update from folder…", null, async (_, _) => await UpdateMod(false));
        versions.Items.Add("Update from ZIP…", null, async (_, _) => await UpdateMod(true));
        versions.Items.Add("Restore previous version…", null, async (_, _) => await UpdateMod(false, true));
        Button? updateMod = null;
        updateMod = Button("Update mod…", () => versions.Show(updateMod!, new Point(0, updateMod!.Height)));
        var versionActions = Toolbar(); versionActions.Controls.Add(updateMod);
        versionActions.Controls.Add(Button("Profiles…", ShowProfiles));
        left.Controls.Add(versionActions, 0, 4);
        left.Controls.Add(imports, 0, 1);
        modGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "On", FillWeight = 17 });
        Column(modGrid, "name", "Mod", 60); Column(modGrid, "version", "Version", 23);
        modGrid.CurrentCellDirtyStateChanged += (_, _) => { if (modGrid.IsCurrentCellDirty) modGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        modGrid.CellValueChanged += (_, e) =>
        {
            if (refreshing || busy || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            try { ((Mod)modGrid.Rows[e.RowIndex].Tag!).SetEnabled(Convert.ToBoolean(modGrid.Rows[e.RowIndex].Cells[0].Value)); Dirty(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not change mod"); ReloadMods(); }
        };
        modGrid.SelectionChanged += (_, _) => ShowDetails();
        left.Controls.Add(modGrid, 0, 2);
        var order = Toolbar();
        order.Controls.Add(Button("Move up", async () => await MoveMod(-1)));
        order.Controls.Add(Button("Move down", async () => await MoveMod(1)));
        order.Controls.Add(Button("Remove", RemoveMod));
        left.Controls.Add(order, 0, 3);
        modSummary.Text = "Later mods win overlapping replacements.";
        modSummary.Font = new Font("Segoe UI", 8.5f);
        left.Controls.Add(modSummary, 0, 5);
        body.Controls.Add(left, 0, 0);

        var preview = new WorkspacePage("Change preview") { Padding = new Padding(12) };
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        previewLayout.RowStyles.Add(new(SizeType.Absolute, 48)); previewLayout.RowStyles.Add(new(SizeType.Percent, 38)); previewLayout.RowStyles.Add(new(SizeType.Percent, 62));
        previewLayout.Controls.Add(new Label { Text = "Select an asset to see all contributing mods and the final winner. Later enabled mods win; complete files replace earlier files.", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 0);
        Column(changeGrid, "archive", "Archive", 23); Column(changeGrid, "asset", "Asset path", 37); Column(changeGrid, "mod", "Final source", 24); Column(changeGrid, "action", "Result", 16);
        changeGrid.SelectionChanged += (_, _) => ShowContributors();
        var conflictPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        conflictPanel.RowStyles.Add(new(SizeType.Absolute, 60)); conflictPanel.RowStyles.Add(new(SizeType.Percent, 100)); conflictPanel.RowStyles.Add(new(SizeType.Absolute, 44));
        conflictPanel.SizeChanged += (_, _) => conflictPanel.RowStyles[0].Height = Math.Clamp(conflictPanel.Height - 128, 20, 60);
        conflictPanel.Controls.Add(conflictExplanation, 0, 0);
        Column(contributorGrid, "order", "Order", 15); Column(contributorGrid, "mod", "Contributing mod", 55); Column(contributorGrid, "result", "Result", 30);
        contributorGrid.ColumnHeadersHeight = 26; contributorGrid.RowTemplate.Height = 26;
        contributorGrid.CellDoubleClick += (_, _) => NavigateContributor();
        conflictPanel.Controls.Add(contributorGrid, 0, 1);
        var conflictActions = Toolbar(); conflictActions.Controls.Add(Button("Go to selected mod", NavigateContributor));
        conflictPanel.Controls.Add(conflictActions, 0, 2); previewLayout.Controls.Add(conflictPanel, 0, 2);
        previewLayout.Controls.Add(changeGrid, 0, 1); preview.Controls.Add(previewLayout);
        var archiveTab = new WorkspacePage("Game archives") { Padding = new Padding(12) };
        var archiveLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        archiveLayout.RowStyles.Add(new(SizeType.Percent, 100)); archiveLayout.RowStyles.Add(new(SizeType.Absolute, 52));
        Column(archiveGrid, "archive", "GFS archive", 55); Column(archiveGrid, "entries", "Assets", 20); Column(archiveGrid, "size", "Size", 25);
        archiveLayout.Controls.Add(archiveGrid, 0, 0);
        var archiveActions = Toolbar();
        archiveActions.Controls.Add(Button("Preview assets", InspectArchive));
        archiveActions.Controls.Add(Button("Extract archive…", async () => await ExtractArchive()));
        archiveActions.Controls.Add(Button("Update archive…", async () => await UpdateArchive()));
        archiveGrid.CellDoubleClick += (_, _) => InspectArchive();
        archiveLayout.Controls.Add(archiveActions, 0, 1); archiveTab.Controls.Add(archiveLayout);
        var detailTab = new WorkspacePage("Mod details") { Padding = new Padding(20) }; detailTab.Controls.Add(details);
        var toolsTab = new WorkspacePage("Backups & tools") { Padding = new Padding(20) };
        var recoveryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        recoveryLayout.RowStyles.Add(new(SizeType.Absolute, 72)); recoveryLayout.RowStyles.Add(new(SizeType.Percent, 100));
        recoveryLayout.RowStyles.Add(new(SizeType.Absolute, 85)); recoveryLayout.RowStyles.Add(new(SizeType.Absolute, 100));
        recoveryLayout.Controls.Add(recoverySummary, 0, 0);
        Column(recoveryGrid, "target", "File / operation", 25); Column(recoveryGrid, "problem", "Status", 30); Column(recoveryGrid, "action", "Available action", 45);
        recoveryGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; recoveryGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        recoveryLayout.Controls.Add(recoveryGrid, 0, 1);
        recoveryLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Restore originals reinstalls the saved baseline and removes mod-added files.\nUpdate archive in Game archives keeps the installed archive as a new baseline, including existing customizations. The previous baseline stays in history.\nStatus checks and opening history do not change game files." }, 0, 2);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        tools.Controls.Add(Button("Refresh status", async () => await Run(RefreshRecoveryStatus)));
        tools.Controls.Add(Button("Open backup folder", () => OpenFolder(RequireGame().BackupFolder)));
        tools.Controls.Add(Button("Open baseline history", () => { var folder = RequireGame().BaselineHistory; if (Directory.Exists(folder)) OpenFolder(folder); else status.Text = "No baseline history has been saved for this installation."; }));
        tools.Controls.Add(Button("Open mod library", () => OpenFolder(library.Root)));
        tools.Controls.Add(Button("Set up mod support…", InstallArchiveCompatibility));
        tools.Controls.Add(Button("Recover interrupted operation", async () => await Run(async () => { var game = RequireGame(); await Task.Run(game.Recover); await RefreshRecoveryStatus(); status.Text = "Recovery complete. Preview before applying."; })));
        recoveryLayout.Controls.Add(tools, 0, 3); toolsTab.Controls.Add(recoveryLayout);
        tabs.SelectedIndexChanged += async (_, _) => { if (tabs.SelectedIndex == 3 && manager != null && !busy) await Run(RefreshRecoveryStatus); };
        tabs.TabPages.AddRange([preview, archiveTab, detailTab, toolsTab]); body.Controls.Add(tabs, 1, 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        status.Text = "Ready. Import a mod folder or ZIP to get started.";
        footer.Controls.Add(status, 0, 0);
        footerButtons.Controls.Add(Button("Preview changes", async () => await Preview()));
        footerButtons.Controls.Add(Button("Restore originals", async () => await Apply(true)));
        footerButtons.Controls.Add(Button("Apply enabled mods", async () => await Apply(false), true));
        footer.Controls.Add(footerButtons, 1, 0); layout.Controls.Add(footer, 0, 4);
        var logHeading = new Panel { Dock = DockStyle.Fill };
        logHeading.Controls.Add(new Label { Text = "ACTIVITY", Font = new Font("Segoe UI Semibold", 8), ForeColor = Color.DimGray, AutoSize = true, Location = new Point(0, 5) });
        progress.Location = new Point(90, 7); logHeading.Controls.Add(progress); layout.Controls.Add(logHeading, 0, 5);
        log.Font = new Font("Consolas", 9); layout.Controls.Add(log, 0, 6);
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; body.Enabled = false; footerButtons.Enabled = false; gamePath.Parent!.Enabled = false; progress.Visible = true; UseWaitCursor = true;
        try { await action(); }
        catch (Exception ex) { AppendLog("ERROR: " + ex.Message); status.Text = "Operation stopped. See activity for details."; if (smokeTest) throw; MessageBox.Show(this, ex.Message, "Operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; body.Enabled = true; footerButtons.Enabled = true; gamePath.Parent!.Enabled = true; progress.Visible = false; UseWaitCursor = false; }
    }
    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }
    private Manager RequireGame()
    {
        if (manager == null || !string.Equals(Path.GetFullPath(gamePath.Text).TrimEnd(Path.DirectorySeparatorChar), manager.GameFolder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Connect the selected Skullgirls installation first.");
        return manager;
    }
    private async Task LoadGame(string path)
    {
        manager = null; archiveGrid.Rows.Clear(); changeGrid.Rows.Clear(); recoveryGrid.Rows.Clear(); recoverySummary.Text = "Connecting installation…";
        var candidate = new Manager(path, dataRoot) { Log = AppendLog };
        var archives = await Task.Run(() => candidate.ArchiveNames().Select(name =>
        {
            string full = Path.Combine(candidate.DataFolder, name);
            return (Name: name, Count: Archives.Read(full).Entries.Count, Size: new FileInfo(full).Length);
        }).ToList());
        manager = candidate;
        settings.GameFolder = candidate.GameFolder;
        Storage.Save(Path.Combine(dataRoot, "settings.json"), settings);
        foreach (var archive in archives) archiveGrid.Rows.Add(archive.Name, archive.Count.ToString("N0"), FormatSize(archive.Size));
        summary.Text = $"CONNECTED   •   {archives.Count} archives   •   {archives.Sum(a => a.Count):N0} assets   •   {FormatSize(archives.Sum(a => a.Size))}";
        status.Text = candidate.NeedsRecovery ? "Interrupted operation found. Open Backups & tools to recover." : "Connected. Import mods, then preview your changes.";
        AppendLog($"Connected to {candidate.GameFolder}. Read {archives.Count} GFS indexes; no game files changed.");
        // Hash verification can be slow for large archives; run it on demand in Backups & tools.
        recoverySummary.Text = $"{candidate.GameFolder}\r\nArchive folder: {Path.GetFileName(candidate.DataFolder)}. Open this tab or Refresh status to verify backups."
            + (candidate.NeedsRecovery ? "\r\nInterrupted operation found. Use Recover interrupted operation." : "");
    }
    private void BrowseGame()
    {
        using var picker = new FolderBrowserDialog { Description = "Select the Skullgirls installation folder", UseDescriptionForTitle = true, InitialDirectory = settings.GameFolder };
        if (picker.ShowDialog(this) == DialogResult.OK) { gamePath.Text = picker.SelectedPath; _ = Run(() => LoadGame(picker.SelectedPath)); }
    }
    private void ReloadMods(string? selected = null)
    {
        refreshing = true;
        try
        {
            mods = library.Mods(); modGrid.Rows.Clear();
            foreach (var mod in mods) { int n = modGrid.Rows.Add(mod.Enabled, mod.Info.Name, mod.Info.Version); modGrid.Rows[n].Tag = mod; }
            int index = mods.FindIndex(m => m.Folder == selected);
            if (index >= 0) modGrid.CurrentCell = modGrid.Rows[index].Cells[1];
            ShowDetails();
        }
        finally { refreshing = false; }
    }
    private Mod? Selected => modGrid.CurrentRow?.Tag as Mod;
    private void ShowDetails()
    {
        var mod = Selected;
        details.Text = mod == null ? "Select a mod to see its details.\r\n\r\nImport a folder or ZIP containing mod.json and archive-name.gfs folders." :
            $"{mod.Info.Name}\r\n\r\nAuthor: {mod.Info.Author}\r\nVersion: {mod.Info.Version}\r\nID: {mod.Info.Id}\r\nEnabled: {mod.Enabled}\r\n\r\n{mod.Info.Description}\r\n\r\nStored in:\r\n{mod.Folder}";
    }
    private void Dirty()
    {
        changeGrid.Rows.Clear();
        status.Text = $"{mods.Count(m => m.Enabled)} of {mods.Count} mods enabled. Preview, then Apply to update the game.";
        ShowDetails();
    }
    private async Task ImportFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose a replacement mod folder", UseDescriptionForTitle = true, InitialDirectory = library.Root };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await Import(picker.SelectedPath);
    }
    private async Task ImportZip()
    {
        using var picker = new OpenFileDialog { Title = "Import a replacement mod", Filter = "ZIP mods (*.zip)|*.zip", InitialDirectory = library.Root, RestoreDirectory = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await Import(picker.FileName);
    }
    private async Task Import(string path) => await Run(async () =>
    {
        if (Directory.Exists(path) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)), library.Root, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(path, Mod.MODINFO_NAME)))
        {
            ReloadMods(path); Dirty(); AppendLog("Mod found in the Mods folder."); return;
        }
        var mod = await Task.Run(() => library.Import(path));
        ReloadMods(mod.Folder); Dirty(); AppendLog($"Imported {mod.Info.Name}. Disabled until you enable it.");
        if (File.Exists(Path.Combine(mod.Folder, SpriteStorage.Receipt)))
            AppendLog("Updated sprite memory sizes to cover the imported art. Details: " + Path.Combine(mod.Folder, SpriteStorage.Receipt));
    });
    private async Task MoveMod(int delta)
    {
        if (Selected is not { } selected) return;
        int old = mods.IndexOf(selected), next = old + delta;
        if (next < 0 || next >= mods.Count) return;
        bool hadPreview = changeGrid.Rows.Count > 0;
        mods.RemoveAt(old); mods.Insert(next, selected); library.SetOrder(mods); ReloadMods(selected.Folder); Dirty();
        if (hadPreview) await Preview();
    }
    private async Task UpdateMod(bool zip, bool rollback = false)
    {
        if (Selected is not { } selected) { status.Text = "Select a mod to update first."; return; }
        string input = "";
        if (!rollback && zip)
        {
            using var picker = new OpenFileDialog { Title = "Choose the replacement package", Filter = "ZIP mods (*.zip)|*.zip", RestoreDirectory = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            input = picker.FileName;
        }
        else if (!rollback)
        {
            using var picker = new FolderBrowserDialog { Description = "Choose the complete replacement mod folder", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            input = picker.SelectedPath;
        }
        await Run(async () =>
        {
            using var update = await Task.Run(() => rollback ? library.PrepareRollback(selected) : library.PrepareUpdate(selected, input));
            using var review = CreateUpdateReview(update, rollback, out var mapping);
            if (review.ShowDialog(this) != DialogResult.OK) return;
            var mod = await Task.Run(() => library.CommitUpdate(update, mapping.Checked));
            ReloadMods(mod.Folder); Dirty(); AppendLog($"{mod.Info.Name} library version is now {mod.Info.Version}. Previous package retained; Preview and Apply to update the game.");
        });
    }
    private Form CreateUpdateReview(ModUpdate update, bool rollback, out CheckBox idMapping)
    {
        var review = new Form { Text = rollback ? "Restore previous mod version" : "Review mod update", ClientSize = new Size(740, 530), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new(SizeType.Absolute, 110)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 50)); layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = $"{update.Current.Info.Name} v{update.Current.Info.Version} → {update.Replacement.Info.Name} v{update.Replacement.Info.Version}\r\nID: {update.Current.Info.Id} → {update.Replacement.Info.Id}\r\nEnabled: {update.Current.Enabled} → {update.Replacement.Enabled}; load-order position retained.\r\nOnly the library changes. Preview and Apply afterward to update the game." }, 0, 0);
        var assets = Grid(); assets.Dock = DockStyle.Fill; assets.ReadOnly = true;
        assets.Columns.Add("kind", "Change"); assets.Columns.Add("asset", "Asset"); assets.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (var change in update.Changes) assets.Rows.Add(change.Kind, change.Path);
        layout.Controls.Add(assets, 0, 1);
        bool differentId = update.Current.Info.Id != update.Replacement.Info.Id;
        var mapping = new CheckBox { Dock = DockStyle.Fill, Text = "Map this different mod ID to the selected library entry.", Visible = differentId };
        layout.Controls.Add(mapping, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var accept = new Button { Text = rollback ? "Restore version" : "Update library", AutoSize = true, DialogResult = DialogResult.OK, Enabled = !differentId };
        mapping.CheckedChanged += (_, _) => accept.Enabled = mapping.Checked;
        actions.Controls.Add(accept); actions.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        layout.Controls.Add(actions, 0, 3); review.Controls.Add(layout); Theme.Apply(review);
        idMapping = mapping; return review;
    }
    private void RemoveMod()
    {
        if (Selected is not { } selected) return;
        if (MessageBox.Show(this, $"Remove {selected.Info.Name} from the library?\n\nApply again afterward to remove its changes from the game. A copy will be kept in Data/removed-mods.", "Remove mod", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        string removed = Path.Combine(dataRoot, "removed-mods"); Directory.CreateDirectory(removed);
        Directory.Move(selected.Folder, Path.Combine(removed, Guid.NewGuid().ToString("N")));
        ReloadMods(); Dirty(); AppendLog($"Removed {selected.Info.Name} from the library.");
    }
    private void InstallArchiveCompatibility()
    {
        try
        {
            var game = RequireGame();
            if (ArchiveCompatibility.RestoreSaved(game.GameFolder, game.Profile))
            {
                ArchiveCompatibility.Install(game.GameFolder, game.Profile, Path.Combine(game.GameFolder, "CoreMessaging.dll"));
                AppendLog("Mod support is ready. A verified copy is saved outside the game folder for future reinstalls.");
                status.Text = "Mod support ready. Enable your mods, then click Apply.";
                return;
            }
            MessageBox.Show(this, "Some sprite mods need a separate support file before Skullgirls can load them. Select CoreMessaging.dll or the original download ZIP containing it. Import your sprite mod ZIP with Import mod.\n\nThis is needed only once. The manager saves a verified copy outside the game folder and restores it automatically after a reinstall.", "One-time mod support setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var picker = new OpenFileDialog { Title = "One-time setup: select CoreMessaging.dll or a ZIP containing it", InitialDirectory = library.Root, Filter = "Mod support file or ZIP|CoreMessaging.dll;*.zip" };
            if(picker.ShowDialog(this)!=DialogResult.OK) return;
            ArchiveCompatibility.Install(game.GameFolder,game.Profile,picker.FileName);
            AppendLog("Mod support installed and saved for future game reinstalls.");
            status.Text="Mod support ready. Enable your mods, then click Apply.";
        }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Archive compatibility"); }
    }
    private void CreateMod()
    {
        try
        {
            var names = RequireGame().ArchiveNames();
            string name = names.Contains("story_art.gfs") ? "story_art.gfs" : names.FirstOrDefault() ?? throw new InvalidOperationException("No game archives found.");
            OpenAssetBrowser(name);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not start a new mod"); }
    }
    private async Task Preview() => await Run(async () =>
    {
        var game = RequireGame(); var snapshot = mods.ToList();
        var plan = await Task.Run(() => game.Preview(snapshot));
        ShowPlan(plan); tabs.SelectedIndex = 0;
    });
    private void ShowPlan(Plan plan)
    {
        changeGrid.Rows.Clear();
        foreach (var outcome in plan.Outcomes)
        {
            int n = changeGrid.Rows.Add(outcome.Archive, outcome.Asset, outcome.Winner.Mod, outcome.Conflict ? $"Wins over {outcome.Contributors.Count - 1}" : outcome.Winner.Action);
            changeGrid.Rows[n].Tag = outcome;
            if (outcome.Conflict) { changeGrid.Rows[n].DefaultCellStyle.BackColor = Theme.Burgundy; changeGrid.Rows[n].DefaultCellStyle.ForeColor = Theme.Gold; }
        }
        foreach (var change in plan.Changes.Where(c => c.ModFolder == null)) changeGrid.Rows.Add(change.Archive, change.Asset, change.Mod, change.Action);
        ShowContributors();
        status.Text = $"{changeGrid.Rows.Count:N0} affected assets / restores • {plan.Outcomes.Count(c => c.Conflict)} conflicting assets • Later mods win.";
        if(manager != null && ArchiveCompatibility.Required(manager.GameFolder,plan.Replacements.Keys) && !ArchiveCompatibility.Installed(manager.GameFolder)) status.Text="Mod support needed. Click Apply to restore saved support, or use Backups & tools → Set up mod support.";
    }
    private void ShowContributors()
    {
        contributorGrid.Rows.Clear();
        if (changeGrid.CurrentRow?.Tag is not AssetOutcome outcome)
        {
            conflictExplanation.Text = changeGrid.CurrentRow == null ? "Preview changes, then select an asset to inspect its contributors." : "Restores use the saved baseline; no mod supplies this entry.";
            return;
        }
        conflictExplanation.Text = outcome.Explanation;
        foreach (var source in outcome.Contributors)
        {
            int row = contributorGrid.Rows.Add(source.LoadOrder, source.Mod, ReferenceEquals(source, outcome.Winner) ? "Final winner" : $"Overridden by {outcome.Winner.Mod}");
            contributorGrid.Rows[row].Tag = source.ModFolder;
        }
        contributorGrid.CurrentCell = contributorGrid.Rows[^1].Cells[1];
    }
    private void NavigateContributor()
    {
        if (contributorGrid.CurrentRow?.Tag is not string folder) return;
        ReloadMods(folder); tabs.SelectedIndex = 2;
    }
    private async Task RefreshRecoveryStatus()
    {
        var game = RequireGame(); recoverySummary.Text = "Checking installation and backup hashes…";
        var report = await Task.Run(game.InspectInstallation);
        recoveryGrid.Rows.Clear();
        foreach (var issue in report.Issues) recoveryGrid.Rows.Add(issue.Target, issue.Problem, issue.Action);
        recoverySummary.Text = $"{game.GameFolder} • {Path.GetFileName(game.DataFolder)}\r\n"
            + $"{report.TrackedArchives} tracked archives; {report.TrackedLooseFiles} tracked loose files; {report.HistoryCount} baseline history entries.\r\n"
            + (report.NeedsRecovery ? "Recovery pending. Use Recover interrupted operation below." : report.Issues.Count > 0 ? $"{report.Issues.Count} issues found. Review the affected files below." : "Saved hashes verified. No recovery issues found in tracked files.")
            + " Untracked files have no saved baseline to verify.";
    }
    private async Task Apply(bool restore)
    {
        await Run(async () =>
        {
            var game = RequireGame(); var snapshot = mods.ToList();
            var plan = await Task.Run(() => game.Preview(restore ? [] : snapshot));
            ShowPlan(plan); tabs.SelectedIndex = 0;
            int looseCount = plan.LooseFiles.Keys.Concat(plan.LooseRestore).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int count = plan.Replacements.Count + plan.Restore.Count + looseCount;
            if (count == 0 && plan.Changes.Count == 0) { status.Text = "No changes to apply."; return; }
            var names = plan.Replacements.Keys.Concat(plan.Restore).ToList();
            long disk = names.Sum(n => new FileInfo(Path.Combine(game.DataFolder, n)).Length);
            disk += plan.LooseFiles.Values.Sum(p => new FileInfo(p).Length);
            disk += plan.LooseFiles.Keys.Concat(plan.LooseRestore).Distinct(StringComparer.OrdinalIgnoreCase).Sum(p => File.Exists(Path.Combine(game.GameFolder,p)) ? new FileInfo(Path.Combine(game.GameFolder,p)).Length : 0);
            string message = (restore ? "Restore backed-up originals" : "Apply the previewed changes") + $" to {plan.Replacements.Count + plan.Restore.Count} archive(s) and {looseCount} loose file(s)?\n\n" + string.Join("\n", names) +
                $"\n\nGame: {game.GameFolder}\nAllow roughly {FormatSize(disk * 3)} free space for backups, staging and rollback (more if assets grow).\nClose Skullgirls first. Keep this window open until the operation finishes.";
            message += $"\n\nArchive folder: {game.DataFolder}";
            if (MessageBox.Show(this, message, restore ? "Restore originals" : "Apply enabled mods", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            if (!restore && ArchiveCompatibility.Required(game.GameFolder, plan.Replacements.Keys) && !ArchiveCompatibility.Installed(game.GameFolder))
            {
                if (!ArchiveCompatibility.RestoreSaved(game.GameFolder, game.Profile))
                {
                    InstallArchiveCompatibility();
                    if (!ArchiveCompatibility.Installed(game.GameFolder)) return;
                }
                AppendLog("Mod support restored. Continuing with the selected mods.");
            }
            await Task.Run(() => game.Apply(snapshot, restore));
            status.Text = restore ? "Originals restored. Enabled mod selections were kept." : "Mods applied successfully. You can launch Skullgirls.";
            changeGrid.Rows.Clear();
        });
    }
    private string? SelectedArchive => archiveGrid.CurrentRow?.Cells[0].Value as string;
    private async Task UpdateArchive()
    {
        if (SelectedArchive is not { } name) { status.Text = "Select an archive in Game archives first."; return; }
        await Run(async () =>
        {
            var game = RequireGame();
            var reviewed = await Task.Run(() => game.ReviewArchiveUpdate(name));
            if (reviewed.CurrentHash == reviewed.OriginalHash && reviewed.CurrentHash == reviewed.LastHash)
            { status.Text = $"{name} already matches its baseline."; return; }
            string message = $"Keep the currently installed {name} as the new baseline?\n\nThe installed game file will not change. Any customizations already in this archive become part of the baseline: Restore originals will keep them, and disabling mods will not remove them.\n\nThe previous backup and profile state will be preserved separately in:\n{game.BaselineHistory}\n\nClose Skullgirls first. Preview your enabled mods again afterward.";
            if (MessageBox.Show(this, message, "Update archive baseline", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            string? history = await Task.Run(() => game.UpdateArchiveBaseline(reviewed));
            changeGrid.Rows.Clear();
            status.Text = history == null ? "Baseline already current." : $"Updated {name} baseline. Previous backup preserved. Preview enabled mods before applying.";
        });
    }
    private void OpenAssetBrowser(string name)
    {
        using var browser = new AssetBrowser(Path.Combine(RequireGame().DataFolder, name), library);
        browser.ModCreated += mod => { ReloadMods(mod.Folder); Dirty(); AppendLog($"Created mod folder: {mod.Folder}"); };
        browser.AssetStaged += saved => { ReloadMods(saved.Mod.Folder); Dirty(); AppendLog($"Staged {Path.GetFileName(saved.Destination)} in {saved.Mod.Info.Name}. Game files have not changed."); };
        browser.ShowDialog(this);
    }
    private void InspectArchive()
    {
        if (SelectedArchive is not { } name) return;
        try
        {
            OpenAssetBrowser(name);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not inspect archive"); }
    }
    private async Task ExtractArchive()
    {
        if (SelectedArchive is not { } name) return;
        using var picker = new FolderBrowserDialog { Description = "Choose a parent folder for the extracted archive", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await Run(async () =>
        {
            string source = Path.Combine(RequireGame().DataFolder, name);
            string output = Path.Combine(picker.SelectedPath, Path.GetFileNameWithoutExtension(name) + "-extracted-" + Guid.NewGuid().ToString("N")[..8]);
            AppendLog($"Extracting {name} to {output}...");
            await Task.Run(() => Archives.Extract(source, output));
            AppendLog("Extraction complete. These are copies; game files were not changed."); OpenFolder(output);
        });
    }
    private void Launch()
    {
        try
        {
            var game = RequireGame();
            if (game.NeedsRecovery) throw new InvalidOperationException("Recover the interrupted operation before launching.");
            Process.Start(new ProcessStartInfo("steam://rungameid/245170") { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not launch"); }
    }
    private static void OpenFolder(string path) { Directory.CreateDirectory(path); FolderLauncher.Open(path); }
    private static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.00} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";

    public Task ConnectForSmoke() => LoadGame(gamePath.Text);

    public async Task SmokePreview()
    {
        // Disposable GUI fixture; never writes to the selected game installation.
        string folder = Path.Combine(library.Root, "smoke");
        Directory.CreateDirectory(Path.Combine(folder, "core.gfs", "core"));
        new ModInfo { Id = "smoke.example", Name = "Example music configuration", Author = "Preview fixture", Version = new Version(1, 0), Description = "Disposable UI test fixture." }.Write(Path.Combine(folder, "mod.json"));
        File.WriteAllText(Path.Combine(folder, "core.gfs", "core", "music.ini"), "UI fixture only");
        string looseFixture = Path.Combine(folder, "loose", "Digital Art Compendium", "SGMM-Smoke", "preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(looseFixture)!);
        using (var fixtureImage = new Bitmap(1,1)) fixtureImage.Save(looseFixture, System.Drawing.Imaging.ImageFormat.Png);
        var mod = new Mod(folder); mod.SetEnabled(true);
        ReloadMods(folder);
        await Preview();
        if (modGrid.Rows.Count != 1 || changeGrid.Rows.Count != 2 || archiveGrid.Rows.Count < 17 || !changeGrid.Rows.Cast<DataGridViewRow>().Any(r => r.Cells[0].Value?.ToString() == "Loose files")) throw new InvalidOperationException($"GUI smoke test failed: mods={modGrid.Rows.Count}, changes={changeGrid.Rows.Count}, archives={archiveGrid.Rows.Count}; {log.Text}");
        File.WriteAllText(Path.Combine(Program.ProjectRoot, "ui-smoke-results.txt"), "PASS connected game indexes, imported fixture view, enabled state, change preview, archive table. No game archive was written.\n");
    }

    public async Task SmokeReview()
    {
        IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        var selected = Selected ?? throw new InvalidOperationException("Smoke fixture is not selected.");
        var updateButton = Descendants(body).OfType<Button>().Single(b => b.Text == "Update mod…");
        if (!updateButton.Visible || !updateButton.Parent!.ClientRectangle.Contains(updateButton.Bounds))
            throw new InvalidOperationException("Update mod button is not visible.");
        string input = Path.Combine(dataRoot, "update-fixture"); Directory.CreateDirectory(Path.Combine(input, "core.gfs", "core"));
        new ModInfo { Id = selected.Info.Id, Name = selected.Info.Name, Version = new Version(2, 0) }.Write(Path.Combine(input, "mod.json"));
        File.WriteAllText(Path.Combine(input, "core.gfs", "core", "music.ini"), "updated fixture only");
        using (var update = library.PrepareUpdate(selected, input))
        {
            using var dialog = CreateUpdateReview(update, false, out var mapping);
            dialog.Show(this); dialog.PerformLayout();
            var confirm = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Update library");
            if (!confirm.Visible || !confirm.Enabled || mapping.Visible || !confirm.Parent!.ClientRectangle.Contains(confirm.Bounds))
                throw new InvalidOperationException("Update review controls are not visible or correctly enabled.");
            using var screenshot = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, dialog.Size));
            screenshot.Save(Path.Combine(Program.ProjectRoot, "mod-update-preview.png")); dialog.Close();
            selected = library.CommitUpdate(update);
        }
        ReloadMods(selected.Folder);
        using (var rollback = library.PrepareRollback(selected))
        {
            using var dialog = CreateUpdateReview(rollback, true, out _); dialog.Show(this);
            if (!Descendants(dialog).OfType<Button>().Any(b => b.Text == "Restore version" && b.Visible && b.Enabled))
                throw new InvalidOperationException("Rollback review action is missing.");
            dialog.Close(); selected = library.CommitUpdate(rollback);
        }
        new ModInfo { Id = "smoke.different", Name = "Different identity", Version = new Version(3, 0) }.Write(Path.Combine(input, "mod.json"));
        using (var update = library.PrepareUpdate(selected, input))
        {
            using var dialog = CreateUpdateReview(update, false, out var mapping); dialog.Show(this);
            var confirm = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Update library");
            if (!mapping.Visible || confirm.Enabled) throw new InvalidOperationException("Different ID does not require mapping in review.");
            mapping.Checked = true; if (!confirm.Enabled) throw new InvalidOperationException("Explicit mapping does not enable confirmation.");
            dialog.Close();
        }
        var other = library.CreateDraft("Second contribution");
        Directory.CreateDirectory(Path.Combine(other.Folder, "core.gfs", "core"));
        File.WriteAllText(Path.Combine(other.Folder, "core.gfs", "core", "music.ini"), "later fixture only"); other.SetEnabled(true);
        library.SetOrder([selected, other]); ReloadMods(other.Folder); await Preview();
        var row = changeGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is AssetOutcome o && o.Conflict);
        changeGrid.CurrentCell = row.Cells[1]; ShowContributors();
        if (contributorGrid.Rows.Count != 2 || contributorGrid.Rows[^1].Tag as string != other.Folder || !conflictExplanation.Text.Contains("wins")
            || contributorGrid.DisplayedRowCount(false) < 2)
            throw new InvalidOperationException("Conflict contributors do not identify final winner.");
        using (var screenshot = new Bitmap(Width, Height)) { DrawToBitmap(screenshot, new Rectangle(Point.Empty, Size)); screenshot.Save(Path.Combine(Program.ProjectRoot, "conflicts-preview.png")); }
        var normalSize = Size; Size = MinimumSize; PerformLayout();
        if (contributorGrid.DisplayedRowCount(false) < 1) throw new InvalidOperationException("Conflict contributors are hidden at minimum window size.");
        Size = normalSize; PerformLayout();
        NavigateContributor(); if (Selected?.Folder != other.Folder) throw new InvalidOperationException("Conflict navigation selected the wrong mod.");
        await MoveMod(-1);
        if (changeGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<AssetOutcome>().Single(o => o.Conflict).Winner.ModFolder != selected.Folder)
            throw new InvalidOperationException("Conflict winner did not update after reordering.");
        // Do not trigger the tab's automatic inspection while the smoke test awaits its own inspection.
        await Run(async () => { tabs.SelectedIndex = 3; await RefreshRecoveryStatus(); });
        using (var screenshot = new Bitmap(Width, Height)) { DrawToBitmap(screenshot, new Rectangle(Point.Empty, Size)); screenshot.Save(Path.Combine(Program.ProjectRoot, "recovery-preview.png")); }
        if (!recoverySummary.Text.Contains("No recovery issues") || recoveryGrid.Rows.Count != 0)
            throw new InvalidOperationException("Fresh isolated profile has unexpected recovery issues.");
        tabs.SelectedIndex = 0;
        File.AppendAllText(Path.Combine(Program.ProjectRoot, "ui-smoke-results.txt"), "PASS update and rollback review controls, preserved selection, conflict winner, contributor navigation, reordering, and read-only recovery status.\n");
    }

    public async Task SmokeAssets()
    {
        var game = RequireGame();
        tabs.SelectedIndex = 1;
        IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        var updateButton = Descendants(tabs.TabPages[1]).OfType<Button>().Single(b => b.Text == "Update archive…");
        var modUpdateButton = Descendants(body).OfType<Button>().Single(b => b.Text == "Update mod…");
        if (!modUpdateButton.Visible || !modUpdateButton.Parent!.ClientRectangle.Contains(modUpdateButton.Bounds))
            throw new InvalidOperationException("Update mod button is not visible within the mod toolbar.");
        if (!updateButton.Visible || !updateButton.Parent!.ClientRectangle.Contains(updateButton.Bounds))
            throw new InvalidOperationException("Update archive button is not visible within the archive toolbar.");
        using (var shot = new Bitmap(Width, Height))
        {
            DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
            shot.Save(Path.Combine(Program.ProjectRoot, "game-archives-preview.png"));
        }
        foreach (var sample in new[] {
            (Archive: "story_art.gfs", Asset: "story_art/Cinematics/Annie/01.dds", Kind: "Image", Shot: "asset-image-preview.png"),
            (Archive: "story_art.gfs", Asset: "story_art/THs/annie/annie.dds", Kind: "Image", Shot: "asset-story-portrait-preview.png"),
            (Archive: "sprites.gfs", Asset: "sprites/character_select_annie.dds", Kind: "Image", Shot: "asset-standalone-texture-preview.png"),
            (Archive: "core.gfs", Asset: "core/music.ini", Kind: "Text", Shot: "asset-text-preview.png"),
            (Archive: "music-win.gfs", Asset: "music/BunnyBurrow_Intro.wav", Kind: "Audio", Shot: "asset-audio-preview.png"),
            (Archive: "ui.gfs", Asset: "ui/palettized_hud_art/character_annie_colors.pcx", Kind: "Image", Shot: "asset-pcx-preview.png"),
            (Archive: "characters-art.gfs", Asset: "characters/annie.art", Kind: "Workshop", Shot: "asset-workshop-preview.png") })
        {
            using var browser = new AssetBrowser(Path.Combine(game.DataFolder, sample.Archive), library);
            browser.Show(this); await browser.SmokeSelect(sample.Asset);
            if (browser.PreviewKind != sample.Kind) throw new InvalidOperationException($"Preview failed: {sample.Asset}: {browser.PreviewDescription}");
            if (sample.Kind == "Audio") { browser.VerifyAudioDevice(); await browser.VerifyPlaybackVisual(); }
            if (sample.Shot == "asset-image-preview.png") await browser.SmokeCharacterFilter("Annie");
            using var bitmap = new Bitmap(browser.Width, browser.Height);
            browser.DrawToBitmap(bitmap, new Rectangle(Point.Empty, browser.Size)); bitmap.Save(Path.Combine(Program.ProjectRoot, sample.Shot));
            if (sample.Shot == "asset-image-preview.png") await browser.SmokeArchiveFilterFallback();
            browser.Close();
        }
        File.AppendAllText(Path.Combine(Program.ProjectRoot, "ui-smoke-results.txt"), "PASS actual DDS/PCX/text previews, character-filter archive fallback, and real-device silent PCM playback: position, pause, paused seek, resume, loop, completion and Stop.\n");
        using var composer = new AssetBrowser(Path.Combine(game.DataFolder, "core.gfs"), library);
        composer.Show(this); await composer.SmokeSelect("core/music.ini");
        await composer.SmokeStage(Path.Combine(dataRoot, "drop-fixture.ini"));
        using (var bitmap = new Bitmap(composer.Width, composer.Height))
        { composer.DrawToBitmap(bitmap, new Rectangle(Point.Empty, composer.Size)); bitmap.Save(Path.Combine(Program.ProjectRoot, "mod-composer-preview.png")); }
        composer.Close();
        File.AppendAllText(Path.Combine(Program.ProjectRoot, "ui-smoke-results.txt"), "PASS drop workflow creates a disabled mod, uses exact asset path and previews the staged replacement without changing the game.\n");
        string fixture = Path.Combine(dataRoot, "composer-image-fixture");
        string seed = Path.Combine(fixture, "seed"); Directory.CreateDirectory(Path.Combine(seed, "ui"));
        byte[] originalTexture = TextureEncoding.Encode(new(4, 4, Enumerable.Repeat(Color.Goldenrod.ToArgb(), 16).ToArray(), "fixture"), new(4, 4, "DXT5", 1));
        File.WriteAllBytes(Path.Combine(seed, "ui", "demo.dds"), originalTexture);
        string fixtureData = Path.Combine(fixture, "data01"); Directory.CreateDirectory(fixtureData);
        string fixtureArchive = Path.Combine(fixtureData, "ui-fixture.gfs");
        using (var writer = new ThemModdingHerds.IO.Binary.Writer(fixtureArchive))
            ThemModdingHerds.GFS.RevergePackageExt.Write(writer, ThemModdingHerds.GFS.RevergePackage.Create(seed));
        using var imageComposer = new AssetBrowser(fixtureArchive, library);
        imageComposer.Show(this); await imageComposer.SmokeSelect("ui/demo.dds"); await imageComposer.SmokeImageDrop(Path.Combine(fixture, "dropped.png")); imageComposer.Close();
        File.AppendAllText(Path.Combine(Program.ProjectRoot, "ui-smoke-results.txt"), "PASS PNG file-drop events convert to matching DDS and preview the staged image. Multi-file drop rejected.\n");
    }
}
