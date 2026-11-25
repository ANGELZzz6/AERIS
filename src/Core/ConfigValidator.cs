using System.Text.Json;
using System.Text.RegularExpressions;

namespace IslasEscritorio.Core;

public sealed record ConfigValidationResult(bool IsValid, IReadOnlyList<string> Errors, AppConfig? Config);

public static partial class ConfigValidator
{
    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColorRegex();

    public static ConfigValidationResult ParseAndValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new(false, ["El archivo JSON está vacío."], null);

        AppConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AppConfig>(json, ConfigSerializerOptions.Default);
        }
        catch (JsonException ex)
        {
            return new(false, [$"Error de sintaxis JSON: {ex.Message}"], null);
        }

        if (parsed is null)
            return new(false, ["No se pudo deserializar la configuración."], null);

        return Validate(parsed);
    }

    public static ConfigValidationResult Validate(AppConfig config)
    {
        var errors = new List<string>();

        if (config.Version < 1)
            errors.Add("La versión del esquema debe ser >= 1.");

        // Migrar versiones futuras/anteriores si fuera necesario.
        config.Global ??= new GlobalConfig();
        config.Activation ??= new ActivationConfig();
        config.Appearance ??= new AppearanceConfig();
        config.Appearance.Animacion ??= new AnimationConfig();
        config.Islands ??= [];

        if (config.Activation.RetardoAperturaMs is < 0 or > 5000)
            errors.Add("activacion.retardoAperturaMs debe estar entre 0 y 5000 ms.");

        if (config.Activation.RetardoCierreMs is < 0 or > 5000)
            errors.Add("activacion.retardoCierreMs debe estar entre 0 y 5000 ms.");

        if (config.Activation.GrosorZonaPx is < 1 or > 64)
            errors.Add("activacion.grosorZonaPx debe estar entre 1 y 64 px.");

        if (!TryParseCornerBehavior(config.Activation.ComportamientoEsquinas, out _))
            errors.Add($"activacion.comportamientoEsquinas inválido: '{config.Activation.ComportamientoEsquinas}'.");

        if (config.Appearance.Opacidad is < 0.1 or > 1.0)
            errors.Add("apariencia.opacidad debe estar entre 0.1 y 1.0.");

        if (!IsValidHexColor(config.Appearance.ColorFondo))
            errors.Add($"apariencia.colorFondo inválido: '{config.Appearance.ColorFondo}'.");

        if (config.Appearance.RadioEsquinasPx is < 0 or > 48)
            errors.Add("apariencia.radioEsquinasPx debe estar entre 0 y 48.");

        if (config.Appearance.TamanoIconoPx is < 16 or > 128)
            errors.Add("apariencia.tamanoIconoPx debe estar entre 16 y 128.");

        if (!TryParseAnimationType(config.Appearance.Animacion.Tipo, out _))
            errors.Add($"apariencia.animacion.tipo inválido: '{config.Appearance.Animacion.Tipo}'.");

        if (config.Appearance.Animacion.DuracionMs is < 0 or > 2000)
            errors.Add("apariencia.animacion.duracionMs debe estar entre 0 y 2000 ms.");

        if (!TryParseAnimationEasing(config.Appearance.Animacion.Easing, out _))
            errors.Add($"apariencia.animacion.easing inválido: '{config.Appearance.Animacion.Easing}'.");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < config.Islands.Count; i++)
        {
            IslandConfig island = config.Islands[i];
            string prefix = $"islas[{i}]";
            island.Presentacion ??= new SpecialIslandPresentation();
            if (!string.Equals(island.Tipo, "archivos", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(island.Tipo, "musica", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{prefix}.tipo debe ser 'archivos' o 'musica'.");
            if (island.Presentacion.Modo is not ("borde" or "siempre" or "flotante"))
                errors.Add($"{prefix}.presentacion.modo debe ser 'borde', 'siempre' o 'flotante'.");
            if (island.Presentacion.Opacidad is < 0.1 or > 1.0)
                errors.Add($"{prefix}.presentacion.opacidad debe estar entre 0.1 y 1.0.");
            if (!string.IsNullOrWhiteSpace(island.Presentacion.ColorFondo) && !IsValidHexColor(island.Presentacion.ColorFondo))
                errors.Add($"{prefix}.presentacion.colorFondo no es un color hexadecimal válido.");

            if (string.IsNullOrWhiteSpace(island.Id))
                errors.Add($"{prefix}.id no puede estar vacío.");
            else if (!seenIds.Add(island.Id))
                errors.Add($"{prefix}.id '{island.Id}' está duplicado.");

            if (!TryParseEdge(island.Borde, out _))
                errors.Add($"{prefix}.borde inválido: '{island.Borde}'.");

            island.Posicion ??= new IslandPositionConfig();
            if (!TryParseAnchor(island.Posicion.Ancla, out _))
                errors.Add($"{prefix}.posicion.ancla inválido: '{island.Posicion.Ancla}'.");

            island.Tamano ??= new IslandSizeConfig();
            if (island.Tamano.AnchoPct is < 5 or > 100)
                errors.Add($"{prefix}.tamano.anchoPct debe estar entre 5 y 100.");
            if (island.Tamano.AltoPct is < 5 or > 100)
                errors.Add($"{prefix}.tamano.altoPct debe estar entre 5 y 100.");
            if (island.Tamano.AnchoPx is < 60 or > 3000)
                errors.Add($"{prefix}.tamano.anchoPx debe estar entre 60 y 3000.");
            if (island.Tamano.AltoPx is < 60 or > 3000)
                errors.Add($"{prefix}.tamano.altoPx debe estar entre 60 y 3000.");

            island.Contenido ??= new IslandContentConfig();
            island.Contenido.Filtros ??= new IslandFilterConfig();
            island.Contenido.Fuentes ??= ["Escritorio usuario", "Escritorio público"];

            if (!TryParseSortOrder(island.Contenido.Orden, out _))
                errors.Add($"{prefix}.contenido.orden inválido: '{island.Contenido.Orden}'.");
            if (!TryParseGrouping(island.Contenido.Agrupacion, out _))
                errors.Add($"{prefix}.contenido.agrupacion inválido: '{island.Contenido.Agrupacion}'.");
            if (!TryParseLayout(island.Contenido.Disposicion, out _))
                errors.Add($"{prefix}.contenido.disposicion inválido: '{island.Contenido.Disposicion}'.");

            island.Estilo ??= new IslandStyleConfig();
            if (!IsValidHexColor(island.Estilo.ColorAcento))
                errors.Add($"{prefix}.estilo.colorAcento inválido: '{island.Estilo.ColorAcento}'.");
            if (!string.IsNullOrWhiteSpace(island.Estilo.ColorFondo) && !IsValidHexColor(island.Estilo.ColorFondo))
                errors.Add($"{prefix}.estilo.colorFondo inválido: '{island.Estilo.ColorFondo}'.");
            if (island.Estilo.Opacidad is < 0.1 or > 1.0)
                errors.Add($"{prefix}.estilo.opacidad debe estar entre 0.1 y 1.0.");
        }

        return new ConfigValidationResult(errors.Count == 0, errors, errors.Count == 0 ? config : null);
    }

    public static bool IsValidHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorRegex().IsMatch(value.Trim());

    public static bool TryParseEdge(string? value, out IslandEdge edge)
    {
        edge = IslandEdge.Left;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "izquierdo":
            case "izquierda":
            case "left":
                edge = IslandEdge.Left; return true;
            case "derecho":
            case "derecha":
            case "right":
                edge = IslandEdge.Right; return true;
            case "superior":
            case "arriba":
            case "top":
                edge = IslandEdge.Top; return true;
            case "inferior":
            case "abajo":
            case "bottom":
                edge = IslandEdge.Bottom; return true;
            case "esquinasuperiorizquierda":
            case "esquina_superior_izquierda":
            case "topleft":
            case "esquina":
                edge = IslandEdge.TopLeft; return true;
            case "esquinasuperiorderecha":
            case "esquina_superior_derecha":
            case "topright":
                edge = IslandEdge.TopRight; return true;
            case "esquinainferiorizquierda":
            case "esquina_inferior_izquierda":
            case "bottomleft":
                edge = IslandEdge.BottomLeft; return true;
            case "esquinainferiorderecha":
            case "esquina_inferior_derecha":
            case "bottomright":
                edge = IslandEdge.BottomRight; return true;
            default:
                return false;
        }
    }

    public static bool TryParseAnchor(string? value, out IslandAnchor anchor)
    {
        anchor = IslandAnchor.Center;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "centro" or "center":
                anchor = IslandAnchor.Center; return true;
            case "inicio" or "start":
                anchor = IslandAnchor.Start; return true;
            case "final" or "fin" or "end":
                anchor = IslandAnchor.End; return true;
            default:
                return false;
        }
    }

    public static bool TryParseCornerBehavior(string? value, out CornerBehavior behavior)
    {
        behavior = CornerBehavior.OwnIsland;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "propia" or "own":
                behavior = CornerBehavior.OwnIsland; return true;
            case "ninguna" or "none":
                behavior = CornerBehavior.None; return true;
            case "borde" or "edge":
                behavior = CornerBehavior.EdgePriority; return true;
            default:
                return false;
        }
    }

    public static bool TryParseAnimationType(string? value, out AnimationType type)
    {
        type = AnimationType.Slide;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "deslizar" or "slide":
                type = AnimationType.Slide; return true;
            case "desvanecer" or "fade":
                type = AnimationType.Fade; return true;
            case "escalar" or "scale":
                type = AnimationType.Scale; return true;
            case "ninguna" or "none":
                type = AnimationType.None; return true;
            default:
                return false;
        }
    }

    public static bool TryParseAnimationEasing(string? value, out AnimationEasing easing)
    {
        easing = AnimationEasing.EaseOutCubic;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "easeoutcubic":
                easing = AnimationEasing.EaseOutCubic; return true;
            case "easeoutquad":
                easing = AnimationEasing.EaseOutQuad; return true;
            case "linear" or "lineal":
                easing = AnimationEasing.Linear; return true;
            default:
                return false;
        }
    }

    public static bool TryParseLayout(string? value, out IslandLayoutMode layout)
    {
        layout = IslandLayoutMode.List;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "lista" or "list":
                layout = IslandLayoutMode.List; return true;
            case "rejilla" or "grid":
                layout = IslandLayoutMode.Grid; return true;
            case "iconosgrandes" or "iconos_grandes" or "largeicons":
                layout = IslandLayoutMode.LargeIcons; return true;
            case "solonombres" or "solo_nombres" or "namesonly":
                layout = IslandLayoutMode.NamesOnly; return true;
            default:
                return false;
        }
    }

    public static bool TryParseSortOrder(string? value, out IslandSortOrder sort)
    {
        sort = IslandSortOrder.Name;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "nombre" or "name":
                sort = IslandSortOrder.Name; return true;
            case "ultimouso" or "ultimo_uso" or "lastused":
                sort = IslandSortOrder.LastUsed; return true;
            case "fechamodificacion" or "fecha" or "modified":
                sort = IslandSortOrder.ModifiedDate; return true;
            case "tipo" or "type":
                sort = IslandSortOrder.Type; return true;
            case "tamano" or "tamaño" or "size":
                sort = IslandSortOrder.Size; return true;
            default:
                return false;
        }
    }

    public static bool TryParseGrouping(string? value, out IslandGroupingMode grouping)
    {
        grouping = IslandGroupingMode.None;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "ninguna" or "none":
                grouping = IslandGroupingMode.None; return true;
            case "tipo" or "type":
                grouping = IslandGroupingMode.Type; return true;
            default:
                return false;
        }
    }
}
