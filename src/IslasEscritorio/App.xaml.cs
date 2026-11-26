using System.Windows;
using System.Windows.Threading;
using IslasEscritorio.Core;
using IslasEscritorio.Desktop;
using IslasEscritorio.Inventory;
using IslasEscritorio.Islands;
using IslasEscritorio.Settings;
using IslasEscritorio.Tray;
using Application = System.Windows.Application;

namespace IslasEscritorio;

public partial class App : Application
{
    private ConfigService? _configService;
    private DesktopIconController? _desktopController;
    private DesktopInventory? _inventory;
    private IslandCoordinator? _coordinator;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private TaskbarCustomizationService? _taskbarService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        _configService = new ConfigService();
        _taskbarService = new TaskbarCustomizationService();
        _taskbarService.Apply(_configService.Current.Taskbar);
        _configService.ConfigChanged += cfg => _taskbarService?.Apply(cfg.Taskbar);
        _desktopController = new DesktopIconController();
        _inventory = new DesktopInventory();
        _coordinator = new IslandCoordinator(_configService, _desktopController, _inventory, Dispatcher);
        _tray = new TrayIconService();

        _tray.OpenSettingsClicked += OpenOrFocusSettings;
        _tray.TogglePauseClicked += () => _coordinator.TogglePause();
        _tray.ToggleIslandsClicked += () => _coordinator.ToggleIslandsVisibility();
        _tray.RestoreDesktopClicked += () => _coordinator.RestoreDesktopAndPause();
        _tray.OcrRequested += () => _coordinator.StartOcrScreenCapture();
        _tray.MemoryOptimizeRequested += () =>
        {
            var (opt, freed) = MemoryOptimizer.OptimizeMemory();
            double freedMb = freed / (1024.0 * 1024.0);
            _tray.ShowNotification("AERIS - Memoria RAM Optimizada ⚡", $"Procesos optimizados: {opt}\nMemoria liberada: {freedMb:F1} MB");
        };
        _tray.ExitClicked += ShutdownCleanly;

        _coordinator.PauseStateChanged += paused => _tray.SetPaused(paused);
        _coordinator.OpenSettingsRequested += OpenOrFocusSettings;
        _coordinator.NotificationRequested += (title, msg) => _tray.ShowNotification(title, msg);

        _coordinator.Start();
    }

    private void OpenOrFocusSettings()
    {
        if (_configService is null || _coordinator is null || _desktopController is null)
            return;

        if (_settingsWindow is { IsVisible: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_configService, _coordinator, _desktopController);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShutdownCleanly()
    {
        _settingsWindow?.Close();
        _tray?.Dispose();
        _taskbarService?.Dispose();
        _coordinator?.Dispose();
        Shutdown(0);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _desktopController?.RestoreIconsNow();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _desktopController?.RestoreIconsNow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _coordinator?.Dispose();
        _taskbarService?.Dispose();
        base.OnExit(e);
    }
}
