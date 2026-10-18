using PBIExplorer.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PBIExplorer.Services.Export;

/// <summary>Documento de Word (.docx) sin necesitar Office instalado: portada con resumen y una sección por
/// tabla, en hoja A4 apaisada, con el encabezado repetido en cada página y el código en monoespaciado.</summary>
public static class WordExporter
{
    // A4 apaisada, en veinteavos de punto (twips), con márgenes de 1,27 cm.
    private const int PageWidth = 16838;
    private const int PageHeight = 11906;
    private const int Margin = 720;
    private const int UsableWidth = PageWidth - 2 * Margin;

    public static void WriteFile(string path, ExportDocument document)
    {
        using var word = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var main = word.AddMainDocumentPart();
        AddStyles(main);

        var body = new Body();
        body.Append(Paragraph(document.SourceName, "Title"));
        body.Append(Paragraph(Loc.F("Export_DocGeneratedAt", document.GeneratedAt), "Subtitle"));
        body.Append(Paragraph(ExportText.PrivacyNote, "Note"));

        body.Append(Paragraph(Loc.T("Export_Summary"), "Heading1"));
        body.Append(SummaryTable(document.Summary));

        foreach (var table in document.Tables)
        {
            body.Append(Paragraph(table.Title, "Heading1", pageBreakBefore: true));
            if (!string.IsNullOrEmpty(table.Note)) body.Append(Paragraph(table.Note, "Note"));
            body.Append(table.Rows.Count == 0 ? Paragraph(Loc.T("Export_NoRows"), "Note") : DataTable(table));
        }

        body.Append(new SectionProperties(
            new PageSize { Width = PageWidth, Height = PageHeight, Orient = PageOrientationValues.Landscape },
            new PageMargin { Top = Margin, Bottom = Margin, Left = (uint)Margin, Right = (uint)Margin }));

        main.Document = new Document(body);
        main.Document.Save();
    }

