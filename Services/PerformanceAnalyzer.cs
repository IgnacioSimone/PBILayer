using PBIExplorer.Infrastructure;
using PBIExplorer.Models;

namespace PBIExplorer.Services;

public enum PerformanceSeverity { Alta, Media, Baja }

public class PerformanceWarning
{
    public PerformanceSeverity Severity { get; set; }
    public string Category { get; set; } = "";
    public string Table { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";

    public string SeverityText => Severity switch
    {
        PerformanceSeverity.Alta => Loc.T("Severity_High"),
        PerformanceSeverity.Media => Loc.T("Severity_Medium"),
        _ => Loc.T("Severity_Low")
    };
}

/// <summary>
/// Alertas de rendimiento basadas únicamente en metadata del modelo (relaciones,
/// tipos de columna, texto de las fórmulas DAX) — esta herramienta nunca consulta
/// filas reales, así que NO puede evaluar cardinalidad real de una columna ni el
/// tiempo real de un query. Son heurísticas de patrones conocidos como antipatrón,
/// no un profiler: hay que leerlas como pistas para revisar, no como diagnóstico.
/// </summary>
public static class PerformanceAnalyzer
{
    public static List<PerformanceWarning> Analyze(ModelSnapshot model)
    {
        var warnings = new List<PerformanceWarning>();

        foreach (var r in model.Relationships.Where(r => r.CrossFilterBehavior == "Ambos sentidos"))
        {
            warnings.Add(new PerformanceWarning
            {
                Severity = PerformanceSeverity.Alta,
                Category = Loc.T("Perf_Bidirectional_Cat"),
                Table = r.FromTable,
                Subject = $"{r.FromTable}[{r.FromColumn}] ↔ {r.ToTable}[{r.ToColumn}]",
                Message = Loc.T("Perf_Bidirectional_Msg")
            });
        }

        foreach (var r in model.Relationships.Where(r =>
                     r.Cardinality.Contains("muchos a muchos", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(new PerformanceWarning
            {
                Severity = PerformanceSeverity.Alta,
                Category = Loc.T("Perf_ManyToMany_Cat"),
                Table = r.FromTable,
                Subject = $"{r.FromTable}[{r.FromColumn}] ↔ {r.ToTable}[{r.ToColumn}]",
                Message = Loc.T("Perf_ManyToMany_Msg")
            });
        }

        foreach (var group in model.Columns
                     .Where(c => c.ColumnType is "Calculada" or "De tabla calculada")
                     .GroupBy(c => c.Table))
        {
            var count = group.Count();
            if (count < 3) continue; // una o dos no ameritan alerta
            warnings.Add(new PerformanceWarning
            {
                Severity = PerformanceSeverity.Media,
                Category = Loc.T("Perf_CalcColumns_Cat"),
                Table = group.Key,
                Subject = group.Key,
                Message = Loc.F("Perf_CalcColumns_Msg", count)
            });
        }

        const int nestedCalculateThreshold = 3;
        foreach (var m in model.Measures)
        {
            var depth = MaxNestedCalculateDepth(m.Expression);
            if (depth < nestedCalculateThreshold) continue;

            warnings.Add(new PerformanceWarning
            {
                Severity = PerformanceSeverity.Media,
                Category = Loc.T("Perf_NestedCalc_Cat"),
                Table = m.Table,
                Subject = m.Name,
                Message = Loc.F("Perf_NestedCalc_Msg", depth)
            });
        }

        return warnings
            .OrderBy(w => w.Severity) // Alta(0) < Media(1) < Baja(2) en el enum, en ese orden
            .ThenBy(w => w.Category)
            .ToList();
    }

    /// <summary>
    /// Heurística de texto (como DaxReferenceParser): cuenta el anidamiento máximo de
    /// llamadas a CALCULATE/CALCULATETABLE contando paréntesis, sin parsear DAX de
    /// verdad. No distingue si "CALCULATE" aparece dentro de un string literal DAX
    /// (raro, pero posible) — en ese caso podría sobrestimar el anidamiento en 1.
    /// </summary>
    internal static int MaxNestedCalculateDepth(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 0;

        var maxDepth = 0;
        var currentDepth = 0;
        var opensAreCalculate = new Stack<bool>();
        var i = 0;

        while (i < expression.Length)
        {
            var c = expression[i];

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_')) i++;
                var word = expression[start..i];

                var j = i;
                while (j < expression.Length && char.IsWhiteSpace(expression[j])) j++;

                if (j < expression.Length && expression[j] == '(')
                {
                    var isCalculate = word.Equals("CALCULATE", StringComparison.OrdinalIgnoreCase) ||
                                       word.Equals("CALCULATETABLE", StringComparison.OrdinalIgnoreCase);
                    opensAreCalculate.Push(isCalculate);
                    if (isCalculate)
                    {
                        currentDepth++;
                        if (currentDepth > maxDepth) maxDepth = currentDepth;
                    }
                    i = j + 1; // consumir el '(' de esta llamada
                    continue;
                }
                continue;
            }

            if (c == '(')
            {
                opensAreCalculate.Push(false);
                i++;
                continue;
            }

            if (c == ')')
            {
                if (opensAreCalculate.Count > 0 && opensAreCalculate.Pop())
                    currentDepth--;
                i++;
                continue;
            }

            i++;
        }

        return maxDepth;
    }
}
