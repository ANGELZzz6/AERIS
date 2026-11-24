using System.IO.Enumeration;

namespace IslasEscritorio.Core;

public static class FilterMatcher
{
    public static IReadOnlyList<DesktopItem> FilterAndSort(
        IEnumerable<DesktopItem> allItems,
        IslandContentConfig content)
    {
        bool includeUser = content.Fuentes is null or { Count: 0 } ||
            content.Fuentes.Any(s => Normalize(s) is "escritorio usuario" or "usuario" or "user");
        bool includePublic = content.Fuentes is null or { Count: 0 } ||
            content.Fuentes.Any(s => Normalize(s) is "escritorio público" or "escritorio publico" or "público" or "publico" or "public");

        IslandFilterConfig filters = content.Filtros ?? new IslandFilterConfig();
        HashSet<DesktopItemKind> allowedKinds = ParseAllowedKinds(filters.Tipos);

        var filtered = new List<DesktopItem>();
        HashSet<string> assigned = new(content.ElementosAsignados ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (DesktopItem item in allItems)
        {
            // Si el usuario asigna manualmente contenido, esa asignación prevalece sobre los filtros automáticos.
            if (assigned.Count > 0)
            {
                if (assigned.Contains(item.FullPath)) filtered.Add(item);
                continue;
            }
            if (item.Source == DesktopSource.UserDesktop && !includeUser) continue;
            if (item.Source == DesktopSource.PublicDesktop && !includePublic) continue;
            if (!allowedKinds.Contains(item.Kind)) continue;

            if (MatchesAnyPattern(item.FileName, filters.Excluir))
                continue;

            // Las carpetas solo requieren coincidencia con "incluir" si hay filtros explícitos distintos de extensión o si incluyen "*".
            bool hasInclude = filters.Incluir is { Count: > 0 };
            if (hasInclude && !MatchesInclude(item, filters.Incluir))
                continue;

            filtered.Add(item);
        }

        ConfigValidator.TryParseSortOrder(content.Orden, out IslandSortOrder sortOrder);
        ConfigValidator.TryParseGrouping(content.Agrupacion, out IslandGroupingMode grouping);

        return SortItems(filtered, sortOrder, grouping);
    }

    private static bool MatchesInclude(DesktopItem item, List<string> patterns)
    {
        if (item.Kind == DesktopItemKind.Folder)
        {
            // Si el usuario incluyó "*" o un patrón sin extensión o coincidente con el nombre de la carpeta, se muestra.
            // Si solo puso extensiones tipo "*.docx" pero también permitió "carpeta" en tipos, permitimos carpetas si hay "*" o si coincide el nombre,
            // o si no hay filtros restrictivos. Para respetar filtros explícitos como {"tipos": ["archivo"], ...} o {"incluir": ["*.exe"]}:
            // si el usuario puso "carpeta" en tipos y no incluyó patrones de extensión exclusivamente, o coincide el patrón:
            bool onlyExtensionGlobs = patterns.All(p => p.StartsWith("*.", StringComparison.Ordinal));
            if (onlyExtensionGlobs && MatchesAnyPattern(item.FileName, patterns))
                return true;
            return MatchesAnyPattern(item.FileName, patterns);
        }

        return MatchesAnyPattern(item.FileName, patterns);
    }

    public static bool MatchesAnyPattern(string fileName, IReadOnlyList<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
            return false;

        foreach (string raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string pattern = raw.Trim();
            if (pattern == "*" || pattern == "*.*")
                return true;

            if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static List<DesktopItem> SortItems(
        List<DesktopItem> items,
        IslandSortOrder sortOrder,
        IslandGroupingMode grouping)
    {
        IOrderedEnumerable<DesktopItem> ordered = grouping == IslandGroupingMode.Type
            ? items.OrderBy(i => KindRank(i.Kind))
            : items.OrderBy(_ => 0);

        ordered = sortOrder switch
        {
            IslandSortOrder.LastUsed => ordered
                .ThenByDescending(i => i.LastAccessTimeUtc)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            IslandSortOrder.ModifiedDate => ordered
                .ThenByDescending(i => i.LastWriteTimeUtc)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            IslandSortOrder.Type => ordered
                .ThenBy(i => KindRank(i.Kind))
                .ThenBy(i => i.Extension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            IslandSortOrder.Size => ordered
                .ThenByDescending(i => i.SizeBytes)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => ordered
                .ThenBy(i => i.Kind == DesktopItemKind.Folder ? 0 : 1)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        return ordered.ToList();
    }

    private static int KindRank(DesktopItemKind kind) => kind switch
    {
        DesktopItemKind.Folder => 0,
        DesktopItemKind.Shortcut => 1,
        _ => 2
    };

    private static HashSet<DesktopItemKind> ParseAllowedKinds(IReadOnlyList<string>? types)
    {
        var set = new HashSet<DesktopItemKind>();
        if (types is null || types.Count == 0)
        {
            set.Add(DesktopItemKind.File);
            set.Add(DesktopItemKind.Folder);
            set.Add(DesktopItemKind.Shortcut);
            return set;
        }

        foreach (string raw in types)
        {
            switch (Normalize(raw))
            {
                case "archivo" or "archivos" or "file":
                    set.Add(DesktopItemKind.File);
                    break;
                case "carpeta" or "carpetas" or "folder":
                    set.Add(DesktopItemKind.Folder);
                    break;
                case "acceso directo" or "accesos directos" or "shortcut" or "lnk":
                    set.Add(DesktopItemKind.Shortcut);
                    break;
            }
        }
        return set;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;
}
