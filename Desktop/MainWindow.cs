using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SkullgirlsModManager.Desktop;

public sealed class MainWindow : Window
{
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkullgirlsModManager");
    private readonly Library library;
    private Manager? manager;
    private readonly TextBox game = new() { Watermark = "Skullgirls game folder (Steam)", MinWidth = 420 };
    private readonly StackPanel rows = new() { Spacing = 8 };
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 170 };
    private readonly TextBlock status = new() { Text = "Choose your Skullgirls folder to preview and apply mods." };
    private readonly StackPanel main = new() { Spacing = 10, Margin = new Thickness(6) };
    private List<Mod> mods = [];
    private string? reviewed;

    public MainWindow(string[] args)
    {
        Title = "Skullgirls Mod Manager"; Width = 1100; Height = 860; MinWidth = 800; MinHeight = 600;
        library = new Library(Path.Combine(data, "Mods"));
        var heading = new Grid();
        heading.Children.Add(new DecoHeader());
        heading.Children.Add(new TextBlock { Text = "SKULLGIRLS", FontFamily = new Avalonia.Media.FontFamily("DejaVu Serif"), FontSize = 30,
            FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 58, 0, 0), Classes = { "title" } });
        heading.Children.Add(new TextBlock { Text = "M O D   A T E L I E R", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 98, 0, 0), Classes = { "section" } });
        main.Children.Add(heading);
        main.Children.Add(new TextBlock { Text = "01  /  GAME INSTALLATION", Classes = { "section" }, FontSize = 12 });
        main.Children.Add(Line(game, Button("Browse…", BrowseGame), Button("Connect", Connect)));
        main.Children.Add(status);
        main.Children.Add(Line(Button("Import ZIP…", ImportZip), Button("Import folder…", ImportFolder),
            Button("Set up mod support…", SetupSupport), Button("Recover", Recover)));
        main.Children.Add(new TextBlock { Text = "02  /  MOD LIBRARY  ·  LATER MODS TAKE PRECEDENCE", FontSize = 16, Classes = { "section" } });
        main.Children.Add(new ScrollViewer { Content = rows, Height = 130 });
        main.Children.Add(Line(Button("Preview changes", Preview), Button("Apply reviewed changes…", Apply), Button("Restore originals…", Restore)));
        main.Children.Add(new TextBlock { Text = "03  /  REVIEW & ACTIVITY", Classes = { "section" }, FontSize = 12 });
        main.Children.Add(output);
        Content = new ScrollViewer { Content = new DecoFrame(main) };
        Reload();
        string config = Path.Combine(data, "game.txt");
        Opened += async (_, _) => await Run(async () =>
        {
            string? saved = null;
            try { if (File.Exists(config)) saved = File.ReadAllText(config).Trim(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string found = await Task.Run(() => GameDiscovery.Find(saved));
            if (found.Length > 0) { game.Text = found; await Connect(); }
            if (args.Length == 2 && args[0] == "--import") await Import(args[1]);
            if (args.Length == 2 && args[0] == "--render-preview")
            {
                Reload(); main.IsEnabled = true;
                await Task.Delay(500);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                bitmap.Render(this); bitmap.Save(args[1]); Close();
            }
        });
    }
    private static StackPanel Line(params Control[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        if (label == "Apply reviewed changes…") button.Classes.Add("primary");
        button.Click += async (_, _) => await Run(action);
        return button;
    }
    private async Task Run(Func<Task> action)
    {
        main.IsEnabled = false;
        try { await action(); }
        catch (Exception e) { output.Text = e.Message; }
        finally { main.IsEnabled = true; }
    }
    private void Reload()
    {
        reviewed = null;
        mods = library.Mods(); rows.Children.Clear();
        foreach (var mod in mods)
        {
            var enabled = new CheckBox { Content = $"{mod.Info.Name}  {mod.Info.Version}", IsChecked = mod.Enabled, MinWidth = 420 };
            enabled.IsCheckedChanged += (_, _) => { mod.SetEnabled(enabled.IsChecked == true); reviewed = null; };
            var row = Line(enabled, Button("↑", () => Move(mod, -1)), Button("↓", () => Move(mod, 1)));
            if (mod.Info.OptionalPaths.Count > 0) row.Children.Add(Button("Options…", () => Options(mod)));
            rows.Children.Add(row);
        }
        if (mods.Count == 0) rows.Children.Add(new TextBlock { Text = "No mods imported. Your original ZIP files are kept unchanged." });
    }
    private async Task Options(Mod mod)
    {
        var selected = mod.OptionalSelections();
        var choices = mod.Info.OptionalPaths.Select(o => new CheckBox { Content = o.Name, IsChecked = selected[o.Id] }).ToList();
        var dialog = new Window { Title = mod.Info.Name + " — Optional components", Width = 540, Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Choose components, then Preview and Apply to update the game.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var list = new StackPanel { Spacing = 8 };
        foreach (var choice in choices) list.Children.Add(choice);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 220 });
        var save = new Button { Content = "Save" }; var cancel = new Button { Content = "Cancel" };
        bool accepted = false;
        save.Click += (_, _) => { accepted = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        panel.Children.Add(Line(cancel, save)); dialog.Content = panel;
        await dialog.ShowDialog(this);
        if (!accepted) return;
        mod.SetOptionalSelections(mod.Info.OptionalPaths.Select((o, i) => (o.Id, Enabled: choices[i].IsChecked == true)).ToDictionary(o => o.Id, o => o.Enabled));
        reviewed = null; output.Text = "Optional components saved. Preview changes, then Apply to update the game.";
    }
    private Task Move(Mod mod, int offset)
    {
        int from = mods.IndexOf(mod), to = from + offset;
        if (to >= 0 && to < mods.Count) { (mods[from], mods[to]) = (mods[to], mods[from]); library.SetOrder(mods); Reload(); }
        return Task.CompletedTask;
    }
    private async Task<string?> PickFolder(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickFile(string title, bool zipOnly = false)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false,
            FileTypeFilter = zipOnly ? [new FilePickerFileType("Mod ZIP") { Patterns = ["*.zip"] }] : null });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task BrowseGame() { var path = await PickFolder("Choose Skullgirls game folder"); if (path != null) { game.Text = path; await Connect(); } }
    private Task Connect()
    {
        manager = null; reviewed = null;
        var connected = new Manager(game.Text ?? "", data);
        connected.Log = message => Dispatcher.UIThread.Post(() => output.Text += message + Environment.NewLine);
        manager = connected;
        File.WriteAllText(Path.Combine(data, "game.txt"), connected.GameFolder);
        status.Text = $"Connected: {connected.DataFolder}" + (File.Exists(Path.Combine(connected.GameFolder, "SkullGirls.exe")) ? " · Windows / Proton" : " · Native Linux" + (NativeCompatibility.Installed(connected.GameFolder) ? " · Mod support ready" : " · Mod support not set up")) + (connected.NeedsRecovery ? " · Recovery required" : "");
        return Task.CompletedTask;
    }
    private async Task ImportZip() { var path = await PickFile("Import mod ZIP", true); if (path != null) await Import(path); }
    private async Task ImportFolder() { var path = await PickFolder("Import mod folder"); if (path != null) await Import(path); }
    private async Task Import(string path)
    {
        var imported = await Task.Run(() => library.Import(path));
        Reload();
        output.Text = $"Imported {imported.Info.Name} {imported.Info.Version}. Enable it and preview changes.\nOriginal package: {path}";
    }
    private Manager Connected() => manager ?? throw new InvalidOperationException("Connect to your Skullgirls folder first.");
    private static string Describe(Plan plan) => string.Join(Environment.NewLine, plan.Changes.Select(c =>
        $"{c.Action}: {c.Archive}/{c.Asset} ← {c.Mod}" + (c.Conflict ? " [conflict: later mod wins]" : "")));
    private async Task Preview()
    {
        var connected = Connected();
        var plan = await Task.Run(() => connected.Preview(library.Mods()));
        reviewed = Describe(plan);
        string support = NativeCompatibility.IsNative(connected.GameFolder) && !NativeCompatibility.Installed(connected.GameFolder)
            && plan.Replacements.Keys.Any(a => ArchiveCompatibility.ProtectedArchives.Contains(a, StringComparer.OrdinalIgnoreCase))
            ? "NATIVE SETUP REQUIRED · Click Set up mod support before applying.\n\n" : "";
        output.Text = plan.Changes.Count == 0 ? "No changes to apply. Enable a mod in the library first." : support + reviewed;
    }
    private async Task<bool> Confirm(string title, string message)
    {
        bool accepted = false;
        var dialog = new Window { Title = title, Width = 540, Height = 220, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = "Continue" }; var cancel = new Button { Content = "Cancel" };
        yes.Click += (_, _) => { accepted = true; dialog.Close(); }; cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 20, Children = {
            new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Line(cancel, yes) } };
        await dialog.ShowDialog(this); return accepted;
    }
    private async Task Apply()
    {
        var connected = Connected(); var selected = library.Mods();
        var plan = await Task.Run(() => connected.Preview(selected));
        if (reviewed == null || reviewed != Describe(plan)) { reviewed = null; throw new InvalidOperationException("Preview the current selection before applying."); }
        if (!await Confirm("Apply enabled mods", $"Apply {plan.Changes.Count} changes to {connected.DataFolder}? Close Skullgirls first. Verified originals will be backed up.")) return;
        output.Text = "";
        await Task.Run(() => connected.Apply(selected)); reviewed = null;
        output.Text += "Apply complete.";
    }
    private async Task Restore()
    {
        var connected = Connected();
        if (!await Confirm("Restore originals", "Restore original game archives and the native executable from verified backups? Close Skullgirls first.")) return;
        await Task.Run(() => { connected.Apply([], true); NativeCompatibility.Restore(connected.GameFolder, connected.Profile); }); reviewed = null; output.Text = "Originals restored.";
    }
    private async Task Recover() { await Task.Run(() => Connected().Recover()); reviewed = null; output.Text = "Recovery complete."; }
    private async Task SetupSupport()
    {
        var connected = Connected();
        if (NativeCompatibility.IsNative(connected.GameFolder))
        {
            if (!await Confirm("Native Linux mod support", "Enable archive replacement mods for this verified native game build? This changes one archive-verification branch in the executable and saves a verified original backup. Restore originals undoes it.")) return;
            await Task.Run(() => NativeCompatibility.Install(connected.GameFolder, connected.Profile));
            await Connect();
            reviewed = null;
            output.Text = "Native mod support installed. Enable the mod, preview changes, then apply. Restore originals also restores the original executable.";
            return;
        }
        var path = await PickFile("Choose the supported compatibility loader ZIP or CoreMessaging.dll");
        if (path == null) return;
        if (!await Confirm("Set up mod support", "Install the supplied compatibility loader into the Windows game folder? On Linux this is for the game running through Proton. Close Skullgirls first.")) return;
        await Task.Run(() => ArchiveCompatibility.Install(connected.GameFolder, connected.Profile, path));
        await Connect(); output.Text = "Mod support installed. If using Proton, configure its DLL override as described in the Linux build guide.";
    }
}
