using System.Windows.Threading;
using IslasEscritorio.Core;
using IslasEscritorio.Desktop;
using IslasEscritorio.Input;
using IslasEscritorio.Inventory;
using Application = System.Windows.Application;

namespace IslasEscritorio.Islands;

/// <summary>
/// Orquestador principal de la aplicación en tiempo de ejecución:
/// conecta el hook de ratón, zonas, ventanas de islas, inventario, atajos e iconos del escritorio.
/// </summary>
public sealed class IslandCoordinator : IDisposable
{
    private readonly ConfigService _configService;
    private readonly DesktopIconController _desktopController;
    private readonly DesktopInventory _inventory;
    private readonly ZoneManager _zoneManager;
    private readonly MouseHook _mouseHook;
    private readonly GlobalHotkeyManager _hotkeys;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _oneShotTimer;
    private readonly Dictionary<string, IslandWindow> _windows = new();

    private IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _disposed;

    public bool IsPaused => _zoneManager.Paused;
    public event Action<bool>? PauseStateChanged;
    public event Action? OpenSettingsRequested;
    public event Action<string, string>? NotificationRequested;

    public IslandCoordinator(
        ConfigService configService,
        DesktopIconController desktopController,
        DesktopInventory inventory,
        Dispatcher dispatcher)
    {
        _configService = configService;
        _desktopController = desktopController;
        _inventory = inventory;
        _dispatcher = dispatcher;
        _zoneManager = new ZoneManager();
        _hotkeys = new GlobalHotkeyManager();

        _oneShotTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        _oneShotTimer.Tick += OnOneShotTimerTick;

        _mouseHook = new MouseHook(OnRelevantMouseMove, _zoneManager.IsPointRelevantFast);

        _configService.ConfigChanged += cfg => _dispatcher.BeginInvoke(() => ApplyConfiguration(cfg));
        _configService.ConfigError += err => _dispatcher.BeginInvoke(() =>
            NotificationRequested?.Invoke("Configuración JSON inválida", err));

        _desktopController.ExplorerOrDisplayChanged += () => _dispatcher.BeginInvoke(RefreshMonitorsAndZones);
        _inventory.InventoryChanged += _ => _dispatcher.BeginInvoke(RefreshAllIslandContents);
        _hotkeys.HotkeyPressed += action => _dispatcher.BeginInvoke(() => HandleHotkey(action));
    }

    public void Start()
    {
        AppConfig config = _configService.Current;
        StartupRegistration.SetEnabled(config.Global.IniciarConWindows);

        _desktopController.Initialize(config.Global.OcultarIconosEscritorio);
        _inventory.Start();
        ApplyConfiguration(config);
        _mouseHook.Install();

        if (!string.IsNullOrWhiteSpace(_configService.LastError))
            NotificationRequested?.Invoke("Aviso de configuración", _configService.LastError);
    }

    public void TogglePause()
    {
        bool nextPaused = !_zoneManager.Paused;
        _zoneManager.Paused = nextPaused;
        if (nextPaused)
        {
            _oneShotTimer.Stop();
            ApplyTransitions(_zoneManager.ForceCloseAll());
        }
        PauseStateChanged?.Invoke(nextPaused);
    }

    public void RestoreDesktopAndPause()
    {
        _desktopController.RestoreIconsNow();
        if (!_zoneManager.Paused)
            TogglePause();

        NotificationRequested?.Invoke(
            "Escritorio restaurado",
            "Los iconos nativos del escritorio se han restaurado y la detección quedó en pausa.");
    }

    public void ToggleIslandsVisibility()
    {
        if (_zoneManager.Paused)
            TogglePause();

        ApplyTransitions(_zoneManager.ToggleAllOpen());
        RefreshAllIslandContents();
    }

    public IReadOnlyList<DesktopItem> GetFilteredItemsForIsland(IslandConfig island) =>
        string.Equals(island.Tipo, "musica", StringComparison.OrdinalIgnoreCase)
            ? []
            : FilterMatcher.FilterAndSort(_inventory.GetItemsSnapshot(), island.Contenido);

    public IReadOnlyList<DesktopItem> GetAllDesktopItems() => _inventory.GetItemsSnapshot();

    public IconCache IconCache => _inventory.IconCache;

    private void ApplyConfiguration(AppConfig config)
    {
        _monitors = MonitorHelper.GetMonitors();
        _desktopController.SetIconsHidden(config.Global.OcultarIconosEscritorio && !_zoneManager.Paused);
        _hotkeys.ApplyConfig(config.Global);
        RebuildZonesAndWindows(config);
    }

    private void RefreshMonitorsAndZones()
    {
        _monitors = MonitorHelper.GetMonitors();
        RebuildZonesAndWindows(_configService.Current);
    }

    private void RebuildZonesAndWindows(AppConfig config)
    {
        _oneShotTimer.Stop();
        _zoneManager.Configure(config, _monitors);

        IReadOnlyList<ResolvedIslandZone> zones = _zoneManager.GetZonesSnapshot();
        var activeIds = new HashSet<string>(zones.Select(z => z.RuntimeId));

        foreach (string obsoleteId in _windows.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            IslandWindow win = _windows[obsoleteId];
            _windows.Remove(obsoleteId);
            win.Close();
        }

        IReadOnlyList<DesktopItem> allItems = _inventory.GetItemsSnapshot();
        foreach (ResolvedIslandZone zone in zones)
        {
            if (!_windows.TryGetValue(zone.RuntimeId, out IslandWindow? win))
            {
                win = new IslandWindow(
                    zone,
                    config.Appearance,
                    config.Activation.SiempreEncima,
                    OnIslandPinChanged);
                _windows[zone.RuntimeId] = win;
            }
            else
            {
                win.UpdateConfiguration(zone, config.Appearance, config.Activation.SiempreEncima);
            }

            IReadOnlyList<DesktopItem> filtered = string.Equals(zone.Config.Tipo, "musica", StringComparison.OrdinalIgnoreCase)
                ? []
                : FilterMatcher.FilterAndSort(allItems, zone.Config.Contenido);
            win.RenderItems(filtered, _inventory.IconCache);
        }
    }

