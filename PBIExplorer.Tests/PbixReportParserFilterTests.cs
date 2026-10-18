using System.IO.Compression;
using System.Text;
using PBIExplorer.Services;

namespace PBIExplorer.Tests;

public class PbixReportParserFilterTests
{
    private static string BuildSyntheticPbix(Action<ZipArchive> populate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pbilayer_filters_{Guid.NewGuid():N}.pbix");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            populate(zip);
        }
        return path;
    }

    private static void AddEntry(ZipArchive zip, string name, string content, Encoding? encoding = null)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, encoding ?? new UTF8Encoding(false));
        writer.Write(content);
    }

    [Fact]
    public void ParseFilters_Pbir_ExtractsReportPageAndVisualFilters()
    {
        var path = BuildSyntheticPbix(zip =>
        {
            AddEntry(zip, "Report/definition/report.json", """
                { "filterConfig": { "filters": [
                    { "name": "rpt1", "type": "Categorical",
                      "field": { "Column": { "Expression": { "SourceRef": { "Entity": "Ventas" } }, "Property": "Anio" } },
                      "filter": { "Where": [ { "Condition": { "In": { "Values": [
                          [ { "Literal": { "Value": "'2024'" } } ],
                          [ { "Literal": { "Value": "'2025'" } } ]
                      ] } } } ] }
                    }
                ] } }
                """);

            AddEntry(zip, "Report/definition/pages/p1/page.json", """
                { "displayName": "Resumen",
                  "filterConfig": { "filters": [
                    { "name": "pg1", "type": "Categorical",
                      "field": { "Column": { "Expression": { "SourceRef": { "Entity": "Clientes" } }, "Property": "Pais" } },
                      "filter": { "Where": [ { "Condition": { "Not": { "Expression": { "In": { "Values": [
                          [ { "Literal": { "Value": "'Test'" } } ]
                      ] } } } } } ] }
                    }
                  ] } }
                """);

            AddEntry(zip, "Report/definition/pages/p1/visuals/v1/visual.json", """
                { "visual": {
                    "visualType": "columnChart",
                    "query": {},
                    "visualContainerObjects": { "title": [ { "properties": { "text": { "expr": { "Literal": { "Value": "'Ventas por mes'" } } } } } ] }
                  },
                  "filterConfig": { "filters": [
                    { "name": "vis1", "type": "TopN",
                      "field": { "Measure": { "Expression": { "SourceRef": { "Entity": "Ventas" } }, "Property": "Total" } }
                    }
                  ] } }
                """);
        });

        try
        {
            var filters = new PbixReportParser().ParseFilters(path);

            Assert.Equal(3, filters.Count);

            var report = Assert.Single(filters, f => f.Scope == "Reporte");
            Assert.Equal("Ventas", report.Table);
            Assert.Equal("Anio", report.Field);
            Assert.Equal("incluye: 2024, 2025", report.Summary);

            var page = Assert.Single(filters, f => f.Scope == "Página");
            Assert.Equal("Resumen", page.Page);
            Assert.Equal("Clientes", page.Table);
            Assert.Equal("excluye: Test", page.Summary);

            var visual = Assert.Single(filters, f => f.Scope == "Visual");
            Assert.Equal("Resumen", visual.Page);
            Assert.Equal("Ventas por mes", visual.VisualTitle);
            Assert.Equal("Medida", visual.FieldKind);
            Assert.Equal("Total", visual.Field);
            Assert.Contains("TopN", visual.Summary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseFilters_LegacyLayout_ExtractsAllLevels()
    {
        var path = BuildSyntheticPbix(zip =>
        {
            var reportFilters = """
                [ { "name": "rpt1", "type": "Categorical",
                    "expression": { "Column": { "Expression": { "SourceRef": { "Entity": "Ventas" } }, "Property": "Region" } },
                    "filter": { "Where": [ { "Condition": { "In": { "Values": [ [ { "Literal": { "Value": "'Norte'" } } ] ] } } } ] }
                } ]
                """.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");

            var pageFilters = """
                [ { "name": "pg1", "type": "Categorical",
                    "expression": { "Column": { "Expression": { "SourceRef": { "Entity": "Clientes" } }, "Property": "Segmento" } }
                } ]
                """.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");

            var visualFilters = """
                [ { "name": "vis1", "type": "Advanced",
                    "expression": { "Column": { "Expression": { "SourceRef": { "Entity": "Ventas" } }, "Property": "Monto" } }
                } ]
                """.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");

            var visualConfig = """
                { "singleVisual": { "visualType": "card" } }
                """.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");

            var layout = $$"""
                {
                  "filters": "{{reportFilters}}",
                  "sections": [
                    {
                      "displayName": "Pagina 1",
                      "filters": "{{pageFilters}}",
                      "visualContainers": [
                        { "config": "{{visualConfig}}", "filters": "{{visualFilters}}" }
                      ]
                    }
                  ]
                }
                """;

            AddEntry(zip, "Report/Layout", layout, Encoding.Unicode);
        });

        try
        {
            var filters = new PbixReportParser().ParseFilters(path);

            Assert.Equal(3, filters.Count);
            Assert.Contains(filters, f => f.Scope == "Reporte" && f.Table == "Ventas" && f.Field == "Region" && f.Summary.Contains("Norte"));
            Assert.Contains(filters, f => f.Scope == "Página" && f.Page == "Pagina 1" && f.Table == "Clientes" && f.Field == "Segmento");
            Assert.Contains(filters, f => f.Scope == "Visual" && f.Table == "Ventas" && f.Field == "Monto" && f.FilterType == "Advanced");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseFilters_NoFilterConfigAnywhere_ReturnsEmpty_DoesNotThrow()
    {
        var path = BuildSyntheticPbix(zip =>
        {
            AddEntry(zip, "Report/definition/report.json", "{}");
            AddEntry(zip, "Report/definition/pages/p1/page.json", """{ "displayName": "Sin filtros" }""");
        });

        try
        {
            var filters = new PbixReportParser().ParseFilters(path);
            Assert.Empty(filters);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
