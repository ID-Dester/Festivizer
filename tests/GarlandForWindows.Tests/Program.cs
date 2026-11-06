using GarlandForWindows;
var monitor = new Native.Rect { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };
void Check(bool expected, Native.Rect rect, long style, string label)
{
    if (GeometryPolicy.IsFullscreen(rect, monitor, style) != expected) throw new Exception(label);
    Console.WriteLine($"PASS {label}");
}
Check(true, monitor, 0, "exact monitor");
foreach (var side in new[] { "left", "top", "right", "bottom" }) {
    var r = monitor;
    switch (side) { case "left": r.Left += 2; break; case "top": r.Top += 2; break; case "right": r.Right -= 2; break; case "bottom": r.Bottom -= 2; break; }
    Check(true, r, 0, side + " 2 pixels");
    switch (side) { case "left": r.Left++; break; case "top": r.Top++; break; case "right": r.Right--; break; case "bottom": r.Bottom--; break; }
    Check(false, r, 0, side + " 3 pixels");
}
Check(false, monitor, Native.WS_CAPTION, "caption");
Check(false, monitor, Native.WS_THICKFRAME, "thick frame");
var work = monitor; work.Bottom -= 48; Check(false, work, 0, "work area is not monitor");

