using System.Runtime.InteropServices;

namespace IslasEscritorio.Desktop;

public static class DesktopIcons
{
    private const uint SwHide = 0;
    private const uint SwShow = 5;
    private const uint WmNull = 0;

    public static IntPtr FindListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr view = FindDescendant(progman);
        if (view != IntPtr.Zero) return view;

        IntPtr worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            view = FindDescendant(worker);
            if (view != IntPtr.Zero) return view;
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindDescendant(IntPtr parent)
    {
        if (parent == IntPtr.Zero) return IntPtr.Zero;
        IntPtr defView = FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
        return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    public static bool SetVisible(bool visible)
    {
        IntPtr view = FindListView();
        if (view == IntPtr.Zero) return false;
        ShowWindow(view, visible ? SwShow : SwHide);
        SendMessageTimeout(view, WmNull, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _);
        return IsWindow(view);
    }

    public static bool IsVisible(IntPtr view) => view != IntPtr.Zero && IsWindow(view) && IsWindowVisible(view);

    public static uint GetExplorerProcessId()
    {
        IntPtr view = FindListView();
        if (view == IntPtr.Zero) view = FindWindow("Progman", null);
        if (view == IntPtr.Zero) view = FindWindow("Shell_TrayWnd", null);
        if (view == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(view, out uint pid);
        return pid;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr window, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
