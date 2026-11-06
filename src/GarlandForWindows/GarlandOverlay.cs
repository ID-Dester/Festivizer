using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;

namespace GarlandForWindows;

internal sealed class GarlandOverlay : Window
{
    private readonly GarlandVisual visual;
    private IntPtr handle;
    internal GarlandOverlay(bool frame)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; ResizeMode = ResizeMode.NoResize;
        visual = new GarlandVisual(frame); Content = visual;
        SourceInitialized += (_, _) => {
            handle = new WindowInteropHelper(this).Handle;
            long style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
        };
    }
    internal void Place(Native.Rect r, IntPtr insertAfter)
    {
        if (r.Width <= 0 || r.Height <= 0) { Hide(); return; }
        if (!IsVisible) Show();
        Native.SetWindowPos(handle, insertAfter, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
    internal void Animate(double seconds) { visual.Seconds = seconds; visual.InvalidateVisual(); }
}

internal sealed class GarlandVisual(bool frame) : FrameworkElement
{
    internal double Seconds { get; set; }
    private static readonly Color[] Colors = [Color.FromRgb(255,65,55), Color.FromRgb(70,225,110), Color.FromRgb(255,210,45), Color.FromRgb(65,150,255)];
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;
        var wire = new Pen(new SolidColorBrush(Color.FromRgb(20,65,40)), 1.5);
        DrawSide(dc, new Point(8, 8), new Point(w - 8, 8), wire, 0);
        if (frame) {
            DrawSide(dc, new Point(w - 8, 8), new Point(w - 8, h - 8), wire, 1);
            DrawSide(dc, new Point(w - 8, h - 8), new Point(8, h - 8), wire, 2);
            DrawSide(dc, new Point(8, h - 8), new Point(8, 8), wire, 3);
        }
    }
    private void DrawSide(DrawingContext dc, Point a, Point b, Pen wire, int side)
    {
        Vector d = b - a; double length = d.Length; if (length < 1) return;
        d.Normalize(); Vector normal = new(-d.Y, d.X); int count = Math.Max(1, (int)(length / 42));
        var path = new StreamGeometry(); using (var ctx = path.Open()) {
            ctx.BeginFigure(a, false, false);
            for (int i = 0; i < count; i++) {
                Point p = a + d * (length * (i + 1) / count);
                Point mid = a + d * (length * (i + .5) / count) + normal * 3;
                ctx.QuadraticBezierTo(mid, p, true, false);
            }
        }
        path.Freeze(); dc.DrawGeometry(null, wire, path);
        for (int i = 0; i < count; i++) {
            Point p = a + d * (length * (i + .5) / count) + normal * 3;
            Color c = Colors[(i + side) % Colors.Length];
            double brightness = .55 + .45 * (Math.Sin(Seconds * 1.7 + i * 2.13 + side) + 1) / 2;
            var glow = new RadialGradientBrush(Color.FromArgb((byte)(75 * brightness), c.R, c.G, c.B), ColorsTransparent(c));
            dc.DrawEllipse(glow, null, p, 9, 9);
            var bulb = new SolidColorBrush(Color.FromArgb((byte)(255 * brightness), c.R, c.G, c.B));
            dc.DrawEllipse(bulb, null, p, 3.5, 4.5);
        }
    }
    private static Color ColorsTransparent(Color c) => Color.FromArgb(0, c.R, c.G, c.B);
}

