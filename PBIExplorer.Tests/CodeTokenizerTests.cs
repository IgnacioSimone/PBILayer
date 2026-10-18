using PBIExplorer.Services;

namespace PBIExplorer.Tests;

public class CodeTokenizerTests
{
    private static IEnumerable<string> Of(IReadOnlyList<CodeToken> tokens, CodeKind kind) =>
        tokens.Where(t => t.Kind == kind).Select(t => t.Text);

    [Fact]
    public void Dax_highlights_functions_keywords_references_and_numbers()
    {
        var tokens = CodeTokenizer.Tokenize(
            "VAR total = SUM(Ventas[Monto]) RETURN DIVIDE([Total], 100) * 0.5", CodeLanguage.Dax);

        Assert.Equal(new[] { "VAR", "RETURN" }, Of(tokens, CodeKind.Keyword));
        Assert.Equal(new[] { "SUM", "DIVIDE" }, Of(tokens, CodeKind.Function));
        Assert.Equal(new[] { "Ventas[Monto]", "[Total]" }, Of(tokens, CodeKind.Reference));
        Assert.Equal(new[] { "100", "0.5" }, Of(tokens, CodeKind.Number));
    }

    [Fact]
    public void Dax_quoted_table_names_are_part_of_the_reference()
    {
        var tokens = CodeTokenizer.Tokenize("CALCULATE([Ventas], 'Dim Fecha'[Año] = 2025)", CodeLanguage.Dax);

        Assert.Contains("'Dim Fecha'[Año]", Of(tokens, CodeKind.Reference));
        Assert.Contains("[Ventas]", Of(tokens, CodeKind.Reference));
    }

    [Fact]
    public void Dax_strings_and_comments_swallow_what_looks_like_code()
    {
        var tokens = CodeTokenizer.Tokenize(
            "IF(1, \"SUM(x) // no\", 2) // comentario SUM(y)\n-- otro VAR\n/* bloque\nRETURN */ 3", CodeLanguage.Dax);

        Assert.Equal(new[] { "\"SUM(x) // no\"" }, Of(tokens, CodeKind.String));
        Assert.Equal(new[] { "// comentario SUM(y)", "-- otro VAR", "/* bloque\nRETURN */" }, Of(tokens, CodeKind.Comment));
        Assert.Equal(new[] { "IF" }, Of(tokens, CodeKind.Function));
        Assert.Empty(Of(tokens, CodeKind.Keyword));
    }

    [Fact]
    public void Escaped_double_quotes_stay_inside_the_string()
    {
        var tokens = CodeTokenizer.Tokenize("\"dijo \"\"hola\"\" y se fue\" & 1", CodeLanguage.Dax);

        Assert.Equal(new[] { "\"dijo \"\"hola\"\" y se fue\"" }, Of(tokens, CodeKind.String));
    }

    [Fact]
    public void M_highlights_keywords_functions_and_quoted_step_names()
    {
        var tokens = CodeTokenizer.Tokenize(
            "let\n    Origen = Sql.Database(\"srv\", \"db\"),\n    #\"Filas filtradas\" = Table.SelectRows(Origen, each [Año] > 2020)\nin\n    #\"Filas filtradas\"",
            CodeLanguage.M);

        Assert.Equal(new[] { "let", "each", "in" }, Of(tokens, CodeKind.Keyword));
        Assert.Equal(new[] { "Sql.Database", "Table.SelectRows" }, Of(tokens, CodeKind.Function));
        Assert.Equal(new[] { "\"srv\"", "\"db\"" }, Of(tokens, CodeKind.String));
        Assert.Equal(new[] { "#\"Filas filtradas\"", "#\"Filas filtradas\"" }, Of(tokens, CodeKind.Reference));
        Assert.Equal(new[] { "2020" }, Of(tokens, CodeKind.Number));
    }

    [Fact]
    public void M_does_not_treat_double_dash_as_a_comment()
    {
        var tokens = CodeTokenizer.Tokenize("1 -- 2", CodeLanguage.M);

        Assert.Empty(Of(tokens, CodeKind.Comment));
    }

    [Theory]
    [InlineData(CodeLanguage.Dax)]
    [InlineData(CodeLanguage.M)]
    public void Tokens_always_reassemble_into_the_original_text(CodeLanguage language)
    {
        const string code = "VAR x = \"sin cerrar\n// c\n[roto 'raro [\n#\"abierto\n/* sin cierre\n3.5e+10 .5 ñandú_1 Table.Foo(";

        var tokens = CodeTokenizer.Tokenize(code, language);

        Assert.Equal(code, string.Concat(tokens.Select(t => t.Text)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_code_gives_no_tokens(string? code) =>
        Assert.Empty(CodeTokenizer.Tokenize(code, CodeLanguage.Dax));
}
