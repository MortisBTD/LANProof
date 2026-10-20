using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LANProof.App.Controls;

internal static class GlassDesign
{
    public static readonly FontFamily UiFont = new("Bahnschrift, Segoe UI Variable, Segoe UI");
    public static readonly FontFamily NumericFont = new("Cascadia Mono, Consolas");
    public static IBrush Divider => Brush("#24667788");
    private static readonly Dictionary<string, SolidColorBrush> BrushesByToken = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, string), LinearGradientBrush> Gradients = [];
    private static readonly Dictionary<string, string> DarkColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#202839"] = "#EAF0FA", ["#67758A"] = "#BDC8DC", ["#087CF4"] = "#64B1FF",
        ["#F0F4F9"] = "#132035", ["#AAB8CA"] = "#637D9E",
        ["#12815E"] = "#65DDB2", ["#CD4054"] = "#FF8F9E", ["#7C899E"] = "#8998B3",
        ["#DDE8EEF6"] = "#FC111926", ["#E8EEF6"] = "#111926",
        ["#F7FFFFFF"] = "#FA1B2738", ["#24667788"] = "#35AAC1E0", ["#486A90B8"] = "#183B618C",
        ["#D9FFFFFF"] = "#DA202D43", ["#8FFFFFFF"] = "#B0142033",
        ["#FFFFFFFF"] = "#42BDCEE8", ["#70FFFFFF"] = "#2518273C",
        ["#90FFFFFF"] = "#6088BFFF", ["#80FFFFFF"] = "#487D9BC5", ["#50FFFFFF"] = "#305D7AA8",
        ["#F0FFFFFF"] = "#F02A3B56", ["#F4F8FC"] = "#1D2B42", ["#75FFFFFF"] = "#65C0DCFF",
        ["#AFFFFFFF"] = "#C01A283F", ["#DFFFFFFF"] = "#40677A99", ["#BFFFFFFF"] = "#50637D9E",
        ["#ADFFFFFF"] = "#D0132035", ["#EDFFFFFF"] = "#D92C3D57", ["#A8FFFFFF"] = "#C0203049",
        ["#F9FFFFFF"] = "#EA354C6C", ["#ACFFFFFF"] = "#C7273D5A", ["#DAFFFFFF"] = "#D938506F",
        ["#24FFFFFF"] = "#075C85BE", ["#05FFFFFF"] = "#021A2639",
        ["#B0CADCF8"] = "#6531477A", ["#B2B2E6F3"] = "#50337589", ["#A8D6C7F4"] = "#504D387C", ["#9CC7EDE6"] = "#30246466",
        ["#90DEF3E9"] = "#80315C50", ["#90FBE4E7"] = "#805B3345", ["#70EDF1F6"] = "#80303F55",
        ["#1B687F9F"] = "#357D95B5", ["#9481D9"] = "#B5A4FF", ["#8861D2"] = "#B9A1FF",
        ["#193B527B"] = "#70000000", ["#CFFFFFFF"] = "#25CADFFF", ["#7886DCC6"] = "#60347585"
    };
    public static bool IsDark { get; private set; } = true;
    public static Color ColorFor(string token) => Color.Parse(IsDark && DarkColors.TryGetValue(token, out var dark) ? dark : token);
    public static IBrush Brush(string color)
    {
        if (!BrushesByToken.TryGetValue(color, out var brush)) BrushesByToken[color] = brush = new SolidColorBrush(ColorFor(color));
        return brush;
    }
    public static readonly IBrush Ink = Brush("#202839"), Muted = Brush("#67758A"), Blue = Brush("#087CF4"),
        Green = Brush("#12815E"), Red = Brush("#CD4054");

    public static void SetDark(bool dark)
    {
        IsDark = dark;
        foreach (var (token, brush) in BrushesByToken) brush.Color = ColorFor(token);
        foreach (var (tokens, gradient) in Gradients)
        {
            gradient.GradientStops[0].Color = ColorFor(tokens.Item1);
            gradient.GradientStops[1].Color = ColorFor(tokens.Item2);
        }
    }

    public static LinearGradientBrush Gradient(string top, string bottom)
    {
        if (!Gradients.TryGetValue((top, bottom), out var brush))
            Gradients[(top, bottom)] = brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.85, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(ColorFor(top), 0), new GradientStop(ColorFor(bottom), 1)]
            };
        return brush;
    }

    public static BoxShadows Shadows => new(new BoxShadow { OffsetY=2, Blur=8, Color=ColorFor("#193B527B") });

    public static Border Surface(Control child, double radius = 10, double padding = 20) => new()
    {
        Background = Brush("#F7FFFFFF"), BorderBrush = Divider,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(padding),
        BoxShadow = Shadows,
        Classes = { "glass-surface" },
        Child = child
    };

    public static Border ShellSurface(Control child) => new()
    {
        Background=Gradient("#D9FFFFFF","#8FFFFFFF"), BorderBrush=Divider,BorderThickness=new Thickness(1),
        CornerRadius=new CornerRadius(10),Padding=new Thickness(15),Child=child
    };
}

