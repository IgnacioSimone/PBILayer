using System.IO;
using System.Text;
using PBIExplorer.Infrastructure;
using PBIExplorer.Models;

namespace PBIExplorer.Services;

/// <summary>
/// Vuelca toda la metadata leída (modelo + reporte) a un único archivo Markdown,
/// pensado para pegarlo o adjuntarlo como contexto en un chat con un asistente de IA.
/// Markdown es el formato que mejor parsean los LLM: texto plano, sin XML ni
/// binarios de por medio, con tablas y bloques de código nativos. No contiene
/// datos reales — solo estructura (tablas, DAX, relaciones, visuales y campos).
/// </summary>
public static class AiContextExporter
{
    public static void Export(
        string path,
        string sourceName,
        ModelSnapshot model,
        List<VisualFieldUsage> visuals,
        DashboardSummary summary,
        List<ObjectUsage>? usage = null,
        List<FilterInfo>? filters = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {Loc.F("Ai_Title", sourceName)}");
        sb.AppendLine();
        sb.AppendLine(Loc.T("Ai_Intro"));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(summary.OverviewText))
        {
            sb.AppendLine($"## {Loc.T("Export_Summary")}");
            sb.AppendLine();
            sb.AppendLine(summary.OverviewText);
            sb.AppendLine();

            if (summary.Highlights.Count > 0)
            {
                sb.AppendLine($"### {Loc.T("Ai_Highlights")}");
                sb.AppendLine();
                foreach (var h in summary.Highlights) sb.AppendLine($"- {h}");
                sb.AppendLine();
            }
        }

        if (model.Tables.Count > 0)
        {
            sb.AppendLine($"## {Loc.T("Ai_DataModel")}");
            sb.AppendLine();
            sb.AppendLine($"### {Loc.T("Ai_Tables")}");
            sb.AppendLine();
            sb.AppendLine(Loc.T("Ai_TablesHeader"));
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var t in model.Tables)
                sb.AppendLine($"| {Esc(t.Name)} | {t.ColumnCount} | {t.MeasureCount} | {(t.IsHidden ? Loc.T("Ai_Yes") : Loc.T("Ai_No"))} | {Esc(SensitiveTextGuard.Mask(t.Description))} |");
            sb.AppendLine();

