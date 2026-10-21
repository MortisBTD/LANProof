using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using LANProof.App.Controls;
using LANProof.Runtime;
using LANProof.Core.Diagnostics;
using LANProof.Export;

namespace LANProof.App;

internal static class Program
{
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--smoke") return SmokeAsync(args[1]).GetAwaiter().GetResult();
        App.LaunchInBackground = Array.Exists(args, a => a == "--background");
        try
        {
            using var instance = OperatingSystem.IsWindows() ? new SingleInstance() : null;
            if (instance is { IsPrimary:false }) return 0;
            Instance = instance;
            BuildApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            var directory = ApplicationPaths.DataDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "startup-error.log"), ex.ToString());
            return 1;
        }
    }

    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    private static async Task<int> SmokeAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var session = new MonitoringSession(directory);
        await session.InitializeAsync();
        var start = DateTime.UtcNow;
        await session.MeasureAsync();
        await session.StopAsync();
        var (measurements, events) = await session.ReadAsync(start, DateTime.UtcNow);
        var report = new CorrelationEngine().Analyze(events, measurements, start, DateTime.UtcNow);
        var exporter = new ReportExportService();
        await exporter.ExportToJsonAsync(report, Path.Combine(directory, "smoke.json"));
        await exporter.ExportToCsvAsync(report, Path.Combine(directory, "smoke.csv"));
        await exporter.ExportToPdfAsync(report, Path.Combine(directory, "smoke.pdf"));
        File.WriteAllText(Path.Combine(directory, "result.txt"), $"Measurements={measurements.Count}; Events={events.Count}; {report.Summary}");
        return measurements.Count == 5 ? 0 : 2;
    }
}

public sealed class App : Application
{
    public static bool LaunchInBackground { get; set; }

    public override void Initialize()
    {
        var dark = ThemePreferences.Load(ApplicationPaths.ThemeFile).DarkMode;
        GlassDesign.SetDark(dark);
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(s => s.OfType<Button>().Class("glass-action").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, GlassDesign.Gradient("#FFFFFFFF", "#DAFFFFFF")),
                new Setter(ContentPresenter.BorderBrushProperty, GlassDesign.Brush("#FFFFFFFF")) }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("glass-primary").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, GlassDesign.Gradient("#65B6FF", "#0A79EF")),
                new Setter(ContentPresenter.ForegroundProperty, Brushes.White) }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("glass-primary").Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, GlassDesign.Gradient("#1B88EA", "#0869CC")),
                new Setter(ContentPresenter.ForegroundProperty, Brushes.White) }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow(background: true);
            window.Closed += (_, _) => desktop.Shutdown();
            Program.Instance?.Listen(() => Avalonia.Threading.Dispatcher.UIThread.Post(window.RestoreWindow));
            if (LaunchInBackground) window.StartWithoutWindow();
            else desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
