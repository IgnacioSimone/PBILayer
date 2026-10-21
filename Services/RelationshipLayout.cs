using PBIExplorer.Models;

namespace PBIExplorer.Services;

/// <summary>Una tabla dibujada en el mapa de relaciones (posición de su esquina superior izquierda).</summary>
public sealed record MapNode(string Name, double X, double Y, double Width, double Height, int Degree, bool IsHidden);

/// <summary>Una relación dibujada entre dos tablas. <see cref="FromEnd"/> y <see cref="ToEnd"/> son "1" o "*" (o "" si no se pudo leer).</summary>
public sealed record MapEdge(
    string From, string To, string FromEnd, string ToEnd,
    bool IsActive, bool FilterBothWays, string Description);

public sealed record MapLayout(IReadOnlyList<MapNode> Nodes, IReadOnlyList<MapEdge> Edges, double Width, double Height)
{
    public static MapLayout Empty { get; } = new(Array.Empty<MapNode>(), Array.Empty<MapEdge>(), 0, 0);
    public bool IsEmpty => Nodes.Count == 0;
}

/// <summary>
/// Acomoda las tablas del modelo para dibujarlas como un mapa: la tabla más conectada de cada grupo va al
/// centro y las que se relacionan con ella se reparten en anillos alrededor (por distancia de relación).
/// Es determinista (mismo modelo, mismo dibujo) y no depende de ninguna pantalla: se prueba sola.
/// </summary>
public static class RelationshipLayout
{
    public const double NodeHeight = 34;
    private const double MinNodeWidth = 96;
    private const double MaxNodeWidth = 230;
    private const double Gap = 26;          // aire entre dos tablas del mismo anillo
    private const double RingStep = 120;    // distancia mínima entre anillos (en vertical)
    private const double Stretch = 2.2;    // los anillos son elipses más anchas que altas: la ventana es horizontal
    private const double Margin = 48;
    private const double WrapWidth = 2200;  // ancho a partir del cual los grupos pasan a la fila de abajo

    public static MapLayout Compute(IEnumerable<RelationshipInfo> relationships, ISet<string>? hiddenTables = null)
    {
        var list = relationships.Where(r => !string.IsNullOrWhiteSpace(r.FromTable) && !string.IsNullOrWhiteSpace(r.ToTable)).ToList();
        if (list.Count == 0) return MapLayout.Empty;

        // Vecinos (sin repetir y sin la relación de una tabla consigo misma).
        var neighbors = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var r in list)
        {
            neighbors.TryAdd(r.FromTable, new SortedSet<string>(StringComparer.Ordinal));
            neighbors.TryAdd(r.ToTable, new SortedSet<string>(StringComparer.Ordinal));
            if (r.FromTable == r.ToTable) continue;
            neighbors[r.FromTable].Add(r.ToTable);
            neighbors[r.ToTable].Add(r.FromTable);
        }

        var width = neighbors.Keys.ToDictionary(n => n, NodeWidth);
        var positions = new Dictionary<string, (double X, double Y)>();   // centros, en coordenadas locales de cada grupo
        var groups = new List<(List<string> Members, double Radius)>();

        var visited = new HashSet<string>();
        foreach (var name in neighbors.Keys.OrderByDescending(n => neighbors[n].Count).ThenBy(n => n, StringComparer.Ordinal))
        {
            if (visited.Contains(name)) continue;

            // Niveles por distancia al centro del grupo (recorrido en anchura).
            var levels = new List<List<string>> { new() { name } };
            visited.Add(name);
            while (true)
            {
                var next = new List<string>();
                foreach (var parent in levels[^1])
                    foreach (var n in neighbors[parent])
                        if (visited.Add(n)) next.Add(n);
                if (next.Count == 0) break;
                levels.Add(next);
            }

            var members = levels.SelectMany(l => l).ToList();
            var angle = new Dictionary<string, double> { [name] = 0 };
            positions[name] = (0, 0);
            double previousRadius = 0, maxRadius = width[name] / 2;

            for (var k = 1; k < levels.Count; k++)
            {
                var ring = levels[k];

                // Se ordena por el ángulo medio de sus vecinos ya ubicados, para que las líneas se crucen menos.
                ring = ring
                    .OrderBy(n => MeanAngle(neighbors[n].Where(angle.ContainsKey).Select(m => angle[m])))
                    .ThenBy(n => n, StringComparer.Ordinal)
                    .ToList();

                var needed = ring.Sum(n => width[n] + Gap) / (2 * Math.PI) / Stretch;
                var radius = Math.Max(previousRadius + RingStep, needed);
                var offset = -Math.PI / 2 + (k % 2 == 0 ? Math.PI / ring.Count : 0);

                for (var i = 0; i < ring.Count; i++)
                {
                    var a = offset + 2 * Math.PI * i / ring.Count;
                    angle[ring[i]] = a;
                    positions[ring[i]] = (radius * Stretch * Math.Cos(a), radius * Math.Sin(a));
                }

                previousRadius = radius;
                maxRadius = Math.Max(maxRadius, radius * Stretch + MaxNodeWidth / 2);
            }

            groups.Add((members, maxRadius));
        }

