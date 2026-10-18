using PBIExplorer.Infrastructure;
using PBIExplorer.Models;

namespace PBIExplorer.Services;

public enum DiffKind { Agregado, Eliminado, Modificado }

public class ModelDiffEntry
{
    public DiffKind Kind { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";

    public string KindText => Kind switch
    {
        DiffKind.Agregado => Loc.T("Diff_Added"),
        DiffKind.Eliminado => Loc.T("Diff_Removed"),
        _ => Loc.T("Diff_Modified")
    };
}

/// <summary>
/// Compara dos snapshots del mismo modelo (por ejemplo, un .json exportado hace un
/// tiempo vs. el modelo conectado ahora) para ver qué cambió entre versiones —
/// pensado para auditar ediciones de un tablero que ya conocés, no para diffear dos
/// modelos distintos entre sí (los nombres de tabla/medida/columna son la clave de
/// comparación, así que un simple renombre se ve como "eliminado" + "agregado").
/// </summary>
public static class ModelDiffAnalyzer
{
    public static List<ModelDiffEntry> Compare(ModelSnapshot older, ModelSnapshot newer,
        List<FilterInfo>? olderFilters = null, List<FilterInfo>? newerFilters = null)
    {
        var entries = new List<ModelDiffEntry>();

        CompareByKey(entries, Terms.Display("Tabla"),
            ByKeyFirstWins(older.Tables, t => t.Name),
            ByKeyFirstWins(newer.Tables, t => t.Name),
            (a, b) =>
            {
                var changes = new List<string>();
                if (a.IsHidden != b.IsHidden) changes.Add(Loc.T(b.IsHidden ? "Diff_NowHidden" : "Diff_NowVisible"));
                if (a.Description != b.Description) changes.Add(Loc.T("Diff_DescriptionChanged"));
                return changes;
            });

        CompareByKey(entries, Terms.Display("Medida"),
            ByKeyFirstWins(older.Measures, m => $"{m.Table}[{m.Name}]"),
            ByKeyFirstWins(newer.Measures, m => $"{m.Table}[{m.Name}]"),
            (a, b) =>
            {
                var changes = new List<string>();
                if (a.Expression != b.Expression) changes.Add(Loc.T("Diff_DaxChanged"));
                if (a.FormatString != b.FormatString) changes.Add(Loc.F("Diff_FormatChanged", a.FormatString, b.FormatString));
                if (a.IsHidden != b.IsHidden) changes.Add(Loc.T(b.IsHidden ? "Diff_NowHidden" : "Diff_NowVisible"));
                if (a.DisplayFolder != b.DisplayFolder) changes.Add(Loc.F("Diff_FolderChanged", a.DisplayFolder, b.DisplayFolder));
                return changes;
            });

        CompareByKey(entries, Terms.Display("Columna"),
            ByKeyFirstWins(older.Columns, c => $"{c.Table}[{c.Name}]"),
            ByKeyFirstWins(newer.Columns, c => $"{c.Table}[{c.Name}]"),
            (a, b) =>
            {
                var changes = new List<string>();
                if (a.DataType != b.DataType) changes.Add(Loc.F("Diff_DataTypeChanged", Terms.Display(a.DataType), Terms.Display(b.DataType)));
                if (a.ColumnType != b.ColumnType) changes.Add(Loc.F("Diff_ColumnTypeChanged", Terms.Display(a.ColumnType), Terms.Display(b.ColumnType)));
                if (a.Expression != b.Expression) changes.Add(Loc.T("Diff_ColumnDaxChanged"));
                if (a.IsHidden != b.IsHidden) changes.Add(Loc.T(b.IsHidden ? "Diff_NowHidden" : "Diff_NowVisible"));
                return changes;
            });

        CompareByKey(entries, Loc.T("Diff_Cat_Relationship"),
            ByKeyFirstWins(older.Relationships, r => $"{r.FromTable}[{r.FromColumn}]→{r.ToTable}[{r.ToColumn}]"),
            ByKeyFirstWins(newer.Relationships, r => $"{r.FromTable}[{r.FromColumn}]→{r.ToTable}[{r.ToColumn}]"),
            (a, b) =>
            {
                var changes = new List<string>();
                if (a.Cardinality != b.Cardinality) changes.Add(Loc.F("Diff_CardinalityChanged", Terms.Display(a.Cardinality), Terms.Display(b.Cardinality)));
                if (a.CrossFilterBehavior != b.CrossFilterBehavior) changes.Add(Loc.F("Diff_CrossFilterChanged", Terms.Display(a.CrossFilterBehavior), Terms.Display(b.CrossFilterBehavior)));
                if (a.IsActive != b.IsActive) changes.Add(Loc.T(b.IsActive ? "Diff_NowActive" : "Diff_NowInactive"));
                return changes;
            });

        CompareByKey(entries, Loc.T("Diff_Cat_PowerQuery"),
            ByKeyFirstWins(older.PowerQueries, p => p.Name),
            ByKeyFirstWins(newer.PowerQueries, p => p.Name),
            (a, b) =>
            {
                var changes = new List<string>();
                if (a.MCode != b.MCode) changes.Add(Loc.T("Diff_MCodeChanged"));
                return changes;
            });

        if (olderFilters is not null || newerFilters is not null)
        {
            CompareByKey(entries, Loc.T("Diff_Cat_Filter"),
                ByKeyFirstWins(olderFilters ?? new List<FilterInfo>(), FilterKey),
                ByKeyFirstWins(newerFilters ?? new List<FilterInfo>(), FilterKey),
                (a, b) =>
                {
                    var changes = new List<string>();
                    if (a.FilterType != b.FilterType) changes.Add(Loc.F("Diff_FilterTypeChanged", a.FilterType, b.FilterType));
                    if (a.Summary != b.Summary) changes.Add(Loc.F("Diff_ConditionChanged", a.Summary, b.Summary));
                    return changes;
                });
        }

        return entries
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Category)
            .ThenBy(e => e.Name)
            .ToList();
    }

