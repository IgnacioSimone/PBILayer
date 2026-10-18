using System.IO;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using PBIExplorer.Services.Export;
using Spreadsheet = DocumentFormat.OpenXml.Spreadsheet;

namespace PBIExplorer.Tests;

public class ExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pbilayer-tests-" + Guid.NewGuid().ToString("N"));

    public ExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* si algo todavía lo tiene abierto, el SO lo limpia después */ }
    }

    private static void AssertValid(OpenXmlPackage package)
    {
        var errors = new OpenXmlValidator().Validate(package)
            .Select(e => $"{e.Description} @ {e.Path?.XPath}")
            .Distinct()
            .ToList();
        Assert.True(errors.Count == 0, "El archivo no valida contra el esquema de Open XML:\n" + string.Join("\n", errors.Take(8)));
    }

    private string Temp(string name) => Path.Combine(_dir, name);

    private static ExportTable SampleTable(string title = "Medidas DAX") => new(
        title,
        new[] { "Tabla", "Medida", "DAX" },
        new IReadOnlyList<string>[]
        {
            new[] { "Ventas", "Total", "SUM(Ventas[Monto])" },
            new[] { "Ventas", "Margen \"neto\"", "VAR x = [Total]\nRETURN x * 0.2" },
            new[] { "Ventas", "=HYPERLINK(\"http://x\")", "a;b,c" },
        },
        new HashSet<int> { 2 },
        "Una nota");

    private static ExportDocument SampleDocument() => new(
        "Ventas 2026",
        new DateTime(2026, 10, 18, 9, 30, 0),
        new[] { new ExportFact("Tablas", "5"), new ExportFact("Medidas", "164") },
        new[] { SampleTable(), SampleTable("Columnas") });

    // ---------------- CSV ----------------

    [Fact]
    public void Csv_has_a_utf8_bom_header_and_quotes_what_needs_quoting()
    {
        using var stream = new MemoryStream();

        CsvExporter.Write(stream, SampleTable(), delimiter: ";");

        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal("Tabla;Medida;DAX", lines[0]);
        Assert.Equal("Ventas;Total;SUM(Ventas[Monto])", lines[1]);
        Assert.Equal("Ventas;\"Margen \"\"neto\"\"\";\"VAR x = [Total]\nRETURN x * 0.2\"", lines[2]);   // el DAX multilínea queda entre comillas
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("normal", "normal")]
    [InlineData("a=b", "a=b")]
    public void Csv_neutralizes_cells_that_excel_would_run_as_formulas(string value, string expected) =>
        Assert.Equal(expected, CsvExporter.Neutralize(value));

    [Fact]
    public void Csv_zip_has_one_numbered_file_per_table()
    {
        var path = Temp("todo.zip");

        CsvExporter.WriteZip(path, new[] { SampleTable("Medidas DAX"), SampleTable("Medidas/DAX") });

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(new[] { "01 Medidas DAX.csv", "02 Medidas_DAX.csv" }, zip.Entries.Select(e => e.FullName).Order());
    }

    // ---------------- Excel ----------------

    [Fact]
    public void Excel_file_is_valid_and_has_a_summary_plus_one_sheet_per_table()
    {
        var path = Temp("doc.xlsx");

        ExcelExporter.WriteFile(path, SampleDocument());

        using var book = SpreadsheetDocument.Open(path, isEditable: false);
        AssertValid(book);
        var names = book.WorkbookPart!.Workbook.Descendants<Spreadsheet.Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(new[] { "Resumen", "Medidas DAX", "Columnas" }, names);
    }

    [Fact]
    public void Excel_stores_values_as_text_never_as_formulas()
    {
        var path = Temp("formula.xlsx");

        ExcelExporter.WriteFile(path, SampleDocument());

        using var book = SpreadsheetDocument.Open(path, isEditable: false);
        var sheet = ((WorksheetPart)book.WorkbookPart!.GetPartById(
            book.WorkbookPart.Workbook.Descendants<Spreadsheet.Sheet>().First(s => s.Name == "Medidas DAX").Id!)).Worksheet;
        var cells = sheet.Descendants<Spreadsheet.Cell>().ToList();

        Assert.DoesNotContain(cells, c => c.CellFormula is not null);
        Assert.All(cells, c => Assert.Equal(Spreadsheet.CellValues.InlineString, c.DataType!.Value));
        Assert.Contains(cells, c => c.InnerText == "=HYPERLINK(\"http://x\")");
        Assert.Equal("Tabla", cells.First(c => c.CellReference == "A1").InnerText);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(703, "AAA")]
    public void Excel_column_letters(int number, string letters) => Assert.Equal(letters, ExcelExporter.ColumnLetter(number));

    [Fact]
    public void Excel_sheet_names_are_cleaned_truncated_and_unique()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = ExcelExporter.UniqueName("Uso / limpieza: [todo]?", used);
        var second = ExcelExporter.UniqueName("Uso limpieza todo", used);
        var long1 = ExcelExporter.UniqueName(new string('x', 50), used);
        var long2 = ExcelExporter.UniqueName(new string('x', 50), used);

        Assert.Equal("Uso  limpieza todo", first);
        Assert.Equal("Uso limpieza todo", second);
        Assert.Equal(31, long1.Length);
        Assert.NotEqual(long1, long2);
        Assert.True(long2.Length <= 31);
    }

    [Fact]
    public void Excel_truncates_cells_over_the_excel_limit_and_still_validates()
    {
        var table = new ExportTable("Grande", new[] { "A" },
            new IReadOnlyList<string>[] { new[] { new string('z', 40_000) + "\u0001" } }, new HashSet<int>());
        var path = Temp("grande.xlsx");

        ExcelExporter.WriteFile(path, new ExportDocument("x", DateTime.Now, Array.Empty<ExportFact>(), new[] { table }));

        using var book = SpreadsheetDocument.Open(path, isEditable: false);
        AssertValid(book);
        var text = book.WorkbookPart!.WorksheetParts.SelectMany(p => p.Worksheet.Descendants<Spreadsheet.Text>())
            .Select(t => t.Text).Max(t => t.Length);
        Assert.Equal(32_767, text);
    }

    // ---------------- Word ----------------

    [Fact]
    public void Word_file_is_valid_and_has_the_title_summary_and_every_table()
    {
        var path = Temp("doc.docx");

        WordExporter.WriteFile(path, SampleDocument());

        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        AssertValid(doc);

        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("Ventas 2026", text);
        Assert.Contains("164", text);
        Assert.Contains("Margen \"neto\"", text);
        Assert.Contains("RETURN x * 0.2", text);
        Assert.Equal(3, doc.MainDocumentPart.Document.Body.Descendants<Table>().Count());   // resumen + 2 tablas
    }

    [Fact]
    public void Word_repeats_the_header_row_and_uses_landscape()
    {
        var path = Temp("layout.docx");

        WordExporter.WriteFile(path, SampleDocument());

        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        var body = doc.MainDocumentPart!.Document.Body!;
        Assert.Contains(body.Descendants<TableHeader>(), _ => true);
        Assert.Equal(PageOrientationValues.Landscape, body.Descendants<PageSize>().Single().Orient!.Value);
    }

    [Fact]
    public void Word_column_widths_fit_the_page_and_give_code_more_room()
    {
        var widths = WordExporter.ColumnWidths(SampleTable());

        Assert.Equal(3, widths.Length);
        Assert.True(widths.Sum() <= 16838 - 2 * 720);
        Assert.True(widths[2] > widths[0]);
    }

    [Fact]
    public void Word_survives_control_characters_and_empty_tables()
    {
        var table = new ExportTable("Vacía", new[] { "A" }, Array.Empty<IReadOnlyList<string>>(), new HashSet<int>());
        var withControl = new ExportTable("Raro", new[] { "A" },
            new IReadOnlyList<string>[] { new[] { "a\u0001b\u0000c" } }, new HashSet<int>());
        var path = Temp("raro.docx");

        WordExporter.WriteFile(path, new ExportDocument("x", DateTime.Now, Array.Empty<ExportFact>(), new[] { table, withControl }));

        using var doc = WordprocessingDocument.Open(path, isEditable: false);
        AssertValid(doc);
        Assert.Contains("abc", doc.MainDocumentPart!.Document.Body!.InnerText);
    }

    // ---------------- HTML ----------------

    [Fact]
    public void Html_escapes_everything_and_ships_a_restrictive_content_security_policy()
    {
        var evil = new ExportTable("Prueba <b>", new[] { "<script>alert(1)</script>" },
            new IReadOnlyList<string>[] { new[] { "<img src=x onerror=alert(1)>" } }, new HashSet<int>());

        var html = HtmlExporter.Render(new ExportDocument("<i>Tablero</i>", DateTime.Now, new[] { new ExportFact("<x>", "<y>") }, new[] { evil }));

        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.Contains("&lt;i&gt;Tablero&lt;/i&gt;", html);
        Assert.Contains("default-src 'none'", html);
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", ""));   // sin recursos externos
    }

    [Fact]
    public void Html_has_a_section_nav_search_box_and_monospace_code_cells()
    {
        var html = HtmlExporter.Render(SampleDocument());

        Assert.Contains("href=\"#s0\"", html);
        Assert.Contains("href=\"#s1\"", html);
        Assert.Contains("id=\"q\"", html);
        Assert.Contains("<td class=\"code\"><pre>SUM(Ventas[Monto])</pre></td>", html);
        Assert.Contains("lang=\"es\"", html);
    }

    // ---------------- Utilidades ----------------

    [Theory]
    [InlineData("a:b/c*d", "a_b_c_d")]
    [InlineData("  ", "modelo")]
    [InlineData("termina.", "termina")]
    [InlineData("Ventas 2026", "Ventas 2026")]
    public void Safe_file_names(string input, string expected) => Assert.Equal(expected, ExportText.SafeFileName(input));

    [Fact]
    public void Invalid_xml_characters_are_stripped_but_text_whitespace_stays()
    {
        Assert.Equal("a\tb\nc\r", ExportText.StripInvalidXmlChars("a\tb\nc\r"));
        Assert.Equal("abc", ExportText.StripInvalidXmlChars("a\u0001b\u000Bc"));
        Assert.Equal("😀", ExportText.StripInvalidXmlChars("😀"));
        Assert.Equal("", ExportText.StripInvalidXmlChars("\uD800"));   // sustituto suelto: inválido en XML
    }
}
