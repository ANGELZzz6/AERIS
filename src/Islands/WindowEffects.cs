using System.Runtime.InteropServices;

namespace IslasEscritorio.Islands;

/// <summary>
/// Utilidades Win32/DWM para configurar ventanas sin activación (sin robar el foco),
/// ocultarlas de Alt+Tab y aplicar desenfoque acrílico opcional.
/// </summary>
public static class WindowEffects
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public static void ApplyNoActivateToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        nint exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        exStyle |= WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, exStyle);
    }

    public static void PositionWithoutActivating(IntPtr hwnd, int left, int top, int width, int height, bool topmost)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr insertAfter = topmost ? HwndTopMost : HwndTop;
        SetWindowPos(hwnd, insertAfter, left, top, width, height, SwpNoActivate | SwpShowWindow);
    }

    public static void UpdateTopmost(IntPtr hwnd, bool topmost)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, topmost ? HwndTopMost : HwndNoTopMost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public static void SetBlurBehind(IntPtr hwnd, bool enabled, uint tintAbgr = 0x991E1E2E)
    {
        // Las ventanas WPF con AllowsTransparency="True" gestionan la translucidez y esquinas
        // redondeadas directamente en SurfaceBorder; evitamos SetWindowCompositionAttribute
        // porque en algunas GPUs de Windows 10/11 oculta ventanas WS_EX_LAYERED.
        _ = hwnd;
        _ = enabled;
        _ = tintAbgr;
    }

    private enum AccentState
    {
        AccentDisabled = 0,
        AccentEnableBlurBehind = 3,
        AccentEnableAcrylicBlurBehind = 4
    }

    private enum WindowCompositionAttribute
    {
        WcaAccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
