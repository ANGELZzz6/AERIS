using IslasEscritorio.Core;
using IslasEscritorio.Islands;

namespace IslasEscritorio.Input;

public sealed record ResolvedIslandZone(
    string RuntimeId,
    IslandConfig Config,
    IslandEdge Edge,
    MonitorInfo Monitor,
    ScreenRect ActivationRect,
    ScreenRect TargetWindowRect);

public sealed record IslandStateTransition(
    ResolvedIslandZone Zone,
    IslandState PreviousState,
    IslandState NewState);

/// <summary>
/// Calcula zonas de activación, geometría multimonitor y coordina las máquinas de estado por isla.
/// Mantiene un snapshot inmutable para que el hook de ratón pueda filtrar coordenadas sin bloqueos.
/// </summary>
public sealed class ZoneManager
{
    private readonly object _sync = new();
    private volatile FastFilterSnapshot _fastSnapshot = FastFilterSnapshot.Empty;
    private List<IslandEntry> _entries = [];
    private ScreenPoint _cursor;
    private CornerBehavior _cornerBehavior = CornerBehavior.OwnIsland;
    private int _zoneThicknessPx = 2;
    private bool _paused;

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            RebuildFastSnapshot();
        }
    }

    public void Configure(AppConfig config, IReadOnlyList<MonitorInfo> monitors)
    {
        ConfigValidator.TryParseCornerBehavior(config.Activation.ComportamientoEsquinas, out _cornerBehavior);
        _zoneThicknessPx = Math.Clamp(config.Activation.GrosorZonaPx, 1, 64);

        var nextEntries = new List<IslandEntry>();
        foreach (IslandConfig island in config.Islands)
        {
            if (!island.Habilitada) continue;
            if (!ConfigValidator.TryParseEdge(island.Borde, out IslandEdge edge)) continue;
            SpecialIslandPresentation presentation = island.Presentacion ??= new SpecialIslandPresentation();

            foreach (MonitorInfo monitor in ResolveTargetMonitors(island, config.Activation, monitors))
            {
                string runtimeId = $"{island.Id}@{monitor.Index}";
                bool special = !string.Equals(island.Tipo, "archivos", StringComparison.OrdinalIgnoreCase);
                string mode = presentation.Modo;
                bool persistent = special && mode is "siempre" or "flotante";
                ScreenRect activationRect = persistent
                    ? new ScreenRect(0, 0, 0, 0)
                    : ComputeActivationRect(edge, monitor.Bounds, _zoneThicknessPx);
                ScreenRect targetRect = special && mode == "flotante"
                    ? new ScreenRect(presentation.X, presentation.Y, presentation.X + 340, presentation.Y + 180)
                    : ComputeIslandWindowRect(island, edge, monitor);

                var zone = new ResolvedIslandZone(runtimeId, island, edge, monitor, activationRect, targetRect);
                var machine = new IslandStateMachine(
                    runtimeId,
                    island,
                    config.Activation.RetardoAperturaMs,
                    config.Activation.RetardoCierreMs);

                nextEntries.Add(new IslandEntry(zone, machine));
            }
        }

        lock (_sync)
        {
            _entries = nextEntries;
        }
        RebuildFastSnapshot();
    }

    /// <summary>
    /// Método ultra-rápido y libre de bloqueos invocado desde el callback de WH_MOUSE_LL.
    /// </summary>
    public bool IsPointRelevantFast(int x, int y)
    {
        FastFilterSnapshot snap = _fastSnapshot;
        if (snap.Paused) return false;
        if (snap.AnyNotClosed) return true;

        ScreenPoint pt = new(x, y);
        foreach (ScreenRect rect in snap.ActivationRects)
        {
            if (rect.Contains(pt)) return true;
        }
        return false;
    }

    public IReadOnlyList<ResolvedIslandZone> GetZonesSnapshot()
    {
        lock (_sync)
        {
            return _entries.Select(e => e.Zone).ToList();
        }
    }

    public DateTime? GetNextDeadlineUtc()
    {
        lock (_sync)
        {
            DateTime? earliest = null;
            foreach (IslandEntry entry in _entries)
            {
                if (entry.Machine.NextDeadlineUtc is { } dl && (earliest is null || dl < earliest.Value))
                    earliest = dl;
            }
            return earliest;
        }
    }

    public IReadOnlyList<IslandStateTransition> UpdateCursor(
        ScreenPoint cursor,
        DateTime nowUtc,
        Func<ResolvedIslandZone, bool>? canActivate = null,
        Func<string, ScreenRect?>? getOpenWindowRect = null,
        Func<ResolvedIslandZone, int>? getEffectiveDelay = null)
    {
        lock (_sync)
        {
            _cursor = cursor;
            return EvaluateLocked(nowUtc, canActivate, getOpenWindowRect, getEffectiveDelay);
        }
    }

    public IReadOnlyList<IslandStateTransition> Tick(
        DateTime nowUtc,
        Func<ResolvedIslandZone, bool>? canActivate = null,
        Func<string, ScreenRect?>? getOpenWindowRect = null,
        Func<ResolvedIslandZone, int>? getEffectiveDelay = null)
    {
        lock (_sync)
        {
            return EvaluateLocked(nowUtc, canActivate, getOpenWindowRect, getEffectiveDelay);
        }
    }

    public void SetPinned(string runtimeId, bool pinned)
    {
        lock (_sync)
        {
            IslandEntry? entry = _entries.FirstOrDefault(e => e.Zone.RuntimeId == runtimeId);
            if (entry is not null)
                entry.Machine.IsPinnedOpen = pinned;
        }
    }

    public IReadOnlyList<IslandStateTransition> ForceCloseAll()
    {
        var transitions = new List<IslandStateTransition>();
        lock (_sync)
        {
            foreach (IslandEntry entry in _entries)
            {
                IslandState before = entry.Machine.State;
                if (before != IslandState.Closed)
                {
                    entry.Machine.ForceClose();
                    transitions.Add(new(entry.Zone, before, IslandState.Closed));
                }
            }
        }
        RebuildFastSnapshot();
        return transitions;
    }

    public IReadOnlyList<IslandStateTransition> ToggleAllOpen()
    {
        var transitions = new List<IslandStateTransition>();
        lock (_sync)
        {
            bool anyOpen = _entries.Any(e => e.Machine.State is IslandState.Open or IslandState.Closing);
            foreach (IslandEntry entry in _entries)
            {
                IslandState before = entry.Machine.State;
                if (anyOpen)
                {
                    entry.Machine.ForceClose();
                    if (before != IslandState.Closed)
                        transitions.Add(new(entry.Zone, before, IslandState.Closed));
                }
                else
                {
                    entry.Machine.IsPinnedOpen = true;
                    entry.Machine.ForceOpen();
                    if (before != IslandState.Open)
                        transitions.Add(new(entry.Zone, before, IslandState.Open));
                }
            }
        }
        RebuildFastSnapshot();
        return transitions;
    }

    private List<IslandStateTransition> EvaluateLocked(
        DateTime nowUtc,
        Func<ResolvedIslandZone, bool>? canActivate,
        Func<string, ScreenRect?>? getOpenWindowRect,
        Func<ResolvedIslandZone, int>? getEffectiveDelay)
    {
        var transitions = new List<IslandStateTransition>();
        if (_paused) return transitions;

        // Primero determinamos si el cursor está en una intersección de esquina por monitor.
        var cornerSuppressedEdgesByMonitor = new Dictionary<int, HashSet<IslandEdge>>();
        foreach (IslandEntry entry in _entries)
        {
            if (cornerSuppressedEdgesByMonitor.ContainsKey(entry.Zone.Monitor.Index)) continue;
            ScreenRect mb = entry.Zone.Monitor.Bounds;
            IslandEdge? corner = DetectCornerAtPoint(_cursor, mb, _zoneThicknessPx);
            if (corner.HasValue)
            {
                bool hasCornerIsland = _entries.Any(e =>
                    e.Zone.Monitor.Index == entry.Zone.Monitor.Index && e.Zone.Edge == corner.Value);

                if (_cornerBehavior == CornerBehavior.None ||
                    (_cornerBehavior == CornerBehavior.OwnIsland && hasCornerIsland))
                {
                    cornerSuppressedEdgesByMonitor[entry.Zone.Monitor.Index] = GetAdjacentEdges(corner.Value);
                }
            }
        }

        foreach (IslandEntry entry in _entries)
        {
            bool inActivation = entry.Zone.ActivationRect.Contains(_cursor);
            if (!inActivation && entry.Machine.State == IslandState.Waiting)
            {
                // Histéresis leve mientras espera para evitar cancelaciones por rebote de 1-2 px en el borde.
                ScreenRect tolerantRect = ComputeActivationRect(entry.Zone.Edge, entry.Zone.Monitor.Bounds, _zoneThicknessPx + 4);
                inActivation = tolerantRect.Contains(_cursor);
            }

            if (inActivation &&
                !IsCorner(entry.Zone.Edge) &&
                cornerSuppressedEdgesByMonitor.TryGetValue(entry.Zone.Monitor.Index, out var suppressed) &&
                suppressed.Contains(entry.Zone.Edge))
            {
                inActivation = false;
            }

            bool inIsland = false;
            if (entry.Machine.State is IslandState.Open or IslandState.Closing)
            {
                ScreenRect islandRect = getOpenWindowRect?.Invoke(entry.Zone.RuntimeId)
                    ?? entry.Zone.TargetWindowRect;
                inIsland = islandRect.Contains(_cursor);
            }

            int effectiveDelay = getEffectiveDelay?.Invoke(entry.Zone) ?? entry.Machine.ActivationDelayMs;
            IslandState before = entry.Machine.State;
            bool changed = entry.Machine.Update(
                inActivation,
                inIsland,
                nowUtc,
                canActivate is null ? null : () => canActivate(entry.Zone),
                effectiveDelay);

            if (changed)
                transitions.Add(new(entry.Zone, before, entry.Machine.State));
        }

        if (transitions.Count > 0)
            RebuildFastSnapshot();

        return transitions;
    }

    private void RebuildFastSnapshot()
    {
        lock (_sync)
        {
            ScreenRect[] rects = _entries.Select(e => e.Zone.ActivationRect).ToArray();
            bool anyNotClosed = _entries.Any(e => e.Machine.State != IslandState.Closed);
            _fastSnapshot = new FastFilterSnapshot(_paused, anyNotClosed, rects);
        }
    }

    public static ScreenRect ComputeActivationRect(IslandEdge edge, ScreenRect monitorBounds, int thicknessPx)
    {
        int t = Math.Clamp(thicknessPx, 1, 64);
        int cornerSize = Math.Max(t, 12);
        return edge switch
        {
            IslandEdge.Left => new(monitorBounds.Left, monitorBounds.Top, monitorBounds.Left + t, monitorBounds.Bottom),
            IslandEdge.Right => new(monitorBounds.Right - t, monitorBounds.Top, monitorBounds.Right, monitorBounds.Bottom),
            IslandEdge.Top => new(monitorBounds.Left, monitorBounds.Top, monitorBounds.Right, monitorBounds.Top + t),
            IslandEdge.Bottom => new(monitorBounds.Left, monitorBounds.Bottom - t, monitorBounds.Right, monitorBounds.Bottom),
            IslandEdge.TopLeft => new(monitorBounds.Left, monitorBounds.Top, monitorBounds.Left + cornerSize, monitorBounds.Top + cornerSize),
            IslandEdge.TopRight => new(monitorBounds.Right - cornerSize, monitorBounds.Top, monitorBounds.Right, monitorBounds.Top + cornerSize),
            IslandEdge.BottomLeft => new(monitorBounds.Left, monitorBounds.Bottom - cornerSize, monitorBounds.Left + cornerSize, monitorBounds.Bottom),
            IslandEdge.BottomRight => new(monitorBounds.Right - cornerSize, monitorBounds.Bottom - cornerSize, monitorBounds.Right, monitorBounds.Bottom),
            _ => new(monitorBounds.Left, monitorBounds.Top, monitorBounds.Left, monitorBounds.Top)
        };
    }

    public static ScreenRect ComputeIslandWindowRect(IslandConfig config, IslandEdge edge, MonitorInfo monitor)
    {
        ScreenRect area = monitor.WorkArea;
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        IslandSizeConfig size = config.Tamano ?? new IslandSizeConfig();

        int defaultW = edge is IslandEdge.Top or IslandEdge.Bottom ? (int)(area.Width * 0.5) : (int)Math.Round(260 * scale);
        int defaultH = edge is IslandEdge.Left or IslandEdge.Right ? (int)(area.Height * 0.55) : (int)Math.Round(220 * scale);

        int width = size.AnchoPx.HasValue
            ? (int)Math.Round(size.AnchoPx.Value * scale)
            : size.AnchoPct.HasValue
                ? (int)Math.Round(area.Width * (size.AnchoPct.Value / 100.0))
                : defaultW;

        int height = size.AltoPx.HasValue
            ? (int)Math.Round(size.AltoPx.Value * scale)
            : size.AltoPct.HasValue
                ? (int)Math.Round(area.Height * (size.AltoPct.Value / 100.0))
                : defaultH;

        if (size.MinAnchoPx.HasValue) width = Math.Max(width, (int)Math.Round(size.MinAnchoPx.Value * scale));
        if (size.MaxAnchoPx.HasValue) width = Math.Min(width, (int)Math.Round(size.MaxAnchoPx.Value * scale));
        if (size.MinAltoPx.HasValue) height = Math.Max(height, (int)Math.Round(size.MinAltoPx.Value * scale));
        if (size.MaxAltoPx.HasValue) height = Math.Min(height, (int)Math.Round(size.MaxAltoPx.Value * scale));

        width = Math.Clamp(width, 80, Math.Max(80, area.Width));
        height = Math.Clamp(height, 80, Math.Max(80, area.Height));

        ConfigValidator.TryParseAnchor(config.Posicion?.Ancla, out IslandAnchor anchor);
        int offX = (int)Math.Round((config.Posicion?.DesplazamientoX ?? 0) * scale);
        int offY = (int)Math.Round((config.Posicion?.DesplazamientoY ?? 0) * scale);

        int left, top;
        switch (edge)
        {
            case IslandEdge.Left:
                left = area.Left;
                top = AlignAxis(area.Top, area.Height, height, anchor);
                break;
            case IslandEdge.Right:
                left = area.Right - width;
                top = AlignAxis(area.Top, area.Height, height, anchor);
                break;
            case IslandEdge.Top:
                left = AlignAxis(area.Left, area.Width, width, anchor);
                top = area.Top;
                break;
            case IslandEdge.Bottom:
                left = AlignAxis(area.Left, area.Width, width, anchor);
                top = area.Bottom - height;
                break;
            case IslandEdge.TopLeft:
                left = area.Left;
                top = area.Top;
                break;
            case IslandEdge.TopRight:
                left = area.Right - width;
                top = area.Top;
                break;
            case IslandEdge.BottomLeft:
                left = area.Left;
                top = area.Bottom - height;
                break;
            case IslandEdge.BottomRight:
                left = area.Right - width;
                top = area.Bottom - height;
                break;
            default:
                left = area.Left;
                top = area.Top;
                break;
        }

        left = Math.Clamp(left + offX, area.Left, Math.Max(area.Left, area.Right - width));
        top = Math.Clamp(top + offY, area.Top, Math.Max(area.Top, area.Bottom - height));

        return new ScreenRect(left, top, left + width, top + height);
    }

    private static int AlignAxis(int start, int total, int size, IslandAnchor anchor) => anchor switch
    {
        IslandAnchor.Start => start,
        IslandAnchor.End => start + (total - size),
        _ => start + (total - size) / 2
    };

    private static IEnumerable<MonitorInfo> ResolveTargetMonitors(
        IslandConfig island,
        ActivationConfig activation,
        IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) yield break;

        string target = string.IsNullOrWhiteSpace(island.Monitor)
            ? activation.Monitores
            : island.Monitor;

        string norm = target.Trim().ToLowerInvariant();
        if (norm is "todos" or "all")
        {
            foreach (MonitorInfo m in monitors) yield return m;
            yield break;
        }

        if (norm is "principal" or "primary")
        {
            yield return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            yield break;
        }

        if (int.TryParse(norm, out int idx) && idx >= 0 && idx < monitors.Count)
        {
            yield return monitors[idx];
            yield break;
        }

        MonitorInfo? byName = monitors.FirstOrDefault(
            m => string.Equals(m.DeviceName, target, StringComparison.OrdinalIgnoreCase));
        yield return byName ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    private static IslandEdge? DetectCornerAtPoint(ScreenPoint pt, ScreenRect mb, int thicknessPx)
    {
        int t = Math.Max(thicknessPx, 12);
        bool left = pt.X >= mb.Left && pt.X < mb.Left + t;
        bool right = pt.X >= mb.Right - t && pt.X < mb.Right;
        bool top = pt.Y >= mb.Top && pt.Y < mb.Top + t;
        bool bottom = pt.Y >= mb.Bottom - t && pt.Y < mb.Bottom;

        if (left && top) return IslandEdge.TopLeft;
        if (right && top) return IslandEdge.TopRight;
        if (left && bottom) return IslandEdge.BottomLeft;
        if (right && bottom) return IslandEdge.BottomRight;
        return null;
    }

    private static bool IsCorner(IslandEdge edge) => edge is
        IslandEdge.TopLeft or IslandEdge.TopRight or IslandEdge.BottomLeft or IslandEdge.BottomRight;

    private static HashSet<IslandEdge> GetAdjacentEdges(IslandEdge corner) => corner switch
    {
        IslandEdge.TopLeft => [IslandEdge.Left, IslandEdge.Top],
        IslandEdge.TopRight => [IslandEdge.Right, IslandEdge.Top],
        IslandEdge.BottomLeft => [IslandEdge.Left, IslandEdge.Bottom],
        IslandEdge.BottomRight => [IslandEdge.Right, IslandEdge.Bottom],
        _ => []
    };

    private sealed record IslandEntry(ResolvedIslandZone Zone, IslandStateMachine Machine);
    private sealed record FastFilterSnapshot(bool Paused, bool AnyNotClosed, ScreenRect[] ActivationRects)
    {
        public static readonly FastFilterSnapshot Empty = new(false, false, []);
    }
}
