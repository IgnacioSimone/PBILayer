using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PBIExplorer.Infrastructure;
using Xunit;

namespace PBIExplorer.Tests;

public class LocalizationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PBIExplorer.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la carpeta del proyecto.");
    }

    private static Dictionary<string, string> ReadResx(string file) =>
        XDocument.Load(Path.Combine(RepoRoot(), "Resources", file)).Root!
            .Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static IEnumerable<string> UsedKeys()
    {
        var root = RepoRoot();
        var code = new Regex(@"Loc\.(?:T|F)\(\s*""(?<k>[A-Za-z][A-Za-z0-9_]*)""");
        var xaml = new Regex(@"views:Loc\s+(?:Key=)?(?<k>[A-Za-z][A-Za-z0-9_]*)");
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains("PBIExplorer.Tests")))
        {
            var ext = Path.GetExtension(file);
            if (ext == ".cs") foreach (Match m in code.Matches(File.ReadAllText(file))) yield return m.Groups["k"].Value;
            else if (ext == ".xaml") foreach (Match m in xaml.Matches(File.ReadAllText(file))) yield return m.Groups["k"].Value;
        }
    }

    [Fact]
    public void Every_key_used_in_code_and_xaml_exists_in_both_languages()
    {
        var es = ReadResx("Strings.resx");
        var en = ReadResx("Strings.en.resx");
        var missing = UsedKeys().Distinct().Where(k => !es.ContainsKey(k) || !en.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Faltan textos: " + string.Join(", ", missing));
    }

    [Fact]
    public void Both_languages_have_the_same_keys_and_placeholders()
    {
        var es = ReadResx("Strings.resx");
        var en = ReadResx("Strings.en.resx");
        Assert.Equal(es.Keys.OrderBy(k => k), en.Keys.OrderBy(k => k));

        var ph = new Regex(@"\{(\d+)\}");
        foreach (var (key, value) in es)
        {
            var a = ph.Matches(value).Select(m => m.Value).Distinct().OrderBy(x => x);
            var b = ph.Matches(en[key]).Select(m => m.Value).Distinct().OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), $"'{key}' usa distintos {{valores}} en cada idioma");
        }
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    public void Automatic_language_is_Spanish_only_when_Windows_is_Spanish(string windows, string expected)
    {
        Assert.Equal(expected, Loc.Resolve(AppLanguage.System, new CultureInfo(windows)).TwoLetterISOLanguageName);
    }
}
