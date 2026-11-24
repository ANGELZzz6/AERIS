using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IslasEscritorio.Islands;

/// <summary>
/// Lanza elementos del escritorio y muestra el menú contextual nativo del Shell de Windows (IContextMenu).
/// </summary>
public static class ShellContextMenu
{
    private const uint CmfNormal = 0x00000000;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmLeftAlign = 0x0000;
    private const int SwShowNormal = 1;

    public static void OpenItem(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fullPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"No se pudo abrir '{Path.GetFileName(fullPath)}':\n{ex.Message}",
                "Islas del escritorio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public static void ShowContextMenu(Window ownerWindow, string fullPath, int screenX, int screenY)
    {
        IntPtr ownerHwnd = new WindowInteropHelper(ownerWindow).Handle;
        if (ownerHwnd == IntPtr.Zero) return;

        IntPtr pidl = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        IntPtr hMenu = IntPtr.Zero;

        try
        {
            if (SHParseDisplayName(fullPath, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                return;

            Guid iidShellFolder = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidl, ref iidShellFolder, out object parentObj, out IntPtr childPidl) != 0)
                return;

            parentFolder = parentObj as IShellFolder;
            if (parentFolder is null) return;

            IntPtr[] pidls = [childPidl];
            Guid iidContextMenu = typeof(IContextMenu).GUID;
            if (parentFolder.GetUIObjectOf(ownerHwnd, 1, pidls, ref iidContextMenu, IntPtr.Zero, out object menuObj) != 0)
                return;

            contextMenu = menuObj as IContextMenu;
            if (contextMenu is null) return;

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            if (contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CmfNormal) < 0)
                return;

            SetForegroundWindow(ownerHwnd);
            uint cmd = TrackPopupMenuEx(hMenu, TpmReturnCmd | TpmLeftAlign, screenX, screenY, ownerHwnd, IntPtr.Zero);
            if (cmd > 0)
            {
                var invoke = new Cminvokecommandinfoex
                {
                    cbSize = Marshal.SizeOf<Cminvokecommandinfoex>(),
                    fMask = 0,
                    hwnd = ownerHwnd,
                    lpVerb = (IntPtr)(cmd - 1),
                    nShow = SwShowNormal
                };
                contextMenu.InvokeCommand(ref invoke);
            }
        }
        catch
        {
            // Evitar que un fallo en extensiones de shell de terceros cierre la app.
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
            if (parentFolder is not null) Marshal.ReleaseComObject(parentFolder);
            if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref Cminvokecommandinfoex pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Cminvokecommandinfoex
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public int ptInvokeX;
        public int ptInvokeY;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv, out IntPtr ppidlLast);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
