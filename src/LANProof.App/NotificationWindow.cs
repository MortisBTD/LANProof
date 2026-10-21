using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LANProof.App.Controls;

namespace LANProof.App;

internal sealed class NotificationWindow : Window
{
    private readonly TextBlock _title = new()
    {
        FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = GlassDesign.Ink,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _body = new()
    {
        FontSize = 13, Foreground = GlassDesign.Muted, TextWrapping = TextWrapping.Wrap,
        MaxHeight = 150, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Queue<(string Title, string Body, Action Open)> _pending = new();
    private Action? _open;

    public NotificationWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        Width = 380;
        FontFamily = GlassDesign.UiFont;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var dismiss = new Button { Content = "×", Padding = new Thickness(8, 0),
            Background = Brushes.Transparent, Foreground = GlassDesign.Muted };
        dismiss.Click += (_, e) => { e.Handled = true; Dismiss(); };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(_title); Grid.SetColumn(dismiss, 1); heading.Children.Add(dismiss);
        Content = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14),
            Background = GlassDesign.Brush("#F7FFFFFF"), BorderBrush = GlassDesign.Divider,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 8, Children = { heading, _body } }
        };
        _timer.Tick += (_, _) => Dismiss();
        PointerReleased += (_, e) =>
        {
            if (e.Handled || e.Source is Visual v && (v is Button || v.FindAncestorOfType<Button>() is not null)) return;
            var open = _open;
            Dismiss();
            open?.Invoke();
        };
        Closed += (_, _) => { _timer.Stop(); _pending.Clear(); _open = null; };
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
    }

    public void Present(string title, string body, Action open)
    {
        // Keep simultaneous categories visible in turn instead of replacing the current alert.
        if (_pending.Count < 20) _pending.Enqueue((title, body, open));
        if (!IsVisible) Next();
    }

    private void Dismiss()
    {
        _timer.Stop(); Hide(); _open = null;
        Next();
    }

    private void Next()
    {
        if (!_pending.TryDequeue(out var next)) return;
        _title.Text = next.Title; _body.Text = next.Body; _open = next.Open;
        Show(); Place(); _timer.Start();
    }

    private void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Width = Math.Min(380, Math.Max(180, area.Width / scale - 32));
        var height = Bounds.Height > 0 ? Bounds.Height : 110;
        Position = new PixelPoint(area.Right - (int)(Width * scale) - 16,
            Math.Max(area.Y + 16, area.Bottom - (int)(height * scale) - 16));
    }
}
