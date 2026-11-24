namespace IslasEscritorio.Core;

/// <summary>
/// Borde o esquina de la pantalla donde se ubica una isla.
/// </summary>
public enum IslandEdge
{
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Anclaje de la isla a lo largo del borde asignado.
/// </summary>
public enum IslandAnchor
{
    Start,
    Center,
    End
}

/// <summary>
/// Estados de la máquina de estados por isla.
/// </summary>
public enum IslandState
{
    Closed,
    Waiting,
    Open,
    Closing
}

/// <summary>
/// Comportamiento cuando el cursor entra en la intersección de dos bordes (esquina).
/// </summary>
public enum CornerBehavior
{
    OwnIsland,
    None,
    EdgePriority
}

public enum AnimationType
{
    Slide,
    Fade,
    Scale,
    None
}

public enum AnimationEasing
{
    EaseOutCubic,
    EaseOutQuad,
    Linear
}

public enum IslandLayoutMode
{
    List,
    Grid,
    LargeIcons,
    NamesOnly
}

public enum IslandSortOrder
{
    Name,
    LastUsed,
    ModifiedDate,
    Type,
    Size
}

public enum IslandGroupingMode
{
    None,
    Type
}

public enum DesktopItemKind
{
    File,
    Folder,
    Shortcut
}

public enum DesktopSource
{
    UserDesktop,
    PublicDesktop
}

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool IntersectsWith(ScreenRect other) =>
        !IsEmpty && !other.IsEmpty &&
        Left < other.Right && Right > other.Left &&
        Top < other.Bottom && Bottom > other.Top;
}

public sealed record MonitorInfo(
    int Index,
    string DeviceName,
    bool IsPrimary,
    ScreenRect Bounds,
    ScreenRect WorkArea,
    double DpiScale = 1.0);

public sealed record DesktopItem(
    string FullPath,
    string FileName,
    string DisplayName,
    string Extension,
    DesktopItemKind Kind,
    DesktopSource Source,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    DateTime LastAccessTimeUtc,
    string? TargetPath = null);
