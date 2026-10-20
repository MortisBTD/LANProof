using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LANProof.App.Controls;

public sealed class LogoMark : Control
{
    public LogoMark() { Width = 40; Height = 40; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 64, Bounds.Height / 64));
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(30,255,255,255)), new Pen(Brushes.White, 1.8),
            Geometry.Parse("M32 7 L52 14 V29 C52 42 44 51 32 57 C20 51 12 42 12 29 V14 Z"));
        context.DrawGeometry(null, new Pen(Brushes.White, 3.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            Geometry.Parse("M23 24 V39 H41 M32 24 V31 H41"));
        foreach (var point in new[] { new Point(23,23), new Point(41,31), new Point(41,39) })
            context.DrawEllipse(Brushes.White, null, point, 2.8, 2.8);
    }
}
