using System.Text;

namespace PBIExplorer.Services.Export;

/// <summary>Una tabla lista para escribir en cualquier formato: encabezados y filas ya como texto,
/// con los datos sensibles enmascarados.</summary>
/// <param name="CodeColumns">Índices de columnas con código (DAX / M): se muestran en monoespaciado y con saltos de línea.</param>
public sealed record ExportTable(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlySet<int> CodeColumns,
    string? Note = null);

public sealed record ExportFact(string Label, string Value);

/// <summary>Todo lo que lleva un documento exportado: de qué tablero es, cuándo se generó,
/// un resumen en números y las tablas de cada sección.</summary>
public sealed record ExportDocument(
    string SourceName,
    DateTime GeneratedAt,
    IReadOnlyList<ExportFact> Summary,
    IReadOnlyList<ExportTable> Tables);

public static class ExportText
{
    public static string PrivacyNote => Infrastructure.Loc.T("Export_PrivacyNote");

    /// <summary>Quita caracteres de control que XML (Word, Excel, HTML) no admite; deja tab y saltos de línea.</summary>
    public static string StripInvalidXmlChars(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var valid = c == '\t' || c == '\n' || c == '\r' || c >= 0x20 && c != 0xFFFE && c != 0xFFFF &&
                        !(c >= 0xD800 && c <= 0xDFFF && !IsValidSurrogate(text, i));
            if (!valid && sb is null) sb = new StringBuilder(text, 0, i, text.Length);
            if (valid) sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }

    private static bool IsValidSurrogate(string text, int i) =>
        char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ||
        char.IsLowSurrogate(text[i]) && i > 0 && char.IsHighSurrogate(text[i - 1]);

    /// <summary>Nombre de archivo seguro: sin caracteres que Windows no admite.</summary>
    public static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrEmpty(cleaned) ? "modelo" : cleaned;
    }
}
