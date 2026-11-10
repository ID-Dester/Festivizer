using System.Runtime.InteropServices;
namespace GarlandForWindows;

internal enum MenuState { Unknown, Closed, Open }
internal readonly record struct MenuContext(IntPtr Foreground, uint ProcessId, uint ThreadId);
internal readonly record struct MenuSnapshot(MenuContext Context, MenuState State, IntPtr MenuOwner, int Error);

internal interface IMenuNative
{
    MenuContext ReadContext();
    bool TryGetMenuInfo(uint thread, out uint flags, out IntPtr owner, out int error);
}

internal sealed class MenuNative : IMenuNative
{
    public MenuContext ReadContext()
    {
        IntPtr foreground = Native.GetForegroundWindow();
        uint thread = Native.GetWindowThreadProcessId(foreground, out uint pid);
        return new(foreground, pid, thread);
    }
    public bool TryGetMenuInfo(uint thread, out uint flags, out IntPtr owner, out int error)
    {
        var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf<Native.GuiThreadInfo>() };
        bool ok = Native.GetGUIThreadInfo(thread, ref info);
        error = ok ? 0 : Marshal.GetLastPInvokeError(); flags = info.Flags; owner = info.MenuOwner;
        return ok;
    }
}

internal sealed class MenuStateTracker(IMenuNative native)
{
    internal MenuSnapshot Read()
    {
        MenuContext before = native.ReadContext();
        if (before.Foreground == IntPtr.Zero || before.ProcessId == 0 || before.ThreadId == 0) return new(before, MenuState.Unknown, IntPtr.Zero, 0);
        bool ok = native.TryGetMenuInfo(before.ThreadId, out uint flags, out IntPtr owner, out int error);
        MenuContext after = native.ReadContext();
        if (!ok || before != after) return new(after, MenuState.Unknown, IntPtr.Zero, ok ? 0 : error);
        bool open = (flags & (Native.GUI_INMENUMODE | Native.GUI_POPUPMENUMODE | Native.GUI_SYSTEMMENUMODE)) != 0;
        return new(before, open ? MenuState.Open : MenuState.Closed, owner, 0);
    }
}

internal static class WinEventRouter
{
    internal enum Kind { Ignore, Foreground, Menu, MoveSize, WindowObject }
    internal static Kind Route(uint ev, int objectId, int childId)
    {
        if (ev == Native.EVENT_SYSTEM_FOREGROUND) return Kind.Foreground;
        if (IsMenuEvent(ev, objectId, childId)) return Kind.Menu;
        if (ev is Native.EVENT_SYSTEM_MOVESIZESTART or Native.EVENT_SYSTEM_MOVESIZEEND) return IsWindowObject(objectId, childId) ? Kind.MoveSize : Kind.Ignore;
        return ev >= Native.EVENT_OBJECT_CREATE && IsWindowObject(objectId, childId) ? Kind.WindowObject : Kind.Ignore;
    }
    internal static bool IsMenuEvent(uint ev, int objectId, int childId) =>
        ev is >= Native.EVENT_SYSTEM_MENUSTART and <= Native.EVENT_SYSTEM_MENUPOPUPEND &&
        childId == Native.CHILDID_SELF && objectId is Native.OBJID_WINDOW or Native.OBJID_MENU or Native.OBJID_SYSMENU;
    internal static bool IsWindowObject(int objectId, int childId) => objectId == Native.OBJID_WINDOW && childId == Native.CHILDID_SELF;
}
