using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Models;
using PBIExplorer.Services;
using PBIExplorer.Services.Export;

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

    /// <summary>Contador que aparece junto al ítem de la barra lateral (ej. resultados de la búsqueda global).</summary>
    [ObservableProperty] private string? _badgeText;

    public void SetEmpty(bool isEmpty, string title = "", string body = "")
    {
        IsEmpty = isEmpty;
        EmptyTitle = title;
        EmptyBody = body;
    }
}

/// <summary>Sección que participa de la búsqueda global del encabezado.</summary>
public interface ISearchableSection
{
    string SearchText { get; set; }
}

/// <summary>Una columna de una sección al exportar: encabezado, cómo sacar el texto de la fila y si es código.</summary>
public sealed record ExportColumn<T>(string Header, Func<T, string?> Get, bool IsCode = false);

/// <summary>Sección que se puede exportar como tabla (CSV, Excel, Word, HTML).</summary>
public interface IExportableSection
{
    string Title { get; }
    bool HasExportData { get; }

    /// <param name="filteredView">true: solo lo que se ve ahora (con la búsqueda y el orden de la grilla); false: todas las filas.</param>
    ExportTable ToExportTable(bool filteredView);
}

/// <summary>Sección con una grilla de solo lectura y vista filtrable.</summary>
public abstract partial class GridSectionViewModel<T> : SectionViewModel, ISearchableSection, IExportableSection where T : class
{
    private List<T> _items = new();

    /// <summary>Todas las filas, sin el filtro de la búsqueda.</summary>
    protected virtual IEnumerable<T> AllItems => _items;

    /// <summary>Columnas que se exportan; vacío = esta sección no se exporta.</summary>
    protected virtual IReadOnlyList<ExportColumn<T>> ExportColumns => Array.Empty<ExportColumn<T>>();

    protected static string YesNo(bool value) => value ? "Sí" : "No";

    public bool HasExportData => ExportColumns.Count > 0 && AllItems.Any();

    public ExportTable ToExportTable(bool filteredView)
    {
        var columns = ExportColumns;
        var rows = (filteredView ? View.Cast<T>() : AllItems)
            .Select(item => (IReadOnlyList<string>)columns
                .Select(c => SensitiveTextGuard.Mask(c.Get(item) ?? ""))
                .ToList())
            .ToList();

        var code = columns.Select((c, i) => (c, i)).Where(x => x.c.IsCode).Select(x => x.i).ToHashSet();
        return new ExportTable(Title, columns.Select(c => c.Header).ToList(), rows, code, Note);
    }

    protected GridSectionViewModel(string title, string icon, string? note = null)
        : base(title, icon)
    {
        Note = note;
        _view = CollectionViewSource.GetDefaultView(new ObservableCollection<T>());
    }

    public string? Note { get; }

    [ObservableProperty] private ICollectionView _view;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _visibleCount;

    /// <summary>Fila seleccionada en la grilla (la usa el panel de detalle y la navegación entre dependencias).</summary>
    [ObservableProperty] private T? _selectedItem;

    /// <summary>Panel lateral de la fila seleccionada; null si la sección no tiene detalle o no hay selección.</summary>
    [ObservableProperty] private DetailViewModel? _detail;

    /// <summary>Lo pone la ventana principal; sin esto no hay panel de detalle.</summary>
    public DetailContext? DetailContext { get; set; }

    /// <summary>Las secciones con panel de detalle lo arman acá a partir de la fila seleccionada.</summary>
    protected virtual DetailViewModel? BuildDetail(T item, DetailContext context) => null;

    partial void OnSelectedItemChanged(T? value) =>
        Detail = value is null || DetailContext is null ? null : BuildDetail(value, DetailContext);

    [RelayCommand]
    private void CloseDetail() => SelectedItem = null;

    /// <summary>"340 filas" o, con una búsqueda activa, "12 de 340".</summary>
    public string CountText => IsFiltered
        ? $"{VisibleCount:N0} de {TotalCount:N0}"
        : TotalCount == 1 ? "1 fila" : $"{TotalCount:N0} filas";

    private bool IsFiltered => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Mensaje cuando la búsqueda activa no encuentra nada en esta sección (vacío si no aplica).</summary>
    public string? NoResultsText => IsFiltered && VisibleCount == 0 && TotalCount > 0
        ? $"Sin resultados para \"{SearchText.Trim()}\""
        : null;

