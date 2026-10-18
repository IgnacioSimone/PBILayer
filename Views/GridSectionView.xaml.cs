using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PBIExplorer.Views;

/// <summary>Marco común de las secciones con grilla: nota, buscador, DataGrid y estado
/// vacío. Cada sección solo declara sus columnas (propiedad de contenido) y, si hace
/// falta, un encabezado extra.</summary>
[ContentProperty(nameof(Columns))]
public partial class GridSectionView : UserControl
{
    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(GridSectionView));

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
}
