using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using IslasEscritorio.Core;
using Microsoft.Win32;

namespace IslasEscritorio.Desktop;

/// <summary>
/// Controla de forma segura la visibilidad de los iconos nativos de Explorer,
/// con recuperación al iniciar/salir, proceso vigilante y reacción por eventos (sin sondeo).
/// </summary>
public sealed class DesktopIconController : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private const uint EventObjectShow = 0x8002;
    private const uint WineventOutOfContext = 0x0000;

    private static readonly string MarkerPath = Path.Combine(
        ConfigService.DefaultConfigDirectory, "icons-hidden.state");

    private readonly uint _taskbarCreatedMsg;
    private HwndSource? _messageWindow;
    private IntPtr _winEventHook;
    private WinEventDelegate? _winEventDelegate;
    private bool _shouldBeHidden;
    private bool _initialized;
    private bool _disposed;

    public bool IconsCurrentlyHidden { get; private set; }
    public event Action? ExplorerOrDisplayChanged;

    public DesktopIconController()
    {
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
    }

    public static int RunWatcherIfRequested(string[] args)
    {
        if (args.Length == 2 && args[0] == "--watch" && int.TryParse(args[1], out int pid))
        {
            try
            {
                using Process owner = Process.GetProcessById(pid);
                owner.WaitForExit();
            }
            catch { }

            if (File.Exists(MarkerPath))
            {
                DesktopIcons.SetVisible(true);
                TryDeleteMarker();
            }
            return 0;
        }

        if (args.Length == 1 && args[0] == "--restore")
        {
            DesktopIcons.SetVisible(true);
            TryDeleteMarker();
            return 0;
        }

        return -1;
    }

    public void Initialize(bool hideIconsInitially)
    {
        _initialized = true;
        // Recuperación obligatoria al arrancar si una sesión previa cerró inesperadamente.
        if (File.Exists(MarkerPath))
        {
            DesktopIcons.SetVisible(true);
            TryDeleteMarker();
        }

        StartWatcherProcess();
        CreateMessageWindow();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        SetIconsHidden(hideIconsInitially);
    }

    public void SetIconsHidden(bool hidden)
    {
        _shouldBeHidden = hidden;

        if (hidden)
        {
            IntPtr list = DesktopIcons.FindListView();
            if (list == IntPtr.Zero)
                return;

            Directory.CreateDirectory(ConfigService.DefaultConfigDirectory);
            File.WriteAllText(MarkerPath, "hidden\n", Encoding.UTF8);
            if (DesktopIcons.SetVisible(false))
            {
                IconsCurrentlyHidden = true;
                EnsureExplorerWinEventHook();
            }
        }
        else
        {
            UnhookWinEvent();
            DesktopIcons.SetVisible(true);
            TryDeleteMarker();
            IconsCurrentlyHidden = false;
        }
    }

    public void RestoreIconsNow() => SetIconsHidden(false);

    private void CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("IslasEscritorio_DesktopEvents")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDisplayChange)
        {
            ReapplyIfNeeded();
            ExplorerOrDisplayChanged?.Invoke();
        }
        else if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
        {
            // Explorer se reinició: reenganchamos el WinEventHook al nuevo PID y reaplicamos ocultación.
            UnhookWinEvent();
            ReapplyIfNeeded();
            ExplorerOrDisplayChanged?.Invoke();
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        ReapplyIfNeeded();
        ExplorerOrDisplayChanged?.Invoke();
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => RestoreOnShutdown();
    private void OnProcessExit(object? sender, EventArgs e) => RestoreOnShutdown();

    private void ReapplyIfNeeded()
    {
        if (!_shouldBeHidden || _disposed) return;

        IntPtr list = DesktopIcons.FindListView();
        if (list != IntPtr.Zero && DesktopIcons.IsVisible(list))
            DesktopIcons.SetVisible(false);

        EnsureExplorerWinEventHook();
    }

    private void EnsureExplorerWinEventHook()
    {
        if (_winEventHook != IntPtr.Zero || _disposed) return;
        uint explorerPid = DesktopIcons.GetExplorerProcessId();
        if (explorerPid == 0) return;

        _winEventDelegate ??= OnWinEvent;
        _winEventHook = SetWinEventHook(
            EventObjectShow,
            EventObjectShow,
            IntPtr.Zero,
            _winEventDelegate,
            explorerPid,
            0,
            WineventOutOfContext);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_shouldBeHidden || _disposed || idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        IntPtr list = DesktopIcons.FindListView();
        if (list != IntPtr.Zero && hwnd == list && DesktopIcons.IsVisible(list))
        {
            DesktopIcons.SetVisible(false);
        }
    }

    private void UnhookWinEvent()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }

    private static void StartWatcherProcess()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            var start = new ProcessStartInfo(exe, $"--watch {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(start)?.Dispose();
        }
        catch { }
    }

    private void RestoreOnShutdown()
    {
        UnhookWinEvent();
        if (!_initialized) return;
        DesktopIcons.SetVisible(true);
        TryDeleteMarker();
        IconsCurrentlyHidden = false;
    }

    private static void TryDeleteMarker()
    {
        try { File.Delete(MarkerPath); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        RestoreOnShutdown();
        _messageWindow?.RemoveHook(WndProc);
        _messageWindow?.Dispose();
        _messageWindow = null;
    }

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
