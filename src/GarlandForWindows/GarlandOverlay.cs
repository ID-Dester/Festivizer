using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;

namespace GarlandForWindows;

internal sealed class GarlandOverlay : Window
{
    private readonly GarlandVisual visual;
    private IntPtr handle;
    private OverlaySafety? safety;
    internal event Action? OverlayDpiChanged;
    internal IntPtr Handle => handle;
    internal bool HasLiveHandle => handle != IntPtr.Zero && Native.IsWindow(handle);
    internal GarlandOverlay(bool frame, IntPtr owner = default)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; ResizeMode = ResizeMode.NoResize;
        visual = new GarlandVisual(frame); Content = visual;
        visual.RenderFailed += error => {
            Diagnostics.Error(frame ? "frame-render" : "taskbar-render", $"Overlay render error: {error.Message}");
            Dispatcher.BeginInvoke(() => { if (IsVisible) Hide(); });
        };
        DpiChanged += (_, _) => { visual.InvalidateCache(); OverlayDpiChanged?.Invoke(); };
        if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;
        SourceInitialized += (_, _) => {
            handle = new WindowInteropHelper(this).Handle;
            safety = new OverlaySafety(new OverlayInterop(frame), handle);
            safety.Prepare();
        };
    }
    internal void Place(Native.Rect r, IntPtr insertAfter, bool preserveZOrder = false)
    {
        if (r.Width <= 0 || r.Height <= 0) { Hide(); return; }
        if (handle == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
        if (safety == null) { Hide(); throw new InvalidOperationException("Overlay handle was not initialized."); }
        safety.Place(r, insertAfter, () => { if (IsVisible) return false; Show(); return true; }, Hide, preserveZOrder);
    }
    internal void Configure(Settings s) => visual.Configure(s);
    internal void Animate(double phase) { visual.Phase = phase; visual.InvalidateVisual(); }
}

internal sealed class GarlandVisual(bool frame) : FrameworkElement
{
    private sealed record Bulb(Point Position, int Index, int Side, Brush Glow, Brush Fill);
    private readonly List<Bulb> bulbs = [];
    private readonly List<StreamGeometry> wires = [];
    private static readonly Pen Wire = MakeWire();
    private double cachedWidth = -1, cachedHeight = -1;
    private string theme = "classicChristmas";
    private double brightness = 1;
    internal double Phase { get; set; }
    internal event Action<Exception>? RenderFailed;
    internal void InvalidateCache() { cachedWidth = -1; InvalidateVisual(); }
    private static Pen MakeWire() { var pen = new Pen(new SolidColorBrush(Color.FromRgb(20,65,40)), 1.5); pen.Freeze(); return pen; }
    internal void Configure(Settings s) { if (theme != s.Theme) { theme = s.Theme; cachedWidth = -1; } brightness = s.BrightnessPercent / 100.0; InvalidateVisual(); }
    private Color[] Palette() => theme switch {
        "warmWhite" => [Color.FromRgb(255,225,166)],
        "coldWhite" => [Color.FromRgb(220,238,255)],
        "multicolor" => [Color.FromRgb(255,65,55), Color.FromRgb(255,146,61), Color.FromRgb(255,210,45), Color.FromRgb(70,225,110), Color.FromRgb(65,150,255), Color.FromRgb(190,120,255)],
        _ => [Color.FromRgb(255,65,55), Color.FromRgb(70,225,110), Color.FromRgb(255,210,45), Color.FromRgb(65,150,255)]
    };
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        try { DrawGarland(dc); }
        catch (Exception error) { RenderFailed?.Invoke(error); }
    }
    private void DrawGarland(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 24 || h < 24) return;
        if (w != cachedWidth || h != cachedHeight) Rebuild(w, h);
        foreach (var path in wires) dc.DrawGeometry(null, Wire, path);
        foreach (var b in bulbs) {
            double intensity = brightness * (.55 + .45 * (Math.Sin(Phase * 1.7 + b.Index * 2.13 + b.Side) + 1) / 2);
            dc.PushOpacity(intensity); dc.DrawEllipse(b.Glow, null, b.Position, 9, 9); dc.DrawEllipse(b.Fill, null, b.Position, 3.5, 4.5); dc.Pop();
        }
    }
    private void Rebuild(double w, double h)
    {
        cachedWidth = w; cachedHeight = h; wires.Clear(); bulbs.Clear(); var colors = Palette();
        AddSide(new Point(12,12), new Point(w-12,12), 0, colors);
        if (frame) { AddSide(new Point(w-12,12), new Point(w-12,h-12), 1, colors); AddSide(new Point(w-12,h-12), new Point(12,h-12), 2, colors); AddSide(new Point(12,h-12), new Point(12,12), 3, colors); }
    }
    private void AddSide(Point a, Point b, int side, Color[] colors)
    {
        Vector d = b - a; double length = d.Length; if (length < 1) return;
        d.Normalize(); Vector normal = new(-d.Y, d.X); int count = Math.Max(1, (int)(length / 42));
        var path = new StreamGeometry(); using (var ctx = path.Open()) {
            ctx.BeginFigure(a, false, false);
            for (int i = 0; i < count; i++) ctx.QuadraticBezierTo(a + d * (length * (i + .5) / count) + normal * 3, a + d * (length * (i + 1) / count), true, false);
        }
        path.Freeze(); wires.Add(path);
        for (int i = 0; i < count; i++) {
            Point p = a + d * (length * (i + .5) / count) + normal * 3; Color c = colors[(i + side) % colors.Length];
            var glow = new RadialGradientBrush(Color.FromArgb(75,c.R,c.G,c.B), Color.FromArgb(0,c.R,c.G,c.B)); glow.Freeze();
            var fill = new SolidColorBrush(c); fill.Freeze(); bulbs.Add(new Bulb(p, i, side, glow, fill));
        }
    }
}
