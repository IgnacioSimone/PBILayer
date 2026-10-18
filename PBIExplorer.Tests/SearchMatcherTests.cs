using PBIExplorer.Models;
using PBIExplorer.Services;

namespace PBIExplorer.Tests;

public class SearchMatcherTests
{
    private static readonly VisualFieldUsage Row = new() { Page = "Página Principal", Field = "Ventas Año" };

    [Theory]
    [InlineData("pagina")]
    [InlineData("PÁGINA")]
    [InlineData("ano")]
    [InlineData("ventas")]
    public void Ignores_case_and_accents(string search) => Assert.True(SearchMatcher.Matches(Row, search));

    [Fact]
    public void Returns_false_when_no_text_property_contains_the_search() =>
        Assert.False(SearchMatcher.Matches(Row, "inexistente"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_search_matches_everything(string? search) => Assert.True(SearchMatcher.Matches(Row, search));
}
