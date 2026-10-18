using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Models;
using PBIExplorer.Services;

namespace PBIExplorer.ViewModels;

/// <summary>Una sección de la barra lateral: título, icono y su estado vacío.</summary>
public abstract partial class SectionViewModel : ObservableObject
{
    protected SectionViewModel(string title, string icon)
    {
        Title = title;
        Icon = icon;
    }

    public string Title { get; }
    public string Icon { get; }

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _emptyIcon = "";
    [ObservableProperty] private string _emptyTitle = "";
    [ObservableProperty] private string _emptyBody = "";
    [ObservableProperty] private string? _emptyActionText;
    [ObservableProperty] private IRelayCommand? _emptyActionCommand;

    public void SetEmpty(bool isEmpty, string title = "", string body = "")
    {
        IsEmpty = isEmpty;
        EmptyTitle = title;
        EmptyBody = body;
    }
}

/// <summary>Sección con una grilla de solo lectura, buscador y vista filtrable.</summary>
public abstract partial class GridSectionViewModel<T> : SectionViewModel where T : class
{
    protected GridSectionViewModel(string title, string icon, string? note = null, bool hasSearch = true)
        : base(title, icon)
    {
        Note = note;
        HasSearch = hasSearch;
        _view = CollectionViewSource.GetDefaultView(new ObservableCollection<T>());
    }

    public string? Note { get; }
    public bool HasSearch { get; }

    [ObservableProperty] private ICollectionView _view;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _totalCount;

    public int ItemCount => TotalCount;

    /// <summary>Reemplaza el contenido entero; el filtro de texto ya escrito se reaplica.</summary>
    public virtual void SetItems(IEnumerable<T> items)
    {
        var list = new ObservableCollection<T>(items);
        TotalCount = list.Count;
        View = CollectionViewSource.GetDefaultView(list);
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = SearchText;
        View.Filter = string.IsNullOrWhiteSpace(text) ? null : item => SearchMatcher.Matches(item, text);
    }
}

public sealed class MeasuresSectionViewModel() : GridSectionViewModel<MeasureInfo>("Medidas DAX", "");
public sealed class TablesSectionViewModel() : GridSectionViewModel<TableInfo>("Tablas", "");
public sealed class ColumnsSectionViewModel() : GridSectionViewModel<ColumnInfo>("Columnas", "");
public sealed class RelationshipsSectionViewModel() : GridSectionViewModel<RelationshipInfo>("Relaciones", "");
public sealed class PowerQuerySectionViewModel() : GridSectionViewModel<PowerQueryInfo>("Power Query (M)", "");
public sealed class FiltersSectionViewModel() : GridSectionViewModel<FilterInfo>("Filtros", "");

public sealed class VisualsSectionViewModel() : GridSectionViewModel<VisualFieldUsage>("Visuales y campos", "", hasSearch: false);

public sealed class PerformanceSectionViewModel() : GridSectionViewModel<PerformanceWarning>(
    "Rendimiento", "",
    note: "Heurísticas sobre patrones conocidos como antipatrón (relaciones bidireccionales o muchos a muchos, columnas calculadas, CALCULATE anidado). Esta herramienta nunca consulta filas reales, así que no puede medir cardinalidad ni tiempo de query real — son pistas para revisar, no un profiler.",
    hasSearch: false);

public sealed partial class UsageSectionViewModel() : GridSectionViewModel<ObjectUsage>(
    "Uso y limpieza", "",
    note: "Cruza visuales del reporte, dependencias DAX y relaciones. No sigue dependencias de Power Query entre consultas (M) — una tabla auxiliar que solo alimenta a otra vía M puede figurar como \"sin uso\" sin serlo. Confirmá antes de borrar.",
    hasSearch: false)
{
    private List<ObjectUsage> _all = new();

    public IReadOnlyList<ObjectUsage> All => _all;

    [ObservableProperty] private bool _onlyUnused;

    public override void SetItems(IEnumerable<ObjectUsage> items)
    {
        _all = items.ToList();
        Refresh();
    }

    partial void OnOnlyUnusedChanged(bool value) => Refresh();

    private void Refresh() => base.SetItems(OnlyUnused ? _all.Where(u => u.IsUnused) : _all);
}

public sealed partial class CompareSectionViewModel : GridSectionViewModel<ModelDiffEntry>
{
    public CompareSectionViewModel() : base(
        "Comparar versiones", "",
        note: "Compara el modelo conectado ahora contra un snapshot .json exportado antes (botón \"Exportar JSON\") del mismo tablero — pensado para auditar qué cambió entre una versión y otra.",
        hasSearch: false)
    {
        SetEmpty(true, "Elegí un snapshot para comparar",
            "Elegí un archivo .json exportado antes de este mismo tablero para ver qué cambió desde entonces.");
        EmptyIcon = "";
        EmptyActionText = "Elegir snapshot .json…";
    }

    public override void SetItems(IEnumerable<ModelDiffEntry> items)
    {
        base.SetItems(items);
        IsEmpty = false;
    }
}
