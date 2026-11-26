using System.Runtime.InteropServices;
using IslasEscritorio.Core;

namespace IslasEscritorio.Input;

public static class MonitorHelper
{
    private const uint MonitorInfoPrimary = 1;
    private const int MdtEffectiveDpi = 0;
    private const int GwlStyle = -16;
    private const nint WsCaption = 0x00C00000;

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    double scale = 1.0;
                    try
                    {
                        if (GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0)
                            scale = dpiX / 96.0;
                    }
                    catch
                    {
                        scale = 1.0;
                    }

                    monitors.Add(new MonitorInfo(
                        Index: index++,
                        DeviceName: info.DeviceName,
                        IsPrimary: (info.Flags & MonitorInfoPrimary) != 0,
                        Bounds: new ScreenRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
                        WorkArea: new ScreenRect(info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom),
                        DpiScale: scale));
                }
                return true;
            }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            monitors.Add(new MonitorInfo(
                0, "DISPLAY1", true,
                new ScreenRect(0, 0, 1920, 1080),
                new ScreenRect(0, 0, 1920, 1040),
                1.0));
        }

        return monitors;
    }

    public static ScreenPoint? TryGetCursorPosition() =>
        GetCursorPos(out Point pt) ? new ScreenPoint(pt.X, pt.Y) : null;

    public static bool IsFullscreenWindowOnMonitor(ScreenRect monitorBounds, IntPtr ownWindowHandle)
    {
        try
        {
            if (SHQueryUserNotificationState(out UserNotificationState state) == 0 &&
                state is UserNotificationState.RunningD3DFullScreen or UserNotificationState.PresentationMode)
            {
                return true;
            }
        }
        catch { }

        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ownWindowHandle) return false;

        var classBuf = new char[64];
        int len = GetClassName(fg, classBuf, classBuf.Length);
        if (len > 0)
        {
            string cls = new(classBuf, 0, len);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;
        }

        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId)
            return false;

        // Una ventana normal maximizada (con barra de título) no debe bloquear las islas
        // aunque la barra de tareas esté en otro monitor o oculta automáticamente.
        nint style = GetWindowLongPtr(fg, GwlStyle);
        if (IsZoomed(fg) && (style & WsCaption) == WsCaption)
            return false;

        if (!GetWindowRect(fg, out Rect rect))
            return false;

        return rect.Left <= monitorBounds.Left + 2 &&
               rect.Top <= monitorBounds.Top + 2 &&
               rect.Right >= monitorBounds.Right - 2 &&
               rect.Bottom >= monitorBounds.Bottom - 2;
    }

    public static bool IsMaximizedWindowOnMonitor(ScreenRect monitorBounds)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var classBuf = new char[64];
        int len = GetClassName(fg, classBuf, classBuf.Length);
        if (len > 0)
        {
            string cls = new(classBuf, 0, len);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;
        }

        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId)
            return false;

        if (IsZoomed(fg))
        {
            if (!GetWindowRect(fg, out Rect rect))
                return true;

            int intersectLeft = Math.Max(rect.Left, monitorBounds.Left);
            int intersectTop = Math.Max(rect.Top, monitorBounds.Top);
            int intersectRight = Math.Min(rect.Right, monitorBounds.Right);
            int intersectBottom = Math.Min(rect.Bottom, monitorBounds.Bottom);

            if (intersectRight > intersectLeft && intersectBottom > intersectTop)
            {
                long intersectArea = (long)(intersectRight - intersectLeft) * (intersectBottom - intersectTop);
                long monitorArea = (long)monitorBounds.Width * monitorBounds.Height;
                return intersectArea > (monitorArea / 3);
            }

            return true;
        }

        return false;
    }

    public static bool IsMouseButtonPressed()
    {
        const int vkLButton = 0x01;
        const int vkRButton = 0x02;
        return (GetAsyncKeyState(vkLButton) & 0x8000) != 0 ||
               (GetAsyncKeyState(vkRButton) & 0x8000) != 0;
    }

    private enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState pquns);
}
