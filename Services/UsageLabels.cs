namespace PBIExplorer.Services;

/// <summary>Cómo se escribe, en las listas "depende de" / "usado por", cada tipo de objeto:
/// <c>[Medida]</c>, <c>Tabla[Columna]</c> y <c>Consulta «Nombre»</c>.</summary>
public static class UsageLabels
{
    private const string QueryPrefix = "Consulta «";

    public static string Query(string name) => $"{QueryPrefix}{name}»";

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
