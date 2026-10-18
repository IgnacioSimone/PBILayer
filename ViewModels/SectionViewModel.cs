using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Infrastructure;
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

    protected static string YesNo(bool value) => Loc.T(value ? "Common_Yes" : "Common_No");

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
        ? Loc.F("Count_Of", VisibleCount.ToString("N0"), TotalCount.ToString("N0"))
        : TotalCount == 1 ? Loc.T("Count_OneRow") : Loc.F("Count_Rows", TotalCount.ToString("N0"));

    private bool IsFiltered => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Mensaje cuando la búsqueda activa no encuentra nada en esta sección (vacío si no aplica).</summary>
    public string? NoResultsText => IsFiltered && VisibleCount == 0 && TotalCount > 0
        ? Loc.F("NoResults", SearchText.Trim())
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

public sealed class MeasuresSectionViewModel() : GridSectionViewModel<MeasureInfo>(Loc.T("Section_Measures"), "")
{
    protected override IReadOnlyList<ExportColumn<MeasureInfo>> ExportColumns { get; } = new ExportColumn<MeasureInfo>[]
    {
        new(Loc.T("Col_Table"), m => m.Table), new(Loc.T("Col_Measure"), m => m.Name), new(Loc.T("Col_Folder"), m => m.DisplayFolder),
        new(Loc.T("Col_Dax"), m => m.Expression, IsCode: true), new(Loc.T("Col_Format"), m => m.FormatString),
        new(Loc.T("Col_Hidden"), m => YesNo(m.IsHidden)), new(Loc.T("Col_Description"), m => m.Description)
    };

    protected override DetailViewModel? BuildDetail(MeasureInfo m, DetailContext context) =>
        new(m.Name, Loc.F("Detail_MeasureSubtitle", m.Table), m.Expression, CodeLanguage.Dax,
            DetailFacts.Of((Loc.T("Col_Folder"), m.DisplayFolder), (Loc.T("Col_Format"), m.FormatString),
                (Loc.T("Col_Hidden"), m.IsHidden ? Loc.T("Common_Yes") : null), (Loc.T("Col_Description"), m.Description)),
            context.FindUsage("Medida", m.Table, m.Name), context);
}

public sealed class TablesSectionViewModel() : GridSectionViewModel<TableInfo>(Loc.T("Section_Tables"), "")
{
    protected override IReadOnlyList<ExportColumn<TableInfo>> ExportColumns { get; } = new ExportColumn<TableInfo>[]
    {
        new(Loc.T("Col_Table"), t => t.Name), new(Loc.T("Col_Columns"), t => t.ColumnCount.ToString()), new(Loc.T("Col_Measures"), t => t.MeasureCount.ToString()),
        new(Loc.T("Col_Description"), t => t.Description), new(Loc.T("Col_Hidden"), t => YesNo(t.IsHidden))
    };
}

public sealed class ColumnsSectionViewModel() : GridSectionViewModel<ColumnInfo>(Loc.T("Section_Columns"), "")
{
    protected override IReadOnlyList<ExportColumn<ColumnInfo>> ExportColumns { get; } = new ExportColumn<ColumnInfo>[]
    {
        new(Loc.T("Col_Table"), c => c.Table), new(Loc.T("Col_Column"), c => c.Name), new(Loc.T("Col_DataType"), c => c.DataTypeText),
        new(Loc.T("Col_Source"), c => c.ColumnTypeText), new(Loc.T("Col_Folder"), c => c.DisplayFolder),
        new(Loc.T("Col_DaxOrSource"), c => c.DaxOrSource, IsCode: true),
        new(Loc.T("Col_Hidden"), c => YesNo(c.IsHidden)), new(Loc.T("Col_Description"), c => c.Description)
    };

    protected override DetailViewModel? BuildDetail(ColumnInfo c, DetailContext context)
    {
        var calculated = !string.IsNullOrWhiteSpace(c.Expression);
        return new DetailViewModel(c.Name, Loc.F("Detail_ColumnSubtitle", c.Table), calculated ? c.Expression : null, CodeLanguage.Dax,
            DetailFacts.Of((Loc.T("Col_DataType"), c.DataTypeText), (Loc.T("Col_Source"), c.ColumnTypeText),
                (Loc.T("Col_SourceColumn"), calculated ? null : c.SourceColumn), (Loc.T("Col_Folder"), c.DisplayFolder),
                (Loc.T("Col_Hidden"), c.IsHidden ? Loc.T("Common_Yes") : null), (Loc.T("Col_Description"), c.Description)),
            context.FindUsage("Columna", c.Table, c.Name), context);
    }
}

public sealed class RelationshipsSectionViewModel() : GridSectionViewModel<RelationshipInfo>(Loc.T("Section_Relationships"), "")
{
    protected override IReadOnlyList<ExportColumn<RelationshipInfo>> ExportColumns { get; } = new ExportColumn<RelationshipInfo>[]
    {
        new(Loc.T("Col_FromTable"), r => r.FromTable), new(Loc.T("Col_FromColumn"), r => r.FromColumn),
        new(Loc.T("Col_ToTable"), r => r.ToTable), new(Loc.T("Col_ToColumn"), r => r.ToColumn),
        new(Loc.T("Col_Cardinality"), r => r.CardinalityText), new(Loc.T("Col_CrossFilter"), r => r.CrossFilterText),
        new(Loc.T("Col_Active"), r => YesNo(r.IsActive))
    };
}