            if (model.Columns.Count > 0)
            {
                sb.AppendLine($"### {Loc.T("Ai_ColumnsByTable")}");
                sb.AppendLine();
                foreach (var tableGroup in model.Columns.GroupBy(c => c.Table))
                {
                    sb.AppendLine($"**{tableGroup.Key}**");
                    sb.AppendLine();
                    sb.AppendLine(Loc.T("Ai_ColumnsHeader"));
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var c in tableGroup)
                        sb.AppendLine($"| {Esc(c.Name)} | {Esc(c.DataTypeText)} | {Esc(c.ColumnTypeText)} | {(c.IsHidden ? Loc.T("Ai_Yes") : Loc.T("Ai_No"))} |");
                    sb.AppendLine();

                    var calculated = tableGroup.Where(c => !string.IsNullOrWhiteSpace(c.Expression)).ToList();
                    foreach (var c in calculated)
                    {
                        sb.AppendLine(Loc.F("Ai_CalcColumn", tableGroup.Key, c.Name));
                        sb.AppendLine("```dax");
                        sb.AppendLine(SensitiveTextGuard.Mask(c.Expression));
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }
                }
            }

            if (model.Measures.Count > 0)
            {
                sb.AppendLine($"### {Loc.T("Ai_Measures")}");
                sb.AppendLine();
                foreach (var tableGroup in model.Measures.GroupBy(m => m.Table))
                {
                    sb.AppendLine($"**{tableGroup.Key}**");
                    sb.AppendLine();
                    foreach (var m in tableGroup)
                    {
                        var folder = string.IsNullOrWhiteSpace(m.DisplayFolder) ? "" : Loc.F("Ai_Folder", m.DisplayFolder);
                        var hidden = m.IsHidden ? Loc.T("Ai_HiddenTag") : "";
                        sb.AppendLine($"- `{m.Name}`{folder}{hidden}");
                        sb.AppendLine("  ```dax");
                        foreach (var line in SensitiveTextGuard.Mask(m.Expression).Replace("\r\n", "\n").Split('\n'))
                            sb.AppendLine($"  {line}");
                        sb.AppendLine("  ```");
                    }
                    sb.AppendLine();
                }
            }

            if (model.Relationships.Count > 0)
            {
                sb.AppendLine($"### {Loc.T("Ai_Relationships")}");
                sb.AppendLine();
                sb.AppendLine(Loc.T("Ai_RelationshipsHeader"));
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var r in model.Relationships)
                    sb.AppendLine($"| {Esc(r.FromTable)}.{Esc(r.FromColumn)} | {Esc(r.ToTable)}.{Esc(r.ToColumn)} | {r.CardinalityText} | {r.CrossFilterText} | {(r.IsActive ? Loc.T("Ai_Yes") : Loc.T("Ai_No"))} |");
                sb.AppendLine();
            }

            if (model.PowerQueries.Count > 0)
            {
                sb.AppendLine("### Power Query (M)");
                sb.AppendLine();
                foreach (var pq in model.PowerQueries)
                {
                    sb.AppendLine($"**{pq.KindText}: {pq.Name}**");
                    if (!string.IsNullOrWhiteSpace(pq.Description)) sb.AppendLine($"_{Esc(SensitiveTextGuard.Mask(pq.Description))}_");
                    sb.AppendLine("```m");
                    sb.AppendLine(SensitiveTextGuard.Mask(pq.MCode));
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        if (visuals.Count > 0)
        {
            sb.AppendLine($"## {Loc.T("Ai_ReportStructure")}");
            sb.AppendLine();
            foreach (var pageGroup in visuals.GroupBy(v => v.Page))
            {
                sb.AppendLine($"### {pageGroup.Key}");
                sb.AppendLine();

                var pageSummary = summary.Pages.FirstOrDefault(p => p.PageName == pageGroup.Key);
                if (pageSummary is not null) sb.AppendLine(pageSummary.Description).AppendLine();

                sb.AppendLine(Loc.T("Ai_VisualsHeader"));
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var v in pageGroup)
                    sb.AppendLine($"| {Esc(v.VisualType)} | {Esc(SensitiveTextGuard.Mask(v.Title))} | {Esc(v.FieldKindText)} | {Esc(v.Table)} | {Esc(v.Field)} |");
                sb.AppendLine();
            }
        }

        if (filters is { Count: > 0 })
        {
            sb.AppendLine($"## {Loc.T("Ai_Filters")}");
            sb.AppendLine();
            sb.AppendLine(Loc.T("Ai_FiltersIntro"));
            sb.AppendLine();

            var reportFilters = filters.Where(f => f.Scope == "Reporte").ToList();
            if (reportFilters.Count > 0)
            {
                sb.AppendLine($"### {Loc.T("Ai_FilterReport")}");
                sb.AppendLine();
                sb.AppendLine(Loc.T("Ai_FilterHeader"));
                sb.AppendLine("|---|---|---|");
                foreach (var f in reportFilters)
                    sb.AppendLine($"| {Esc(f.Table)}[{Esc(f.Field)}] | {Esc(f.FilterType)} | {Esc(SensitiveTextGuard.Mask(f.Summary))} |");
                sb.AppendLine();
            }

            foreach (var pageGroup in filters.Where(f => f.Scope == "Página").GroupBy(f => f.Page))
            {
                sb.AppendLine($"### {Loc.F("Ai_FilterPage", pageGroup.Key)}");
                sb.AppendLine();
                sb.AppendLine(Loc.T("Ai_FilterHeader"));
                sb.AppendLine("|---|---|---|");
                foreach (var f in pageGroup)
                    sb.AppendLine($"| {Esc(f.Table)}[{Esc(f.Field)}] | {Esc(f.FilterType)} | {Esc(SensitiveTextGuard.Mask(f.Summary))} |");
                sb.AppendLine();
            }

            var visualFilters = filters.Where(f => f.Scope == "Visual").ToList();
            if (visualFilters.Count > 0)
            {
                sb.AppendLine($"### {Loc.T("Ai_FilterVisual")}");
                sb.AppendLine();
                sb.AppendLine(Loc.T("Ai_FilterVisualHeader"));
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var f in visualFilters)
                    sb.AppendLine($"| {Esc(f.Page)} | {Esc(SensitiveTextGuard.Mask(f.VisualText))} | {Esc(f.Table)}[{Esc(f.Field)}] | {Esc(f.FilterType)} | {Esc(SensitiveTextGuard.Mask(f.Summary))} |");
                sb.AppendLine();
            }
        }

        var unused = usage?.Where(u => u.IsUnused).ToList();
        if (unused is { Count: > 0 })
        {
            sb.AppendLine($"## {Loc.T("Ai_Unused")}");
            sb.AppendLine();
            sb.AppendLine(Loc.T("Ai_UnusedIntro"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("Ai_UnusedHeader"));
            sb.AppendLine("|---|---|---|");
            foreach (var u in unused)
                sb.AppendLine($"| {Esc(u.KindText)} | {Esc(u.Table)} | {Esc(u.Name)} |");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Escapa el pipe para no romper las tablas Markdown.</summary>
    private static string Esc(string? value) => (value ?? "").Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
