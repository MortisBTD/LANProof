using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LANProof.App;
using LANProof.App.Controls;
using LANProof.Runtime;

[assembly: AvaloniaTestApplication(typeof(LANProof.Tests.UiTestBuilder))]

namespace LANProof.Tests;

public static class UiTestBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class ThemeUiTests
{
    private sealed class ImmediateProbe(LANProof.Core.Models.ProbeType type) : LANProof.Core.Interfaces.IProbe
    {
        public LANProof.Core.Models.ProbeType Type=>type;
        public string Name=>"fixture";
        public Task<LANProof.Core.Models.ProbeResult> ExecuteAsync(CancellationToken ct=default)=>Task.FromResult(
            new LANProof.Core.Models.ProbeResult {ProbeType=Type,IsSuccess=true,RoundTripMs=12,PacketLossPercent=0});
    }

    [AvaloniaFact]
    public async Task NewMeasurementsUpdateExistingPageAndReadOnlyNewRows()
    {
        var directory=Path.Combine(Path.GetTempPath(),"LANProof-live-ui-"+Guid.NewGuid().ToString("N"));
        var session=new MonitoringSession(directory,_=>Enum.GetValues<LANProof.Core.Models.ProbeType>()
            .Select(t=>(LANProof.Core.Interfaces.IProbe)new ImmediateProbe(t)).ToList());
        var window=new MainWindow(session,autoStart:false,appearancePath:Path.Combine(directory,"appearance.json"));
        try
        {
            window.Show();
            var once=window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="MeasureOnce");
            for(int i=0;i<300 && !once.IsEnabled;i++) await Task.Delay(10);
            Assert.True(once.IsEnabled);
            var page=window.GetLogicalDescendants().OfType<ContentControl>().Single(c=>c.Name=="PageContent");
            var original=page.Content;
            for(int count=1;count<=2;count++)
            {
                await session.MeasureAsync();
                for(int i=0;i<300 && window.VisiblePeriodMeasurementCount!=count*5;i++) await Task.Delay(10);
                Assert.Equal(count*5,window.VisiblePeriodMeasurementCount);
                Assert.Equal(count*5,window.HistoryRowsRead);Assert.Same(original,page.Content);
            }
            var preview=Environment.GetEnvironmentVariable("LANPROOF_UI_QA_DIR");
            if(!string.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(preview);await Task.Delay(220);Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var frame=window.CaptureRenderedFrame();
                Assert.NotNull(frame);frame.Save(Path.Combine(preview,"dark-overview-synthetic.png"));
            }
        }
        finally
        {
            window.Close();for(int i=0;i<200 && window.IsVisible;i++) await Task.Delay(10);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);
        }
    }

    [AvaloniaFact]
    public async Task EveryPageRendersInDarkAndLightThemes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LANProof-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window = new MainWindow(new MonitoringSession(directory), autoStart: false,
            appearancePath: Path.Combine(directory,"appearance.json"));
        try
        {
            GlassDesign.SetDark(true); Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            window.Show();
            var once = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "MeasureOnce");
            for (int i=0; i<200 && !once.IsEnabled; i++) await Task.Delay(10);
            Assert.True(once.IsEnabled);
            foreach (var mode in new[] { "dark", "light" })
            {
                if (mode == "light")
                {
                    var toggle = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ThemeToggle");
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (int i=0; i<200 && GlassDesign.IsDark; i++) await Task.Delay(10);
                    Assert.False(GlassDesign.IsDark);
                }
                for (int page=0; page<6; page++)
                {
                    var navigation = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Nav"+page);
                    navigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var preview = Environment.GetEnvironmentVariable("LANPROOF_UI_QA_DIR");
                    if (!string.IsNullOrEmpty(preview)) await Task.Delay(220);
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    if (!string.IsNullOrEmpty(preview))
                    {
                        Directory.CreateDirectory(preview);
                        frame.Save(Path.Combine(preview,$"{mode}-{page}.png"));
                    }
                }
            }
        }
        finally
        {
            window.Close();
            for (int i=0; i<200 && window.IsVisible; i++) await Task.Delay(10);
            Assert.False(window.IsVisible);
            GlassDesign.SetDark(true); Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory,true);
        }
    }

    [AvaloniaFact]
    public void ThemeRecolorsExistingGlassAndTextInBothDirections()
    {
        var text = (SolidColorBrush)GlassDesign.Ink;
        var panel = GlassDesign.Gradient("#D9FFFFFF", "#8FFFFFFF");
        GlassDesign.SetDark(true);
        Assert.Equal(Color.Parse("#EAF0FA"), text.Color);
        Assert.Equal(Color.Parse("#DA202D43"), panel.GradientStops[0].Color);
        GlassDesign.SetDark(false);
        Assert.Equal(Color.Parse("#202839"), text.Color);
        Assert.Equal(Color.Parse("#D9FFFFFF"), panel.GradientStops[0].Color);
        GlassDesign.SetDark(true);
    }

    [AvaloniaFact]
    public async Task ThemeButtonSwitchesAndPersistsWithoutRestartAndWindowIconLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LANProof-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            GlassDesign.SetDark(true);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            var preferences = Path.Combine(directory,"appearance.json");
            var window = new MainWindow(new MonitoringSession(directory), autoStart: false, appearancePath: preferences);
            Assert.NotNull(window.Icon);
            var button = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ThemeToggle");
            Assert.Equal("Светлая тема", button.Content);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i=0; i<200 && !Equals(button.Content,"Тёмная тема"); i++) await Task.Delay(10);
            Assert.Equal("Тёмная тема", button.Content);
            Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
            Assert.False(ThemePreferences.Load(preferences).DarkMode);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i=0; i<200 && !Equals(button.Content,"Светлая тема"); i++) await Task.Delay(10);
            Assert.Equal("Светлая тема", button.Content);
            Assert.True(ThemePreferences.Load(preferences).DarkMode);
            Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
        }
        finally
        {
            GlassDesign.SetDark(true);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Directory.Delete(directory,true);
        }
    }
}