    /// <summary>Los filtros no tienen un nombre único garantizado — la clave de comparación
    /// es el nivel + dónde vive + qué campo filtra.</summary>
    private static string FilterKey(FilterInfo f) => $"{f.Scope} · {f.Page} · {f.VisualText} · {f.Table}[{f.Field}]";

    /// <summary>Como Dictionary.ToDictionary, pero si hay una clave repetida (no debería
    /// pasar en un modelo válido, pero esta herramienta no controla eso) se queda con la
    /// primera en vez de tirar una excepción y arruinar toda la comparación.</summary>
    private static Dictionary<string, T> ByKeyFirstWins<T>(IEnumerable<T> items, Func<T, string> keySelector) =>
        items.GroupBy(keySelector).ToDictionary(g => g.Key, g => g.First());

    private static void CompareByKey<T>(
        List<ModelDiffEntry> entries, string category,
        Dictionary<string, T> older, Dictionary<string, T> newer,
        Func<T, T, List<string>> diffFields)
    {
        foreach (var key in newer.Keys.Except(older.Keys))
            entries.Add(new ModelDiffEntry { Kind = DiffKind.Agregado, Category = category, Name = key, Detail = Loc.T("Diff_NewInVersion") });

        foreach (var key in older.Keys.Except(newer.Keys))
            entries.Add(new ModelDiffEntry { Kind = DiffKind.Eliminado, Category = category, Name = key, Detail = Loc.T("Diff_GoneInVersion") });

        foreach (var key in newer.Keys.Intersect(older.Keys))
        {
            var changes = diffFields(older[key], newer[key]);
            if (changes.Count > 0)
                entries.Add(new ModelDiffEntry { Kind = DiffKind.Modificado, Category = category, Name = key, Detail = string.Join("; ", changes) });
        }
    }
}
