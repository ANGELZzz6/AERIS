using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using IslasEscritorio.Core;
using IslasEscritorio.Desktop;
using IslasEscritorio.Islands;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace IslasEscritorio.Settings;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;
    private readonly IslandCoordinator _coordinator;
    private readonly DesktopIconController _desktopController;

    private AppConfig _workingConfig = null!;
    private bool _loadingUi = true;

    public SettingsWindow(
        ConfigService configService,
        IslandCoordinator coordinator,
        DesktopIconController desktopController)
    {
        _configService = configService;
        _coordinator = coordinator;
        _desktopController = desktopController;
        _workingConfig = _configService.Current.Clone();

        InitializeComponent();
        PopulateComboBoxes();
        LoadConfigToUi(_workingConfig);
        AssignmentIslandCombo.ItemsSource = _workingConfig.Islands;
        AssignmentIslandCombo.DisplayMemberPath = nameof(IslandConfig.Nombre);
        if (_workingConfig.Islands.Count > 0) AssignmentIslandCombo.SelectedIndex = 0;
        AvailableItemsList.ItemsSource = _coordinator.GetAllDesktopItems();

        if (!string.IsNullOrWhiteSpace(_configService.LastError))
            ShowErrorBanner(_configService.LastError);
    }

    private void OnAssignmentIslandChanged(object sender, SelectionChangedEventArgs e) => RefreshAssignmentLists();

    private void RefreshAssignmentLists()
    {
        if (AvailableItemsList is null || AssignedItemsList is null) return;
        IslandConfig? island = AssignmentIslandCombo.SelectedItem as IslandConfig;
        if (island is null) return;
        IReadOnlyList<DesktopItem> all = _coordinator.GetAllDesktopItems();
        List<DesktopItem> currentlyShown = island.Contenido.ElementosAsignados.Count > 0
            ? all.Where(i => island.Contenido.ElementosAsignados.Contains(i.FullPath, StringComparer.OrdinalIgnoreCase)).ToList()
            : _coordinator.GetFilteredItemsForIsland(island).ToList();
        HashSet<string> shownPaths = currentlyShown.Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssignedItemsList.ItemsSource = currentlyShown;
        AvailableItemsList.ItemsSource = all.Where(i => !shownPaths.Contains(i.FullPath)).ToList();
        AssignedListTitle.Text = $"Asignados a {island.Nombre}";
    }

    private void OnAssignmentListMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not System.Windows.Controls.ListBox list || list.SelectedItem is not DesktopItem item) return;
        System.Windows.DragDrop.DoDragDrop(list, item.FullPath, System.Windows.DragDropEffects.Move);
    }

    private void OnAssignedItemsDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (AssignmentIslandCombo.SelectedItem is not IslandConfig target || !e.Data.GetDataPresent(typeof(string))) return;
        string path = (string)e.Data.GetData(typeof(string))!;
        foreach (IslandConfig island in _workingConfig.Islands)
        {
            EnsureExplicitAssignment(island);
            island.Contenido.ElementosAsignados.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }
        if (!target.Contenido.ElementosAsignados.Contains(path, StringComparer.OrdinalIgnoreCase)) target.Contenido.ElementosAsignados.Add(path);
        RefreshAssignmentLists();
    }

    private void OnAvailableItemsDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (AssignmentIslandCombo.SelectedItem is not IslandConfig island || !e.Data.GetDataPresent(typeof(string))) return;
        string path = (string)e.Data.GetData(typeof(string))!;
        EnsureExplicitAssignment(island);
        island.Contenido.ElementosAsignados.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RefreshAssignmentLists();
    }

    private void EnsureExplicitAssignment(IslandConfig island)
    {
        if (island.Contenido.ElementosAsignados.Count != 0) return;
        island.Contenido.ElementosAsignados = _coordinator.GetFilteredItemsForIsland(island).Select(i => i.FullPath).ToList();
    }

    private void PopulateComboBoxes()
    {
        IslandEdgeCombo.ItemsSource = new[]
        {
            "izquierdo", "derecho", "superior", "inferior",
            "esquinaSuperiorIzquierda", "esquinaSuperiorDerecha",
            "esquinaInferiorIzquierda", "esquinaInferiorDerecha"
        };
        IslandAnchorCombo.ItemsSource = new[] { "inicio", "centro", "final" };
        IslandLayoutCombo.ItemsSource = new[] { "lista", "rejilla", "iconosGrandes", "soloNombres" };
        IslandSortCombo.ItemsSource = new[] { "nombre", "ultimoUso", "fechaModificacion", "tipo", "tamano" };
        IslandGroupCombo.ItemsSource = new[] { "ninguna", "tipo" };

        ThemeCombo.ItemsSource = new[] { "oscuro", "claro" };
        AnimTypeCombo.ItemsSource = new[] { "deslizar", "desvanecer", "escalar", "ninguna" };
        AnimEasingCombo.ItemsSource = new[] { "easeOutCubic", "easeOutQuad", "linear" };
        DefaultMonitorsCombo.ItemsSource = new[] { "todos", "principal" };
        CornerBehaviorCombo.ItemsSource = new[] { "propia", "ninguna", "borde" };
    }

    private void LoadConfigToUi(AppConfig cfg)
    {
        _loadingUi = true;
        try
        {
            HideDesktopIconsCheck.IsChecked = cfg.Global.OcultarIconosEscritorio;
            StartWithWindowsCheck.IsChecked = cfg.Global.IniciarConWindows;
            LanguageCombo.SelectedIndex = string.Equals(cfg.Global.Idioma, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            ConfigPathText.Text = $"Archivo activo: {_configService.ConfigPath}";

            TaskbarEnabledCheck.IsChecked = cfg.Taskbar.Habilitada;
            TaskbarStyleCombo.SelectedValue = cfg.Taskbar.Estilo;
            TaskbarColorBox.Text = cfg.Taskbar.Color;
            TaskbarAlignmentCombo.SelectedValue = cfg.Taskbar.Alineacion;
            TaskbarLabelsCheck.IsChecked = cfg.Taskbar.OcultarEtiquetas;
            TaskbarBorderCheck.IsChecked = cfg.Taskbar.MostrarBorde;
            TaskbarCombineCombo.SelectedValue = cfg.Taskbar.CombinarBotones;

            ThemeCombo.SelectedItem = cfg.Appearance.Tema;
            BgColorBox.Text = cfg.Appearance.ColorFondo;
            OpacitySlider.Value = cfg.Appearance.Opacidad;
            OpacityValueText.Text = $"{cfg.Appearance.Opacidad:0.00}";
            BlurCheck.IsChecked = cfg.Appearance.Desenfoque;
            GlassCheck.IsChecked = cfg.Appearance.EstiloVidrio;
            TextColorBox.Text = cfg.Appearance.ColorTexto;
            ScrollbarStyleCombo.SelectedValue = cfg.Appearance.BarraDesplazamiento;
            CornerRadiusSlider.Value = cfg.Appearance.RadioEsquinasPx;
            CornerValueText.Text = $"{cfg.Appearance.RadioEsquinasPx} px";
            ShadowCheck.IsChecked = cfg.Appearance.Sombra;
            FontBox.Text = cfg.Appearance.Fuente;
            IconSizeSlider.Value = cfg.Appearance.TamanoIconoPx;
            IconSizeValueText.Text = $"{cfg.Appearance.TamanoIconoPx} px";
            AnimTypeCombo.SelectedItem = cfg.Appearance.Animacion.Tipo;
            AnimDurationBox.Text = cfg.Appearance.Animacion.DuracionMs.ToString();
            AnimEasingCombo.SelectedItem = cfg.Appearance.Animacion.Easing;

            OpenDelayBox.Text = cfg.Activation.RetardoAperturaMs.ToString();
            CloseDelayBox.Text = cfg.Activation.RetardoCierreMs.ToString();
            ZoneThicknessBox.Text = cfg.Activation.GrosorZonaPx.ToString();
            DefaultMonitorsCombo.SelectedItem = cfg.Activation.Monitores;
            CornerBehaviorCombo.SelectedItem = cfg.Activation.ComportamientoEsquinas;
            DisableFullscreenCheck.IsChecked = cfg.Activation.DesactivarEnPantallaCompleta;
            DisableDraggingCheck.IsChecked = cfg.Activation.DesactivarMientrasSeArrastra;
            AlwaysOnTopCheck.IsChecked = cfg.Activation.SiempreEncima;
            SelectComboByTag(MaximizedBehaviorCombo, cfg.Activation.ComportamientoMaximizada);
            MaximizedDelayBox.Text = cfg.Activation.RetardoMaximizadaMs.ToString();

            ShortcutRestoreBox.Text = cfg.Global.AtajoRestaurar;
            ShortcutPauseBox.Text = cfg.Global.AtajoPausar;
            ShortcutToggleBox.Text = cfg.Global.AtajoAlternar;
            ShortcutSettingsBox.Text = cfg.Global.AtajoAjustes;
            ShortcutOcrBox.Text = cfg.Global.AtajoOcr;

            RefreshIslandsList(selectIndex: cfg.Islands.Count > 0 ? 0 : -1);
            RefreshSpecialIslandsCombo();
        }
        finally
        {
            _loadingUi = false;
        }
        UpdateLivePreview();
    }

    private void RefreshSpecialIslandsCombo(string? selectId = null)
    {
        if (SpecialIslandCombo is null) return;
        var specialIslands = _workingConfig.Islands
            .Where(i => !string.Equals(i.Tipo, "archivos", StringComparison.OrdinalIgnoreCase))
            .ToList();

        SpecialIslandCombo.ItemsSource = null;
        SpecialIslandCombo.ItemsSource = specialIslands;

        if (specialIslands.Count > 0)
        {
            if (selectId is not null)
            {
                IslandConfig? match = specialIslands.FirstOrDefault(i => i.Id == selectId);
                SpecialIslandCombo.SelectedItem = match ?? specialIslands[0];
            }
            else if (SpecialIslandCombo.SelectedIndex < 0)
            {
                SpecialIslandCombo.SelectedIndex = 0;
            }
        }
    }

    private static void SelectComboByTag(ComboBox combo, string? tagValue)
    {
        if (combo is null || string.IsNullOrEmpty(tagValue)) return;
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, tagValue, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string? GetComboTag(ComboBox combo)
    {
        if (combo is null) return null;
        if (combo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            return tag;
        return combo.SelectedValue as string;
    }

    private void OnSpecialIslandChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpecialIslandCombo.SelectedItem is not IslandConfig island || SpecialModeCombo is null) return;
        _loadingUi = true;
        try
        {
            SelectComboByTag(SpecialModeCombo, island.Presentacion.Modo);
            SpecialXBox.Text = island.Presentacion.X.ToString();
            SpecialYBox.Text = island.Presentacion.Y.ToString();
            SpecialBgBox.Text = island.Presentacion.ColorFondo ?? string.Empty;
            SpecialOpacityBox.Text = island.Presentacion.Opacidad?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            string type = island.Tipo.ToLowerInvariant();

            if (MusicOptionsGroup is not null) MusicOptionsGroup.Visibility = type == "musica" ? Visibility.Visible : Visibility.Collapsed;
            if (ClockOptionsGroup is not null) ClockOptionsGroup.Visibility = type == "reloj" ? Visibility.Visible : Visibility.Collapsed;
            if (SystemOptionsGroup is not null) SystemOptionsGroup.Visibility = type == "sistema" ? Visibility.Visible : Visibility.Collapsed;
            if (NotesOptionsGroup is not null) NotesOptionsGroup.Visibility = type == "notas" ? Visibility.Visible : Visibility.Collapsed;
            if (NihilNoxOptionsGroup is not null) NihilNoxOptionsGroup.Visibility = type == "nihilnox" ? Visibility.Visible : Visibility.Collapsed;

            switch (type)
            {
                case "musica":
                    SpecialShowCoverCheck.IsChecked = island.Presentacion.MostrarPortada;
                    SelectComboByTag(SpecialCoverStyleCombo, island.Presentacion.EstiloPortada);
                    SelectComboByTag(SpecialButtonsStyleCombo, island.Presentacion.EstiloBotones);
                    SpecialShowAppCheck.IsChecked = island.Presentacion.MostrarAppOrigen;
                    SpecialShowEqCheck.IsChecked = island.Presentacion.MostrarEcualizador;
                    break;

                case "reloj":
                    SpecialClock24Check.IsChecked = island.Presentacion.FormatoHora24;
                    SpecialClockSecondsCheck.IsChecked = island.Presentacion.MostrarSegundos;
                    SpecialClockDateCheck.IsChecked = island.Presentacion.MostrarFecha;
                    break;

                case "sistema":
                    SpecialSysRamCheck.IsChecked = island.Presentacion.MostrarRam;
                    SpecialSysDiskCheck.IsChecked = island.Presentacion.MostrarDisco;
                    SpecialSysCpuCheck.IsChecked = island.Presentacion.MostrarCpu;
                    break;

                case "notas":
                    SpecialNotesText.Text = island.Presentacion.TextoNota ?? string.Empty;
                    break;

                case "nihilnox":
                    var cfg = NihilNoxServerManager.LoadConfig();
                    if (NihilNoxTokenBox is not null) NihilNoxTokenBox.Text = cfg.GetValueOrDefault("ngrok_token", "");
                    if (NihilNoxDomainBox is not null) NihilNoxDomainBox.Text = cfg.GetValueOrDefault("ngrok_domain", "");
                    if (NihilNoxApiKeyBox is not null) NihilNoxApiKeyBox.Text = cfg.GetValueOrDefault("api_key", "");
                    if (NihilNoxFfmpegBox is not null) NihilNoxFfmpegBox.Text = cfg.GetValueOrDefault("ffmpeg_path", "");
                    break;
            }
        }
        finally
        {
            _loadingUi = false;
        }
        UpdateLivePreview();
    }

    private void OnSpecialFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi || !IsInitialized) return;
        CollectSpecialIslandSettings();
        UpdateLivePreview();
    }

    private void CollectSpecialIslandSettings()
    {
        if (SpecialIslandCombo.SelectedItem is not IslandConfig island) return;
        island.Presentacion.Modo = GetComboTag(SpecialModeCombo) ?? "borde";
        if (int.TryParse(SpecialXBox.Text, out int x)) island.Presentacion.X = x;
        if (int.TryParse(SpecialYBox.Text, out int y)) island.Presentacion.Y = y;
        island.Presentacion.ColorFondo = string.IsNullOrWhiteSpace(SpecialBgBox.Text) ? null : SpecialBgBox.Text.Trim();
        island.Presentacion.Opacidad = double.TryParse(SpecialOpacityBox.Text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double opacity) ? opacity : null;

        string type = island.Tipo.ToLowerInvariant();
        switch (type)
        {
            case "musica":
                island.Presentacion.MostrarPortada = SpecialShowCoverCheck.IsChecked == true;
                if (GetComboTag(SpecialCoverStyleCombo) is string coverStyle) island.Presentacion.EstiloPortada = coverStyle;
                if (GetComboTag(SpecialButtonsStyleCombo) is string btnStyle) island.Presentacion.EstiloBotones = btnStyle;
                island.Presentacion.MostrarAppOrigen = SpecialShowAppCheck.IsChecked == true;
                island.Presentacion.MostrarEcualizador = SpecialShowEqCheck.IsChecked == true;
                break;

            case "reloj":
                island.Presentacion.FormatoHora24 = SpecialClock24Check.IsChecked == true;
                island.Presentacion.MostrarSegundos = SpecialClockSecondsCheck.IsChecked == true;
                island.Presentacion.MostrarFecha = SpecialClockDateCheck.IsChecked == true;
                break;

            case "sistema":
                island.Presentacion.MostrarRam = SpecialSysRamCheck.IsChecked == true;
                island.Presentacion.MostrarDisco = SpecialSysDiskCheck.IsChecked == true;
                island.Presentacion.MostrarCpu = SpecialSysCpuCheck.IsChecked == true;
                break;

            case "notas":
                island.Presentacion.TextoNota = SpecialNotesText.Text;
                break;
        }
    }

    private void OnAddMusicIslandClick(object sender, RoutedEventArgs e)
    {
        var island = AppConfig.CreateDefaultMusicIsland();
        island.Id = $"musica-{Guid.NewGuid().ToString("N")[..5]}";
        _workingConfig.Islands.Add(island);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
        RefreshSpecialIslandsCombo(island.Id);
    }

    private void OnAddClockIslandClick(object sender, RoutedEventArgs e)
    {
        var island = AppConfig.CreateDefaultClockIsland();
        island.Id = $"reloj-{Guid.NewGuid().ToString("N")[..5]}";
        _workingConfig.Islands.Add(island);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
        RefreshSpecialIslandsCombo(island.Id);
    }

    private void OnAddSystemIslandClick(object sender, RoutedEventArgs e)
    {
        var island = AppConfig.CreateDefaultSystemIsland();
        island.Id = $"sistema-{Guid.NewGuid().ToString("N")[..5]}";
        _workingConfig.Islands.Add(island);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
        RefreshSpecialIslandsCombo(island.Id);
    }

    private void OnAddNotesIslandClick(object sender, RoutedEventArgs e)
    {
        var island = AppConfig.CreateDefaultNotesIsland();
        island.Id = $"notas-{Guid.NewGuid().ToString("N")[..5]}";
        _workingConfig.Islands.Add(island);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
        RefreshSpecialIslandsCombo(island.Id);
    }

    private void OnAddNihilNoxIslandClick(object sender, RoutedEventArgs e)
    {
        var island = AppConfig.CreateDefaultNihilNoxIsland();
        island.Id = $"nihilnox-{Guid.NewGuid().ToString("N")[..5]}";
        _workingConfig.Islands.Add(island);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
        RefreshSpecialIslandsCombo(island.Id);
    }

    private void RefreshIslandsList(int selectIndex)
    {
        _loadingUi = true;
        try
        {
            IslandsListBox.ItemsSource = null;
            IslandsListBox.ItemsSource = _workingConfig.Islands
                .Select(i => $"{(i.Habilitada ? "●" : "○")} {i.Nombre} ({i.Borde})")
                .ToList();

            if (selectIndex >= 0 && selectIndex < _workingConfig.Islands.Count)
                IslandsListBox.SelectedIndex = selectIndex;
        }
        finally
        {
            _loadingUi = false;
        }
        LoadSelectedIslandToEditor();
    }

    private IslandConfig? GetSelectedIsland()
    {
        int idx = IslandsListBox.SelectedIndex;
        return idx >= 0 && idx < _workingConfig.Islands.Count ? _workingConfig.Islands[idx] : null;
    }

    private void LoadSelectedIslandToEditor()
    {
        _loadingUi = true;
        try
        {
            IslandConfig? island = GetSelectedIsland();
            IslandEditorPanel.IsEnabled = island is not null;
            if (island is null) return;

            IslandEnabledCheck.IsChecked = island.Habilitada;
            SelectComboByTag(IslandTypeCombo, island.Tipo);
            IslandNameBox.Text = island.Nombre;
            IslandIdBox.Text = island.Id;
            IslandEdgeCombo.SelectedItem = island.Borde;
            IslandAnchorCombo.SelectedItem = island.Posicion.Ancla;
            IslandMonitorBox.Text = island.Monitor;
            IslandWidthPxBox.Text = island.Tamano.AnchoPx?.ToString() ?? string.Empty;
            IslandWidthPctBox.Text = island.Tamano.AnchoPct?.ToString() ?? string.Empty;
            IslandHeightPxBox.Text = island.Tamano.AltoPx?.ToString() ?? string.Empty;
            IslandHeightPctBox.Text = island.Tamano.AltoPct?.ToString() ?? string.Empty;
            IslandOffsetXBox.Text = island.Posicion.DesplazamientoX.ToString();
            IslandOffsetYBox.Text = island.Posicion.DesplazamientoY.ToString();
            IslandAccentBox.Text = island.Estilo.ColorAcento;
            IslandLayoutCombo.SelectedItem = island.Contenido.Disposicion;
            IslandSortCombo.SelectedItem = island.Contenido.Orden;
            IslandGroupCombo.SelectedItem = island.Contenido.Agrupacion;
            IslandIncludeBox.Text = string.Join(", ", island.Contenido.Filtros.Incluir);
            IslandExcludeBox.Text = string.Join(", ", island.Contenido.Filtros.Excluir);

            var sources = island.Contenido.Fuentes;
            SourceUserCheck.IsChecked = sources.Any(s => s.Contains("usuario", StringComparison.OrdinalIgnoreCase));
            SourcePublicCheck.IsChecked = sources.Any(s => s.Contains("público", StringComparison.OrdinalIgnoreCase) || s.Contains("publico", StringComparison.OrdinalIgnoreCase));

            var types = island.Contenido.Filtros.Tipos;
            TypeFileCheck.IsChecked = types.Any(t => t.Equals("archivo", StringComparison.OrdinalIgnoreCase));
            TypeFolderCheck.IsChecked = types.Any(t => t.Equals("carpeta", StringComparison.OrdinalIgnoreCase));
            TypeShortcutCheck.IsChecked = types.Any(t => t.Equals("acceso directo", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _loadingUi = false;
        }
        UpdateLivePreview();
    }

    private void OnSelectedIslandChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || !IsInitialized) return;
        LoadSelectedIslandToEditor();
    }

    private void OnIslandFieldChanged(object sender, EventArgs e)
    {
        if (_loadingUi || !IsInitialized) return;
        IslandConfig? island = GetSelectedIsland();
        if (island is null) return;

        island.Habilitada = IslandEnabledCheck.IsChecked == true;
        island.Tipo = GetComboTag(IslandTypeCombo) ?? "archivos";
        RefreshSpecialIslandsCombo(island.Id);
        island.Nombre = IslandNameBox.Text.Trim();
        island.Id = string.IsNullOrWhiteSpace(IslandIdBox.Text) ? island.Id : IslandIdBox.Text.Trim();
        if (IslandEdgeCombo.SelectedItem is string edge) island.Borde = edge;
        if (IslandAnchorCombo.SelectedItem is string anchor) island.Posicion.Ancla = anchor;
        island.Monitor = string.IsNullOrWhiteSpace(IslandMonitorBox.Text) ? "principal" : IslandMonitorBox.Text.Trim();

        island.Tamano.AnchoPx = int.TryParse(IslandWidthPxBox.Text, out int wPx) ? wPx : null;
        island.Tamano.AnchoPct = double.TryParse(IslandWidthPctBox.Text, out double wPct) ? wPct : null;
        island.Tamano.AltoPx = int.TryParse(IslandHeightPxBox.Text, out int hPx) ? hPx : null;
        island.Tamano.AltoPct = double.TryParse(IslandHeightPctBox.Text, out double hPct) ? hPct : null;
        island.Posicion.DesplazamientoX = int.TryParse(IslandOffsetXBox.Text, out int ox) ? ox : 0;
        island.Posicion.DesplazamientoY = int.TryParse(IslandOffsetYBox.Text, out int oy) ? oy : 0;

        if (!string.IsNullOrWhiteSpace(IslandAccentBox.Text))
            island.Estilo.ColorAcento = IslandAccentBox.Text.Trim();

        if (IslandLayoutCombo.SelectedItem is string layout) island.Contenido.Disposicion = layout;
        if (IslandSortCombo.SelectedItem is string sort) island.Contenido.Orden = sort;
        if (IslandGroupCombo.SelectedItem is string grp) island.Contenido.Agrupacion = grp;

        island.Contenido.Filtros.Incluir = SplitCsv(IslandIncludeBox.Text);
        island.Contenido.Filtros.Excluir = SplitCsv(IslandExcludeBox.Text);

        var sources = new List<string>();
        if (SourceUserCheck.IsChecked == true) sources.Add("Escritorio usuario");
        if (SourcePublicCheck.IsChecked == true) sources.Add("Escritorio público");
        island.Contenido.Fuentes = sources;

        var types = new List<string>();
        if (TypeFileCheck.IsChecked == true) types.Add("archivo");
        if (TypeFolderCheck.IsChecked == true) types.Add("carpeta");
        if (TypeShortcutCheck.IsChecked == true) types.Add("acceso directo");
        island.Contenido.Filtros.Tipos = types;

        // Actualizar etiqueta en la lista sin perder foco del cuadro actual.
        int sel = IslandsListBox.SelectedIndex;
        if (sel >= 0 && IslandsListBox.ItemsSource is List<string> labels && sel < labels.Count)
        {
            string expected = $"{(island.Habilitada ? "●" : "○")} {island.Nombre} ({island.Borde})";
            if (labels[sel] != expected)
            {
                _loadingUi = true;
                labels[sel] = expected;
                IslandsListBox.ItemsSource = null;
                IslandsListBox.ItemsSource = labels;
                IslandsListBox.SelectedIndex = sel;
                _loadingUi = false;
            }
        }

        UpdateLivePreview();
    }

    private void OnAppearanceChanged(object sender, EventArgs e)
    {
        if (_loadingUi || !IsInitialized || _workingConfig is null) return;

        _workingConfig.Appearance.Tema = ThemeCombo.SelectedItem as string ?? "oscuro";
        _workingConfig.Appearance.ColorFondo = BgColorBox.Text.Trim();
        _workingConfig.Appearance.Opacidad = Math.Round(OpacitySlider.Value, 2);
        OpacityValueText.Text = $"{_workingConfig.Appearance.Opacidad:0.00}";
        _workingConfig.Appearance.Desenfoque = BlurCheck.IsChecked == true;
        _workingConfig.Appearance.EstiloVidrio = GlassCheck.IsChecked == true;
        _workingConfig.Appearance.ColorTexto = TextColorBox.Text.Trim();
        _workingConfig.Appearance.BarraDesplazamiento = ScrollbarStyleCombo.SelectedValue as string ?? "linea";
        _workingConfig.Appearance.RadioEsquinasPx = (int)CornerRadiusSlider.Value;
        CornerValueText.Text = $"{_workingConfig.Appearance.RadioEsquinasPx} px";
        _workingConfig.Appearance.Sombra = ShadowCheck.IsChecked == true;
        _workingConfig.Appearance.Fuente = FontBox.Text.Trim();
        _workingConfig.Appearance.TamanoIconoPx = (int)IconSizeSlider.Value;
        IconSizeValueText.Text = $"{_workingConfig.Appearance.TamanoIconoPx} px";

        UpdateLivePreview();
    }

    private void UpdateLivePreview()
    {
        IslandConfig? island = GetSelectedIsland();
        AppearanceConfig app = _workingConfig.Appearance;
        bool isDark = !string.Equals(app.Tema, "claro", StringComparison.OrdinalIgnoreCase);

        Color bg = ParseColor(app.ColorFondo, isDark ? Color.FromRgb(30, 30, 46) : Color.FromRgb(245, 245, 250));
        byte alpha = (byte)Math.Round(Math.Clamp(app.Opacidad, 0.1, 1.0) * 255);
        PreviewSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        AppearancePreviewSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        Color previewFg = ParseColor(app.ColorTexto, isDark ? Color.FromRgb(236, 239, 248) : Color.FromRgb(28, 30, 38));
        AppearancePreviewTitle.Foreground = new SolidColorBrush(previewFg);
        AppearancePreviewItem1.Foreground = new SolidColorBrush(previewFg);
        AppearancePreviewItem2.Foreground = new SolidColorBrush(previewFg);
        AppearancePreviewItem3.Foreground = new SolidColorBrush(previewFg);
        AppearancePreviewSurface.CornerRadius = new CornerRadius(Math.Clamp(app.RadioEsquinasPx, 0, 32));
        AppearancePreviewSurface.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        PreviewSurface.CornerRadius = new CornerRadius(Math.Clamp(app.RadioEsquinasPx, 0, 32));

        Color fg = ParseColor(app.ColorTexto, isDark ? Color.FromRgb(236, 239, 248) : Color.FromRgb(28, 30, 38));
        PreviewTitleText.Foreground = new SolidColorBrush(fg);
        PreviewTitleText.Text = island is null ? "Sin isla seleccionada" : island.Nombre;

        Color accent = ParseColor(island?.Estilo.ColorAcento, Color.FromRgb(137, 180, 250));
        PreviewAccentPill.Background = new SolidColorBrush(accent);

        PreviewContentHost.Children.Clear();
        if (island is null) return;

        if (!string.Equals(island.Tipo, "archivos", StringComparison.OrdinalIgnoreCase))
        {
            switch (island.Tipo.ToLowerInvariant())
            {
                case "musica":
                    var musicBox = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                    var musicRow = new StackPanel { Orientation = Orientation.Horizontal };
                    if (island.Presentacion.MostrarPortada)
                    {
                        var artBox = new Border
                        {
                            Width = 44,
                            Height = 44,
                            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                            CornerRadius = island.Presentacion.EstiloPortada is "circular" or "vinilo" ? new CornerRadius(22) : new CornerRadius(6),
                            Margin = new Thickness(0, 0, 10, 0)
                        };
                        artBox.Child = new TextBlock { Text = "🎵", FontSize = 20, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                        musicRow.Children.Add(artBox);
                    }
                    var musicInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    musicInfo.Children.Add(new TextBlock { Text = "Canción en reproducción", Foreground = new SolidColorBrush(fg), FontWeight = FontWeights.Bold, FontSize = 12 });
                    musicInfo.Children.Add(new TextBlock { Text = "Artista", Foreground = new SolidColorBrush(fg), Opacity = 0.7, FontSize = 11 });
                    musicRow.Children.Add(musicInfo);
                    musicBox.Children.Add(musicRow);

                    var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                    btnRow.Children.Add(new TextBlock { Text = "⏮   ▶   ⏭   |   🔊  ═══⚪══", Foreground = new SolidColorBrush(fg), FontSize = 13, FontWeight = FontWeights.SemiBold });
                    musicBox.Children.Add(btnRow);

                    PreviewContentHost.Children.Add(musicBox);
                    return;

                case "reloj":
                    var clockBox = new StackPanel { Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                    clockBox.Children.Add(new TextBlock { Text = DateTime.Now.ToString("HH:mm:ss"), Foreground = new SolidColorBrush(fg), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
                    clockBox.Children.Add(new TextBlock { Text = DateTime.Now.ToString("dddd, d MMMM"), Foreground = new SolidColorBrush(fg), Opacity = 0.7, FontSize = 11, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
                    PreviewContentHost.Children.Add(clockBox);
                    return;

                case "sistema":
                    var sysBox = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                    sysBox.Children.Add(new TextBlock { Text = "RAM: 42% | DISCO: 58% | CPU: 12%", Foreground = new SolidColorBrush(fg), FontSize = 11 });
                    PreviewContentHost.Children.Add(sysBox);
                    return;

                case "notas":
                    var noteBox = new TextBlock { Text = string.IsNullOrWhiteSpace(island.Presentacion.TextoNota) ? "(Nota vacía)" : island.Presentacion.TextoNota, Foreground = new SolidColorBrush(fg), FontSize = 11, Opacity = 0.85 };
                    PreviewContentHost.Children.Add(noteBox);
                    return;

                case "nihilnox":
                    var nnBox = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                    nnBox.Children.Add(new TextBlock { Text = "🟢 NihilNox Server (Activo)", Foreground = new SolidColorBrush(fg), FontSize = 12, FontWeight = FontWeights.Bold });
                    nnBox.Children.Add(new TextBlock { Text = "https://tudominio.ngrok.dev", Foreground = new SolidColorBrush(fg), FontSize = 11, Opacity = 0.75 });
                    PreviewContentHost.Children.Add(nnBox);
                    return;
            }
        }

        IReadOnlyList<DesktopItem> items = _coordinator.GetFilteredItemsForIsland(island);
        int previewCount = Math.Min(items.Count, 12);
        for (int i = 0; i < previewCount; i++)
        {
            DesktopItem item = items[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            BitmapSource? icon = _coordinator.IconCache.GetIcon(item.FullPath, 24);
            if (icon is not null)
            {
                row.Children.Add(new Image
                {
                    Source = icon,
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }
            row.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                Foreground = new SolidColorBrush(fg),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            PreviewContentHost.Children.Add(row);
        }

        if (items.Count == 0)
        {
            PreviewContentHost.Children.Add(new TextBlock
            {
                Text = "(Sin elementos coincidentes)",
                Foreground = new SolidColorBrush(fg),
                Opacity = 0.65,
                FontSize = 11
            });
        }
        else if (items.Count > previewCount)
        {
            PreviewContentHost.Children.Add(new TextBlock
            {
                Text = $"+ {items.Count - previewCount} elementos más...",
                Foreground = new SolidColorBrush(fg),
                Opacity = 0.65,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
    }

    private bool CollectNonIslandFieldsAndSave()
    {
        CollectSpecialIslandSettings();

        // Preservar posiciones y modo actuales de las islas si el usuario las movió en el escritorio
        var liveIslands = _configService.Current.Islands.ToDictionary(i => i.Id, i => i.Presentacion);
        foreach (var island in _workingConfig.Islands)
        {
            if (liveIslands.TryGetValue(island.Id, out var livePres))
            {
                island.Presentacion.X = livePres.X;
                island.Presentacion.Y = livePres.Y;
                island.Presentacion.Modo = livePres.Modo;
            }
        }

        _workingConfig.Global.OcultarIconosEscritorio = HideDesktopIconsCheck.IsChecked == true;
        _workingConfig.Global.IniciarConWindows = StartWithWindowsCheck.IsChecked == true;
        _workingConfig.Global.Idioma = LanguageCombo.SelectedIndex == 1 ? "en" : "es";
        _workingConfig.Global.AtajoRestaurar = ShortcutRestoreBox.Text.Trim();
        _workingConfig.Global.AtajoPausar = ShortcutPauseBox.Text.Trim();
        _workingConfig.Global.AtajoAlternar = ShortcutToggleBox.Text.Trim();
        _workingConfig.Global.AtajoAjustes = ShortcutSettingsBox.Text.Trim();
        _workingConfig.Global.AtajoOcr = ShortcutOcrBox.Text.Trim();

        string estiloTaskbar = TaskbarStyleCombo.SelectedValue as string ?? "predeterminado";
        _workingConfig.Taskbar.Estilo = estiloTaskbar;
        _workingConfig.Taskbar.Habilitada = TaskbarEnabledCheck.IsChecked == true || !string.Equals(estiloTaskbar, "predeterminado", StringComparison.OrdinalIgnoreCase);
        _workingConfig.Taskbar.Color = TaskbarColorBox.Text.Trim();
        _workingConfig.Taskbar.Alineacion = TaskbarAlignmentCombo.SelectedValue as string ?? "centro";
        _workingConfig.Taskbar.OcultarEtiquetas = TaskbarLabelsCheck.IsChecked == true;
        _workingConfig.Taskbar.MostrarBorde = TaskbarBorderCheck.IsChecked == true;
        _workingConfig.Taskbar.CombinarBotones = TaskbarCombineCombo.SelectedValue as string ?? "siempre";

        if (int.TryParse(AnimDurationBox.Text, out int animMs))
            _workingConfig.Appearance.Animacion.DuracionMs = animMs;
        if (AnimTypeCombo.SelectedItem is string animType)
            _workingConfig.Appearance.Animacion.Tipo = animType;
        if (AnimEasingCombo.SelectedItem is string easing)
            _workingConfig.Appearance.Animacion.Easing = easing;

        if (int.TryParse(OpenDelayBox.Text, out int openDelay))
            _workingConfig.Activation.RetardoAperturaMs = openDelay;
        if (int.TryParse(CloseDelayBox.Text, out int closeDelay))
            _workingConfig.Activation.RetardoCierreMs = closeDelay;
        if (int.TryParse(ZoneThicknessBox.Text, out int thick))
            _workingConfig.Activation.GrosorZonaPx = thick;
        if (DefaultMonitorsCombo.SelectedItem is string mon)
            _workingConfig.Activation.Monitores = mon;
        if (CornerBehaviorCombo.SelectedItem is string cb)
            _workingConfig.Activation.ComportamientoEsquinas = cb;

        _workingConfig.Activation.DesactivarEnPantallaCompleta = DisableFullscreenCheck.IsChecked == true;
        _workingConfig.Activation.DesactivarMientrasSeArrastra = DisableDraggingCheck.IsChecked == true;
        _workingConfig.Activation.SiempreEncima = AlwaysOnTopCheck.IsChecked == true;
        _workingConfig.Activation.ComportamientoMaximizada = GetComboTag(MaximizedBehaviorCombo) ?? "desactivar";
        if (int.TryParse(MaximizedDelayBox.Text, out int maxDelay))
            _workingConfig.Activation.RetardoMaximizadaMs = maxDelay;

        if (!_configService.TrySave(_workingConfig, out IReadOnlyList<string> errors))
        {
            ShowErrorBanner(string.Join("\n", errors));
            return false;
        }

        ErrorBanner.Visibility = Visibility.Collapsed;
        StatusText.Text = "Configuración guardada y aplicada en caliente.";
        return true;
    }

    private void OnAddIslandClick(object sender, RoutedEventArgs e)
    {
        var newIsland = new IslandConfig
        {
            Id = $"isla-{Guid.NewGuid().ToString("N")[..5]}",
            Nombre = "Nueva isla",
            Borde = "izquierdo",
            Tamano = new IslandSizeConfig { AnchoPx = 230, AltoPct = 50 }
        };
        _workingConfig.Islands.Add(newIsland);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
    }

    private void OnDuplicateIslandClick(object sender, RoutedEventArgs e)
    {
        IslandConfig? selected = GetSelectedIsland();
        if (selected is null) return;

        string json = System.Text.Json.JsonSerializer.Serialize(selected, ConfigSerializerOptions.Default);
        IslandConfig copy = System.Text.Json.JsonSerializer.Deserialize<IslandConfig>(json, ConfigSerializerOptions.Default)!;
        copy.Id = $"{selected.Id}-copia-{Guid.NewGuid().ToString("N")[..3]}";
        copy.Nombre = $"{selected.Nombre} (copia)";
        _workingConfig.Islands.Add(copy);
        RefreshIslandsList(_workingConfig.Islands.Count - 1);
    }

    private void OnRemoveIslandClick(object sender, RoutedEventArgs e)
    {
        int idx = IslandsListBox.SelectedIndex;
        if (idx < 0 || idx >= _workingConfig.Islands.Count) return;
        _workingConfig.Islands.RemoveAt(idx);
        RefreshIslandsList(Math.Min(idx, _workingConfig.Islands.Count - 1));
    }

    private void OnMoveIslandUpClick(object sender, RoutedEventArgs e)
    {
        int idx = IslandsListBox.SelectedIndex;
        if (idx <= 0) return;
        (_workingConfig.Islands[idx - 1], _workingConfig.Islands[idx]) = (_workingConfig.Islands[idx], _workingConfig.Islands[idx - 1]);
        RefreshIslandsList(idx - 1);
    }

    private void OnMoveIslandDownClick(object sender, RoutedEventArgs e)
    {
        int idx = IslandsListBox.SelectedIndex;
        if (idx < 0 || idx >= _workingConfig.Islands.Count - 1) return;
        (_workingConfig.Islands[idx + 1], _workingConfig.Islands[idx]) = (_workingConfig.Islands[idx], _workingConfig.Islands[idx + 1]);
        RefreshIslandsList(idx + 1);
    }

    private void OnImportJsonClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Archivos JSON (*.json)|*.json|Todos (*.*)|*.*",
            Title = "Importar configuración"
        };
        if (dialog.ShowDialog() == true)
        {
            if (_configService.TryImportFromFile(dialog.FileName, out string error))
            {
                _workingConfig = _configService.Current.Clone();
                LoadConfigToUi(_workingConfig);
                StatusText.Text = "Configuración importada correctamente.";
            }
            else
            {
                MessageBox.Show(error, "Error al importar JSON", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnExportJsonClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Archivos JSON (*.json)|*.json",
            FileName = "config.json",
            Title = "Exportar configuración"
        };
        if (dialog.ShowDialog() == true)
        {
            _configService.ExportToFile(dialog.FileName);
            StatusText.Text = $"Configuración exportada a {dialog.FileName}";
        }
    }

    private void OnOpenJsonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_configService.ConfigPath) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("¿Restaurar toda la configuración a los valores por defecto?",
                "Islas del escritorio", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _configService.ResetToDefaults();
            _workingConfig = _configService.Current.Clone();
            LoadConfigToUi(_workingConfig);
            StatusText.Text = "Valores por defecto restaurados.";
        }
    }

    private void OnRestoreIconsNowClick(object sender, RoutedEventArgs e)
    {
        _coordinator.RestoreDesktopAndPause();
        StatusText.Text = "Iconos del escritorio restaurados.";
    }

    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        e.Handled = true;
        var parts = new List<string>();
        ModifierKeys mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        box.Text = string.Join("+", parts);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => CollectNonIslandFieldsAndSave();

    private void OnSaveAndCloseClick(object sender, RoutedEventArgs e)
    {
        if (CollectNonIslandFieldsAndSave())
            Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void ShowErrorBanner(string msg)
    {
        ErrorBannerText.Text = msg;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private static List<string> SplitCsv(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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

    private void OnRunOcrClick(object sender, RoutedEventArgs e)
    {
        _coordinator.StartOcrScreenCapture();
    }

    private void OnStartNihilNoxClick(object sender, RoutedEventArgs e)
    {
        NihilNoxServerManager.StartServer();
    }

    private void OnStopNihilNoxClick(object sender, RoutedEventArgs e)
    {
        NihilNoxServerManager.StopServer();
    }

    private void OnSaveNihilNoxConfigClick(object sender, RoutedEventArgs e)
    {
        string token = NihilNoxTokenBox?.Text.Trim() ?? "";
        string domain = NihilNoxDomainBox?.Text.Trim() ?? "";
        string apiKey = NihilNoxApiKeyBox?.Text.Trim() ?? "";
        string ffmpeg = NihilNoxFfmpegBox?.Text.Trim() ?? "";

        NihilNoxServerManager.SaveConfig(token, domain, apiKey, ffmpeg);
        StatusText.Text = "Configuración de NihilNox guardada.";
    }

    private void OnNihilNoxConfigFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingUi) return;
    }
}
