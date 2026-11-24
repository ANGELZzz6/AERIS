using System.ComponentModel;
using System.Runtime.InteropServices;
using IslasEscritorio.Core;

namespace IslasEscritorio.Input;

/// <summary>
/// Hook de ratón de bajo nivel (WH_MOUSE_LL) ejecutado en un hilo dedicado con bucle de mensajes Win32
/// (GetMessage/DispatchMessage) para que Windows nunca congele el cursor esperando el timeout del hook.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const uint WmQuit = 0x0012;

    private readonly HookProc _callback;
    private readonly Action<ScreenPoint> _onRelevantMove;
    private readonly Func<int, int, bool>? _fastFilter;

    private IntPtr _hook;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private Exception? _startupError;

    private int _latestX;
    private int _latestY;
    private int _notifyScheduled;
    private volatile bool _disposed;

    public MouseHook(Action<ScreenPoint> onRelevantMove, Func<int, int, bool>? fastFilter = null)
    {
        _callback = HookCallback;
        _onRelevantMove = onRelevantMove;
        _fastFilter = fastFilter;
    }

    public void Install()
    {
        if (_hookThread is not null) return;

        _hookThread = new Thread(HookThreadMain)
        {
            Name = "IslasEscritorio.MouseHook",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _hookThread.Start();
        _ready.Wait();

        if (_startupError is not null)
            throw _startupError;
    }

    public ScreenPoint GetLatestCursor() =>
        new(Volatile.Read(ref _latestX), Volatile.Read(ref _latestY));

    private void HookThreadMain()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            // Forzamos la creación de la cola de mensajes antes de instalar el hook.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

            _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                _startupError = new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "No se pudo instalar el hook de ratón (WH_MOUSE_LL).");
                _ready.Set();
                return;
            }

            _ready.Set();

            // Bucle de mensajes Win32 obligatorio: garantiza retorno inmediato al SO y ~0 % CPU en reposo.
            while (GetMessage(out Msg msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            _ready.Set();
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private unsafe IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == (IntPtr)WmMouseMove && !_disposed)
        {
            MouseLowLevelStruct* data = (MouseLowLevelStruct*)lParam;
            int x = data->Pt.X;
            int y = data->Pt.Y;

            Volatile.Write(ref _latestX, x);
            Volatile.Write(ref _latestY, y);

            // Solo despertamos al hilo principal si el cursor toca una zona activa o hay islas no cerradas.
            if (_fastFilter is null || _fastFilter(x, y))
            {
                if (Interlocked.CompareExchange(ref _notifyScheduled, 1, 0) == 0)
                {
                    ScreenPoint point = new(x, y);
                    ThreadPool.UnsafeQueueUserWorkItem(static state =>
                    {
                        var hook = (MouseHook)state!;
                        Volatile.Write(ref hook._notifyScheduled, 0);
                        if (!hook._disposed)
                            hook._onRelevantMove(hook.GetLatestCursor());
                    }, this);
                }
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

        _hookThread?.Join(500);
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
}
