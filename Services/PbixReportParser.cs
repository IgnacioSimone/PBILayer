using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PBIExplorer.Models;

namespace PBIExplorer.Services;

/// <summary>
/// Un .pbix es un .zip. El reporte puede venir en dos formatos:
///   - PBIR (moderno): cada visual vive en su propio
///     Report/definition/pages/*/visuals/*/visual.json.
///   - Layout clásico: todo el reporte es un único archivo
///     Report/Layout, codificado en UTF-16LE, con un JSON por sección
///     y cada visual guardando su configuración en un campo "config"
///     que es, a su vez, un JSON serializado como string.
/// Esta clase detecta cuál de los dos hay y lee solo esa estructura
/// (qué visual usa qué medida o columna) directamente en memoria —
/// nunca toca DataModel (donde viven los datos reales) ni escribe nada a disco.
/// </summary>
public class PbixReportParser
{
    private static readonly HashSet<string> IgnoredVisualTypes = new()
    {
        "image", "textbox", "shape", "actionButton", "basicShape"
    };

    // Un solo visual.json o el Report/Layout entero jamás llegan a pesar esto en un
    // reporte real; es un tope de seguridad contra un .pbix armado a mano (zip-bomb)
    // que alguien podría elegir a través de "Abrir .pbix" — no solo el suyo propio.
    private const long MaxEntryBytes = 20_000_000;
    private const long MaxCompressionRatio = 100;

