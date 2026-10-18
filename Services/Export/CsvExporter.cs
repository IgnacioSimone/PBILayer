using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PBIExplorer.Services.Export;

public static class CsvExporter
{
    /// <summary>El separador de lista de Windows (";" en configuración regional de Argentina), para que
    /// Excel abra las columnas separadas con un doble clic.</summary>
    public static string DefaultDelimiter => CultureInfo.CurrentCulture.TextInfo.ListSeparator is { Length: > 0 } s ? s : ",";

    public static void Write(Stream stream, ExportTable table, string? delimiter = null)
    {
        delimiter ??= DefaultDelimiter;

        // UTF-8 con marca de orden: sin ella Excel rompe los acentos.
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        writer.WriteLine(string.Join(delimiter, table.Headers.Select(h => Field(h, delimiter))));
        foreach (var row in table.Rows)
            writer.WriteLine(string.Join(delimiter, row.Select(v => Field(v, delimiter))));
    }

    public static void WriteFile(string path, ExportTable table)
    {
        using var stream = File.Create(path);
        Write(stream, table);
    }

    /// <summary>Un .zip con un CSV por tabla.</summary>
    public static void WriteZip(string path, IEnumerable<ExportTable> tables)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        foreach (var table in tables)
        {
            var name = $"{index++:00} {ExportText.SafeFileName(table.Title)}.csv";
            while (!used.Add(name)) name = "_" + name;

            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            Write(entryStream, table);
        }
    }

    internal static string Field(string? value, string delimiter)
    {
        var text = Neutralize(value ?? "");
        var needsQuotes = text.Contains(delimiter, StringComparison.Ordinal) || text.Contains('"') ||
                          text.Contains('\n') || text.Contains('\r');
        return needsQuotes ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    /// <summary>Protección contra inyección de fórmulas (OWASP): Excel interpreta como fórmula una celda
    /// que empieza con = + - @ (o tab / retorno). Un apóstrofe adelante la deja como texto.</summary>
    internal static string Neutralize(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
}