/// <summary>Resolution-independent ambient light behind translucent surfaces.</summary>
public sealed class GlassBackdrop : Control
{
    public GlassBackdrop() => IsHitTestVisible = false;
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(GlassDesign.Brush("#DDE8EEF6"), null, rect);
        Glow(context, rect, .12, .10, .5, "#486A90B8");
    }

    private static void Glow(DrawingContext context, Rect rect, double x, double y, double radius, string color)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(x, y, RelativeUnit.Relative), GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(radius, RelativeUnit.Relative), RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
            GradientStops = [new GradientStop(GlassDesign.ColorFor(color), 0), new GradientStop(Colors.Transparent, 1)]
        };
        context.DrawRectangle(brush, null, rect);
    }
}

/// <summary>Small line icons without platform-dependent symbol fonts.</summary>
public sealed class NetworkGlyph : Control
{
    public int Kind { get; set; }
    public IBrush Stroke { get; set; } = GlassDesign.Blue;
    public NetworkGlyph() { Width = 22; Height = 22; IsHitTestVisible = false; }

    public override void Render(DrawingContext context)
    {
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 32, Bounds.Height / 32));
        var pen = new Pen(Stroke, 1.9, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(pen, new(x1, y1), new(x2, y2));
        switch (Kind)
        {
            case 0:
                foreach (var (x, y) in new[] { (5, 5), (19, 5), (5, 19), (19, 19) }) context.DrawRectangle(null, pen, new Rect(x, y, 8, 8), 2, 2);
                break;
            case 1:
                context.DrawGeometry(null, pen, Geometry.Parse("M 3,23 L 10,15 L 16,19 L 23,8 L 29,12"));
                break;
            case 2:
                context.DrawEllipse(null, pen, new Point(14, 14), 9, 9); Line(21, 21, 28, 28); Line(10, 14, 18, 14); Line(14, 10, 14, 18);
                break;
            case 3:
                context.DrawRectangle(null, pen, new Rect(7, 3, 18, 26), 3, 3); Line(11, 11, 21, 11); Line(11, 16, 21, 16); Line(11, 21, 17, 21);
                break;
            case 4:
                Line(5, 8, 27, 8); Line(5, 16, 27, 16); Line(5, 24, 27, 24);
                foreach (var (x, y) in new[] { (11, 8), (22, 16), (14, 24) }) context.DrawEllipse(GlassDesign.Brush("#F4F8FC"), pen, new Point(x, y), 3, 3);
                break;
            case 5:
                context.DrawGeometry(null, pen, Geometry.Parse("M 4,12 C 11,5 21,5 28,12 M 9,18 C 13,14 19,14 23,18 M 13,23 C 15,21 17,21 19,23"));
                context.DrawEllipse(Stroke, null, new Point(16, 27), 1.4, 1.4); break;
            case 6:
                context.DrawGeometry(null, pen, Geometry.Parse("M 4,17 L 10,17 L 14,7 L 19,25 L 23,14 L 28,14")); break;
            case 7:
                Line(16, 5, 16, 25); Line(9, 18, 16, 25); Line(23, 18, 16, 25); break;
            default:
                context.DrawEllipse(null, pen, new Point(16, 16), 11, 11); Line(16, 9, 16, 16); Line(16, 16, 21, 19); break;
        }
    }
}

public sealed class ConnectionHalo : Control
{
    public bool? Healthy { get; set; }
    public ConnectionHalo() { Width = 140; Height = 140; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2;
        var color = Healthy == false ? "#D96472" : "#3A9DC6";
        context.DrawEllipse(new RadialGradientBrush { GradientStops = [new(GlassDesign.ColorFor("#7886DCC6"), 0), new(Colors.Transparent, 1)] }, null, center, radius, radius);
        for (int i = 0; i < 3; i++)
            context.DrawEllipse(null, new Pen(GlassDesign.Brush(i == 0 ? "#80FFFFFF" : "#50FFFFFF"), 1.2), center, radius - 4 - i * 16, radius - 4 - i * 16);
        context.DrawEllipse(GlassDesign.Gradient("#F0FFFFFF", "#80FFFFFF"), new Pen(Brushes.White, 1.4), center, radius * .39, radius * .39);
        using var transform = context.PushTransform(Matrix.CreateTranslation(center.X - 16, center.Y - 16));
        var pen = new Pen(GlassDesign.Brush(color), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, Geometry.Parse(Healthy == null ? "M 16,8 L 16,16 L 22,20" : Healthy == false ? "M 8,8 L 24,24 M 24,8 L 8,24" : "M 8,16 L 14,22 L 25,10"));
    }
}
