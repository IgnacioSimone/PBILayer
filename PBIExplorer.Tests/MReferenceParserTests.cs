using PBIExplorer.Services;

namespace PBIExplorer.Tests;

public class MReferenceParserTests
{
    private static IReadOnlySet<string> Names(params string[] names) => names.ToHashSet();

    [Fact]
    public void Finds_a_query_referenced_by_its_plain_name()
    {
        const string code = "let\n    Origen = Fact,\n    Filtrado = Table.SelectRows(Origen, each [Año] > 2020)\nin\n    Filtrado";

        var refs = MReferenceParser.ExtractReferences(code, Names("Fact", "Dim"), self: "Ventas");

        Assert.Equal(new[] { "Fact" }, refs);
    }

    [Fact]
    public void Finds_queries_whose_names_need_quotes()
    {
        const string code = "let Origen = #\"Ventas crudas\" in Origen";

        var refs = MReferenceParser.ExtractReferences(code, Names("Ventas crudas"));

        Assert.Equal(new[] { "Ventas crudas" }, refs);
    }

    [Fact]
    public void Finds_parameters_and_custom_functions()
    {
        const string code = "let Origen = Sql.Database(ServidorSql, BaseSql), Limpia = LimpiarTexto(Origen) in Limpia";

        var refs = MReferenceParser.ExtractReferences(code, Names("ServidorSql", "BaseSql", "LimpiarTexto"));

        Assert.Equal(new[] { "ServidorSql", "BaseSql", "LimpiarTexto" }, refs);
    }

    [Fact]
    public void Ignores_names_inside_strings_and_comments()
    {
        const string code = "let\n    // Fact se usa abajo\n    Origen = Table.FromRows({{\"Fact\", \"Dim\"}}), /* Dim */\n    X = \"Fact\"\nin X";

        var refs = MReferenceParser.ExtractReferences(code, Names("Fact", "Dim"));

        Assert.Empty(refs);
    }

    [Fact]
    public void Ignores_field_access_and_the_query_itself()
    {
        const string code = "let Origen = Ventas, Col = Origen[Fact] in Col";

        var refs = MReferenceParser.ExtractReferences(code, Names("Fact", "Ventas"), self: "Ventas");

        Assert.Empty(refs);
    }

    [Fact]
    public void Does_not_match_part_of_a_longer_name()
    {
        var refs = MReferenceParser.ExtractReferences("let Origen = FactTabla, B = Fact2 in B", Names("Fact"));

        Assert.Empty(refs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_code_has_no_references(string? code) =>
        Assert.Empty(MReferenceParser.ExtractReferences(code, Names("Fact")));
}
