using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslasEscritorio.Core;
using IslasEscritorio.Input;
using IslasEscritorio.Inventory;
using Button = System.Windows.Controls.Button;
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

namespace IslasEscritorio.Islands;

public partial class IslandWindow : Window
{
    private const double ShadowMarginDip = 10.0;

    private ResolvedIslandZone _zone;
    private AppearanceConfig _appearance;
    private bool _topmost;
    private Action<string, bool>? _onPinChanged;

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

    public string RuntimeId => _zone.RuntimeId;
    public ScreenRect CurrentPhysicalBounds { get; private set; }

    public IslandWindow(
        ResolvedIslandZone zone,
        AppearanceConfig appearance,
        bool topmost,
        Action<string, bool>? onPinChanged = null)
    {
        InitializeComponent();
        _zone = zone;
        _appearance = appearance;
        _topmost = topmost;
        _onPinChanged = onPinChanged;
        CurrentPhysicalBounds = zone.TargetWindowRect;

        SourceInitialized += OnSourceInitialized;
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
        TitleText.Text = string.IsNullOrWhiteSpace(_zone.Config.Nombre) ? _zone.Config.Id : _zone.Config.Nombre;
        CountText.Text = items.Count.ToString();
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
        double opacity = Math.Clamp(_zone.Config.Estilo.Opacidad ?? _appearance.Opacidad, 0.1, 1.0);
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
        StopAnimationLoop();
        base.OnClosed(e);
    }
}
