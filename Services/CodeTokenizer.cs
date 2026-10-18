using System.Text;

namespace PBIExplorer.Services;

public enum CodeLanguage
{
    Dax,
    M
}

public enum CodeKind
{
    Plain,
    Keyword,
    Function,
    String,
    Number,
    Comment,
    Reference
}

public readonly record struct CodeToken(CodeKind Kind, string Text);

/// <summary>
/// Divide una fórmula DAX o una consulta M en fragmentos para colorearla. Es un lexer
/// liviano, no un parser: alcanza para resaltar sin equivocarse con strings y comentarios,
/// y nunca falla — ante algo raro lo deja como texto plano. Siempre se cumple que
/// concatenar el texto de todos los tokens devuelve el código original.
/// </summary>
public static class CodeTokenizer
{
    private static readonly HashSet<string> DaxKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "VAR", "RETURN", "TRUE", "FALSE", "IN", "NOT", "DEFINE", "EVALUATE", "MEASURE",
        "ORDER", "BY", "ASC", "DESC", "START", "AT", "COLUMN", "TABLE"
    };

    private static readonly HashSet<string> MKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "each", "else", "error", "false", "if", "in", "is", "let", "meta", "not",
        "null", "or", "otherwise", "section", "shared", "then", "true", "try", "type",
        "#binary", "#date", "#datetime", "#datetimezone", "#duration", "#infinity", "#nan",
        "#sections", "#shared", "#table", "#time"
    };

    public static IReadOnlyList<CodeToken> Tokenize(string? code, CodeLanguage language)
    {
        var tokens = new List<CodeToken>();
        if (string.IsNullOrEmpty(code)) return tokens;

        var i = 0;
        var n = code.Length;
        var plain = new StringBuilder();

        void Flush()
        {
            if (plain.Length == 0) return;
            tokens.Add(new CodeToken(CodeKind.Plain, plain.ToString()));
            plain.Clear();
        }

        void Emit(CodeKind kind, int start, int end)
        {
            Flush();
            tokens.Add(new CodeToken(kind, code[start..end]));
            i = end;
        }

        while (i < n)
        {
            var c = code[i];

            // Comentarios: // y /* */ en ambos lenguajes; -- solo en DAX.
            if (c == '/' && i + 1 < n && code[i + 1] == '/' || language == CodeLanguage.Dax && c == '-' && i + 1 < n && code[i + 1] == '-')
            {
                Emit(CodeKind.Comment, i, LineEnd(code, i));
                continue;
            }
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                Emit(CodeKind.Comment, i, close < 0 ? n : close + 2);
                continue;
            }

            if (c == '"')
            {
                Emit(CodeKind.String, i, StringEnd(code, i));
                continue;
            }

            if (language == CodeLanguage.M && c == '#' && i + 1 < n && code[i + 1] == '"')
            {
                // Identificador entre comillas de M (#"Paso 1"): se destaca como referencia.
                Emit(CodeKind.Reference, i, StringEnd(code, i + 1));
                continue;
            }

            if (language == CodeLanguage.Dax)
            {
                if (c == '\'')
                {
                    var end = code.IndexOf('\'', i + 1);
                    if (end > 0)
                    {
                        end++;
                        Emit(CodeKind.Reference, i, BracketEnd(code, end));
                        continue;
                    }
                }
                if (c == '[')
                {
                    var end = code.IndexOf(']', i + 1);
                    if (end > 0)
                    {
                        Emit(CodeKind.Reference, i, end + 1);
                        continue;
                    }
                }
            }

            if (char.IsDigit(c))
            {
                Emit(CodeKind.Number, i, NumberEnd(code, i));
                continue;
            }

            if (IsIdentStart(c) || language == CodeLanguage.M && c == '#' && i + 1 < n && IsIdentStart(code[i + 1]))
            {
                var end = IdentEnd(code, i, language);
                var word = code[i..end];

                if (language == CodeLanguage.Dax)
                {
                    if (end < n && code[end] == '[')
                    {
                        // Tabla[Columna]
                        var close = code.IndexOf(']', end + 1);
                        Emit(CodeKind.Reference, i, close > 0 ? close + 1 : end);
                    }
                    else if (NextNonSpace(code, end) == '(') Emit(CodeKind.Function, i, end);
                    else if (DaxKeywords.Contains(word)) Emit(CodeKind.Keyword, i, end);
                    else Emit(CodeKind.Plain, i, end);
                }
                else
                {
                    if (MKeywords.Contains(word)) Emit(CodeKind.Keyword, i, end);
                    else if (NextNonSpace(code, end) == '(' || char.IsUpper(word[0]) && word.Contains('.')) Emit(CodeKind.Function, i, end);
                    else Emit(CodeKind.Plain, i, end);
                }
                continue;
            }

            plain.Append(c);
            i++;
        }

        Flush();
        return Merge(tokens);
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static int IdentEnd(string code, int start, CodeLanguage language)
    {
        var i = start;
        if (code[i] == '#') i++;
        // En M los nombres de función llevan puntos (Table.SelectRows); en DAX no.
        while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_' || language == CodeLanguage.M && code[i] == '.' &&
                                   i + 1 < code.Length && IsIdentStart(code[i + 1])))
            i++;
        return i;
    }

    private static int NumberEnd(string code, int start)
    {
        var i = start;
        while (i < code.Length && char.IsDigit(code[i])) i++;
        if (i + 1 < code.Length && code[i] == '.' && char.IsDigit(code[i + 1]))
        {
            i++;
            while (i < code.Length && char.IsDigit(code[i])) i++;
        }
        if (i < code.Length && (code[i] == 'e' || code[i] == 'E'))
        {
            var j = i + 1;
            if (j < code.Length && (code[j] == '+' || code[j] == '-')) j++;
            if (j < code.Length && char.IsDigit(code[j]))
            {
                while (j < code.Length && char.IsDigit(code[j])) j++;
                i = j;
            }
        }
        return i;
    }

    /// <summary>Fin de un string que empieza en <paramref name="start"/> (una comilla doble); "" es una comilla escapada.</summary>
    private static int StringEnd(string code, int start)
    {
        var i = start + 1;
        while (i < code.Length)
        {
            if (code[i] == '"')
            {
                if (i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return code.Length;
    }

    /// <summary>Si después de 'Tabla' viene [Columna], el fin de esa columna; si no, <paramref name="pos"/>.</summary>
    private static int BracketEnd(string code, int pos)
    {
        if (pos < code.Length && code[pos] == '[')
        {
            var close = code.IndexOf(']', pos + 1);
            if (close > 0) return close + 1;
        }
        return pos;
    }

    private static int LineEnd(string code, int start)
    {
        var end = code.IndexOf('\n', start);
        return end < 0 ? code.Length : end;
    }

    private static char NextNonSpace(string code, int from)
    {
        for (var i = from; i < code.Length; i++)
            if (!char.IsWhiteSpace(code[i])) return code[i];
        return '\0';
    }

    private static List<CodeToken> Merge(List<CodeToken> tokens)
    {
        var merged = new List<CodeToken>(tokens.Count);
        foreach (var token in tokens)
        {
            if (merged.Count > 0 && merged[^1].Kind == CodeKind.Plain && token.Kind == CodeKind.Plain)
                merged[^1] = new CodeToken(CodeKind.Plain, merged[^1].Text + token.Text);
            else
                merged.Add(token);
        }
        return merged;
    }
}