    private void RefreshAllIslandContents()
    {
        IReadOnlyList<DesktopItem> allItems = _inventory.GetItemsSnapshot();
        foreach (ResolvedIslandZone zone in _zoneManager.GetZonesSnapshot())
        {
            if (_windows.TryGetValue(zone.RuntimeId, out IslandWindow? win))
            {
                IReadOnlyList<DesktopItem> filtered = string.Equals(zone.Config.Tipo, "musica", StringComparison.OrdinalIgnoreCase)
                    ? []
                    : FilterMatcher.FilterAndSort(allItems, zone.Config.Contenido);
                win.RenderItems(filtered, _inventory.IconCache);
            }
        }
    }

    private void OnRelevantMouseMove(ScreenPoint point)
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            IReadOnlyList<IslandStateTransition> transitions = _zoneManager.UpdateCursor(
                point,
                DateTime.UtcNow,
                CanActivateZone,
                GetOpenWindowBounds);

            ApplyTransitions(transitions);
            ScheduleNextTimerIfNeeded();
        });
    }

    private void OnOneShotTimerTick(object? sender, EventArgs e)
    {
        _oneShotTimer.Stop();
        if (_disposed) return;

        ScreenPoint latest = MonitorHelper.TryGetCursorPosition() ?? _mouseHook.GetLatestCursor();
        IReadOnlyList<IslandStateTransition> transitions = _zoneManager.UpdateCursor(
            latest,
            DateTime.UtcNow,
            CanActivateZone,
            GetOpenWindowBounds);

        ApplyTransitions(transitions);
        ScheduleNextTimerIfNeeded();
    }

    private void ScheduleNextTimerIfNeeded()
    {
        DateTime? next = _zoneManager.GetNextDeadlineUtc();
        if (next is null)
        {
            _oneShotTimer.Stop();
            return;
        }

        TimeSpan delay = next.Value - DateTime.UtcNow;
        _oneShotTimer.Interval = delay > TimeSpan.FromMilliseconds(5)
            ? delay
            : TimeSpan.FromMilliseconds(5);
        _oneShotTimer.Start();
    }

    private bool CanActivateZone(ResolvedIslandZone zone)
    {
        ActivationConfig act = _configService.Current.Activation;
        if (act.DesactivarMientrasSeArrastra && MonitorHelper.IsMouseButtonPressed())
            return false;

        if (act.DesactivarEnPantallaCompleta)
        {
            IntPtr ownHandle = Application.Current?.MainWindow is { } mw
                ? new System.Windows.Interop.WindowInteropHelper(mw).Handle
                : IntPtr.Zero;
            if (MonitorHelper.IsFullscreenWindowOnMonitor(zone.Monitor.Bounds, ownHandle))
                return false;
        }

        return true;
    }

    private ScreenRect? GetOpenWindowBounds(string runtimeId) =>
        _windows.TryGetValue(runtimeId, out IslandWindow? win) && win.IsVisible
            ? win.CurrentPhysicalBounds
            : null;

    private void ApplyTransitions(IReadOnlyList<IslandStateTransition> transitions)
    {
        foreach (IslandStateTransition tr in transitions)
        {
            if (!_windows.TryGetValue(tr.Zone.RuntimeId, out IslandWindow? win))
                continue;

            if (tr.NewState == IslandState.Open && tr.PreviousState != IslandState.Closing)
            {
                // El inventario y los iconos ya se renderizan al iniciar y al cambiar archivos/configuración.
                // Evitar reconstruir toda la lista justo al abrir elimina trabajo costoso del camino visual.
                win.AnimateOpen();
            }
            else if (tr.NewState == IslandState.Closed &&
                     tr.PreviousState is IslandState.Open or IslandState.Closing)
            {
                win.AnimateClose();
            }
        }
    }

    private void OnIslandPinChanged(string runtimeId, bool pinned)
    {
        _zoneManager.SetPinned(runtimeId, pinned);
        if (!pinned)
        {
            ScreenPoint latest = _mouseHook.GetLatestCursor();
            ApplyTransitions(_zoneManager.UpdateCursor(latest, DateTime.UtcNow, CanActivateZone, GetOpenWindowBounds));
            ScheduleNextTimerIfNeeded();
        }
    }

    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.RestoreDesktop:
                RestoreDesktopAndPause();
                break;
            case HotkeyAction.TogglePause:
                TogglePause();
                break;
            case HotkeyAction.ToggleIslands:
                ToggleIslandsVisibility();
                break;
            case HotkeyAction.OpenSettings:
                OpenSettingsRequested?.Invoke();
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _oneShotTimer.Stop();
        _mouseHook.Dispose();
        _hotkeys.Dispose();

        foreach (IslandWindow win in _windows.Values)
            win.Close();
        _windows.Clear();

        _inventory.Dispose();
        _desktopController.Dispose();
        _configService.Dispose();
    }
}
