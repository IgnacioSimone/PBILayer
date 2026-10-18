using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace PBIExplorer.Views;

/// <summary>Marco común de las secciones con grilla: nota, contador, DataGrid, copiar y
/// estado vacío. Cada sección solo declara sus columnas (propiedad de contenido) y, si
/// hace falta, un encabezado extra.</summary>
[ContentProperty(nameof(Columns))]
public partial class GridSectionView : UserControl
{
    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(GridSectionView));

    private DataGridCell? _contextCell;

    public GridSectionView()
    {
        InitializeComponent();
        Columns.CollectionChanged += OnColumnsChanged;
    }

    public ObservableCollection<DataGridColumn> Columns { get; } = new();

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (DataGridColumn column in e.NewItems)
            Grid.Columns.Add(column);
    }

    // =====================================================================
    // Copiar al portapapeles
    // =====================================================================

    /// <summary>El clic derecho también selecciona la fila y recuerda la celda: el menú
    /// contextual se abre después y necesita saber sobre cuál se hizo.</summary>
    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contextCell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (_contextCell is null) return;

        var row = FindParent<DataGridRow>(_contextCell);
        if (row is not null) row.IsSelected = true;
    }

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (_contextCell is null) return;
        SetClipboard(CellText(_contextCell.Column, _contextCell.DataContext));
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (_contextCell?.DataContext is null) return;
        SetClipboard(RowText(_contextCell.DataContext));
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join('\t', Grid.Columns.Select(c => c.Header?.ToString() ?? "")));
        foreach (var item in Grid.Items)
            builder.AppendLine(RowText(item));

        SetClipboard(builder.ToString());
    }

    private string RowText(object item) =>
        string.Join('\t', Grid.Columns.Select(c => CellText(c, item)));

    private static string CellText(DataGridColumn column, object item)
    {
        var content = column.OnCopyingCellClipboardContent(item)?.ToString() ?? "";
        // Un tab o salto de línea dentro de una celda (ej. DAX multilínea) rompería el pegado en Excel.
        return content.Replace("\t", " ").Replace("\r\n", " ").Replace('\n', ' ');
    }

    private static void SetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Otro programa tiene el portapapeles tomado en este instante; no vale la pena molestar.
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
