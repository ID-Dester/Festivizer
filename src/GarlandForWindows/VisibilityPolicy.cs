namespace GarlandForWindows;

internal static class VisibilityPolicy
{
    internal static bool Taskbar(Settings s, bool visible, bool fullscreen, bool healthy) => s.GarlandsEnabled && s.TaskbarEnabled && visible && !fullscreen && healthy;
    internal static bool Frame(Settings s, bool eligible, bool fullscreen, bool healthy) => s.GarlandsEnabled && s.WindowFrameEnabled && eligible && !fullscreen && healthy;
    // Health is checked by FrameOverlayController after this precondition permits an update.
    internal static bool MayUpdateFrame(Settings s, bool eligible, bool fullscreen, MenuState menu, bool trayOpen) =>
        s.GarlandsEnabled && s.WindowFrameEnabled && eligible && !fullscreen && menu == MenuState.Closed && !trayOpen;
    internal static int DipToPixels(int dip, uint dpi) => (int)Math.Ceiling(dip * (dpi == 0 ? 96 : dpi) / 96.0);
}