public sealed class PowerQuerySectionViewModel() : GridSectionViewModel<PowerQueryInfo>(Loc.T("Section_PowerQuery"), "")
{
    protected override IReadOnlyList<ExportColumn<PowerQueryInfo>> ExportColumns { get; } = new ExportColumn<PowerQueryInfo>[]
    {
        new(Loc.T("Col_Name"), q => q.Name), new(Loc.T("Col_Kind"), q => q.KindText), new(Loc.T("Col_MCode"), q => q.MCode, IsCode: true),
        new(Loc.T("Col_Hidden"), q => YesNo(q.IsHidden)), new(Loc.T("Col_Description"), q => q.Description)
    };

    protected override DetailViewModel? BuildDetail(PowerQueryInfo q, DetailContext context)
    {
        // Una consulta que carga una tabla comparte el veredicto de esa tabla; un parámetro,
        // función o auxiliar sin carga tiene el suyo.
        var isTable = q.Kind == "Tabla";
        var usage = context.FindUsage(isTable ? "Tabla" : "Consulta", isTable ? q.Name : "", q.Name);

        return new DetailViewModel(q.Name, Loc.F("Detail_QuerySubtitle", q.KindText), q.MCode, CodeLanguage.M,
            DetailFacts.Of((Loc.T("Col_Hidden"), q.IsHidden ? Loc.T("Common_Yes") : null), (Loc.T("Col_Description"), q.Description)),
            usage, context);
    }
}
public sealed class FiltersSectionViewModel() : GridSectionViewModel<FilterInfo>(Loc.T("Section_Filters"), "")
{
    protected override IReadOnlyList<ExportColumn<FilterInfo>> ExportColumns { get; } = new ExportColumn<FilterInfo>[]
    {
        new(Loc.T("Col_Level"), f => f.ScopeText), new(Loc.T("Col_Page"), f => f.Page), new(Loc.T("Col_Visual"), f => f.VisualText),
        new(Loc.T("Col_Kind"), f => f.FieldKindText), new(Loc.T("Col_Field"), f => f.TargetText), new(Loc.T("Col_FilterType"), f => f.FilterType),
        new(Loc.T("Col_Condition"), f => f.Summary, IsCode: true)
    };
}
public sealed class VisualsSectionViewModel() : GridSectionViewModel<VisualFieldUsage>(Loc.T("Section_Visuals"), "")
{
    protected override IReadOnlyList<ExportColumn<VisualFieldUsage>> ExportColumns { get; } = new ExportColumn<VisualFieldUsage>[]
    {
        new(Loc.T("Col_Page"), v => v.Page), new(Loc.T("Col_Visual"), v => v.VisualType), new(Loc.T("Col_Title"), v => v.Title),
        new(Loc.T("Col_Kind"), v => v.FieldKindText), new(Loc.T("Col_Table"), v => v.Table), new(Loc.T("Col_Field"), v => v.Field)
    };
}

public sealed class PerformanceSectionViewModel() : GridSectionViewModel<PerformanceWarning>(
    Loc.T("Section_Performance"), "",
    note: Loc.T("Note_Performance"))
{
    protected override IReadOnlyList<ExportColumn<PerformanceWarning>> ExportColumns { get; } = new ExportColumn<PerformanceWarning>[]
    {
        new(Loc.T("Col_Severity"), w => w.SeverityText), new(Loc.T("Col_Category"), w => w.Category), new(Loc.T("Col_Table"), w => w.Table),
        new(Loc.T("Col_Object"), w => w.Subject), new(Loc.T("Col_Detail"), w => w.Message)
    };
}

public sealed partial class UsageSectionViewModel() : GridSectionViewModel<ObjectUsage>(
    Loc.T("Section_Usage"), "",
    note: Loc.T("Note_Usage"))
{
    private List<ObjectUsage> _all = new();

    public IReadOnlyList<ObjectUsage> All => _all;

    // El documento exportado lleva todos los objetos, no solo los que dejó el filtro "solo sin uso".
    protected override IEnumerable<ObjectUsage> AllItems => _all;

    protected override IReadOnlyList<ExportColumn<ObjectUsage>> ExportColumns { get; } = new ExportColumn<ObjectUsage>[]
    {
        new(Loc.T("Col_Kind"), u => u.KindText), new(Loc.T("Col_Table"), u => u.Table), new(Loc.T("Col_Name"), u => u.Name), new(Loc.T("Col_Status"), u => u.Status),
        new(Loc.T("Col_DependsOn"), u => u.DependsOnText), new(Loc.T("Col_UsedBy"), u => u.UsedByText)
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
        Loc.T("Section_Compare"), "",
        note: Loc.T("Note_Compare"))
    {
        SetEmpty(true, Loc.T("Compare_EmptyTitle"), Loc.T("Compare_EmptyBody"));
        EmptyIcon = "";
        EmptyActionText = Loc.T("Compare_EmptyAction");
    }

    protected override IReadOnlyList<ExportColumn<ModelDiffEntry>> ExportColumns { get; } = new ExportColumn<ModelDiffEntry>[]
    {
        new(Loc.T("Col_Change"), d => d.KindText), new(Loc.T("Col_Category"), d => d.Category), new(Loc.T("Col_Object"), d => d.Name), new(Loc.T("Col_Detail"), d => d.Detail)
    };

    public override void SetItems(IEnumerable<ModelDiffEntry> items)
    {
        base.SetItems(items);
        IsEmpty = false;
    }
}
