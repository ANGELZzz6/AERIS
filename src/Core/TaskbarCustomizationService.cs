using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace IslasEscritorio.Core;

[ComImport]
[Guid("5BCF9150-C28A-4EF2-913C-4C3EA2F5EAD0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITaskbarAppearanceService
{
    [PreserveSig]
    int SetTaskbarAppearance(IntPtr taskbar, int brush, uint color);

    [PreserveSig]
    int SetTaskbarBlur(IntPtr taskbar, uint color, float blurAmount);

    [PreserveSig]
    int ReturnTaskbarToDefaultAppearance(IntPtr taskbar);

    [PreserveSig]
    int SetTaskbarBorderVisibility(IntPtr taskbar, bool visible);

    [PreserveSig]
    int RestoreAllTaskbarsToDefault();

    [PreserveSig]
    int RestoreAllTaskbarsToDefaultWhenProcessDies(uint pid);
}

/// <summary>Aplica preferencias de barra de tareas del usuario mediante inyección XAML nativa (ExplorerTAP/TranslucentTB) con opción de mostrar/ocultar borde (ShowLine) y restaura al salir.</summary>
public sealed class TaskbarCustomizationService : IDisposable
{
    private const string AdvancedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const int WcaAccentPolicy = 19;
    private const uint WmDwmCompositionChanged = 0x031E;

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtAuto = 0;
    private const int DwmsbtNone = 1;
    private const int DwmsbtAcrylic = 3;

    private const int AccentDisabled = 0;
    private const int AccentEnableGradient = 1;
    private const int AccentEnableTransparentGradient = 2;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int SwHide = 0;
    private const int SwShowna = 8;

    private static readonly Guid IidITaskbarAppearanceService = new("5BCF9150-C28A-4EF2-913C-4C3EA2F5EAD0");

    private readonly Dictionary<(string Path, string Name), object?> _original = [];
    private bool _captured;
    private bool _active;
    private readonly DispatcherTimer _refreshTimer;
    private ITaskbarAppearanceService? _tapService;
    private bool _tapInjectionAttempted;

    public bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public TaskbarCustomizationService()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => RefreshTaskbars();
    }

    public void Apply(TaskbarConfig config)
    {
        bool habilitar = config.Habilitada || (config.Estilo != null && !config.Estilo.Equals("normal", StringComparison.OrdinalIgnoreCase) && !config.Estilo.Equals("predeterminado", StringComparison.OrdinalIgnoreCase));
        if (!habilitar)
        {
            Restore();
            return;
        }

        if (_active && _lastConfig == config)
        {
            ApplyTaskbarAccent(config);
            return;
        }

        CaptureOriginalValues();
        Set(AdvancedPath, "TaskbarAl", config.Alineacion == "izquierda" ? 0 : 1);
        Set(AdvancedPath, "TaskbarGlomLevel", config.CombinarBotones switch { "nunca" => 2, "lleno" => 1, _ => 0 });
        Set(AdvancedPath, "MMTaskbarGlomLevel", config.CombinarBotones switch { "nunca" => 2, "lleno" => 1, _ => 0 });

        if (config.Estilo is "normal" or "predeterminado")
        {
            RestoreSettingOnlyIfPreviouslyApplied(PersonalizePath, "EnableTransparency");
        }
        else if (config.Estilo is "clear" or "claro" or "transparente" or "acrylic" or "acrilico" or "blur" or "desenfoque")
        {
            Set(PersonalizePath, "EnableTransparency", 1);
        }
        else
        {
            Set(PersonalizePath, "EnableTransparency", 0);
        }

        _active = true;
        _lastConfig = config;
        _lastStyle = config.Estilo;
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
        ApplyTaskbarAccent(config);
    }

    private TaskbarConfig? _lastConfig;
    private string? _lastStyle;

    private void RefreshTaskbars()
    {
        if (_active && _lastConfig is not null)
            ApplyTaskbarAccent(_lastConfig);
    }

    private void EnsureTapServiceInjected(IntPtr primaryTaskbar)
    {
        if (_tapService != null || _tapInjectionAttempted || primaryTaskbar == IntPtr.Zero)
            return;

        _tapInjectionAttempted = true;
        try
        {
            try
            {
                InjectExplorerHook(primaryTaskbar);
            }
            catch { }

            Guid riid = IidITaskbarAppearanceService;
            int hr = InjectExplorerTAP(primaryTaskbar, ref riid, out object objService);
            if (hr == 0 && objService is ITaskbarAppearanceService service)
            {
                _tapService = service;
                try
                {
                    _tapService.RestoreAllTaskbarsToDefaultWhenProcessDies((uint)Environment.ProcessId);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inyección TAP falló: {ex.Message}");
        }
    }

    private struct TaskbarTargetInfo
    {
        public IntPtr TaskbarHwnd;
        public IntPtr WorkerWHwnd;
    }

    private static List<TaskbarTargetInfo> GetTaskbarTargets()
    {
        var targets = new List<TaskbarTargetInfo>();

        void CollectTarget(IntPtr taskbarHwnd)
        {
            if (taskbarHwnd == IntPtr.Zero) return;

            var target = new TaskbarTargetInfo
            {
                TaskbarHwnd = taskbarHwnd,
                WorkerWHwnd = IntPtr.Zero
            };

            EnumChildWindows(taskbarHwnd, (childHwnd, _) =>
            {
                var sb = new StringBuilder(256);
                if (GetClassName(childHwnd, sb, sb.Capacity) > 0)
                {
                    if (sb.ToString().Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
                    {
                        target.WorkerWHwnd = childHwnd;
                    }
                }
                return true;
            }, IntPtr.Zero);

            targets.Add(target);
        }

        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        CollectTarget(primary);

        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null);
            if (secondary == IntPtr.Zero) break;
            CollectTarget(secondary);
        }

        return targets;
    }

    private void ApplyTaskbarAccent(TaskbarConfig config)
    {
        List<TaskbarTargetInfo> targets = GetTaskbarTargets();
        if (targets.Count == 0) return;

        IntPtr primary = targets[0].TaskbarHwnd;
        EnsureTapServiceInjected(primary);

        string estilo = config.Estilo?.ToLowerInvariant() ?? "normal";

        // Mostrar u ocultar la línea superior de la barra de tareas según config.MostrarBorde
        foreach (var target in targets)
        {
            if (target.WorkerWHwnd != IntPtr.Zero)
            {
                ShowWindow(target.WorkerWHwnd, config.MostrarBorde ? SwShowna : SwHide);
            }
        }

        if (_tapService != null)
        {
            foreach (var target in targets)
            {
                IntPtr hwnd = target.TaskbarHwnd;
                if (estilo is "clear" or "claro" or "transparente")
                {
                    _tapService.SetTaskbarAppearance(hwnd, 0 /* SolidColor */, 0x00000000);
                    _tapService.SetTaskbarBorderVisibility(hwnd, config.MostrarBorde);
                }
                else if (estilo is "acrylic" or "acrilico")
                {
                    uint color = ParseColorToAbgr(config.Color, defaultAlpha: 180);
                    _tapService.SetTaskbarAppearance(hwnd, 1 /* Acrylic */, color);
                    _tapService.SetTaskbarBorderVisibility(hwnd, config.MostrarBorde);
                }
                else if (estilo is "blur" or "desenfoque")
                {
                    uint color = ParseColorToAbgr(config.Color, defaultAlpha: 180);
                    _tapService.SetTaskbarBlur(hwnd, color, 30.0f);
                    _tapService.SetTaskbarBorderVisibility(hwnd, config.MostrarBorde);
                }
                else if (estilo is "opaque" or "opaco" or "color")
                {
                    uint color = ParseColorToAbgr(config.Color, defaultAlpha: 255) | 0xFF000000;
                    _tapService.SetTaskbarAppearance(hwnd, 0 /* SolidColor */, color);
                    _tapService.SetTaskbarBorderVisibility(hwnd, config.MostrarBorde);
                }
                else
                {
                    _tapService.ReturnTaskbarToDefaultAppearance(hwnd);
                    _tapService.SetTaskbarBorderVisibility(hwnd, true);
                }
            }
            return;
        }

        // Fallback Win32
        int state;
        int flags = 2;
        uint abgrColor;
        int backdropType = DwmsbtAuto;

        switch (estilo)
        {
            case "clear":
            case "claro":
            case "transparente":
                state = AccentEnableTransparentGradient;
                flags = 2;
                abgrColor = 0x00000000;
                backdropType = DwmsbtNone;
                break;

            case "acrylic":
            case "acrilico":
                state = AccentEnableAcrylicBlurBehind;
                flags = 0;
                abgrColor = ParseColorToAbgr(config.Color, defaultAlpha: 180);
                if ((abgrColor & 0xFF000000) == 0) abgrColor |= 0x01000000;
                backdropType = DwmsbtAcrylic;
                break;

            case "blur":
            case "desenfoque":
                state = AccentEnableBlurBehind;
                flags = 2;
                abgrColor = ParseColorToAbgr(config.Color, defaultAlpha: 180);
                backdropType = DwmsbtAcrylic;
                break;

            case "opaque":
            case "opaco":
            case "color":
                state = AccentEnableGradient;
                flags = 2;
                abgrColor = ParseColorToAbgr(config.Color, defaultAlpha: 255) | 0xFF000000;
                backdropType = DwmsbtNone;
                break;

            case "normal":
            case "predeterminado":
            default:
                state = AccentDisabled;
                flags = 0;
                abgrColor = 0;
                backdropType = DwmsbtAuto;
                break;
        }

        foreach (var target in targets)
        {
            SetAccentAndBackdrop(target.TaskbarHwnd, state, flags, abgrColor, backdropType);
        }
    }

    private static void SetAccentAndBackdrop(IntPtr hwnd, int accentState, int accentFlags, uint color, int backdropType)
    {
        if (hwnd == IntPtr.Zero) return;

        var policy = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            GradientColor = color,
            AnimationId = 0
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, memory, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = memory,
                SizeOfData = (nuint)size
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int backdrop = backdropType;
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }

        PostMessage(hwnd, WmDwmCompositionChanged, (IntPtr)1, IntPtr.Zero);
    }

    private void CaptureOriginalValues()
    {
        if (_captured) return;
        foreach ((string path, string name) in new[]
        {
            (AdvancedPath, "TaskbarAl"), (AdvancedPath, "TaskbarGlomLevel"), (AdvancedPath, "MMTaskbarGlomLevel"),
            (AdvancedPath, "TaskbarBadges"), (PersonalizePath, "EnableTransparency")
        })
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
            _original[(path, name)] = key?.GetValue(name);
        }
        _captured = true;
    }

    private static void Set(string path, string name, int value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private void RestoreSetting(string path, string name)
    {
        if (!_original.TryGetValue((path, name), out object? value)) return;
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(path);
        if (value is null) key.DeleteValue(name, false);
        else key.SetValue(name, value);
    }

    private void RestoreSettingOnlyIfPreviouslyApplied(string path, string name)
    {
        if (_active) RestoreSetting(path, name);
    }

    private static uint ParseColorToAbgr(string hexColor, byte defaultAlpha)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return 0;

        string hex = hexColor.Trim().TrimStart('#');
        byte a = defaultAlpha;
        byte r = 0, g = 0, b = 0;

        try
        {
            if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint argb))
            {
                a = (byte)((argb >> 24) & 0xFF);
                r = (byte)((argb >> 16) & 0xFF);
                g = (byte)((argb >> 8) & 0xFF);
                b = (byte)(argb & 0xFF);
            }
            else if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
            {
                r = (byte)((rgb >> 16) & 0xFF);
                g = (byte)((rgb >> 8) & 0xFF);
                b = (byte)(rgb & 0xFF);
            }
            else
            {
                return 0;
            }
        }
        catch
        {
            return 0;
        }

        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | (uint)r;
    }

    public void Restore()
    {
        if (!_captured || !_active) return;
        _refreshTimer.Stop();

        if (_tapService != null)
        {
            try { _tapService.RestoreAllTaskbarsToDefault(); } catch { }
        }
        else
        {
            ApplyTaskbarAccent(new TaskbarConfig { Habilitada = false, Estilo = "normal" });
        }

        foreach (var entry in _original.Where(e => e.Key.Name is "TaskbarAl" or "TaskbarGlomLevel" or "MMTaskbarGlomLevel" || _active))
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(entry.Key.Path);
            if (entry.Value is null) key.DeleteValue(entry.Key.Name, false);
            else key.SetValue(entry.Key.Name, entry.Value);
        }
        _active = false;
        _lastConfig = null;
        _lastStyle = null;
    }

    public void Dispose()
    {
        Restore();
        _refreshTimer.Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint SizeOfData;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("ExplorerTAP.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int InjectExplorerTAP(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);

    [DllImport("ExplorerHooks.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr InjectExplorerHook(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
