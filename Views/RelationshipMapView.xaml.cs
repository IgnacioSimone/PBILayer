using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PBIExplorer.Services;

namespace PBIExplorer.Views;

/// <summary>Dibuja el mapa de relaciones del modelo: cada tabla es una caja y cada relación una línea con
/// "1" / "*" en los extremos y una flecha hacia donde viaja el filtro. Todos los colores salen de los
/// recursos del tema (SetResourceReference), así que cambian solos al pasar de claro a oscuro.</summary>
public partial class RelationshipMapView : UserControl
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MapLayout), typeof(RelationshipMapView),
        new PropertyMetadata(MapLayout.Empty, (d, _) => ((RelationshipMapView)d).Render()));

    private const double MinZoom = 0.25, MaxZoom = 2.5;

    private readonly Dictionary<string, Border> _nodes = new();
    private readonly List<(MapEdge Edge, List<Shape> Shapes, List<FrameworkElement> Labels)> _edges = new();
    private bool _fitPending;

    public RelationshipMapView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (_fitPending) Fit(); };
        SizeChanged += (_, _) => { if (_fitPending) Fit(); };
    }

    public MapLayout Layout
    {
        get => (MapLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    private void Render()
    {
        Map.Children.Clear();
        _nodes.Clear();
        _edges.Clear();

        var layout = Layout;
        if (layout is null || layout.IsEmpty) return;

        Map.Width = layout.Width;
        Map.Height = layout.Height;

        var byName = layout.Nodes.ToDictionary(n => n.Name);

        // Primero las líneas (quedan debajo) y después las cajas.
        foreach (var edge in layout.Edges)
        {
            if (!byName.TryGetValue(edge.From, out var from) || !byName.TryGetValue(edge.To, out var to)) continue;
            _edges.Add(DrawEdge(edge, from, to));
        }

        foreach (var node in layout.Nodes) DrawNode(node);

        _fitPending = true;
        if (IsLoaded) Fit();
    }

    // ---------- Cajas ----------

    private void DrawNode(MapNode node)
    {
        var text = new TextBlock
        {
            Text = node.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 8, 0)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");

        var box = new Border
        {
            Width = node.Width, Height = node.Height, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5),
            Child = text, Opacity = node.IsHidden ? 0.55 : 1, Tag = node.IsHidden ? 0.55 : 1.0, Cursor = Cursors.Hand,
            ToolTip = $"{node.Name}\n{Txt("Map_Connections", node.Degree)}{(node.IsHidden ? "\n" + Txt("Map_HiddenTable") : "")}"
        };
        box.SetResourceReference(Border.BackgroundProperty, "SidebarBg");
        box.SetResourceReference(Border.BorderBrushProperty, "Separator");

        box.MouseEnter += (_, _) => Highlight(node.Name, true);
        box.MouseLeave += (_, _) => Highlight(node.Name, false);

        Canvas.SetLeft(box, node.X);
        Canvas.SetTop(box, node.Y);
        Panel.SetZIndex(box, 10);
        Map.Children.Add(box);
        _nodes[node.Name] = box;
    }

    // ---------- Líneas ----------

    private (MapEdge, List<Shape>, List<FrameworkElement>) DrawEdge(MapEdge edge, MapNode from, MapNode to)
    {
        var a = Center(from);
        var b = Center(to);
        var start = EdgePoint(from, a, b);
        var end = EdgePoint(to, b, a);

        var shapes = new List<Shape>();
        var labels = new List<FrameworkElement>();

        var line = new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, StrokeThickness = 1.6 };
        line.SetResourceReference(Shape.StrokeProperty, "InkFaint");
        if (!edge.IsActive) line.StrokeDashArray = new DoubleCollection { 4, 3 };
        shapes.Add(line);

        // Zona más ancha e invisible para que la línea sea fácil de apuntar con el mouse.
        var hit = new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, StrokeThickness = 14, Stroke = Brushes.Transparent, Cursor = Cursors.Arrow };
        var tip = edge.Description + (edge.IsActive ? "" : "\n" + Txt("Map_Inactive"))
                  + "\n" + (edge.FilterBothWays ? Txt("Map_FilterBoth") : Txt("Map_FilterOne", edge.To, edge.From));
        hit.ToolTip = tip;
        hit.MouseEnter += (_, _) => Highlight(edge.From, true, edge.To);
        hit.MouseLeave += (_, _) => Highlight(edge.From, false, edge.To);

        // Flecha: el filtro viaja desde la tabla "hacia" a la tabla "desde" (y en ambos sentidos si corresponde).
        shapes.Add(Arrow(start, end));
        if (edge.FilterBothWays) shapes.Add(Arrow(end, start));

        foreach (var s in shapes) Map.Children.Add(s);
        Map.Children.Add(hit);

        AddEnd(labels, edge.FromEnd, start, end);
        AddEnd(labels, edge.ToEnd, end, start);

        return (edge, shapes, labels);
    }

    /// <summary>Triángulo en el extremo <paramref name="tip"/>, mirando desde <paramref name="tail"/>.</summary>
    private static Polygon Arrow(Point tail, Point tip)
    {
        var dir = tip - tail;
        var length = dir.Length;
        if (length < 1) return new Polygon();
        dir /= length;
        var normal = new Vector(-dir.Y, dir.X);
        const double size = 10, half = 5;
        var back = tip - dir * size;

        var arrow = new Polygon { Points = new PointCollection { tip, back + normal * half, back - normal * half } };
        arrow.SetResourceReference(Shape.FillProperty, "InkFaint");
        return arrow;
    }

    /// <summary>Pastillita con "1" o "*" cerca de un extremo de la línea.</summary>
    private void AddEnd(List<FrameworkElement> labels, string text, Point at, Point other)
    {
        if (text.Length == 0) return;

        var dir = other - at;
        if (dir.Length < 1) return;
        dir.Normalize();
        var p = at + dir * 18;
        var normal = new Vector(-dir.Y, dir.X);
        p += normal * 11;   // un poco al costado, para no tapar la línea ni la flecha

        var chip = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        chip.SetResourceReference(Border.BorderBrushProperty, "Separator");
        ((TextBlock)chip.Child).SetResourceReference(TextBlock.ForegroundProperty, "Accent");

        Canvas.SetLeft(chip, p.X - 9);
        Canvas.SetTop(chip, p.Y - 9);
        Panel.SetZIndex(chip, 5);
        Map.Children.Add(chip);
        labels.Add(chip);
    }

    private static Point Center(MapNode n) => new(n.X + n.Width / 2, n.Y + n.Height / 2);

    /// <summary>Punto donde la línea que va de <paramref name="from"/> a <paramref name="toward"/> sale de la caja.</summary>
    private static Point EdgePoint(MapNode node, Point from, Point toward)
    {
        var d = toward - from;
        if (d.Length < 0.001) return from;

        var tx = d.X == 0 ? double.PositiveInfinity : node.Width / 2 / Math.Abs(d.X);
        var ty = d.Y == 0 ? double.PositiveInfinity : node.Height / 2 / Math.Abs(d.Y);
        return from + d * Math.Min(tx, ty);
    }

    // ---------- Resaltado ----------

    /// <summary>Al pasar por una tabla (o una línea) se resaltan sus relaciones y se atenúa el resto.</summary>
    private void Highlight(string table, bool on, string? otherTable = null)
    {
        foreach (var (edge, shapes, labels) in _edges)
        {
            var related = otherTable is null
                ? edge.From == table || edge.To == table
                : (edge.From == table && edge.To == otherTable) || (edge.From == otherTable && edge.To == table);

            foreach (var shape in shapes)
            {
                shape.Opacity = !on || related ? 1 : 0.15;
                if (shape is Line line) line.StrokeThickness = on && related ? 2.8 : 1.6;
                shape.SetResourceReference(shape is Polygon ? Shape.FillProperty : Shape.StrokeProperty, on && related ? "Accent" : "InkFaint");
            }
            foreach (var label in labels) label.Opacity = !on || related ? 1 : 0.15;
        }

        foreach (var (name, box) in _nodes)
        {
            var connected = name == table || name == otherTable ||
                            _edges.Any(e => (e.Edge.From == table && e.Edge.To == name) || (e.Edge.To == table && e.Edge.From == name));
            box.Opacity = !on || connected ? (double)box.Tag : 0.3;
        }
    }

    // ---------- Zoom ----------

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    private void Fit()
    {
        var layout = Layout;
        if (layout is null || layout.IsEmpty || Scroller.ActualWidth < 50 || Scroller.ActualHeight < 50) return;

        _fitPending = false;
        var scale = Math.Min(Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth, Scroller.ActualWidth) / layout.Width;
        scale = Math.Min(scale, (Scroller.ActualHeight - 8) / layout.Height);
        SetZoom(Math.Clamp(scale, MinZoom, 1));
        Scroller.ScrollToHome();
    }

    private void SetZoom(double value)
    {
        Zoom.ScaleX = Zoom.ScaleY = Math.Clamp(value, MinZoom, MaxZoom);
    }

    // Ctrl + rueda = zoom; la rueda sola desplaza como siempre.
    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(Zoom.ScaleX * (e.Delta > 0 ? 1.12 : 1 / 1.12));
        e.Handled = true;
    }

    private static string Txt(string key, params object[] args) =>
        args.Length == 0 ? Infrastructure.Loc.T(key) : Infrastructure.Loc.F(key, args);
}
