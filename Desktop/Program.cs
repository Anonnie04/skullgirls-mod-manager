using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace SkullgirlsModManager.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<DesktopApp>()
        .UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}

public sealed class DesktopApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://SkullgirlsModManager/"))
        { Source = new Uri("avares://SkullgirlsModManager/DesktopTheme.axaml") });
        RequestedThemeVariant = ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args ?? []);
        base.OnFrameworkInitializationCompleted();
    }
}
