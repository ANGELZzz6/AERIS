using System.IO;
using IslasEscritorio.Core;

namespace IslasEscritorio.Inventory;

/// <summary>
/// Lee los elementos de %USERPROFILE%\Desktop y %PUBLIC%\Desktop y observa cambios
/// mediante FileSystemWatcher sin modificar ni mover ningún archivo del usuario.
/// </summary>
public sealed class DesktopInventory : IDisposable
{
    private readonly string _userDesktopPath;
    private readonly string _publicDesktopPath;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _sync = new();
    private CancellationTokenSource? _debounceCts;
    private List<DesktopItem> _items = [];
    private bool _disposed;

    public IconCache IconCache { get; } = new(256);
    public event Action<IReadOnlyList<DesktopItem>>? InventoryChanged;

    public DesktopInventory(string? userDesktopPath = null, string? publicDesktopPath = null)
    {
        _userDesktopPath = userDesktopPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _publicDesktopPath = publicDesktopPath ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    public void Start()
    {
        RefreshNow(raiseEvent: false);
        SetupWatcher(_userDesktopPath);
        if (!string.Equals(_publicDesktopPath, _userDesktopPath, StringComparison.OrdinalIgnoreCase))
            SetupWatcher(_publicDesktopPath);
    }

    public IReadOnlyList<DesktopItem> GetItemsSnapshot()
    {
        lock (_sync) return _items.ToList();
    }

    public void RefreshNow(bool raiseEvent = true)
    {
        var items = new List<DesktopItem>();
        ReadDirectory(_userDesktopPath, DesktopSource.UserDesktop, items);
        if (!string.Equals(_publicDesktopPath, _userDesktopPath, StringComparison.OrdinalIgnoreCase))
            ReadDirectory(_publicDesktopPath, DesktopSource.PublicDesktop, items);

        lock (_sync)
        {
            _items = items;
        }

        if (raiseEvent)
            InventoryChanged?.Invoke(items);
    }

    private static void ReadDirectory(string directory, DesktopSource source, List<DesktopItem> destination)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        try
        {
            var dirInfo = new DirectoryInfo(directory);
            foreach (FileSystemInfo entry in dirInfo.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                if ((entry.Attributes & FileAttributes.System) != 0 &&
                    string.Equals(entry.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    // Lo incluimos como archivo solo para que el filtro de exclusión decida,
                    // o lo mantenemos con metadatos reales para respetar las reglas del usuario.
                }

                bool isDir = entry is DirectoryInfo;
                string ext = isDir ? string.Empty : entry.Extension;
                bool isShortcut = !isDir && (
                    ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".url", StringComparison.OrdinalIgnoreCase));

                DesktopItemKind kind = isDir
                    ? DesktopItemKind.Folder
                    : isShortcut
                        ? DesktopItemKind.Shortcut
                        : DesktopItemKind.File;

                string displayName = IconCache.TryGetShellDisplayName(entry.FullName)
                    ?? (isShortcut ? Path.GetFileNameWithoutExtension(entry.Name) : entry.Name);

                long size = entry is FileInfo fi ? SafeGetLength(fi) : 0;

                destination.Add(new DesktopItem(
                    FullPath: entry.FullName,
                    FileName: entry.Name,
                    DisplayName: displayName,
                    Extension: ext,
                    Kind: kind,
                    Source: source,
                    SizeBytes: size,
                    LastWriteTimeUtc: entry.LastWriteTimeUtc,
                    LastAccessTimeUtc: entry.LastAccessTimeUtc));
            }
        }
        catch
        {
            // Ignorar errores transitorios de acceso durante operaciones de Explorer.
        }
    }

    private static long SafeGetLength(FileInfo fi)
    {
        try { return fi.Length; } catch { return 0; }
    }

    private void SetupWatcher(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        watcher.Created += OnDirectoryChanged;
        watcher.Deleted += OnDirectoryChanged;
        watcher.Renamed += OnDirectoryRenamed;
        watcher.Changed += OnDirectoryChanged;
        _watchers.Add(watcher);
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        IconCache.Invalidate(e.FullPath);
        ScheduleRefresh();
    }

    private void OnDirectoryRenamed(object sender, RenamedEventArgs e)
    {
        IconCache.Invalidate(e.OldFullPath);
        IconCache.Invalidate(e.FullPath);
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_disposed) return;

        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;

            _ = Task.Delay(180, token).ContinueWith(t =>
            {
                if (!t.IsCanceled && !_disposed)
                    RefreshNow(raiseEvent: true);
            }, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (FileSystemWatcher w in _watchers)
            w.Dispose();
        _watchers.Clear();

        lock (_sync)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }
        IconCache.Clear();
    }
}
