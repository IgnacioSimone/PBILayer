using PBIExplorer.Models;
using PBIExplorer.Services;

namespace PBIExplorer.Tests;

public class UsageAnalyzerTests
{
    private static ObjectUsage Find(IEnumerable<ObjectUsage> usage, string kind, string name) =>
        usage.Single(u => u.Kind == kind && u.Name == name);

    [Fact]
    public void A_measure_referenced_with_its_table_prefix_counts_as_used()
    {
        // DAX permite escribir Tabla[Medida]; no es una columna aunque lleve tabla delante.
        var model = new ModelSnapshot
        {
            Tables = { new TableInfo { Name = "FCT" } },
            Columns = { new ColumnInfo { Table = "FCT", Name = "Monto" } },
            Measures =
            {
                new MeasureInfo { Table = "FCT", Name = "Base", Expression = "SUM(FCT[Monto])" },
                new MeasureInfo { Table = "FCT", Name = "Derivada", Expression = "FCT[Base] * 2" },
            }
        };
        var visuals = new List<VisualFieldUsage> { new() { FieldKind = "Medida", Table = "FCT", Field = "Derivada" } };

        var usage = UsageAnalyzer.Analyze(model, visuals);

        var baseMeasure = Find(usage, "Medida", "Base");
        Assert.False(baseMeasure.IsUnused);
        Assert.Equal(new[] { "[Derivada]" }, baseMeasure.UsedBy);
        Assert.Equal(new[] { "FCT[Base]" }, Find(usage, "Medida", "Derivada").DependsOn);
    }

    [Fact]
    public void A_qualified_reference_to_a_real_column_stays_a_column_reference()
    {
        var model = new ModelSnapshot
        {
            Columns = { new ColumnInfo { Table = "FCT", Name = "Monto" } },
            Measures = { new MeasureInfo { Table = "FCT", Name = "Total", Expression = "SUM(FCT[Monto])" } }
        };

        var usage = UsageAnalyzer.Analyze(model, new List<VisualFieldUsage>());

        Assert.Equal(new[] { "[Total]" }, Find(usage, "Columna", "Monto").UsedBy);
        Assert.True(Find(usage, "Medida", "Total").IsUnused);
    }

    [Fact]
    public void A_column_and_a_measure_with_the_same_name_resolve_to_the_column_when_qualified()
    {
        var model = new ModelSnapshot
        {
            Columns = { new ColumnInfo { Table = "FCT", Name = "Monto" } },
            Measures =
            {
                new MeasureInfo { Table = "FCT", Name = "Monto", Expression = "1" },
                new MeasureInfo { Table = "FCT", Name = "Usa", Expression = "SUM(FCT[Monto])" }
            }
        };

        var usage = UsageAnalyzer.Analyze(model, new List<VisualFieldUsage>());

        Assert.Equal(new[] { "[Usa]" }, Find(usage, "Columna", "Monto").UsedBy);
    }

    [Fact]
    public void Measure_references_ignore_case()
    {
        var model = new ModelSnapshot
        {
            Measures =
            {
                new MeasureInfo { Table = "T", Name = "Total", Expression = "1" },
                new MeasureInfo { Table = "T", Name = "Otra", Expression = "[total] + 1" },
            }
        };

        var usage = UsageAnalyzer.Analyze(model, new List<VisualFieldUsage>());

        Assert.False(Find(usage, "Medida", "Total").IsUnused);
    }
}
