using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LANProof.Core.Models;

namespace LANProof.App.Controls;

public sealed class LatencyChart : Control
{
    public IReadOnlyList<ProbeResult> Measurements { get; set; } = [];
    public event Action<DateTime>? TimeSelected;
    public DateTime? SelectedTime { get; set; }
    private static readonly IBrush Muted = GlassDesign.Brush("#7C899E");
    public LatencyChart() { MinHeight = 180; ClipToBounds = true; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var times = Measurements.Select(x => x.Timestamp).Distinct().Order().ToArray();
        if (times.Length == 0) return;
        var ratio = Math.Clamp((e.GetPosition(this).X - 48) / Math.Max(1, Bounds.Width - 70), 0, 1);
        var target = times[0].AddTicks((long)((times[^1] - times[0]).Ticks * ratio));
        SelectedTime = times.MinBy(t => Math.Abs((t - target).Ticks));
        TimeSelected?.Invoke(SelectedTime.Value);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var w = Bounds.Width; var h = Bounds.Height;
        var points = Measurements.Where(p => p.ProbeType is ProbeType.Gateway or ProbeType.External).ToList();
        if (w < 100 || h < 70) return;
        var left = 48d; var right = w - 20; var top = 18d; var bottom = h - 30;
        var max = Math.Max(40, points.Where(x => x.IsSuccess).Select(x => x.RoundTripMs ?? 0).DefaultIfEmpty().Max() * 1.15);
        for (int i = 0; i < 5; i++)
        {
            var y = top + (bottom - top) * i / 4;
            context.DrawLine(new Pen(GlassDesign.Brush("#1B687F9F"), 1, DashStyle.Dash), new(left, y), new(right, y));
            Text(context, $"{max * (4-i) / 4:F0}", new(2, y - 7));
        }
        Text(context, "мс", new(2, 0));
        if (points.Count == 0) { Text(context, "Измерения появятся после первого цикла", new(left + 20, h / 2)); return; }
        var start = points.Min(p => p.Timestamp); var end = points.Max(p => p.Timestamp);
        var span = Math.Max(1, (end - start).TotalSeconds);
        double X(DateTime time) => left + (right - left) * (time - start).TotalSeconds / span;
        for (int i = 0; i < 5; i++)
            Text(context, start.AddSeconds(span * i / 4).ToLocalTime().ToString("HH:mm:ss"), new(left + (right - left) * i / 4 - 15, bottom + 10), w);
        foreach (var type in new[] { ProbeType.Gateway, ProbeType.External })
        {
            var brush = GlassDesign.Brush(type == ProbeType.Gateway ? "#9481D9" : "#087CF4");
            Point? previous = null; DateTime? previousTime = null;
            // Keep every failed observation visible; line sampling only limits rendering cost.
            var series = points.Where(p => p.ProbeType == type).ToList();
            var stride = Math.Max(1, series.Count / 1200);
            for (int i = 0; i < series.Count; i++)
            {
                var p = series[i];
                if (!p.IsSuccess || p.RoundTripMs == null)
                {
                    context.DrawLine(new Pen(p.IsConclusive ? GlassDesign.Red : Muted, 2), new(X(p.Timestamp), bottom - 12), new(X(p.Timestamp), bottom));
                    previous = null; previousTime = null; continue;
                }
                if (i % stride != 0 && i != series.Count - 1) continue;
                var point = new Point(X(p.Timestamp), bottom - (bottom - top) * p.RoundTripMs.Value / max);
                if (previous is { } prev && previousTime.HasValue && (p.Timestamp - previousTime.Value).TotalSeconds <= Math.Max(60, span / 400))
                {
                    if (type == ProbeType.External)
                    {
                        var area = new StreamGeometry();
                        using (var geometry = area.Open())
                        {
                            geometry.BeginFigure(new Point(prev.X, bottom), true); geometry.LineTo(prev); geometry.LineTo(point); geometry.LineTo(new Point(point.X, bottom)); geometry.EndFigure(true);
                        }
                        context.DrawGeometry(GlassDesign.Gradient("#25087CF4", "#03087CF4"), null, area);
                    }
                    context.DrawLine(new Pen(brush, 2.4, lineCap: PenLineCap.Round), prev, point);
                }
                context.DrawEllipse(GlassDesign.Brush("#F4F8FC"), new Pen(brush, 1.8), point, 2.7, 2.7);
                previous = point; previousTime = p.Timestamp;
            }
        }
        if (SelectedTime is { } selected && selected >= start && selected <= end)
            context.DrawLine(new Pen(Muted, 1, DashStyle.Dash), new(X(selected), top), new(X(selected), bottom));
    }

    private static void Text(DrawingContext context, string text, Point point, double right=double.PositiveInfinity)
    {
        var formatted=new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(GlassDesign.NumericFont), 11, Muted);
        context.DrawText(formatted,new Point(Math.Max(0,Math.Min(point.X,right-formatted.Width)),point.Y));
    }
}
