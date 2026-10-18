using PBIExplorer.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace PBIExplorer.Services.Export;

/// <summary>Libro de Excel (.xlsx) sin necesitar Excel instalado: una hoja de resumen y una hoja por sección,
/// con encabezado fijo, filtros y columnas de código en monoespaciado. Los valores se guardan como texto
/// (nunca como fórmula), así que un "=…" en un nombre o filtro no se ejecuta.</summary>
public static class ExcelExporter
{
    // Límite de Excel por celda.
    private const int MaxCellLength = 32_767;

    private const uint StyleDefault = 0;
    private const uint StyleHeader = 1;
    private const uint StyleCode = 2;
    private const uint StyleTop = 3;
    private const uint StyleTitle = 4;
    private const uint StyleLabel = 5;

    public static void WriteFile(string path, ExportDocument document)
    {
        using var spreadsheet = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = spreadsheet.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        AddStyles(workbookPart);

        uint nextId = 1;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSheet(workbookPart, sheets, UniqueName(Loc.T("Export_Summary"), usedNames), nextId++, SummarySheet(document));
        foreach (var table in document.Tables)
            AddSheet(workbookPart, sheets, UniqueName(table.Title, usedNames), nextId++, TableSheet(table));

        workbookPart.Workbook.Save();
    }

    private static void AddSheet(WorkbookPart workbookPart, Sheets sheets, string name, uint id, Worksheet worksheet)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        part.Worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(part), SheetId = id, Name = name });
    }

    private static Worksheet SummarySheet(ExportDocument document)
    {
        var data = new SheetData();
        var row = 1u;
        data.Append(Row(row++, (document.SourceName, StyleTitle)));
        data.Append(Row(row++, (Loc.F("Export_GeneratedAt", document.GeneratedAt), StyleDefault)));
        data.Append(Row(row++, (ExportText.PrivacyNote, StyleDefault)));
        row++;
        foreach (var fact in document.Summary)
            data.Append(Row(row++, (fact.Label, StyleLabel), (fact.Value, StyleDefault)));

        return new Worksheet(
            new Columns(Col(1, 28), Col(2, 22)),
            data);
    }

    private static Worksheet TableSheet(ExportTable table)
    {
        var data = new SheetData();
        data.Append(Row(1, table.Headers.Select(h => (h, StyleHeader)).ToArray()));

        var rowNumber = 2u;
        foreach (var values in table.Rows)
        {
            var cells = values.Select((v, i) => (v, table.CodeColumns.Contains(i) ? StyleCode : StyleTop)).ToArray();
            data.Append(Row(rowNumber++, cells));
        }

        var columns = new Columns();
        for (var c = 0; c < table.Headers.Count; c++)
            columns.Append(Col((uint)c + 1, ColumnWidth(table, c)));

        var worksheet = new Worksheet(
            new SheetViews(new SheetView(
                new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
            { WorkbookViewId = 0 }),
            columns,
            data);

        if (table.Rows.Count > 0)
            worksheet.Append(new AutoFilter { Reference = $"A1:{ColumnLetter(table.Headers.Count)}{table.Rows.Count + 1}" });

        return worksheet;
    }

    private static Row Row(uint index, params (string Text, uint Style)[] cells)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < cells.Length; i++)
        {
            var (text, style) = cells[i];
            var clean = ExportText.StripInvalidXmlChars(text);
            if (clean.Length > MaxCellLength) clean = clean[..(MaxCellLength - 1)] + "…";

            row.Append(new Cell
            {
                CellReference = $"{ColumnLetter(i + 1)}{index}",
                DataType = CellValues.InlineString,
                StyleIndex = style,
                InlineString = new InlineString(new Text(clean) { Space = SpaceProcessingModeValues.Preserve })
            });
        }
        return row;
    }

    private static Column Col(uint index, double width) =>
        new() { Min = index, Max = index, Width = width, CustomWidth = true };

    /// <summary>Ancho según el contenido de las primeras filas (en caracteres), acotado.</summary>
    private static double ColumnWidth(ExportTable table, int column)
    {
        var isCode = table.CodeColumns.Contains(column);
        var longest = table.Headers[column].Length;
        foreach (var row in table.Rows.Take(200))
        {
            if (column >= row.Count) continue;
            var firstLine = row[column].Split('\n', 2)[0];
            longest = Math.Max(longest, firstLine.Length);
        }
        return Math.Clamp(longest + 2, 8, isCode ? 70 : 45);
    }

    /// <summary>A, B, … Z, AA, AB…</summary>
    internal static string ColumnLetter(int number)
    {
        var letters = "";
        while (number > 0)
        {
            var rest = (number - 1) % 26;
            letters = (char)('A' + rest) + letters;
            number = (number - 1) / 26;
        }
        return letters;
    }

    /// <summary>Excel limita los nombres de hoja a 31 caracteres, sin []:*?/\ y sin repetirse.</summary>
    internal static string UniqueName(string title, HashSet<string> used)
    {
        var name = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        if (name.Length == 0) name = "Hoja";
        if (name.Length > 31) name = name[..31];

        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = name[..Math.Min(name.Length, 31 - suffix.Length)] + suffix;
        }
        return candidate;
    }

    private static void AddStyles(WorkbookPart workbookPart)
    {
        var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),                                         // 0
                new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FF151616" }, new FontName { Val = "Calibri" }), // 1
                new Font(new FontSize { Val = 10 }, new FontName { Val = "Consolas" }),                                        // 2
                new Font(new Bold(), new FontSize { Val = 18 }, new FontName { Val = "Calibri" })),                            // 3
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FFFFF3C4" }) { PatternType = PatternValues.Solid })),
            new Borders(
                new Border(),
                new Border(new BottomBorder(new Color { Rgb = "FFDCDCD6" }) { Style = BorderStyleValues.Thin })),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(
                new CellFormat(),                                                                                              // 0 normal
                new CellFormat { FontId = 1, FillId = 2, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },    // 1 encabezado
                new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }) { FontId = 2, ApplyFont = true, ApplyAlignment = true }, // 2 código
                new CellFormat(new Alignment { Vertical = VerticalAlignmentValues.Top }) { ApplyAlignment = true },            // 3 texto
                new CellFormat { FontId = 3, ApplyFont = true },                                                               // 4 título
                new CellFormat { FontId = 1, ApplyFont = true }));                                                             // 5 etiqueta
        styles.Stylesheet.Save();
    }
}
