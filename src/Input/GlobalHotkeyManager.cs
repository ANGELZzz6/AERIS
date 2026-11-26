using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using IslasEscritorio.Core;

namespace IslasEscritorio.Input;

public enum HotkeyAction
{
    RestoreDesktop = 1,
    TogglePause = 2,
    ToggleIslands = 3,
    OpenSettings = 4,
    TriggerOcr = 5
}

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _window;
    private readonly List<int> _registeredIds = [];
    private bool _disposed;

    public event Action<HotkeyAction>? HotkeyPressed;

    public GlobalHotkeyManager()
    {
        var parameters = new HwndSourceParameters("IslasEscritorio_Hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);
    }

    public void ApplyConfig(GlobalConfig global)
    {
        UnregisterAll();
        TryRegister(HotkeyAction.RestoreDesktop, global.AtajoRestaurar);
        TryRegister(HotkeyAction.TogglePause, global.AtajoPausar);
        TryRegister(HotkeyAction.ToggleIslands, global.AtajoAlternar);
        TryRegister(HotkeyAction.OpenSettings, global.AtajoAjustes);
        TryRegister(HotkeyAction.TriggerOcr, global.AtajoOcr);
    }

    private void TryRegister(HotkeyAction action, string? shortcut)
    {
        if (!TryParseShortcut(shortcut, out uint modifiers, out uint vk))
            return;

        int id = (int)action;
        if (RegisterHotKey(_window.Handle, id, modifiers | ModNoRepeat, vk))
            _registeredIds.Add(id);
    }

    public static bool TryParseShortcut(string? shortcut, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        string[] parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        Key? key = null;
        foreach (string part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift" or "mayus":
                    modifiers |= ModShift;
                    break;
                case "win" or "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out Key parsed))
                        key = parsed;
                    else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                    {
                        string keyName = char.IsDigit(part[0]) ? "D" + part[0] : part.ToUpperInvariant();
                        if (Enum.TryParse<Key>(keyName, out Key dkey))
                            key = dkey;
                    }
                    break;
            }
        }

        if (key is null || key == Key.None) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (Enum.IsDefined(typeof(HotkeyAction), id))
            {
                HotkeyPressed?.Invoke((HotkeyAction)id);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_window.Handle, id);
        _registeredIds.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
