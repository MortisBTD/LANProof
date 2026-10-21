using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using LANProof.App;
using LANProof.App.Controls;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using LANProof.Runtime;

namespace LANProof.Tests;

public sealed class BackgroundUiTests
{
    private sealed class Probe : IProbe
    {
        public ProbeType Type=>ProbeType.Gateway;
        public string Name=>"fixture";
        public Task<ProbeResult> ExecuteAsync(CancellationToken ct=default)=>Task.FromResult(new ProbeResult
            {ProbeType=Type,IsSuccess=true,PacketLossPercent=0});
    }
    [AvaloniaFact] public async Task HiddenStartCloseRestorePauseAndExitKeepOneSession()
    {
        var dir=Path.Combine(Path.GetTempPath(),"LANProof-background-"+Guid.NewGuid().ToString("N"));
        var session=new MonitoringSession(dir,_=>[new Probe()]);
        var window=new MainWindow(session,appearancePath:Path.Combine(dir,"theme.json"),background:true);
        bool closed=false; window.Closed+=(_,_)=>closed=true;
        try
        {
            window.StartWithoutWindow();
            for(int i=0;i<400 && !session.IsRunning;i++) await Task.Delay(10);
            Assert.True(session.IsRunning); Assert.False(window.IsVisible);
            window.RestoreWindow(); Assert.True(window.IsVisible);
            window.Close(); Assert.False(window.IsVisible); Assert.False(closed); Assert.True(session.IsRunning);
            await session.MeasureAsync();
            var (rows,_)=await session.ReadAsync(DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow);
            Assert.True(rows.Count>=1);
            window.RestoreWindow(); await window.ToggleFromTray(); Assert.False(session.IsRunning);
            await window.ToggleFromTray(); Assert.True(session.IsRunning);
        }
        finally
        {
            window.ExitApplication();
            for(int i=0;i<400 && !closed;i++) await Task.Delay(10);
            Assert.True(closed); Assert.False(session.IsRunning);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir,true);
        }
    }

    [AvaloniaFact] public async Task TrayExitBeforeEverShowingWindowStopsSession()
    {
        var dir=Path.Combine(Path.GetTempPath(),"LANProof-hidden-exit-"+Guid.NewGuid().ToString("N"));
        var session=new MonitoringSession(dir,_=>[new Probe()]);
        var window=new MainWindow(session,appearancePath:Path.Combine(dir,"theme.json"),background:true);
        bool closed=false; window.Closed+=(_,_)=>closed=true;
        try
        {
            window.StartWithoutWindow();
            for(int i=0;i<400 && !session.IsRunning;i++) await Task.Delay(10);
            Assert.True(session.IsRunning); Assert.False(window.IsVisible);
            window.ExitApplication();
            for(int i=0;i<400 && !closed;i++) await Task.Delay(10);
            Assert.True(closed); Assert.False(session.IsRunning); Assert.False(window.IsVisible);
        }
        finally
        {
            await session.StopAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir,true);
        }
    }

    [AvaloniaFact] public async Task NotificationsQueueAndRenderInBothThemes()
    {
        var window=new NotificationWindow();
        try
        {
            foreach(var dark in new[]{true,false})
            {
                GlassDesign.SetDark(dark); Application.Current!.RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;
                window.Present("Первый сбой","Проверка шлюза не прошла. Синтетический пример.",()=>{});
                window.Present("Второй сбой","Проверка DNS не прошла.",()=>{});
                await Task.Delay(100); Dispatcher.UIThread.RunJobs();
                var texts=window.GetLogicalDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
                Assert.Contains("Первый сбой",texts); Assert.DoesNotContain("Второй сбой",texts);
                var preview=Environment.GetEnvironmentVariable("LANPROOF_UI_QA_DIR");
                if(!string.IsNullOrEmpty(preview))
                {
                    Directory.CreateDirectory(preview); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame=window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(preview,dark?"notification-dark.png":"notification-light.png"));
                }
                var dismiss=window.GetLogicalDescendants().OfType<Button>().Single();
                dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(),t=>t.Text=="Второй сбой");
                dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(window.IsVisible);
            }
        }
        finally { window.Close(); GlassDesign.SetDark(true); Application.Current!.RequestedThemeVariant=ThemeVariant.Dark; }
    }
}