    /// <summary>Reemplaza el contenido entero; el filtro de texto ya escrito se reaplica.</summary>
    public virtual void SetItems(IEnumerable<T> items)
    {
        var list = new ObservableCollection<T>(items);
        _items = list.ToList();
        TotalCount = list.Count;
        View = CollectionViewSource.GetDefaultView(list);
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = SearchText;
        View.Filter = IsFiltered ? item => SearchMatcher.Matches(item, text) : null;

        VisibleCount = IsFiltered ? View.Cast<object>().Count() : TotalCount;
        BadgeText = IsFiltered ? VisibleCount.ToString("N0") : null;
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(NoResultsText));
    }
}

public sealed class MeasuresSectionViewModel() : GridSectionViewModel<MeasureInfo>("Medidas DAX", "")
{
    protected override IReadOnlyList<ExportColumn<MeasureInfo>> ExportColumns { get; } = new ExportColumn<MeasureInfo>[]
    {
        new("Tabla", m => m.Table), new("Medida", m => m.Name), new("Carpeta", m => m.DisplayFolder),
        new("DAX", m => m.Expression, IsCode: true), new("Formato", m => m.FormatString),
        new("Oculta", m => YesNo(m.IsHidden)), new("Descripción", m => m.Description)
    };

    protected override DetailViewModel? BuildDetail(MeasureInfo m, DetailContext context) =>
        new(m.Name, $"Medida · {m.Table}", m.Expression, CodeLanguage.Dax,
            DetailFacts.Of(("Carpeta", m.DisplayFolder), ("Formato", m.FormatString),
                ("Oculta", m.IsHidden ? "Sí" : null), ("Descripción", m.Description)),
            context.FindUsage("Medida", m.Table, m.Name), context);
}

public sealed class TablesSectionViewModel() : GridSectionViewModel<TableInfo>("Tablas", "")
{
    protected override IReadOnlyList<ExportColumn<TableInfo>> ExportColumns { get; } = new ExportColumn<TableInfo>[]
    {
        new("Tabla", t => t.Name), new("Columnas", t => t.ColumnCount.ToString()), new("Medidas", t => t.MeasureCount.ToString()),
        new("Descripción", t => t.Description), new("Oculta", t => YesNo(t.IsHidden))
    };
}

public sealed class ColumnsSectionViewModel() : GridSectionViewModel<ColumnInfo>("Columnas", "")
{
    protected override IReadOnlyList<ExportColumn<ColumnInfo>> ExportColumns { get; } = new ExportColumn<ColumnInfo>[]
    {
        new("Tabla", c => c.Table), new("Columna", c => c.Name), new("Tipo de dato", c => c.DataType),
        new("Origen", c => c.ColumnType), new("Carpeta", c => c.DisplayFolder),
        new("DAX / columna origen", c => c.DaxOrSource, IsCode: true),
        new("Oculta", c => YesNo(c.IsHidden)), new("Descripción", c => c.Description)
    };

    protected override DetailViewModel? BuildDetail(ColumnInfo c, DetailContext context)
    {
        var calculated = !string.IsNullOrWhiteSpace(c.Expression);
        return new DetailViewModel(c.Name, $"Columna · {c.Table}", calculated ? c.Expression : null, CodeLanguage.Dax,
            DetailFacts.Of(("Tipo de dato", c.DataType), ("Origen", c.ColumnType),
                ("Columna de origen", calculated ? null : c.SourceColumn), ("Carpeta", c.DisplayFolder),
                ("Oculta", c.IsHidden ? "Sí" : null), ("Descripción", c.Description)),
            context.FindUsage("Columna", c.Table, c.Name), context);
    }
}

public sealed class RelationshipsSectionViewModel() : GridSectionViewModel<RelationshipInfo>("Relaciones", "")
{
    protected override IReadOnlyList<ExportColumn<RelationshipInfo>> ExportColumns { get; } = new ExportColumn<RelationshipInfo>[]
    {
        new("Desde tabla", r => r.FromTable), new("Desde columna", r => r.FromColumn),
        new("Hacia tabla", r => r.ToTable), new("Hacia columna", r => r.ToColumn),
        new("Cardinalidad", r => r.Cardinality), new("Filtro cruzado", r => r.CrossFilterBehavior),
        new("Activa", r => YesNo(r.IsActive))
    };
}

public sealed class PowerQuerySectionViewModel() : GridSectionViewModel<PowerQueryInfo>("Power Query (M)", "")
{
    protected override IReadOnlyList<ExportColumn<PowerQueryInfo>> ExportColumns { get; } = new ExportColumn<PowerQueryInfo>[]
    {
        new("Nombre", q => q.Name), new("Tipo", q => q.Kind), new("Código M", q => q.MCode, IsCode: true),
        new("Oculta", q => YesNo(q.IsHidden)), new("Descripción", q => q.Description)
    };