        // Los grupos (conjuntos de tablas conectadas entre sí) se ponen uno al lado del otro, en filas.
        var placed = new Dictionary<string, (double X, double Y)>();
        double cursorX = Margin, cursorY = Margin, rowHeight = 0;
        foreach (var (members, radius) in groups.OrderByDescending(g => g.Members.Count))
        {
            var size = radius * 2 + Gap;
            if (cursorX > Margin && cursorX + size > WrapWidth)
            {
                cursorX = Margin;
                cursorY += rowHeight + Gap;
                rowHeight = 0;
            }

            var cx = cursorX + radius + Gap / 2;
            var cy = cursorY + radius + Gap / 2;
            foreach (var m in members) placed[m] = (cx + positions[m].X, cy + positions[m].Y);

            cursorX += size + Gap;
            rowHeight = Math.Max(rowHeight, size);
        }

        // Se normaliza para que nada quede con coordenadas negativas.
        var minX = placed.Min(p => p.Value.X - width[p.Key] / 2);
        var minY = placed.Min(p => p.Value.Y - NodeHeight / 2);
        var shiftX = Margin - minX;
        var shiftY = Margin - minY;

        var hidden = hiddenTables ?? new HashSet<string>();
        var nodes = placed
            .Select(p => new MapNode(
                p.Key,
                p.Value.X - width[p.Key] / 2 + shiftX,
                p.Value.Y - NodeHeight / 2 + shiftY,
                width[p.Key], NodeHeight,
                neighbors[p.Key].Count,
                hidden.Contains(p.Key)))
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

        var edges = list.Where(r => r.FromTable != r.ToTable).Select(ToEdge).ToList();

        return new MapLayout(nodes, edges,
            nodes.Max(n => n.X + n.Width) + Margin,
            nodes.Max(n => n.Y + n.Height) + Margin);
    }

    private static MapEdge ToEdge(RelationshipInfo r)
    {
        // "Muchos a uno": primero el extremo de la tabla "desde", después el de la tabla "hacia" (valores en español canónico).
        var parts = r.Cardinality.Split(" a ", 2, StringSplitOptions.TrimEntries);
        string End(string? text) => text is null ? "" : text.StartsWith("uno", StringComparison.OrdinalIgnoreCase) ? "1"
            : text.StartsWith("muchos", StringComparison.OrdinalIgnoreCase) ? "*" : "";

        return new MapEdge(
            r.FromTable, r.ToTable,
            End(parts.ElementAtOrDefault(0)), End(parts.ElementAtOrDefault(1)),
            r.IsActive,
            r.CrossFilterBehavior == "Ambos sentidos",
            $"{r.FromTable}[{r.FromColumn}] → {r.ToTable}[{r.ToColumn}]");
    }

    private static double NodeWidth(string name) => Math.Clamp(name.Length * 7.2 + 30, MinNodeWidth, MaxNodeWidth);

    /// <summary>Promedio de ángulos (por sus senos y cosenos, para que -179° y 179° queden cerca).</summary>
    private static double MeanAngle(IEnumerable<double> angles)
    {
        double sin = 0, cos = 0;
        var any = false;
        foreach (var a in angles) { sin += Math.Sin(a); cos += Math.Cos(a); any = true; }
        return any ? Math.Atan2(sin, cos) : 0;
    }
}
