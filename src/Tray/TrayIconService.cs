using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IslasEscritorio.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;
    private bool _disposed;

    public event Action? OpenSettingsClicked;
    public event Action? TogglePauseClicked;
    public event Action? ToggleIslandsClicked;
    public event Action? RestoreDesktopClicked;
    public event Action? OcrRequested;
    public event Action? MemoryOptimizeRequested;
    public event Action? ExitClicked;

    public TrayIconService()
    {
        _activeIcon = CreateTrayIcon(paused: false);
        _pausedIcon = CreateTrayIcon(paused: true);

        var menu = new ContextMenuStrip();
        var settingsItem = new ToolStripMenuItem("Ajustes...") { Font = new Font(menu.Font, FontStyle.Bold) };
        settingsItem.Click += (_, _) => OpenSettingsClicked?.Invoke();

        _pauseItem = new ToolStripMenuItem("Pausar");
        _pauseItem.Click += (_, _) => TogglePauseClicked?.Invoke();

        var toggleIslandsItem = new ToolStripMenuItem("Mostrar / ocultar islas");
        toggleIslandsItem.Click += (_, _) => ToggleIslandsClicked?.Invoke();

        var ocrItem = new ToolStripMenuItem("🔍 Extraer texto de pantalla (OCR)");
        ocrItem.Click += (_, _) => OcrRequested?.Invoke();

        var optimizeRamItem = new ToolStripMenuItem("⚡ Optimizar Memoria RAM");
        optimizeRamItem.Click += (_, _) => MemoryOptimizeRequested?.Invoke();

        var restoreItem = new ToolStripMenuItem("Restaurar escritorio");
        restoreItem.Click += (_, _) => RestoreDesktopClicked?.Invoke();

        var exitItem = new ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => ExitClicked?.Invoke();

        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(toggleIslandsItem);
        menu.Items.Add(ocrItem);
        menu.Items.Add(optimizeRamItem);
        menu.Items.Add(restoreItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadApplicationIcon() ?? _activeIcon,
            Text = "AERIS",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettingsClicked?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Text = paused ? "Reanudar" : "Pausar";
        _notifyIcon.Icon = paused ? _pausedIcon : _activeIcon;
        _notifyIcon.Text = paused ? "AERIS (En pausa)" : "AERIS";
    }

    private static Icon? LoadApplicationIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            return executable is null ? null : Icon.ExtractAssociatedIcon(executable);
        }
        catch { return null; }
    }

    public void ShowNotification(string title, string message)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(3500, title, message, ToolTipIcon.Info);
    }

    private static Icon CreateTrayIcon(bool paused)
    {
        using var bmp = new Bitmap(32, 32);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bgBrush = new SolidBrush(Color.FromArgb(30, 30, 46));
        using var borderPen = new Pen(paused ? Color.FromArgb(249, 226, 175) : Color.FromArgb(137, 180, 250), 2f);
        g.FillEllipse(bgBrush, 2, 2, 28, 28);
        g.DrawEllipse(borderPen, 2, 2, 28, 28);

        Color barColor = paused ? Color.FromArgb(249, 226, 175) : Color.FromArgb(137, 180, 250);
        using var barBrush = new SolidBrush(barColor);
        g.FillRectangle(barBrush, 7, 9, 5, 14);
        g.FillRectangle(barBrush, 20, 11, 5, 12);
        g.FillRectangle(barBrush, 11, 7, 10, 4);

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _activeIcon.Dispose();
        _pausedIcon.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
