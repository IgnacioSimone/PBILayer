using PBIExplorer.Models;
using PBIExplorer.Services;
using Xunit;

namespace PBIExplorer.Tests;

public class RelationshipLayoutTests
{
    private static RelationshipInfo Rel(string from, string to, string cardinality = "Muchos a uno",
        string filter = "Único sentido", bool active = true) => new()
    {
        FromTable = from, FromColumn = "id", ToTable = to, ToColumn = "id",
        Cardinality = cardinality, CrossFilterBehavior = filter, IsActive = active
    };

    private static bool Overlap(MapNode a, MapNode b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static void AssertNoOverlaps(MapLayout layout)
    {
        for (var i = 0; i < layout.Nodes.Count; i++)
            for (var j = i + 1; j < layout.Nodes.Count; j++)
                Assert.False(Overlap(layout.Nodes[i], layout.Nodes[j]),
                    $"{layout.Nodes[i].Name} y {layout.Nodes[j].Name} se superponen");
    }

    [Fact]
    public void Without_relationships_the_map_is_empty()
    {
        Assert.True(RelationshipLayout.Compute(Array.Empty<RelationshipInfo>()).IsEmpty);
    }

    [Fact]
    public void A_star_model_puts_the_most_connected_table_in_the_middle_and_nothing_overlaps()
    {
        var layout = RelationshipLayout.Compute(new[]
        {
            Rel("Ventas", "Clientes"), Rel("Ventas", "Productos"), Rel("Ventas", "Fechas"), Rel("Ventas", "Tiendas")
        });

        Assert.Equal(5, layout.Nodes.Count);
        Assert.Equal(4, layout.Edges.Count);
        AssertNoOverlaps(layout);

        var hub = layout.Nodes.Single(n => n.Name == "Ventas");
        Assert.Equal(4, hub.Degree);
        var hubCenter = (X: hub.X + hub.Width / 2, Y: hub.Y + hub.Height / 2);
        foreach (var other in layout.Nodes.Where(n => n != hub))
        {
            var d = Math.Sqrt(Math.Pow(other.X + other.Width / 2 - hubCenter.X, 2) + Math.Pow(other.Y + other.Height / 2 - hubCenter.Y, 2));
            Assert.True(d >= 100, "las tablas satélite deben quedar a distancia del centro");
        }
    }

    [Fact]
    public void Everything_fits_inside_the_reported_canvas_with_no_negative_coordinates()
    {
        var layout = RelationshipLayout.Compute(new[] { Rel("A", "B"), Rel("B", "C"), Rel("C", "D"), Rel("D", "A") });

        Assert.All(layout.Nodes, n =>
        {
            Assert.True(n.X >= 0 && n.Y >= 0);
            Assert.True(n.X + n.Width <= layout.Width && n.Y + n.Height <= layout.Height);
        });
    }

    [Fact]
    public void Separate_groups_of_tables_do_not_overlap()
    {
        var layout = RelationshipLayout.Compute(new[]
        {
            Rel("A1", "A2"), Rel("A1", "A3"), Rel("B1", "B2"), Rel("C1", "C2"), Rel("C1", "C3"), Rel("C1", "C4")
        });

        Assert.Equal(9, layout.Nodes.Count);
        AssertNoOverlaps(layout);
    }

    [Fact]
    public void The_layout_is_deterministic()
    {
        var rels = new[] { Rel("Ventas", "Clientes"), Rel("Ventas", "Fechas"), Rel("Clientes", "Regiones") };

        var one = RelationshipLayout.Compute(rels);
        var two = RelationshipLayout.Compute(rels.Reverse());

        Assert.Equal(one.Nodes, two.Nodes);
    }

    [Fact]
    public void Cardinality_and_filter_direction_are_read_from_the_relationship()
    {
        var layout = RelationshipLayout.Compute(new[]
        {
            Rel("Ventas", "Clientes", "Muchos a uno", "Único sentido"),
            Rel("Ventas", "Fechas", "Uno a uno", "Ambos sentidos", active: false)
        });

        var single = layout.Edges.Single(e => e.To == "Clientes");
        Assert.Equal("*", single.FromEnd);
        Assert.Equal("1", single.ToEnd);
        Assert.False(single.FilterBothWays);
        Assert.True(single.IsActive);

        var both = layout.Edges.Single(e => e.To == "Fechas");
        Assert.Equal("1", both.FromEnd);
        Assert.True(both.FilterBothWays);
        Assert.False(both.IsActive);
    }

    [Fact]
    public void Hidden_tables_are_flagged_and_self_relationships_are_not_drawn_as_lines()
    {
        var layout = RelationshipLayout.Compute(
            new[] { Rel("Ventas", "Auxiliar"), Rel("Ventas", "Ventas") },
            new HashSet<string> { "Auxiliar" });

        Assert.True(layout.Nodes.Single(n => n.Name == "Auxiliar").IsHidden);
        Assert.False(layout.Nodes.Single(n => n.Name == "Ventas").IsHidden);
        Assert.Single(layout.Edges);
    }

    [Fact]
    public void A_big_model_is_laid_out_without_overlaps()
    {
        var rels = Enumerable.Range(1, 60).Select(i => Rel("Hechos", $"Dimension numero {i}"))
            .Concat(Enumerable.Range(1, 15).Select(i => Rel($"Dimension numero {i}", $"Sub {i}")))
            .ToList();

        var layout = RelationshipLayout.Compute(rels);

        Assert.Equal(76, layout.Nodes.Count);
        AssertNoOverlaps(layout);
    }
}
