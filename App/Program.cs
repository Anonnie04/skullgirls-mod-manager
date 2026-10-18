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
        using var mutex = new Mutex(true, "SkullgirlsModManager.Desktop", out bool first);
        if (!first) { MessageBox.Show("Skullgirls Mod Manager is already open."); return; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Skullgirls Mod Manager");
        bool smoke = args.Contains("--smoke-ui");
        var form = new MainForm(smoke ? Path.Combine(ProjectRoot, "UiSmokeData", "run-" + Guid.NewGuid().ToString("N")) : Path.Combine(ProjectRoot, "Data"), smoke ? null : Path.Combine(ProjectRoot, "Mods"));
        if (args.Contains("--smoke-ui"))
        {
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Delay(1000);
                    await form.SmokePreview();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(ProjectRoot, "ui-preview.png"));
                    await form.SmokeAssets();
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

public class MainForm : Form
{
    private readonly string dataRoot;
    private readonly Library library;
    private Manager? manager;
    private readonly Settings settings;
    private List<Mod> mods = [];
    private bool refreshing;
    private bool busy;
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
    private readonly ProgressBar progress = new() { Width = 120, Height = 12, Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 30 };
    private readonly DecoTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
    private readonly FlowLayoutPanel footerButtons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Right };

    public MainForm(string root, string? modsFolder = null)
    {
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
        Shown += async (_, _) => await Run(() => LoadGame(gamePath.Text));
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
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(0, 0, 16, 0) };
        foreach (float height in new float[] { 28, 52, 0, 48, 30 }) left.RowStyles.Add(height == 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, height));
        var modsLink = new LinkLabel { Text = "YOUR MODS  •  OPEN FOLDER", Font = new Font("Segoe UI Semibold", 10), AutoSize = true, LinkColor = Theme.Gold, ActiveLinkColor = Theme.Text };
        modsLink.LinkClicked += (_, _) => OpenFolder(library.Root);
        left.Controls.Add(modsLink, 0, 0);
        var imports = Toolbar();
        imports.Controls.Add(Button("Import folder", async () => await ImportFolder()));
        imports.Controls.Add(Button("Import ZIP", async () => await ImportZip()));
        imports.Controls.Add(Button("New mod", CreateMod));
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
        order.Controls.Add(Button("Move up", () => MoveMod(-1)));
        order.Controls.Add(Button("Move down", () => MoveMod(1)));
        order.Controls.Add(Button("Remove", RemoveMod));
        left.Controls.Add(order, 0, 3);
        modSummary.Text = "Later mods win overlapping replacements.";
        modSummary.Font = new Font("Segoe UI", 8.5f);
        left.Controls.Add(modSummary, 0, 4);
        body.Controls.Add(left, 0, 0);

        var preview = new WorkspacePage("Change preview") { Padding = new Padding(12) };
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        previewLayout.RowStyles.Add(new(SizeType.Absolute, 48)); previewLayout.RowStyles.Add(new(SizeType.Percent, 100));
        previewLayout.Controls.Add(new Label { Text = "Preview reads the enabled mods in order. Review replacements and conflicts before applying.", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 0);
        Column(changeGrid, "archive", "Archive", 25); Column(changeGrid, "asset", "Asset path", 40); Column(changeGrid, "mod", "Source mod", 23); Column(changeGrid, "action", "Action", 12);
        previewLayout.Controls.Add(changeGrid, 0, 1); preview.Controls.Add(previewLayout);
        var archiveTab = new WorkspacePage("Game archives") { Padding = new Padding(12) };
        var archiveLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        archiveLayout.RowStyles.Add(new(SizeType.Percent, 100)); archiveLayout.RowStyles.Add(new(SizeType.Absolute, 52));
        Column(archiveGrid, "archive", "GFS archive", 55); Column(archiveGrid, "entries", "Assets", 20); Column(archiveGrid, "size", "Size", 25);
        archiveLayout.Controls.Add(archiveGrid, 0, 0);
        var archiveActions = Toolbar();
        archiveActions.Controls.Add(Button("Preview assets", InspectArchive));
        archiveActions.Controls.Add(Button("Extract archive…", async () => await ExtractArchive()));
        archiveGrid.CellDoubleClick += (_, _) => InspectArchive();
        archiveLayout.Controls.Add(archiveActions, 0, 1); archiveTab.Controls.Add(archiveLayout);
        var detailTab = new WorkspacePage("Mod details") { Padding = new Padding(20) }; detailTab.Controls.Add(details);
        var toolsTab = new WorkspacePage("Backups & tools") { Padding = new Padding(20) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        tools.Controls.Add(new Label { Text = "Backups are isolated for each game installation.\nRestore uses the originals captured before this manager's first change.\nIf Steam updates an archive, the manager stops rather than overwriting it.", AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, 0, 0, 20) });
        tools.Controls.Add(Button("Open backup folder", () => OpenFolder(RequireGame().BackupFolder)));
        tools.Controls.Add(Button("Open mod library", () => OpenFolder(library.Root)));
        tools.Controls.Add(Button("Install archive compatibility…", InstallArchiveCompatibility));
        tools.Controls.Add(Button("Recover interrupted operation", async () => await Run(async () => { var game = RequireGame(); await Task.Run(game.Recover); status.Text = "Recovery complete. Preview before applying."; })));
        tools.Controls.Add(new Label { Text = "Velvet-compatible mod layout\n\nmod.json\ncore.gfs/\n    core/music.ini\n\nUse the exact internal path shown in the archive inspector.\nOnly include changed assets. Packed GFS files and TFH resources\nare not imported as replacement mods in this version.", AutoSize = true, Margin = new Padding(0, 24, 0, 0), Font = new Font("Consolas", 10) });
        toolsTab.Controls.Add(tools);
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
        catch (Exception ex) { AppendLog("ERROR: " + ex.Message); status.Text = "Operation stopped. See activity for details."; MessageBox.Show(this, ex.Message, "Operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
        manager = null; archiveGrid.Rows.Clear(); changeGrid.Rows.Clear();
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
    });
    private void MoveMod(int delta)
    {
        if (Selected is not { } selected) return;
        int old = mods.IndexOf(selected), next = old + delta;
        if (next < 0 || next >= mods.Count) return;
        mods.RemoveAt(old); mods.Insert(next, selected); library.SetOrder(mods); ReloadMods(selected.Folder); Dirty();
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
            using var picker = new OpenFileDialog { Title = "Select a compatibility ZIP or supported CoreMessaging.dll", InitialDirectory = library.Root, Filter = "Compatibility ZIP or loader|*.zip;CoreMessaging.dll" };
            if(picker.ShowDialog(this)!=DialogResult.OK) return;
            ArchiveCompatibility.Install(game.GameFolder,game.Profile,picker.FileName);
            AppendLog("Checksum-verified archive compatibility loader installed. No game executable or mod assets were changed.");
            status.Text="Archive compatibility is ready. Relaunch Skullgirls.";
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
        foreach (var change in plan.Changes)
        {
            int n = changeGrid.Rows.Add(change.Archive, change.Asset, change.Mod, change.Conflict ? "Override" : change.Action);
            if (change.Conflict) { changeGrid.Rows[n].DefaultCellStyle.BackColor = Theme.Burgundy; changeGrid.Rows[n].DefaultCellStyle.ForeColor = Theme.Gold; }
        }
        status.Text = $"{plan.Changes.Count:N0} planned entries • {plan.Changes.Count(c => c.Conflict)} overlaps • Later mods win.";
        if(manager != null && ArchiveCompatibility.Required(manager.GameFolder,plan.Replacements.Keys) && !ArchiveCompatibility.Installed(manager.GameFolder)) status.Text="Archive compatibility required: use Backups & tools → Install archive compatibility before Apply.";
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
            if (count == 0) { status.Text = "No changes to apply."; return; }
            var names = plan.Replacements.Keys.Concat(plan.Restore).ToList();
            long disk = names.Sum(n => new FileInfo(Path.Combine(game.DataFolder, n)).Length);
            disk += plan.LooseFiles.Values.Sum(p => new FileInfo(p).Length);
            disk += plan.LooseFiles.Keys.Concat(plan.LooseRestore).Distinct(StringComparer.OrdinalIgnoreCase).Sum(p => File.Exists(Path.Combine(game.GameFolder,p)) ? new FileInfo(Path.Combine(game.GameFolder,p)).Length : 0);
            string message = (restore ? "Restore backed-up originals" : "Apply the previewed changes") + $" to {plan.Replacements.Count + plan.Restore.Count} archive(s) and {looseCount} loose file(s)?\n\n" + string.Join("\n", names) +
                $"\n\nGame: {game.GameFolder}\nAllow roughly {FormatSize(disk * 3)} free space for backups, staging and rollback (more if assets grow).\nClose Skullgirls first. Keep this window open until the operation finishes.";
            if (MessageBox.Show(this, message, restore ? "Restore originals" : "Apply enabled mods", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            await Task.Run(() => game.Apply(snapshot, restore));
            status.Text = restore ? "Originals restored. Enabled mod selections were kept." : "Mods applied successfully. You can launch Skullgirls.";
            changeGrid.Rows.Clear();
        });
    }
    private string? SelectedArchive => archiveGrid.CurrentRow?.Cells[0].Value as string;
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

    public async Task SmokeAssets()
    {
        var game = RequireGame();
        tabs.SelectedIndex = 1;
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
