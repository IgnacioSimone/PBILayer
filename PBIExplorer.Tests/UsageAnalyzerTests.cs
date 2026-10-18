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

    private static ModelSnapshot QueryModel() => new()
    {
        Tables =
        {
            new TableInfo { Name = "Ventas" },     // se usa en un visual
            new TableInfo { Name = "Huerfana" },   // nadie la usa
            new TableInfo { Name = "Auxiliar" },   // tabla cargada que solo alimenta a Ventas
        },
        Columns = { new ColumnInfo { Table = "Ventas", Name = "Monto" } },
        PowerQueries =
        {
            new PowerQueryInfo { Name = "Ventas", Kind = "Tabla", MCode = "let Origen = Auxiliar, Servidor2 = Servidor in Origen" },
            new PowerQueryInfo { Name = "Auxiliar", Kind = "Tabla", MCode = "let Origen = Stg1 in Origen" },
            new PowerQueryInfo { Name = "Huerfana", Kind = "Tabla", MCode = "let Origen = StgSoloHuerfana in Origen" },
            new PowerQueryInfo { Name = "Servidor", Kind = "Parámetro / función", MCode = "\"srv\" meta [IsParameterQuery=true]" },
            new PowerQueryInfo { Name = "Stg1", Kind = "Parámetro / función", MCode = "let Origen = Stg0 in Origen" },
            new PowerQueryInfo { Name = "Stg0", Kind = "Parámetro / función", MCode = "let Origen = 1 in Origen" },
            new PowerQueryInfo { Name = "StgSoloHuerfana", Kind = "Parámetro / función", MCode = "let Origen = 1 in Origen" },
            new PowerQueryInfo { Name = "Basura", Kind = "Parámetro / función", MCode = "1" },
        }
    };

    private static List<ObjectUsage> AnalyzeQueryModel() =>
        UsageAnalyzer.Analyze(QueryModel(), new List<VisualFieldUsage> { new() { FieldKind = "Columna", Table = "Ventas", Field = "Monto" } });

    [Fact]
    public void A_table_that_only_feeds_a_used_table_through_m_is_not_unused()
    {
        var usage = AnalyzeQueryModel();

        var auxiliar = Find(usage, "Tabla", "Auxiliar");
        Assert.False(auxiliar.IsUnused);
        Assert.Equal("Solo uso interno", auxiliar.Status);
        Assert.Equal(new[] { "Consulta «Ventas»" }, auxiliar.UsedBy);
        Assert.Equal(new[] { "Consulta «Stg1»" }, auxiliar.DependsOn);
    }

    [Fact]
    public void Helper_queries_are_used_when_a_used_table_needs_them_even_through_a_chain()
    {
        var usage = AnalyzeQueryModel();

        Assert.False(Find(usage, "Consulta", "Servidor").IsUnused);   // parámetro directo
        Assert.False(Find(usage, "Consulta", "Stg1").IsUnused);       // Ventas -> Auxiliar -> Stg1
        Assert.False(Find(usage, "Consulta", "Stg0").IsUnused);       // ... -> Stg0
    }

    [Fact]
    public void Helpers_that_only_feed_unused_things_or_nothing_are_candidates_for_cleanup()
    {
        var usage = AnalyzeQueryModel();

        Assert.True(Find(usage, "Tabla", "Huerfana").IsUnused);
        Assert.True(Find(usage, "Consulta", "StgSoloHuerfana").IsUnused);   // solo alimenta a una tabla sin uso
        Assert.True(Find(usage, "Consulta", "Basura").IsUnused);
    }

    [Fact]
    public void Tables_that_are_not_part_of_the_model_snapshot_without_queries_are_unaffected()
    {
        var model = new ModelSnapshot { Tables = { new TableInfo { Name = "Solo" } } };

        var usage = UsageAnalyzer.Analyze(model, new List<VisualFieldUsage>());

        Assert.True(Find(usage, "Tabla", "Solo").IsUnused);
        Assert.DoesNotContain(usage, u => u.Kind == "Consulta");
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