    private static Paragraph Paragraph(string text, string style, bool pageBreakBefore = false)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        if (pageBreakBefore) properties.Append(new PageBreakBefore());
        return new Paragraph(properties, new Run(TextOf(text)));
    }

    private static Table SummaryTable(IReadOnlyList<ExportFact> facts)
    {
        var table = NewTable(new[] { 3200, 3200 });
        foreach (var fact in facts)
        {
            table.Append(new TableRow(
                Cell(fact.Label, 3200, bold: true),
                Cell(fact.Value, 3200)));
        }
        return table;
    }

    private static Table DataTable(ExportTable data)
    {
        var widths = ColumnWidths(data);
        var table = NewTable(widths);

        // Encabezado: se repite arriba de cada página.
        var header = new TableRow(new TableRowProperties(new TableHeader(), new CantSplit()));
        for (var c = 0; c < data.Headers.Count; c++)
            header.Append(Cell(data.Headers[c], widths[c], bold: true, shade: "FFF3C4"));
        table.Append(header);

        foreach (var row in data.Rows)
        {
            var tableRow = new TableRow();
            for (var c = 0; c < data.Headers.Count; c++)
                tableRow.Append(Cell(c < row.Count ? row[c] : "", widths[c], code: data.CodeColumns.Contains(c)));
            table.Append(tableRow);
        }
        return table;
    }

    private static Table NewTable(int[] widths)
    {
        var borders = new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4, Color = "DCDCD6" },
            new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "DCDCD6" },
            new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "DCDCD6" },
            new RightBorder { Val = BorderValues.Single, Size = 4, Color = "DCDCD6" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "DCDCD6" },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "DCDCD6" });

        var table = new Table(new TableProperties(
            new TableWidth { Width = widths.Sum().ToString(), Type = TableWidthUnitValues.Dxa },
            borders,
            new TableLayout { Type = TableLayoutValues.Fixed },
            new TableCellMarginDefault(
                new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 80, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 80, Type = TableWidthValues.Dxa })));

        var grid = new TableGrid();
        foreach (var width in widths) grid.Append(new GridColumn { Width = width.ToString() });
        table.Append(grid);
        return table;
    }

    private static TableCell Cell(string text, int width, bool bold = false, bool code = false, string? shade = null)
    {
        var properties = new TableCellProperties(new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });
        if (shade is not null) properties.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = shade });

        var cell = new TableCell(properties);

        // Un salto de línea del texto (DAX / M de varias líneas) es un párrafo propio, para que se conserve la sangría.
        var lines = ExportText.StripInvalidXmlChars(text).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            // Orden que exige el esquema: fuente, negrita, tamaño.
            var runProperties = new RunProperties();
            if (code) runProperties.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            if (bold) runProperties.Append(new Bold());
            runProperties.Append(new FontSize { Val = code ? "15" : "16" });

            cell.Append(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "0", Before = "0" }),
                new Run(runProperties, TextOf(line))));
        }
        return cell;
    }

    private static Text TextOf(string text) =>
        new(ExportText.StripInvalidXmlChars(text)) { Space = SpaceProcessingModeValues.Preserve };

    /// <summary>Reparte el ancho útil de la hoja según cuánto texto tiene cada columna (acotado), con más
    /// lugar para el código.</summary>
    internal static int[] ColumnWidths(ExportTable table)
    {
        var weights = new double[table.Headers.Count];
        for (var c = 0; c < weights.Length; c++)
        {
            var longest = table.Headers[c].Length;
            foreach (var row in table.Rows.Take(200))
                if (c < row.Count) longest = Math.Max(longest, row[c].Split('\n', 2)[0].Length);

            var isCode = table.CodeColumns.Contains(c);
            weights[c] = Math.Clamp(longest, 6, isCode ? 70 : 32) * (isCode ? 1.3 : 1.0);
        }

        var total = weights.Sum();
        var widths = weights.Select(w => (int)Math.Max(700, UsableWidth * w / total)).ToArray();

        // El redondeo y el mínimo pueden pasarse del ancho de la hoja: se ajusta con la columna más ancha.
        var excess = widths.Sum() - UsableWidth;
        if (excess > 0)
        {
            var widest = Array.IndexOf(widths, widths.Max());
            widths[widest] -= excess;
        }
        return widths;
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new FontSize { Val = "20" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "120" }))),
            ParagraphStyle("Normal", "Normal", isDefault: true),
            ParagraphStyle("Title", "Title", size: "52", bold: true, after: "60"),
            ParagraphStyle("Subtitle", "Subtitle", size: "24", color: "565753", after: "60"),
            ParagraphStyle("Note", "Note", size: "18", color: "565753", after: "160"),
            ParagraphStyle("Heading1", "heading 1", size: "34", bold: true, color: "8A6400", before: "120", after: "160", keepNext: true));
        part.Styles.Save();
    }

    private static Style ParagraphStyle(string id, string name, string? size = null, bool bold = false, string? color = null,
        string? before = null, string? after = null, bool keepNext = false, bool isDefault = false)
    {
        var style = new Style(new StyleName { Val = name }, new PrimaryStyle())
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
            Default = isDefault ? true : null
        };

        var paragraph = new StyleParagraphProperties();
        if (keepNext) paragraph.Append(new KeepNext());
        if (before is not null || after is not null)
        {
            // Solo los atributos que existen: uno vacío no valida.
            var spacing = new SpacingBetweenLines();
            if (before is not null) spacing.Before = before;
            if (after is not null) spacing.After = after;
            paragraph.Append(spacing);
        }
        if (paragraph.HasChildren) style.Append(paragraph);

        var run = new StyleRunProperties();
        if (bold) run.Append(new Bold());
        if (color is not null) run.Append(new Color { Val = color });
        if (size is not null) run.Append(new FontSize { Val = size });
        if (run.HasChildren) style.Append(run);

        return style;
    }
}
