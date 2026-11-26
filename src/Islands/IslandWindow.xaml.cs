using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IslasEscritorio.Core;
using IslasEscritorio.Input;
using IslasEscritorio.Inventory;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using WrapPanel = System.Windows.Controls.WrapPanel;
using Windows.Media.Control;

namespace IslasEscritorio.Islands;

public partial class IslandWindow : Window
{
    private const double ShadowMarginDip = 10.0;

    private ResolvedIslandZone _zone;
    private AppearanceConfig _appearance;
    private bool _topmost;
    private Action<string, bool>? _onPinChanged;
    private Action<string, int, int>? _onFloatingPositionChanged;

    private bool _isAnimating;
    private bool _animatingOpen;
    private long _animStartTicks;
    private double _animDurationMs;
    private AnimationType _animType;
    private AnimationEasing _animEasing;
    private double _slideOffsetX;
    private double _slideOffsetY;
    private double _targetOpacity = 0.88;

    private Point? _dragStartPoint;
    private DesktopItem? _dragCandidateItem;
    private readonly DispatcherTimer _mediaRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _specialTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private GlobalSystemMediaTransportControlsSession? _currentMediaSession;
    private bool _suppressNotesSave;
    private readonly Action<string, string>? _onNoteChanged;

    public string RuntimeId => _zone.RuntimeId;
    public ScreenRect CurrentPhysicalBounds { get; private set; }

    public IslandWindow(
        ResolvedIslandZone zone,
        AppearanceConfig appearance,
        bool topmost,
        Action<string, bool>? onPinChanged = null,
        Action<string, int, int>? onFloatingPositionChanged = null,
        Action<string, string>? onNoteChanged = null)
    {
        InitializeComponent();
        _zone = zone;
        _appearance = appearance;
        _topmost = topmost;
        _onPinChanged = onPinChanged;
        _onFloatingPositionChanged = onFloatingPositionChanged;
        _onNoteChanged = onNoteChanged;
        CurrentPhysicalBounds = zone.TargetWindowRect;

        SourceInitialized += OnSourceInitialized;
        _mediaRefreshTimer.Tick += async (_, _) => await RefreshMediaAsync();
        _specialTimer.Tick += (_, _) => RefreshSpecialIsland();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.ApplyNoActivateToolWindow(hwnd);
        ApplyThemeAndBackdrop(hwnd);
        PositionWindow();
    }

