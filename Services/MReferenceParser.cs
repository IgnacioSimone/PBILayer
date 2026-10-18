using System.Text.RegularExpressions;

namespace PBIExplorer.Services;

/// <summary>
/// Extrae, del código M de una consulta, a qué OTRAS consultas hace referencia (una tabla
/// auxiliar, un parámetro, una función). Es una heurística sobre el texto, no un parser de M:
/// ignora strings y comentarios, entiende <c>#"Nombre con espacios"</c> y los nombres sin
/// comillas, y descarta el acceso a campos <c>[Campo]</c>. Si el nombre de un paso interno
/// (<c>let Fact = ...</c>) coincide con el de otra consulta se cuenta como referencia de más:
/// el error va hacia "esta consulta sí se usa", que es el lado seguro al decidir qué borrar.
/// </summary>
public static partial class MReferenceParser
{
    public static List<string> ExtractReferences(string? mCode, IReadOnlySet<string> knownQueries, string? self = null)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(mCode) || knownQueries.Count == 0) return found;

        void Add(string name)
        {
            if (name != self && knownQueries.Contains(name) && !found.Contains(name)) found.Add(name);
        }

        foreach (var token in CodeTokenizer.Tokenize(mCode, CodeLanguage.M))
        {
            switch (token.Kind)
            {
                case CodeKind.Reference:
                    // #"Nombre ""con"" comillas"
                    if (token.Text.Length >= 3 && token.Text.StartsWith("#\"") && token.Text.EndsWith('"'))
                        Add(token.Text[2..^1].Replace("\"\"", "\""));
                    break;

                case CodeKind.Function:
                    Add(token.Text);   // una función propia: MiFuncion(...)
                    break;

                case CodeKind.Plain:
                    foreach (Match word in IdentifierPattern().Matches(token.Text))
                    {
                        // [Campo] es acceso a un campo de un registro, no una consulta.
                        var before = word.Index > 0 ? token.Text[word.Index - 1] : '\0';
                        var afterIndex = word.Index + word.Length;
                        var after = afterIndex < token.Text.Length ? token.Text[afterIndex] : '\0';
                        if (before == '[' && after == ']') continue;

                        Add(word.Value);
                    }
                    break;
            }
        }

        return found;
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*")]
    private static partial Regex IdentifierPattern();
}
