using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace IslasEscritorio.Inventory;

/// <summary>
/// Caché LRU en memoria para iconos extraídos con SHGetFileInfo / SHGetImageList.
/// Libera inmediatamente el HICON nativo tras crear un BitmapSource congelado.
/// </summary>
public sealed class IconCache
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiSysIconIndex = 0x000004000;
    private const uint ShgfiDisplayName = 0x000000200;
    private const int ShlLarge = 0x0;       // 32x32
    private const int ShlSmall = 0x1;       // 16x16
    private const int ShlExtraLarge = 0x2;  // 48x48
    private const int ShlJumbo = 0x4;       // 256x256
    private const uint IldTransparent = 0x00000001;

    private static readonly Guid IidIImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lru = new();

    public IconCache(int capacity = 256)
    {
        _capacity = Math.Max(32, capacity);
    }

    public int Count
    {
        get { lock (_sync) return _map.Count; }
    }

    public BitmapSource? GetIcon(string fullPath, int desiredSizePx)
    {
        string bucket = desiredSizePx <= 20 ? "16" : desiredSizePx <= 36 ? "32" : desiredSizePx <= 64 ? "48" : "256";
        string key = $"{bucket}|{fullPath}";

        lock (_sync)
        {
            if (_map.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Image;
            }
        }

        BitmapSource? extracted = ExtractIcon(fullPath, desiredSizePx);
        if (extracted is null) return null;

        lock (_sync)
        {
            if (_map.TryGetValue(key, out LinkedListNode<CacheEntry>? existing))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return existing.Value.Image;
            }

            if (_map.Count >= _capacity && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }

            var entry = new CacheEntry(key, extracted);
            LinkedListNode<CacheEntry> newNode = _lru.AddFirst(entry);
            _map[key] = newNode;
        }

        return extracted;
    }

    public void Invalidate(string fullPath)
    {
        lock (_sync)
        {
            foreach (string bucket in new[] { "16", "32", "48", "256" })
            {
                string key = $"{bucket}|{fullPath}";
                if (_map.Remove(key, out LinkedListNode<CacheEntry>? node))
                    _lru.Remove(node);
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    public static string? TryGetShellDisplayName(string fullPath)
    {
        var shfi = new ShFileInfo();
        IntPtr res = SHGetFileInfo(fullPath, 0, ref shfi, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiDisplayName);
        if (res != IntPtr.Zero && !string.IsNullOrWhiteSpace(shfi.szDisplayName))
            return shfi.szDisplayName;
        return null;
    }

    private static BitmapSource? ExtractIcon(string fullPath, int desiredSizePx)
    {
        try
        {
            int listType = desiredSizePx <= 20 ? ShlSmall
                : desiredSizePx <= 36 ? ShlLarge
                : desiredSizePx <= 64 ? ShlExtraLarge
                : ShlJumbo;

            var shfi = new ShFileInfo();
            IntPtr sysImgList = SHGetFileInfo(
                fullPath,
                0,
                ref shfi,
                (uint)Marshal.SizeOf<ShFileInfo>(),
                ShgfiSysIconIndex | (desiredSizePx <= 20 ? ShgfiSmallIcon : ShgfiLargeIcon));

            if (sysImgList != IntPtr.Zero)
            {
                Guid iid = IidIImageList;
                if (SHGetImageList(listType, ref iid, out IImageList? imageList) == 0 && imageList is not null)
                {
                    try
                    {
                        if (imageList.GetIcon(shfi.iIcon, IldTransparent, out IntPtr hIcon) == 0 && hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                return ConvertAndFreeze(hIcon);
                            }
                            finally
                            {
                                DestroyIcon(hIcon);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(imageList);
                    }
                }
            }

            // Respaldo directo con SHGFI_ICON.
            var fallback = new ShFileInfo();
            uint flags = ShgfiIcon | (desiredSizePx <= 20 ? ShgfiSmallIcon : ShgfiLargeIcon);
            if (SHGetFileInfo(fullPath, 0, ref fallback, (uint)Marshal.SizeOf<ShFileInfo>(), flags) != IntPtr.Zero &&
                fallback.hIcon != IntPtr.Zero)
            {
                try
                {
                    return ConvertAndFreeze(fallback.hIcon);
                }
                finally
                {
                    DestroyIcon(fallback.hIcon);
                }
            }
        }
        catch
        {
            // Ignorar archivos bloqueados o eliminados justo durante la lectura.
        }

        return null;
    }

    private static BitmapSource ConvertAndFreeze(IntPtr hIcon)
    {
        BitmapSource src = Imaging.CreateBitmapSourceFromHIcon(
            hIcon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }

    private sealed record CacheEntry(string Key, BitmapSource Image);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        int SetOverlayImage(int iImage, int iOverlay);
        int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        int Draw(IntPtr pimldp);
        int Remove(int i);
        int GetIcon(int i, uint flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