    public void UpdateConfiguration(ResolvedIslandZone zone, AppearanceConfig appearance, bool topmost)
    {
        _zone = zone;
        _appearance = appearance;
        _topmost = topmost;
        CurrentPhysicalBounds = zone.TargetWindowRect;

        if (IsLoaded)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            ApplyThemeAndBackdrop(hwnd);
            PositionWindow();
        }
    }

    public void RenderItems(IReadOnlyList<DesktopItem> items, IconCache iconCache)
    {
        string type = _zone.Config.Tipo.ToLowerInvariant();
        bool isFiles = string.Equals(type, "archivos", StringComparison.OrdinalIgnoreCase);

        ItemsScrollViewer.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        MusicHost.Visibility = string.Equals(type, "musica", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        ClockHost.Visibility = string.Equals(type, "reloj", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        SystemHost.Visibility = string.Equals(type, "sistema", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        NotesHost.Visibility = string.Equals(type, "notas", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

        TitleText.Text = string.IsNullOrWhiteSpace(_zone.Config.Nombre) ? _zone.Config.Id : _zone.Config.Nombre;
        CountText.Text = items.Count.ToString();
        CountBadge.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;

        if (string.Equals(type, "musica", StringComparison.OrdinalIgnoreCase))
        {
            _specialTimer.Stop();
            _mediaRefreshTimer.Start();
            _ = RefreshMediaAsync();
            return;
        }

        if (string.Equals(type, "reloj", StringComparison.OrdinalIgnoreCase))
        {
            _mediaRefreshTimer.Stop();
            _specialTimer.Start();
            RefreshClock();
            return;
        }

        if (string.Equals(type, "sistema", StringComparison.OrdinalIgnoreCase))
        {
            _mediaRefreshTimer.Stop();
            _specialTimer.Start();
            RefreshSystem();
            return;
        }

        if (string.Equals(type, "notas", StringComparison.OrdinalIgnoreCase))
        {
            _mediaRefreshTimer.Stop();
            _specialTimer.Stop();
            _suppressNotesSave = true;
            NotesTextBox.Text = _zone.Config.Presentacion.TextoNota ?? string.Empty;
            _suppressNotesSave = false;
            return;
        }

        _mediaRefreshTimer.Stop();
        _specialTimer.Stop();

        ContentHost.Children.Clear();

        if (items.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Sin elementos que coincidan con los filtros.",
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 12, 4, 12),
                Foreground = TitleText.Foreground
            };
            ContentHost.Children.Add(empty);
            return;
        }

        ConfigValidator.TryParseLayout(_zone.Config.Contenido.Disposicion, out IslandLayoutMode layout);
        ConfigValidator.TryParseGrouping(_zone.Config.Contenido.Agrupacion, out IslandGroupingMode grouping);

        int iconSize = layout switch
        {
            IslandLayoutMode.LargeIcons => Math.Clamp(_appearance.TamanoIconoPx + 16, 48, 96),
            IslandLayoutMode.NamesOnly => 0,
            IslandLayoutMode.List => Math.Clamp(_appearance.TamanoIconoPx / 2 + 8, 24, 40),
            _ => Math.Clamp(_appearance.TamanoIconoPx, 32, 72)
        };

        if (grouping == IslandGroupingMode.Type)
        {
            foreach (IGrouping<DesktopItemKind, DesktopItem> group in items.GroupBy(i => i.Kind))
            {
                string headerText = group.Key switch
                {
                    DesktopItemKind.Folder => "Carpetas",
                    DesktopItemKind.Shortcut => "Accesos directos",
                    _ => "Archivos"
                };

                var header = new TextBlock
                {
                    Text = headerText,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.72,
                    Margin = new Thickness(4, 6, 4, 4),
                    Foreground = TitleText.Foreground
                };
                ContentHost.Children.Add(header);
                AddItemsContainer(group, layout, iconSize, iconCache);
            }
        }
        else
        {
            AddItemsContainer(items, layout, iconSize, iconCache);
        }
    }

    private void RefreshSpecialIsland()
    {
        string type = _zone.Config.Tipo.ToLowerInvariant();
        if (type == "reloj") RefreshClock();
        else if (type == "sistema") RefreshSystem();
    }

    private async Task RefreshMediaAsync()
    {
        try
        {
            _mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _currentMediaSession = _mediaManager.GetCurrentSession();
            if (_currentMediaSession is null)
            {
                MusicTrackText.Text = "No hay música activa";
                MusicArtistText.Text = "Inicia reproducción en Spotify o navegador";
                MusicAppBadge.Visibility = Visibility.Collapsed;
                EqualizerHost.Visibility = Visibility.Collapsed;
                MusicCoverImage.Source = null;
                MusicCoverImage.Visibility = Visibility.Collapsed;
                MusicCoverPlaceholder.Visibility = Visibility.Visible;
                ApplyButtonStyles(_zone.Config.Presentacion.EstiloBotones);
                return;
            }

            var properties = await _currentMediaSession.TryGetMediaPropertiesAsync();
            MusicTrackText.Text = string.IsNullOrWhiteSpace(properties.Title) ? "Reproducción activa" : properties.Title;
            MusicArtistText.Text = string.IsNullOrWhiteSpace(properties.Artist) ? GetFriendlyAppName(_currentMediaSession.SourceAppUserModelId) : properties.Artist;

            string appName = GetFriendlyAppName(_currentMediaSession.SourceAppUserModelId);
            MusicAppText.Text = appName;
            MusicAppBadge.Visibility = _zone.Config.Presentacion.MostrarAppOrigen ? Visibility.Visible : Visibility.Collapsed;

            var state = _currentMediaSession.GetPlaybackInfo().PlaybackStatus;
            bool isPlaying = state == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            MusicPlayPauseButton.Content = isPlaying ? "⏸" : "▶";

            if (isPlaying && _zone.Config.Presentacion.MostrarEcualizador)
            {
                EqualizerHost.Visibility = Visibility.Visible;
                Random rng = Random.Shared;
                EqBar1.Height = rng.Next(4, 15);
                EqBar2.Height = rng.Next(6, 18);
                EqBar3.Height = rng.Next(3, 14);
                EqBar4.Height = rng.Next(5, 16);
            }
            else
            {
                EqualizerHost.Visibility = Visibility.Collapsed;
            }

            bool showCover = _zone.Config.Presentacion.MostrarPortada;
            MusicArtColumn.Width = showCover ? GridLength.Auto : new GridLength(0);
            MusicArtContainer.Visibility = showCover ? Visibility.Visible : Visibility.Collapsed;

            if (showCover)
            {
                ApplyCoverStyle(_zone.Config.Presentacion.EstiloPortada);
                if (properties.Thumbnail is { } streamRef)
                {
                    try
                    {
                        using var ras = await streamRef.OpenReadAsync();
                        using var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(ras);
                        using var ms = new System.IO.MemoryStream();
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;

                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        MusicCoverImage.Source = bitmap;
                        MusicCoverImage.Visibility = Visibility.Visible;
                        MusicCoverPlaceholder.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        MusicCoverImage.Source = null;
                        MusicCoverImage.Visibility = Visibility.Collapsed;
                        MusicCoverPlaceholder.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    MusicCoverImage.Source = null;
                    MusicCoverImage.Visibility = Visibility.Collapsed;
                    MusicCoverPlaceholder.Visibility = Visibility.Visible;
                }
            }

            ApplyButtonStyles(_zone.Config.Presentacion.EstiloBotones);
        }
        catch
        {
            MusicTrackText.Text = "Sesión multimedia no disponible";
            MusicArtistText.Text = "Comprueba que la app publica controles de Windows";
        }
    }

    private static string GetFriendlyAppName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "Multimedia";
        if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (appId.Contains("Edge", StringComparison.OrdinalIgnoreCase) || appId.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (appId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (appId.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) || appId.Contains("iTunes", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        if (appId.Contains("VLC", StringComparison.OrdinalIgnoreCase)) return "VLC Player";
        if (appId.Contains("Zune", StringComparison.OrdinalIgnoreCase) || appId.Contains("Media Player", StringComparison.OrdinalIgnoreCase)) return "Media Player";

        string name = System.IO.Path.GetFileNameWithoutExtension(appId);
        return string.IsNullOrWhiteSpace(name) ? "Multimedia" : name;
    }

    private void ApplyCoverStyle(string style)
    {
        switch (style?.ToLowerInvariant())
        {
            case "cuadrada":
            case "square":
                MusicArtContainer.CornerRadius = new CornerRadius(2);
                MusicArtOverlayBorder.CornerRadius = new CornerRadius(2);
                VinylGrooveBorder.Visibility = Visibility.Collapsed;
                break;
            case "circular":
            case "circle":
                MusicArtContainer.CornerRadius = new CornerRadius(50);
                MusicArtOverlayBorder.CornerRadius = new CornerRadius(50);
                VinylGrooveBorder.Visibility = Visibility.Collapsed;
                break;
            case "vinilo":
            case "vinyl":
                MusicArtContainer.CornerRadius = new CornerRadius(50);
                MusicArtOverlayBorder.CornerRadius = new CornerRadius(50);
                VinylGrooveBorder.Visibility = Visibility.Visible;
                break;
            default:
                MusicArtContainer.CornerRadius = new CornerRadius(12);
                MusicArtOverlayBorder.CornerRadius = new CornerRadius(12);
                VinylGrooveBorder.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void ApplyButtonStyles(string styleName)
    {
        bool isDark = !string.Equals(_appearance.Tema, "claro", StringComparison.OrdinalIgnoreCase);
        Color accent = ParseColor(_zone.Config.Estilo.ColorAcento, Color.FromRgb(137, 180, 250));

        Brush bgBrush;
        Brush borderBrush;
        Thickness borderThick;
        CornerRadius radius;

        switch (styleName?.ToLowerInvariant())
        {
            case "pastilla":
            case "pill":
                bgBrush = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B));
                borderBrush = new SolidColorBrush(accent);
                borderThick = new Thickness(1);
                radius = new CornerRadius(17);
                break;
            case "minimalista":
            case "minimal":
                bgBrush = System.Windows.Media.Brushes.Transparent;
                borderBrush = System.Windows.Media.Brushes.Transparent;
                borderThick = new Thickness(0);
                radius = new CornerRadius(6);
                break;
            default: // "moderno"
                bgBrush = new SolidColorBrush(isDark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0));
                borderBrush = new SolidColorBrush(Color.FromArgb(60, accent.R, accent.G, accent.B));
                borderThick = new Thickness(1);
                radius = new CornerRadius(10);
                break;
        }

        StyleBtn(MusicPrevButton, bgBrush, borderBrush, borderThick, radius);
        StyleBtn(MusicPlayPauseButton, bgBrush, borderBrush, borderThick, radius);
        StyleBtn(MusicNextButton, bgBrush, borderBrush, borderThick, radius);

        Color fg = ParseColor(_appearance.ColorTexto, isDark ? Color.FromRgb(235, 238, 248) : Color.FromRgb(28, 30, 38));
        MusicPrevButton.Foreground = new SolidColorBrush(fg);
        MusicPlayPauseButton.Foreground = new SolidColorBrush(fg);
        MusicNextButton.Foreground = new SolidColorBrush(fg);
    }

    private static void StyleBtn(Button btn, Brush bg, Brush border, Thickness thick, CornerRadius radius)
    {
        btn.Background = bg;
        btn.BorderBrush = border;
        btn.BorderThickness = thick;
        btn.Cursor = System.Windows.Input.Cursors.Hand;

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, radius);
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenter);

        template.VisualTree = borderFactory;
        btn.Template = template;
    }

    private void RefreshClock()
    {
        DateTime now = DateTime.Now;
        var pres = _zone.Config.Presentacion;
        string timeFormat = pres.FormatoHora24
            ? (pres.MostrarSegundos ? "HH:mm:ss" : "HH:mm")
            : (pres.MostrarSegundos ? "hh:mm:ss tt" : "hh:mm tt");

        ClockTimeText.Text = now.ToString(timeFormat);
        ClockDateText.Visibility = pres.MostrarFecha ? Visibility.Visible : Visibility.Collapsed;
        ClockDateText.Text = now.ToString("dddd, d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-ES"));
    }

    private void RefreshSystem()
    {
        try
        {
            var pres = _zone.Config.Presentacion;

            RamRowGrid.Visibility = pres.MostrarRam ? Visibility.Visible : Visibility.Collapsed;
            if (pres.MostrarRam)
            {
                var mem = GetMemoryStatus();
                RamProgressBar.Value = mem.load;
                RamValueText.Text = $"{mem.load}%";
            }

            DiskRowGrid.Visibility = pres.MostrarDisco ? Visibility.Visible : Visibility.Collapsed;
            if (pres.MostrarDisco)
            {
                System.IO.DriveInfo cDrive = new System.IO.DriveInfo("C");
                if (cDrive.IsReady)
                {
                    double usedPct = 100.0 * (cDrive.TotalSize - cDrive.AvailableFreeSpace) / cDrive.TotalSize;
                    DiskProgressBar.Value = Math.Round(usedPct);
                    DiskValueText.Text = $"{DiskProgressBar.Value:0}%";
                }
            }

            CpuRowGrid.Visibility = pres.MostrarCpu ? Visibility.Visible : Visibility.Collapsed;
            if (pres.MostrarCpu)
            {
                double cpuPct = Math.Clamp(Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0 * 40.0), 5, 95);
                CpuProgressBar.Value = Math.Round(cpuPct);
                CpuValueText.Text = $"{CpuProgressBar.Value:0}%";
            }
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (uint load, ulong totalMb, ulong freeMb) GetMemoryStatus()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            return (mem.dwMemoryLoad, mem.ullTotalPhys / (1024 * 1024), mem.ullAvailPhys / (1024 * 1024));
        }
        return (0, 0, 0);
    }

    private void OnNotesTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressNotesSave || _zone?.Config is null) return;
        _zone.Config.Presentacion.TextoNota = NotesTextBox.Text;
        _onNoteChanged?.Invoke(_zone.Config.Id, NotesTextBox.Text);
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_currentMediaSession is not null) await _currentMediaSession.TryTogglePlayPauseAsync();
        await RefreshMediaAsync();
    }
    private async void OnPreviousTrackClick(object sender, RoutedEventArgs e)
    {
        if (_currentMediaSession is not null) await _currentMediaSession.TrySkipPreviousAsync();
        await RefreshMediaAsync();
    }
    private async void OnNextTrackClick(object sender, RoutedEventArgs e)
    {
        if (_currentMediaSession is not null) await _currentMediaSession.TrySkipNextAsync();
        await RefreshMediaAsync();
    }

    private void AddItemsContainer(
        IEnumerable<DesktopItem> items,
        IslandLayoutMode layout,
        int iconSize,
        IconCache iconCache)
    {
        Panel panel = layout is IslandLayoutMode.Grid or IslandLayoutMode.LargeIcons
            ? new WrapPanel { Orientation = Orientation.Horizontal }
            : new StackPanel { Orientation = Orientation.Vertical };

        foreach (DesktopItem item in items)
        {
            panel.Children.Add(CreateItemButton(item, layout, iconSize, iconCache));
        }

        ContentHost.Children.Add(panel);
    }

    private Button CreateItemButton(
        DesktopItem item,
        IslandLayoutMode layout,
        int iconSize,
        IconCache iconCache)
    {
        bool isGrid = layout is IslandLayoutMode.Grid or IslandLayoutMode.LargeIcons;
        bool isDark = !string.Equals(_appearance.Tema, "claro", StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            Tag = item,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = isGrid ? new Thickness(6) : new Thickness(6, 5, 6, 5),
            Margin = isGrid ? new Thickness(3) : new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = isGrid ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            ToolTip = item.FullPath
        };

        if (isGrid)
            button.Width = layout == IslandLayoutMode.LargeIcons ? iconSize + 36 : iconSize + 32;

        // Estilo hover limpio sin cambiar el foco.
        var style = new Style(typeof(Button));
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(
            BackgroundProperty,
            new SolidColorBrush(isDark ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0))));
        style.Triggers.Add(hoverTrigger);

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenter);
        template.VisualTree = borderFactory;
        style.Setters.Add(new Setter(TemplateProperty, template));
        button.Style = style;

        var contentPanel = new StackPanel
        {
            Orientation = isGrid ? Orientation.Vertical : Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (iconSize > 0)
        {
            BitmapSource? icon = iconCache.GetIcon(item.FullPath, iconSize);
            if (icon is not null)
            {
                var img = new Image
                {
                    Source = icon,
                    Width = iconSize,
                    Height = iconSize,
                    Stretch = Stretch.Uniform,
                    Margin = isGrid ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 10, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                contentPanel.Children.Add(img);
            }
        }

        var label = new TextBlock
        {
            Text = item.DisplayName,
            Foreground = new SolidColorBrush(ParseColor(_appearance.ColorTexto, Color.FromRgb(235, 238, 248))),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = isGrid ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            TextAlignment = isGrid ? TextAlignment.Center : TextAlignment.Left,
            MaxHeight = isGrid ? 34 : 20,
            TextWrapping = isGrid ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
        contentPanel.Children.Add(label);
        button.Content = contentPanel;

        button.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            ShellContextMenu.OpenItem(item.FullPath);
        };

        button.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _onPinChanged?.Invoke(RuntimeId, true);
            try
            {
                Point screenPt = button.PointToScreen(e.GetPosition(button));
                ShellContextMenu.ShowContextMenu(this, item.FullPath, (int)screenPt.X, (int)screenPt.Y);
            }
            finally
            {
                _onPinChanged?.Invoke(RuntimeId, false);
            }
        };

        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1)
            {
                _dragStartPoint = e.GetPosition(this);
                _dragCandidateItem = item;
            }
        };

        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null || _dragCandidateItem is null)
                return;

            Point current = e.GetPosition(this);
            Vector diff = current - _dragStartPoint.Value;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DesktopItem dragged = _dragCandidateItem;
                _dragStartPoint = null;
                _dragCandidateItem = null;

                _onPinChanged?.Invoke(RuntimeId, true);
                try
                {
                    var data = new DataObject(System.Windows.DataFormats.FileDrop, new[] { dragged.FullPath });
                    DragDrop.DoDragDrop(button, data, DragDropEffects.Copy | DragDropEffects.Move);
                }
                finally
                {
                    _onPinChanged?.Invoke(RuntimeId, false);
                }
            }
        };

        return button;
    }

    public void AnimateOpen()
    {
        Topmost = _topmost;
        PositionWindow();
        if (!IsVisible)
            Show();

        PositionWindow();
        StartAnimation(opening: true);
    }

    public void ShowPersistent()
    {
        Topmost = _topmost;
        PositionWindow();
        if (!IsVisible) Show();
        PositionWindow();
        ApplyAnimationFrame(1);
    }

    private void OnTitleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!string.Equals(_zone.Config.Tipo, "archivos", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_zone.Config.Presentacion.Modo, "flotante", StringComparison.OrdinalIgnoreCase) &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
            Point screen = PointToScreen(new Point(0, 0));
            int snapX = (int)(Math.Round(screen.X / 16.0) * 16.0);
            int snapY = (int)(Math.Round(screen.Y / 16.0) * 16.0);

            CurrentPhysicalBounds = new ScreenRect(snapX, snapY,
                snapX + (int)Math.Round(Width), snapY + (int)Math.Round(Height));
            _onFloatingPositionChanged?.Invoke(_zone.Config.Id, snapX, snapY);
            PositionWindow();
            e.Handled = true;
        }
    }

    public void AnimateClose()
    {
        if (!IsVisible)
            return;

        StartAnimation(opening: false);
    }

    private void StartAnimation(bool opening)
    {
        StopAnimationLoop();

        ConfigValidator.TryParseAnimationType(_appearance.Animacion.Tipo, out _animType);
        ConfigValidator.TryParseAnimationEasing(_appearance.Animacion.Easing, out _animEasing);
        _animDurationMs = _appearance.Animacion.DuracionMs;
        _animatingOpen = opening;

        if (_animType == AnimationType.None || _animDurationMs <= 0)
        {
            ApplyAnimationFrame(opening ? 1.0 : 0.0);
            if (!opening) Hide();
            return;
        }

        (_slideOffsetX, _slideOffsetY) = _zone.Edge switch
        {
            IslandEdge.Left or IslandEdge.TopLeft or IslandEdge.BottomLeft => (-28.0, 0.0),
            IslandEdge.Right or IslandEdge.TopRight or IslandEdge.BottomRight => (28.0, 0.0),
            IslandEdge.Top => (0.0, -24.0),
            IslandEdge.Bottom => (0.0, 24.0),
            _ => (0.0, 0.0)
        };

        ApplyAnimationFrame(opening ? 0.0 : 1.0);
        _animStartTicks = Stopwatch.GetTimestamp();
        _isAnimating = true;
        CompositionTarget.Rendering += OnAnimationFrame;
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        if (!_isAnimating) return;

        double elapsedMs = Stopwatch.GetElapsedTime(_animStartTicks).TotalMilliseconds;
        double rawT = Math.Clamp(elapsedMs / _animDurationMs, 0.0, 1.0);
        double easedT = ApplyEasing(rawT, _animEasing);
        double progress = _animatingOpen ? easedT : 1.0 - easedT;

        ApplyAnimationFrame(progress);

        if (rawT >= 1.0)
        {
            StopAnimationLoop();
            if (!_animatingOpen)
                Hide();
        }
    }

    private void ApplyAnimationFrame(double progress)
    {
        RootGrid.Opacity = progress;
        switch (_animType)
        {
            case AnimationType.Slide:
                AnimTranslate.X = _slideOffsetX * (1.0 - progress);
                AnimTranslate.Y = _slideOffsetY * (1.0 - progress);
                AnimScale.ScaleX = 1.0;
                AnimScale.ScaleY = 1.0;
                break;
            case AnimationType.Scale:
                double scale = 0.90 + 0.10 * progress;
                AnimScale.ScaleX = scale;
                AnimScale.ScaleY = scale;
                AnimTranslate.X = 0;
                AnimTranslate.Y = 0;
                break;
            default:
                AnimTranslate.X = 0;
                AnimTranslate.Y = 0;
                AnimScale.ScaleX = 1.0;
                AnimScale.ScaleY = 1.0;
                break;
        }
    }

    private void StopAnimationLoop()
    {
        if (_isAnimating)
        {
            _isAnimating = false;
            CompositionTarget.Rendering -= OnAnimationFrame;
        }
    }

    private static double ApplyEasing(double t, AnimationEasing easing) => easing switch
    {
        AnimationEasing.EaseOutCubic => 1.0 - Math.Pow(1.0 - t, 3),
        AnimationEasing.EaseOutQuad => 1.0 - Math.Pow(1.0 - t, 2),
        _ => t
    };

    private void ApplyThemeAndBackdrop(IntPtr hwnd)
    {
        bool isDark = !string.Equals(_appearance.Tema, "claro", StringComparison.OrdinalIgnoreCase);

        string bgHex = !string.IsNullOrWhiteSpace(_zone.Config.Estilo.ColorFondo)
            ? _zone.Config.Estilo.ColorFondo!
            : _appearance.ColorFondo;

        Color baseBg = ParseColor(bgHex, isDark ? Color.FromRgb(30, 30, 46) : Color.FromRgb(245, 245, 250));
        double opacity = Math.Clamp(_zone.Config.Presentacion.Opacidad ?? _zone.Config.Estilo.Opacidad ?? _appearance.Opacidad, 0.1, 1.0);
        if (!string.IsNullOrWhiteSpace(_zone.Config.Presentacion.ColorFondo)) bgHex = _zone.Config.Presentacion.ColorFondo!;
        baseBg = ParseColor(bgHex, baseBg);
        _targetOpacity = opacity;

        byte alpha = (byte)Math.Round(opacity * 255);
        SurfaceBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, baseBg.R, baseBg.G, baseBg.B));
        SurfaceBorder.BorderBrush = new SolidColorBrush(
            isDark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0));
        SurfaceBorder.CornerRadius = new CornerRadius(Math.Clamp(_appearance.RadioEsquinasPx, 0, 48));
        switch (_appearance.BarraDesplazamiento?.ToLowerInvariant())
        {
            case "oculta":
                // Hidden conserva el desplazamiento con rueda aunque no dibuja la barra.
                ItemsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                ItemsScrollViewer.Resources.Remove(typeof(ScrollBar));
                break;
            case "linea":
                ItemsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ItemsScrollViewer.Resources.Remove(typeof(ScrollBar));
                break;
            default:
                ItemsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ItemsScrollViewer.Resources.Remove(typeof(ScrollBar));
                break;
        }

        Color fg = ParseColor(_appearance.ColorTexto, isDark ? Color.FromRgb(235, 238, 248) : Color.FromRgb(28, 30, 38));
        TitleText.Foreground = new SolidColorBrush(fg);
        TitleText.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(_appearance.Fuente) ? "Segoe UI" : _appearance.Fuente);

        Color accent = ParseColor(_zone.Config.Estilo.ColorAcento, Color.FromRgb(137, 180, 250));
        AccentPill.Background = new SolidColorBrush(accent);

        CountBadge.Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B));
        CountText.Foreground = new SolidColorBrush(fg);

        ShadowEffect.Opacity = _appearance.Sombra ? 0.45 : 0.0;

        uint abgr = ((uint)alpha << 24) | ((uint)baseBg.B << 16) | ((uint)baseBg.G << 8) | baseBg.R;
        // El modo vidrio aumenta la translucidez y usa el desenfoque existente.
        if (_appearance.EstiloVidrio)
        {
            alpha = (byte)Math.Round(Math.Min(opacity, 0.72) * 255);
            SurfaceBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, baseBg.R, baseBg.G, baseBg.B));
        }
        WindowEffects.SetBlurBehind(hwnd, _appearance.Desenfoque || _appearance.EstiloVidrio, abgr);
        WindowEffects.UpdateTopmost(hwnd, _topmost);
    }

    private void PositionWindow()
    {
        ScreenRect r = _zone.TargetWindowRect;
        CurrentPhysicalBounds = r;

        double scale = _zone.Monitor.DpiScale <= 0 ? 1.0 : _zone.Monitor.DpiScale;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            scale = target.TransformToDevice.M11;
        }

        int padPx = (int)Math.Round(ShadowMarginDip * scale);
        Left = (r.Left - padPx) / scale;
        Top = (r.Top - padPx) / scale;
        Width = (r.Width + padPx * 2) / scale;
        Height = (r.Height + padPx * 2) / scale;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && IsVisible)
        {
            WindowEffects.PositionWithoutActivating(
                hwnd,
                r.Left - padPx,
                r.Top - padPx,
                r.Width + padPx * 2,
                r.Height + padPx * 2,
                _topmost);
        }
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c)
                return c;
        }
        catch { }
        return fallback;
    }

    protected override void OnClosed(EventArgs e)
    {
        _mediaRefreshTimer.Stop();
        _specialTimer.Stop();
        StopAnimationLoop();
        base.OnClosed(e);
    }
}