    /// <summary>Descarta una entrada si su tamaño declarado (o la relación entre
    /// tamaño comprimido/descomprimido) es la de una zip-bomb, sin necesidad de
    /// descomprimirla primero.</summary>
    private static bool IsSafeEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes) return false;
        if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio) return false;
        return true;
    }

    public List<VisualFieldUsage> ParseVisuals(string pbixPath)
    {
        using var fileStream = File.OpenRead(pbixPath);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);

        var pbirResult = ParsePbir(zip);
        if (pbirResult.Count > 0) return pbirResult;

        return ParseLegacyLayout(zip);
    }

    /// <summary>
    /// Filtros configurados a nivel reporte (todas las páginas), por página, y por
    /// visual — la sección "Filtros" del panel de Power BI Desktop. El esquema real de
    /// "filterConfig"/"filters" no está documentado oficialmente y cambió entre
    /// versiones de Power BI Desktop, así que esto es best-effort: interpreta bien el
    /// caso más común (filtro categórico con una lista de valores incluidos/excluidos)
    /// y para el resto (TopN, fecha relativa, avanzado) informa el tipo sin decodificar
    /// la condición exacta. Nunca falla silenciosamente el resto del reporte por un
    /// filtro con forma inesperada.
    /// </summary>
    public List<FilterInfo> ParseFilters(string pbixPath)
    {
        using var fileStream = File.OpenRead(pbixPath);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);

        var pbirResult = ParsePbirFilters(zip);
        if (pbirResult.Count > 0) return pbirResult;

        return ParseLegacyFilters(zip);
    }

    // =====================================================================
    // Formato PBIR (moderno)
    // =====================================================================

    private static List<VisualFieldUsage> ParsePbir(ZipArchive zip)
    {
        var result = new List<VisualFieldUsage>();

        var pageEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("Report/definition/pages/", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith("/page.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var pageEntry in pageEntries)
        {
            var pageDir = pageEntry.FullName[..pageEntry.FullName.LastIndexOf('/')];

            // Una página con un page.json fuera de lo común no debe tirar abajo el resto
            // del reporte: si no se puede leer el nombre, se sigue con las demás páginas.
            string pageName;
            try { pageName = IsSafeEntry(pageEntry) ? ReadPbirPageName(pageEntry) : pageEntry.Name; }
            catch (JsonException) { pageName = pageEntry.Name; }

            var visualEntries = zip.Entries
                .Where(e => e.FullName.StartsWith(pageDir + "/visuals/", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith("/visual.json", StringComparison.OrdinalIgnoreCase));

            foreach (var visualEntry in visualEntries)
            {
                if (!IsSafeEntry(visualEntry)) continue;

                try
                {
                    using var visualStream = visualEntry.Open();
                    using var doc = JsonDocument.Parse(visualStream);
                    if (!doc.RootElement.TryGetProperty("visual", out var visual)) continue;

                    var visualType = visual.TryGetProperty("visualType", out var vt) ? vt.GetString() ?? "?" : "?";
                    if (IgnoredVisualTypes.Contains(visualType)) continue;

                    var title = ReadPbirTitle(visual);

                    var fields = new List<(string kind, string table, string field)>();
                    if (visual.TryGetProperty("query", out var query))
                    {
                        CollectFields(query, fields, null);
                    }

                    AddVisualRows(result, pageName, visualType, title, fields);
                }
                catch (JsonException)
                {
                    // Un visual.json con un esquema fuera de lo común (ej. un visual
                    // personalizado poco frecuente) no debe perder los demás visuales
                    // de páginas que sí se leen bien.
                }
            }
        }

        return result;
    }

    private static string ReadPbirPageName(ZipArchiveEntry pageEntry)
    {
        using var pageStream = pageEntry.Open();
        using var doc = JsonDocument.Parse(pageStream);
        if (doc.RootElement.TryGetProperty("displayName", out var dn)) return dn.GetString() ?? pageEntry.Name;
        if (doc.RootElement.TryGetProperty("name", out var nm)) return nm.GetString() ?? pageEntry.Name;
        return pageEntry.Name;
    }

    private static List<FilterInfo> ParsePbirFilters(ZipArchive zip)
    {
        var result = new List<FilterInfo>();

        var reportEntry = zip.GetEntry("Report/definition/report.json");
        if (reportEntry is not null && IsSafeEntry(reportEntry))
        {
            try
            {
                using var stream = reportEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                CollectPbirFilterConfig(doc.RootElement, result, "Reporte", "", "", "");
            }
            catch (JsonException) { /* sin filtros de reporte legibles, no es fatal */ }
        }

        var pageEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("Report/definition/pages/", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith("/page.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var pageEntry in pageEntries)
        {
            if (!IsSafeEntry(pageEntry)) continue;
            var pageDir = pageEntry.FullName[..pageEntry.FullName.LastIndexOf('/')];

            string pageName;
            try
            {
                using var pageStream = pageEntry.Open();
                using var pageDoc = JsonDocument.Parse(pageStream);
                pageName = pageDoc.RootElement.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? pageEntry.Name
                    : pageDoc.RootElement.TryGetProperty("name", out var nm) ? nm.GetString() ?? pageEntry.Name : pageEntry.Name;

                CollectPbirFilterConfig(pageDoc.RootElement, result, "Página", pageName, "", "");
            }
            catch (JsonException) { continue; }

            var visualEntries = zip.Entries
                .Where(e => e.FullName.StartsWith(pageDir + "/visuals/", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith("/visual.json", StringComparison.OrdinalIgnoreCase));

            foreach (var visualEntry in visualEntries)
            {
                if (!IsSafeEntry(visualEntry)) continue;
                try
                {
                    using var visualStream = visualEntry.Open();
                    using var visualDoc = JsonDocument.Parse(visualStream);
                    var root = visualDoc.RootElement;
                    if (!root.TryGetProperty("visual", out var visual)) continue;

                    var visualType = visual.TryGetProperty("visualType", out var vt) ? vt.GetString() ?? "?" : "?";
                    if (IgnoredVisualTypes.Contains(visualType)) continue;
                    var title = ReadPbirTitle(visual);

                    CollectPbirFilterConfig(root, result, "Visual", pageName, title ?? "", visualType);
                }
                catch (JsonException) { /* un visual con forma rara no debe perder los demás */ }
            }
        }

        return result;
    }

    private static void CollectPbirFilterConfig(JsonElement root, List<FilterInfo> result,
        string scope, string page, string visualTitle, string visualType)
    {
        if (!root.TryGetProperty("filterConfig", out var filterConfig)) return;
        if (!filterConfig.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array) return;

        foreach (var filter in filters.EnumerateArray())
        {
            try { result.Add(BuildFilterInfo(filter, scope, page, visualTitle, visualType, aliasMap: null)); }
            catch (JsonException) { /* un filtro individual con forma rara no debe perder los demás */ }
        }
    }

    private static string? ReadPbirTitle(JsonElement visual)
    {
        if (!visual.TryGetProperty("visualContainerObjects", out var vco) ||
            !vco.TryGetProperty("title", out var titleArr) ||
            titleArr.ValueKind != JsonValueKind.Array) return null;

        string? title = null;
        foreach (var t in titleArr.EnumerateArray())
        {
            if (t.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("text", out var text) &&
                text.TryGetProperty("expr", out var expr) &&
                expr.TryGetProperty("Literal", out var lit) &&
                lit.TryGetProperty("Value", out var val))
            {
                title = val.GetString()?.Trim('\'');
            }
        }
        return title;
    }

    // =====================================================================
    // Formato Layout clásico (Report/Layout, UTF-16LE, config anidado como string)
    // =====================================================================

    private static List<VisualFieldUsage> ParseLegacyLayout(ZipArchive zip)
    {
        var result = new List<VisualFieldUsage>();

        var layoutEntry = zip.GetEntry("Report/Layout");
        if (layoutEntry is null || !IsSafeEntry(layoutEntry)) return result;

        using var stream = layoutEntry.Open();
        using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("sections", out var sections)) return result;

        foreach (var section in sections.EnumerateArray())
        {
            var pageName = section.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "?"
                : section.TryGetProperty("name", out var nm) ? nm.GetString() ?? "?" : "?";

            if (!section.TryGetProperty("visualContainers", out var containers)) continue;

            foreach (var container in containers.EnumerateArray())
            {
                if (!container.TryGetProperty("config", out var configRaw) || configRaw.ValueKind != JsonValueKind.String)
                    continue;

                var configText = configRaw.GetString();
                if (string.IsNullOrEmpty(configText)) continue;

                try
                {
                    using var configDoc = JsonDocument.Parse(configText);
                    var config = configDoc.RootElement;

                    if (!config.TryGetProperty("singleVisual", out var singleVisual)) continue; // grupo, no un visual real

                    var visualType = singleVisual.TryGetProperty("visualType", out var vt) ? vt.GetString() ?? "?" : "?";
                    if (IgnoredVisualTypes.Contains(visualType)) continue;

                    var title = ReadLegacyTitle(singleVisual);
                    var aliasMap = BuildAliasMap(singleVisual);

                    var fields = new List<(string kind, string table, string field)>();
                    if (singleVisual.TryGetProperty("prototypeQuery", out var prototypeQuery) &&
                        prototypeQuery.TryGetProperty("Select", out var select))
                    {
                        CollectFields(select, fields, aliasMap);
                    }

                    AddVisualRows(result, pageName, visualType, title, fields);
                }
                catch (JsonException)
                {
                    // Un "config" con un esquema fuera de lo común no debe perder los
                    // demás visuales de esta misma página o de las otras páginas.
                }
            }
        }

        return result;
    }

    private static List<FilterInfo> ParseLegacyFilters(ZipArchive zip)
    {
        var result = new List<FilterInfo>();

        var layoutEntry = zip.GetEntry("Report/Layout");
        if (layoutEntry is null || !IsSafeEntry(layoutEntry)) return result;

        using var stream = layoutEntry.Open();
        using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        CollectLegacyFilterString(root, result, "Reporte", "", "", "");

        if (!root.TryGetProperty("sections", out var sections)) return result;

        foreach (var section in sections.EnumerateArray())
        {
            var pageName = section.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "?"
                : section.TryGetProperty("name", out var nm) ? nm.GetString() ?? "?" : "?";

            CollectLegacyFilterString(section, result, "Página", pageName, "", "");

            if (!section.TryGetProperty("visualContainers", out var containers)) continue;

            foreach (var container in containers.EnumerateArray())
            {
                if (!container.TryGetProperty("config", out var configRaw) || configRaw.ValueKind != JsonValueKind.String)
                    continue;
                var configText = configRaw.GetString();
                if (string.IsNullOrEmpty(configText)) continue;

                string? title = null;
                var visualType = "?";
                try
                {
                    using var configDoc = JsonDocument.Parse(configText);
                    if (!configDoc.RootElement.TryGetProperty("singleVisual", out var singleVisual)) continue;

                    visualType = singleVisual.TryGetProperty("visualType", out var vt) ? vt.GetString() ?? "?" : "?";
                    if (IgnoredVisualTypes.Contains(visualType)) continue;
                    title = ReadLegacyTitle(singleVisual);
                }
                catch (JsonException) { continue; }

                CollectLegacyFilterString(container, result, "Visual", pageName, title ?? "", visualType);
            }
        }

        return result;
    }

    /// <summary>El Layout clásico guarda los filtros de cada nivel (reporte/página/visual)
    /// como un array JSON serializado dentro de un string, igual que "config".</summary>
    private static void CollectLegacyFilterString(JsonElement owner, List<FilterInfo> result,
        string scope, string page, string visualTitle, string visualType)
    {
        if (!owner.TryGetProperty("filters", out var filtersRaw) || filtersRaw.ValueKind != JsonValueKind.String) return;
        var filtersText = filtersRaw.GetString();
        if (string.IsNullOrEmpty(filtersText)) return;

        try
        {
            using var filtersDoc = JsonDocument.Parse(filtersText);
            if (filtersDoc.RootElement.ValueKind != JsonValueKind.Array) return;

            foreach (var filter in filtersDoc.RootElement.EnumerateArray())
            {
                try { result.Add(BuildFilterInfo(filter, scope, page, visualTitle, visualType, aliasMap: null)); }
                catch (JsonException) { /* un filtro individual con forma rara no debe perder los demás */ }
            }
        }
        catch (JsonException) { /* filtros con forma rara en este nivel puntual, no es fatal */ }
    }

    private static string? ReadLegacyTitle(JsonElement singleVisual)
    {
        if (!singleVisual.TryGetProperty("vcObjects", out var vco) &&
            !singleVisual.TryGetProperty("objects", out vco)) return null;

        if (!vco.TryGetProperty("title", out var titleArr) || titleArr.ValueKind != JsonValueKind.Array) return null;

        foreach (var t in titleArr.EnumerateArray())
        {
            if (t.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("text", out var text) &&
                text.TryGetProperty("expr", out var expr) &&
                expr.TryGetProperty("Literal", out var lit) &&
                lit.TryGetProperty("Value", out var val))
            {
                return val.GetString()?.Trim('\'');
            }
        }
        return null;
    }

    /// <summary>El Layout clásico referencia tablas por un alias corto ("a","b"...) definido en prototypeQuery.From.</summary>
    private static Dictionary<string, string> BuildAliasMap(JsonElement singleVisual)
    {
        var map = new Dictionary<string, string>();
        if (!singleVisual.TryGetProperty("prototypeQuery", out var prototypeQuery) ||
            !prototypeQuery.TryGetProperty("From", out var from)) return map;

        foreach (var item in from.EnumerateArray())
        {
            var alias = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var entity = item.TryGetProperty("Entity", out var e) ? e.GetString() : null;
            if (alias is not null && entity is not null) map[alias] = entity;
        }
        return map;
    }

    // =====================================================================
    // Compartido
    // =====================================================================

    private static void AddVisualRows(List<VisualFieldUsage> result, string pageName, string visualType,
        string? title, List<(string kind, string table, string field)> fields)
    {
        if (fields.Count == 0)
        {
            result.Add(new VisualFieldUsage
            {
                Page = pageName, VisualType = visualType, Title = title ?? "",
                FieldKind = "", Table = "", Field = "(sin campos de datos)"
            });
            return;
        }

        foreach (var (kind, table, field) in fields.Distinct())
        {
            result.Add(new VisualFieldUsage
            {
                Page = pageName, VisualType = visualType, Title = title ?? "",
                FieldKind = kind, Table = table, Field = field
            });
        }
    }

    private static void CollectFields(JsonElement element, List<(string kind, string table, string field)> output,
        Dictionary<string, string>? aliasMap)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("Measure", out var measure))
                    output.Add(("Medida", ExtractTable(measure, aliasMap), ExtractProperty(measure)));
                if (element.TryGetProperty("Column", out var column))
                    output.Add(("Columna", ExtractTable(column, aliasMap), ExtractProperty(column)));
                foreach (var prop in element.EnumerateObject())
                    CollectFields(prop.Value, output, aliasMap);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectFields(item, output, aliasMap);
                break;
        }
    }

    private static string ExtractTable(JsonElement fieldObj, Dictionary<string, string>? aliasMap)
    {
        if (!fieldObj.TryGetProperty("Expression", out var expr) ||
            !expr.TryGetProperty("SourceRef", out var sourceRef)) return "?";

        // PBIR: {"SourceRef":{"Entity":"Tabla"}}
        if (sourceRef.TryGetProperty("Entity", out var entity)) return entity.GetString() ?? "?";

        // Layout clásico: {"SourceRef":{"Source":"a"}} — "a" se resuelve contra prototypeQuery.From
        if (sourceRef.TryGetProperty("Source", out var source))
        {
            var alias = source.GetString() ?? "?";
            return aliasMap is not null && aliasMap.TryGetValue(alias, out var table) ? table : alias;
        }

        return "?";
    }

    private static string ExtractProperty(JsonElement fieldObj) =>
        fieldObj.TryGetProperty("Property", out var propEl) ? propEl.GetString() ?? "?" : "?";

    // =====================================================================
    // Filtros — compartido entre PBIR y Layout clásico. El objeto "filter" de Power BI
    // (nombre de la propiedad del campo: "field" en PBIR, "expression" en Layout clásico)
    // no tiene esquema documentado oficialmente; esto prueba varios nombres conocidos y
    // se degrada con gracia (campo "?", condición sin interpretar) en vez de fallar.
    // =====================================================================

    private static FilterInfo BuildFilterInfo(JsonElement filter, string scope, string page,
        string visualTitle, string visualType, Dictionary<string, string>? aliasMap)
    {
        var name = filter.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var filterType = filter.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        var hasField = filter.TryGetProperty("field", out var fieldEl) ||
                       filter.TryGetProperty("expression", out fieldEl) ||
                       filter.TryGetProperty("Expression", out fieldEl);

        var fieldKind = "";
        var table = "?";
        var field = "?";
        if (hasField)
        {
            if (fieldEl.TryGetProperty("Measure", out var measure))
            {
                fieldKind = "Medida";
                table = ExtractTable(measure, aliasMap);
                field = ExtractProperty(measure);
            }
            else if (fieldEl.TryGetProperty("Column", out var column))
            {
                fieldKind = "Columna";
                table = ExtractTable(column, aliasMap);
                field = ExtractProperty(column);
            }
            else if (fieldEl.TryGetProperty("HierarchyLevel", out var hierarchyLevel))
            {
                fieldKind = "Jerarquía";
                table = ExtractHierarchyTable(hierarchyLevel, aliasMap);
                field = ExtractHierarchyLevel(hierarchyLevel);
            }
        }

        return new FilterInfo
        {
            Scope = scope,
            Page = page,
            VisualTitle = visualTitle,
            VisualType = visualType,
            Name = name,
            FieldKind = fieldKind,
            Table = table,
            Field = field,
            FilterType = string.IsNullOrEmpty(filterType) ? "?" : filterType,
            Summary = SummarizeCondition(filter, filterType)
        };
    }

    private static string ExtractHierarchyTable(JsonElement hierarchyLevel, Dictionary<string, string>? aliasMap)
    {
        if (hierarchyLevel.TryGetProperty("Expression", out var expr) &&
            expr.TryGetProperty("Hierarchy", out var hierarchy) &&
            hierarchy.TryGetProperty("Expression", out var hExpr) &&
            hExpr.TryGetProperty("SourceRef", out var sourceRef))
        {
            if (sourceRef.TryGetProperty("Entity", out var entity)) return entity.GetString() ?? "?";
            if (sourceRef.TryGetProperty("Source", out var source))
            {
                var alias = source.GetString() ?? "?";
                return aliasMap is not null && aliasMap.TryGetValue(alias, out var tbl) ? tbl : alias;
            }
        }
        return "?";
    }

    private static string ExtractHierarchyLevel(JsonElement hierarchyLevel) =>
        hierarchyLevel.TryGetProperty("Level", out var lvl) ? lvl.GetString() ?? "?" : "?";

    /// <summary>
    /// Best-effort: interpreta el caso más común (filtro categórico con una lista de
    /// valores incluidos o excluidos vía "In"/"Not"). Para TopN, fecha relativa, filtros
    /// avanzados con AND/OR, etc. — cuyo esquema varía bastante y no vale la pena adivinar
    /// — devuelve solo el tipo, para no mostrar una interpretación potencialmente incorrecta.
    /// </summary>
    private static string SummarizeCondition(JsonElement filter, string filterType)
    {
        var typeLabel = string.IsNullOrEmpty(filterType) ? "tipo desconocido" : filterType;

        if (!filter.TryGetProperty("filter", out var cond) ||
            !cond.TryGetProperty("Where", out var where) ||
            where.ValueKind != JsonValueKind.Array || where.GetArrayLength() == 0)
            return $"(tipo: {typeLabel})";

        var values = new List<string>();
        var isExclude = false;

        foreach (var clause in where.EnumerateArray())
        {
            if (!clause.TryGetProperty("Condition", out var condition)) continue;

            var target = condition;
            if (condition.TryGetProperty("Not", out var notEl) && notEl.TryGetProperty("Expression", out var innerNot))
            {
                isExclude = true;
                target = innerNot;
            }

            if (!target.TryGetProperty("In", out var inEl) || !inEl.TryGetProperty("Values", out var valuesArr) ||
                valuesArr.ValueKind != JsonValueKind.Array) continue;

            foreach (var row in valuesArr.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                foreach (var cell in row.EnumerateArray())
                {
                    if (cell.TryGetProperty("Literal", out var lit) && lit.TryGetProperty("Value", out var val))
                        values.Add(FormatLiteral(val.GetString() ?? ""));
                }
            }
        }

        if (values.Count == 0) return $"(tipo: {typeLabel} — condición no interpretada automáticamente)";

        const int maxShown = 8;
        var text = string.Join(", ", values.Take(maxShown));
        if (values.Count > maxShown) text += $" y {values.Count - maxShown} más";
        return (isExclude ? "excluye: " : "incluye: ") + text;
    }

    /// <summary>Power BI codifica los literales como texto: strings entre comillas simples
    /// ('Argentina'), números con un sufijo de tipo (123L, 45.6D, 10M). Esto solo pela esas
    /// dos envolturas conocidas; cualquier otra forma se deja tal cual viene.</summary>
    private static string FormatLiteral(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'') return raw[1..^1];
        if (raw.Length > 1 && "LMD".Contains(raw[^1]) && char.IsDigit(raw[^2])) return raw[..^1];
        return raw;
    }
}
