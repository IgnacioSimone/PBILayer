namespace PBIExplorer.Services;

/// <summary>Cómo se escribe, en las listas "depende de" / "usado por", cada tipo de objeto:
/// <c>[Medida]</c>, <c>Tabla[Columna]</c> y <c>Consulta «Nombre»</c>.</summary>
public static class UsageLabels
{
    private const string QueryPrefix = "Consulta «";

    public static string Query(string name) => $"{QueryPrefix}{name}»";

    /// <summary>La etiqueta tal como se muestra: "Consulta «X»" o "Query «X»" según el idioma. Las otras ("[Medida]",
    /// "Tabla[Columna]") son nombres del modelo y se muestran igual en cualquier idioma.</summary>
    public static string Display(string label) =>
        TryParseQuery(label, out var name) ? $"{Terms.Display("Consulta")} «{name}»" : label;

    public static bool TryParseQuery(string label, out string name)
    {
        if (label.StartsWith(QueryPrefix, StringComparison.Ordinal) && label.EndsWith('»'))
        {
            name = label[QueryPrefix.Length..^1];
            return true;
        }
        name = "";
        return false;
    }
}
