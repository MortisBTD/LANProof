using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LANProof.Core.Models;
using LANProof.Runtime.Notifications;

namespace LANProof.App;

public partial class MainWindow
{
    private TrayIcon? _tray;
    private NativeMenuItem? _trayToggle;
    private NotificationWindow? _toast;
    private NotificationPolicy? _policy;
    private bool _exitRequested;

    private void StartBackgroundMode()
    {
        _policy = new NotificationPolicy();
        _session.AlertsUpdated += OnAlertsUpdated;
        _session.Updated += OnBackgroundTick;
        try { BuildTray(); }
        catch (Exception ex)
        {
            _tray?.Dispose(); _tray = null;
            _message = "Трей недоступен: " + ex.Message + ". Закрытие окна завершит программу.";
        }
    }

    private void BuildTray()
    {
        var open = new NativeMenuItem("Открыть LANProof");
        open.Click += (_, _) => RestoreWindow();
        _trayToggle = new NativeMenuItem("Остановить мониторинг");
        _trayToggle.Click += async (_, _) => await Guard(ToggleFromTray);
        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) => ExitApplication();
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LANProof/Assets/AppIcon.ico"))),
            ToolTipText = "LANProof — мониторинг идёт",
            Menu = new NativeMenu { Items = { open, _trayToggle, exit } }
        };
        _tray.IsVisible = true;
        _tray.Clicked += (_, _) => RestoreWindow();
    }

    internal void ExitApplication() { _exitRequested = true; Close(); }

    internal void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal async Task ToggleFromTray()
    {
        if (_busy || !_ready || _closing) return;
        _busy = true;
        try
        {
            if (_session.IsRunning)
            {
                await _session.StopAsync();
                _message = "Мониторинг остановлен из меню трея.";
            }
            else
            {
                _session.Start();
                _message = "Мониторинг возобновлён.";
            }
        }
        finally { _busy = false; UpdateHeader(); }
    }

    private void UpdateTray()
    {
        if (_tray is null) return;
        if (_trayToggle is not null) _trayToggle.IsEnabled = _ready && !_busy && !_closing;
        _tray.ToolTipText = _session.IsRunning ? "LANProof — мониторинг идёт" : "LANProof — мониторинг на паузе";
        if (_trayToggle is not null)
            _trayToggle.Header = _session.IsRunning ? "Остановить мониторинг" : "Возобновить мониторинг";
    }

    private void OnBackgroundTick()
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateTray();
            if (!_session.IsRunning) _policy?.Suspend();
        });
    }

    private void OnAlertsUpdated(IReadOnlyList<NetworkEvent> active)
    {
        var now = DateTime.UtcNow;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _policy is null) return;
            foreach (var alert in _policy.Evaluate(active, now)) Speak(alert);
        });
    }

    private void Speak(NetworkAlert? alert)
    {
        if (alert is null) return;
        _toast ??= new NotificationWindow();
        _toast.Present(alert.Title, alert.Body, RestoreWindow);
    }

    private Control BackgroundSection()
    {
        if (!_background) return new StackPanel();
        var autostart = new CheckBox
        {
            Content = "Запускать вместе с Windows",
            IsChecked = ReadAutostart(),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var changingAutostart = false;
        autostart.IsEnabled = OperatingSystem.IsWindows();
        autostart.IsCheckedChanged += (_, _) =>
        {
            if (changingAutostart) return;
            try { Autostart.Set(autostart.IsChecked == true); }
            catch (Exception ex)
            {
                changingAutostart = true; autostart.IsChecked = ReadAutostart(); changingAutostart = false;
                _message = "Автозапуск не изменён: " + ex.Message; UpdateHeader();
            }
        };
        return Card(Stack(14, Text("Фоновая работа", 17, bold: true), autostart,
            Text("Окно можно закрыть: измерения продолжатся в трее, а уведомления придут при обрыве и восстановлении связи. Полный выход — через меню трея.", 13, Muted)));
    }

    private bool ReadAutostart()
    {
        try { return Autostart.IsEnabled(); }
        catch (Exception ex) { _message = "Не удалось прочитать автозапуск: " + ex.Message; return false; }
    }

    private void StopBackgroundMode()
    {
        _session.AlertsUpdated -= OnAlertsUpdated;
        _session.Updated -= OnBackgroundTick;
        _toast?.Close();
        _toast = null;
        _tray?.Dispose();
        _tray = null;
    }
}
