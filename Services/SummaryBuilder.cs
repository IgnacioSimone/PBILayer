using System.Text;
using System.Text.RegularExpressions;
using PBIExplorer.Infrastructure;
using PBIExplorer.Models;

namespace PBIExplorer.Services;

/// <summary>
/// Arma una descripción en español de qué muestra el tablero, basada 100% en
/// reglas sobre la metadata ya leída (nombres de páginas, visuales, medidas,
/// columnas y relaciones). No usa IA ni sale a internet: es puro análisis
/// heurístico de texto y conteos, igual que el resto de la app.
/// </summary>
public static class SummaryBuilder
{
    private static readonly Regex CustomVisualGuidSuffix = new(@"[0-9A-Fa-f]{16,}$", RegexOptions.Compiled);

    // Tipo de visual interno de Power BI → clave de texto (Vis_*).
    private static readonly Dictionary<string, string> FriendlyVisualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["card"] = "Vis_card", ["cardVisual"] = "Vis_card", ["multiRowCard"] = "Vis_multiRowCard",
        ["slicer"] = "Vis_slicer", ["tableEx"] = "Vis_table", ["pivotTable"] = "Vis_pivotTable",
        ["matrix"] = "Vis_matrix", ["columnChart"] = "Vis_columnChart", ["clusteredColumnChart"] = "Vis_columnChart",
        ["barChart"] = "Vis_barChart", ["clusteredBarChart"] = "Vis_barChart",
        ["lineChart"] = "Vis_lineChart", ["areaChart"] = "Vis_areaChart",
        ["pieChart"] = "Vis_pieChart", ["donutChart"] = "Vis_donutChart",
        ["funnel"] = "Vis_funnel", ["gauge"] = "Vis_gauge", ["scatterChart"] = "Vis_scatterChart",
        ["treemap"] = "Vis_treemap", ["map"] = "Vis_map", ["filledMap"] = "Vis_filledMap",
        ["pageNavigator"] = "Vis_pageNavigator", ["bookmarkNavigator"] = "Vis_bookmarkNavigator",
        ["textbox"] = "Vis_textbox", ["image"] = "Vis_image", ["shape"] = "Vis_shape", ["actionButton"] = "Vis_actionButton",
        ["waterfallChart"] = "Vis_waterfallChart", ["ribbonChart"] = "Vis_ribbonChart", ["keyDriversVisual"] = "Vis_keyDriversVisual",
        ["decompositionTreeVisual"] = "Vis_decompositionTreeVisual", ["qnaVisual"] = "Vis_qnaVisual"
    };
    public static DashboardSummary Build(ModelSnapshot model, List<VisualFieldUsage> visuals)
    {
        var summary = new DashboardSummary();

        BuildOverview(model, visuals, summary);
        BuildHighlights(model, summary);
        BuildPages(visuals, summary);

        return summary;
    }

    private static void BuildOverview(ModelSnapshot model, List<VisualFieldUsage> visuals, DashboardSummary summary)
    {
        var sb = new StringBuilder();
        var hasModel = model.Tables.Count > 0 || model.Measures.Count > 0;
        var hasVisuals = visuals.Count > 0;

        if (hasModel)
        {
            var visibleTables = model.Tables.Count(t => !t.IsHidden);
            sb.Append(Loc.F("Summary_Model", model.Tables.Count, visibleTables, model.Measures.Count, model.Relationships.Count));
        }

        if (hasVisuals)
        {
            var pages = visuals.Select(v => v.Page).Distinct().Count();
            var visualCount = visuals.Select(v => (v.Page, v.VisualType, v.Title)).Distinct().Count();
            sb.Append(Loc.F(pages == 1 ? "Summary_Report_One" : "Summary_Report_Many", pages, visualCount));
        }

        if (!hasModel && !hasVisuals)
        {
            sb.Append(Loc.T("Summary_NothingLoaded"));
        }

        summary.OverviewText = sb.ToString().Trim();
    }

    private static void BuildHighlights(ModelSnapshot model, DashboardSummary summary)
    {
        if (model.Tables.Count == 0) return;

        var busiest = model.Tables.OrderByDescending(t => t.MeasureCount).FirstOrDefault(t => t.MeasureCount > 0);
        if (busiest is not null)
            summary.Highlights.Add(Loc.F("Summary_BusiestTable", busiest.Name, busiest.MeasureCount));

        var richest = model.Tables.OrderByDescending(t => t.ColumnCount).FirstOrDefault(t => t.ColumnCount > 0);
        if (richest is not null && richest.Name != busiest?.Name)
            summary.Highlights.Add(Loc.F("Summary_RichestTable", richest.Name, richest.ColumnCount));

        if (model.Relationships.Count > 0)
        {
            var connections = model.Relationships
                .SelectMany(r => new[] { r.FromTable, r.ToTable })
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (connections is not null)
                summary.Highlights.Add(Loc.F("Summary_MostConnected", connections.Key, connections.Count()));

            var referencedTables = model.Relationships.SelectMany(r => new[] { r.FromTable, r.ToTable }).ToHashSet();
            var orphanCount = model.Tables.Count(t => !t.IsHidden && !referencedTables.Contains(t.Name));
            if (orphanCount > 0)
                summary.Highlights.Add(Loc.F(orphanCount == 1 ? "Summary_Orphans_One" : "Summary_Orphans_Many", orphanCount));
        }

        var hiddenMeasures = model.Measures.Count(m => m.IsHidden);
        if (hiddenMeasures > 0)
            summary.Highlights.Add(Loc.F(hiddenMeasures == 1 ? "Summary_Hidden_One" : "Summary_Hidden_Many", hiddenMeasures));
    }

    private static void BuildPages(List<VisualFieldUsage> visuals, DashboardSummary summary)
    {
        foreach (var group in visuals.GroupBy(v => v.Page))
        {
            var rows = group.ToList();
            var distinctVisuals = rows.Select(r => (r.VisualType, r.Title)).Distinct().ToList();

            var page = new PageSummary
            {
                PageName = group.Key,
                VisualCount = distinctVisuals.Count
            };

            var measures = rows.Where(r => r.FieldKind == "Medida").Select(r => r.Field).Distinct().Take(6).ToList();
            var slicerColumns = rows.Where(r => r.FieldKind == "Columna" && r.VisualType.Equals("slicer", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Field).Distinct().Take(8).ToList();
            var tables = rows.Select(r => r.Table).Where(t => t != "?" && t != "").Distinct().Take(6).ToList();

            page.KeyMeasures.AddRange(measures);
            page.FilterColumns.AddRange(slicerColumns);
            page.SourceTables.AddRange(tables);

            var typeBreakdown = distinctVisuals
                .Select(v => FriendlyName(v.VisualType))
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Count() > 1 ? Loc.F("Summary_TypeCount", g.Count(), g.Key) : g.Key)
                .ToList();
            page.VisualTypeBreakdown = string.Join(", ", typeBreakdown);

            page.Description = BuildPageSentence(page, typeBreakdown);

            summary.Pages.Add(page);
        }
    }

    private static string BuildPageSentence(PageSummary page, List<string> typeBreakdown)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.F(page.VisualCount == 1 ? "Summary_Page_One" : "Summary_Page_Many", page.VisualCount));
        if (typeBreakdown.Count > 0) sb.Append($" ({string.Join(", ", typeBreakdown)})");
        sb.Append('.');

        if (page.KeyMeasures.Count > 0)
            sb.Append(Loc.F("Summary_PageMeasures", string.Join(", ", page.KeyMeasures)));

        if (page.FilterColumns.Count > 0)
            sb.Append(Loc.F("Summary_PageFilters", string.Join(", ", page.FilterColumns)));

        if (page.SourceTables.Count > 0)
            sb.Append(Loc.F("Summary_PageSources", string.Join(", ", page.SourceTables)));

        return sb.ToString();
    }

    private static string FriendlyName(string visualType)
    {
        if (FriendlyVisualKeys.TryGetValue(visualType, out var friendlyKey)) return Loc.T(friendlyKey);

        // Los visuales custom de Power BI traen un GUID pegado al nombre (ej. sccWorkspaceBarChart1F7D...).
        var stripped = CustomVisualGuidSuffix.Replace(visualType, "");
        return string.IsNullOrWhiteSpace(stripped) ? Loc.T("Summary_CustomVisual") : Loc.F("Summary_CustomVisualNamed", stripped);
    }

}