    protected override DetailViewModel? BuildDetail(PowerQueryInfo q, DetailContext context)
    {
        // Una consulta que carga una tabla comparte el veredicto de esa tabla; un parámetro,
        // función o auxiliar sin carga tiene el suyo.
        var isTable = q.Kind == "Tabla";
        var usage = context.FindUsage(isTable ? "Tabla" : "Consulta", isTable ? q.Name : "", q.Name);

        return new DetailViewModel(q.Name, $"Power Query · {q.Kind}", q.MCode, CodeLanguage.M,
            DetailFacts.Of(("Oculta", q.IsHidden ? "Sí" : null), ("Descripción", q.Description)),
            usage, context);
    }
}
public sealed class FiltersSectionViewModel() : GridSectionViewModel<FilterInfo>("Filtros", "")
{
    protected override IReadOnlyList<ExportColumn<FilterInfo>> ExportColumns { get; } = new ExportColumn<FilterInfo>[]
    {
        new("Nivel", f => f.Scope), new("Página", f => f.Page), new("Visual", f => f.VisualText),
        new("Tipo", f => f.FieldKind), new("Campo", f => f.TargetText), new("Tipo de filtro", f => f.FilterType),
        new("Condición", f => f.Summary, IsCode: true)
    };
}
public sealed class VisualsSectionViewModel() : GridSectionViewModel<VisualFieldUsage>("Visuales y campos", "")
{
    protected override IReadOnlyList<ExportColumn<VisualFieldUsage>> ExportColumns { get; } = new ExportColumn<VisualFieldUsage>[]
    {
        new("Página", v => v.Page), new("Visual", v => v.VisualType), new("Título", v => v.Title),
        new("Tipo", v => v.FieldKind), new("Tabla", v => v.Table), new("Campo", v => v.Field)
    };
}

public sealed class PerformanceSectionViewModel() : GridSectionViewModel<PerformanceWarning>(
    "Rendimiento", "",
    note: "Heurísticas sobre patrones conocidos como antipatrón (relaciones bidireccionales o muchos a muchos, columnas calculadas, CALCULATE anidado). Esta herramienta nunca consulta filas reales, así que no puede medir cardinalidad ni tiempo de query real — son pistas para revisar, no un profiler.")
{
    protected override IReadOnlyList<ExportColumn<PerformanceWarning>> ExportColumns { get; } = new ExportColumn<PerformanceWarning>[]
    {
        new("Severidad", w => w.Severity.ToString()), new("Categoría", w => w.Category), new("Tabla", w => w.Table),
        new("Objeto", w => w.Subject), new("Detalle", w => w.Message)
    };
}

public sealed partial class UsageSectionViewModel() : GridSectionViewModel<ObjectUsage>(
    "Uso y limpieza", "",
    note: "Cruza visuales del reporte, dependencias DAX, relaciones y dependencias entre consultas de Power Query (una tabla auxiliar o un parámetro que alimenta a una tabla en uso cuenta como usado). No ve segmentaciones, marcadores ni Q&A: confirmá antes de borrar.")
{
    private List<ObjectUsage> _all = new();

    public IReadOnlyList<ObjectUsage> All => _all;

    // El documento exportado lleva todos los objetos, no solo los que dejó el filtro "solo sin uso".
    protected override IEnumerable<ObjectUsage> AllItems => _all;

    protected override IReadOnlyList<ExportColumn<ObjectUsage>> ExportColumns { get; } = new ExportColumn<ObjectUsage>[]
    {
        new("Tipo", u => u.Kind), new("Tabla", u => u.Table), new("Nombre", u => u.Name), new("Estado", u => u.Status),
        new("Depende de", u => u.DependsOnText), new("Usado por", u => u.UsedByText)
    };

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
        note: "Compara el modelo conectado ahora contra un snapshot .json exportado antes (botón \"Exportar JSON\") del mismo tablero — pensado para auditar qué cambió entre una versión y otra.")
    {
        SetEmpty(true, "Elegí un snapshot para comparar",
            "Elegí un archivo .json exportado antes de este mismo tablero para ver qué cambió desde entonces.");
        EmptyIcon = "";
        EmptyActionText = "Elegir snapshot .json…";
    }

    protected override IReadOnlyList<ExportColumn<ModelDiffEntry>> ExportColumns { get; } = new ExportColumn<ModelDiffEntry>[]
    {
        new("Cambio", d => d.KindText), new("Categoría", d => d.Category), new("Objeto", d => d.Name), new("Detalle", d => d.Detail)
    };

    public override void SetItems(IEnumerable<ModelDiffEntry> items)
    {
        base.SetItems(items);
        IsEmpty = false;
    }
}
