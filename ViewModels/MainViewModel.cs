using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Infrastructure;
using PBIExplorer.Models;
using PBIExplorer.Services;

namespace PBIExplorer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPowerBiBackend _backend;
    private readonly IDialogService _dialogs;

    private ModelSnapshot _model = new();
    private List<VisualFieldUsage> _visuals = new();
    private List<FilterInfo> _filters = new();
    private List<ObjectUsage> _usage = new();
    private List<PerformanceWarning> _performance = new();
    private string _sourceName = "modelo";

    public MainViewModel(IPowerBiBackend backend, IDialogService dialogs)
    {
        _backend = backend;
        _dialogs = dialogs;

        Summary = new SummaryViewModel();
        Measures = new MeasuresSectionViewModel();
        Tables = new TablesSectionViewModel();
        Columns = new ColumnsSectionViewModel();
        Relationships = new RelationshipsSectionViewModel();
        PowerQuery = new PowerQuerySectionViewModel();
        Usage = new UsageSectionViewModel();
        Visuals = new VisualsSectionViewModel();
        Filters = new FiltersSectionViewModel();
        Performance = new PerformanceSectionViewModel();
        Compare = new CompareSectionViewModel();

        Sections = new SectionViewModel[]
        {
            Summary, Measures, Tables, Columns, Relationships, PowerQuery,
            Usage, Visuals, Filters, Performance, Compare
        };
        _selectedSection = Summary;

        Visuals.EmptyIcon = "";
        Visuals.EmptyActionText = "Elegir archivo .pbix…";
        Visuals.EmptyActionCommand = ChoosePbixCommand;
        Compare.EmptyActionCommand = ChooseSnapshotCommand;
        ShowVisualsEmpty("Buscando el archivo del reporte…",
            "Los visuales y sus campos viven dentro del archivo .pbix, no en el modelo en memoria.");

        Filters.EmptyIcon = "";
        Performance.EmptyIcon = "";

        RefreshAll();
    }

    public SummaryViewModel Summary { get; }
    public MeasuresSectionViewModel Measures { get; }
    public TablesSectionViewModel Tables { get; }
    public ColumnsSectionViewModel Columns { get; }
    public RelationshipsSectionViewModel Relationships { get; }
    public PowerQuerySectionViewModel PowerQuery { get; }
    public UsageSectionViewModel Usage { get; }
    public VisualsSectionViewModel Visuals { get; }
    public FiltersSectionViewModel Filters { get; }
    public PerformanceSectionViewModel Performance { get; }
    public CompareSectionViewModel Compare { get; }

    public IReadOnlyList<SectionViewModel> Sections { get; }

    [ObservableProperty] private SectionViewModel _selectedSection;
    [ObservableProperty] private string _statusText = "Sin conexión — abrí esta herramienta desde Power BI Desktop.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportMarkdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportJsonCommand))]
    private bool _isBusy;

    private bool HasModel => _model.Tables.Count > 0 || _model.Measures.Count > 0;
    private bool HasAnything => HasModel || _visuals.Count > 0;

    partial void OnSelectedSectionChanged(SectionViewModel value) => UpdateStatus();

    // =====================================================================
    // Arranque
    // =====================================================================

    /// <summary>Power BI Desktop invoca la herramienta externa como:
    /// PBIContext.exe "localhost:PUERTO" "GUID-de-la-base".</summary>
    public async Task InitializeAsync(string[] args)
    {
        AppLog.Write($"Inicio. Args: {string.Join(" | ", args)}");

        IsBusy = true;
        try
        {
            if (args.Length >= 3)
                await ConnectToLiveModelAsync(args[1], args[2]);

            RefreshAll();
            await TryAutoLoadVisualsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =====================================================================
    // Modelo en vivo (metadata vía DMVs)
    // =====================================================================

    public async Task ConnectToLiveModelAsync(string server, string database)
    {
        try
        {
            StatusText = $"Conectando a {server} …";
            _model = await _backend.ReadModelAsync(server, database);
            _sourceName = database;

            Measures.SetItems(_model.Measures);
            Tables.SetItems(_model.Tables);
            Columns.SetItems(_model.Columns);
            Relationships.SetItems(_model.Relationships);
            PowerQuery.SetItems(_model.PowerQueries);

            AppLog.Write($"Modelo leído: {_model.Tables.Count} tablas, {_model.Columns.Count} columnas, " +
                         $"{_model.Measures.Count} medidas, {_model.Relationships.Count} relaciones, " +
                         $"{_model.PowerQueries.Count} consultas Power Query, {_model.Warnings.Count} advertencias.");

            RebuildPerformance();
            RebuildUsage();
            RefreshAll();

            if (_model.Warnings.Count > 0)
            {
                _dialogs.ShowWarning(
                    "Algunas secciones no se pudieron leer del motor de este modelo:\n\n" +
                    string.Join("\n\n", _model.Warnings),
                    $"{WpfDialogService.AppName} — advertencias");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR conectando al modelo: {ex}");
            StatusText = "No se pudo conectar al modelo en vivo.";
            _dialogs.ShowWarning($"No se pudo conectar al modelo de Power BI Desktop.\n\n{ex.Message}");
        }
    }

    // =====================================================================
    // Visuales del reporte (leídos del archivo .pbix)
    // =====================================================================

    private async Task TryAutoLoadVisualsAsync()
    {
        var reportName = _backend.GetOpenReportName();
        AppLog.Write($"Reporte abierto según título de ventana: {reportName ?? "(ninguno)"}");

        if (reportName is null)
        {
            ShowVisualsEmpty("No se detectó ningún tablero abierto",
                "Abrí un tablero en Power BI Desktop y volvé a lanzar la herramienta, o elegí el archivo a mano.");
            return;
        }

        ShowVisualsEmpty($"Buscando \"{reportName}.pbix\" en el disco…",
            "Los visuales y sus campos viven dentro del archivo, no en el modelo en memoria.");

        var path = await _backend.FindOpenPbixAsync(TimeSpan.FromSeconds(12));
        AppLog.Write($"Ruta detectada: {path ?? "(no encontrada)"}");

        if (path is null)
        {
            ShowVisualsEmpty($"No encontré \"{reportName}.pbix\" automáticamente",
                "Puede estar en una carpeta fuera de la búsqueda. Elegilo a mano y se carga igual.");
            return;
        }

        LoadReport(path);
    }

    public void LoadReport(string path)
    {
        try
        {
            AppLog.Write($"Leyendo visuales de: {path}");
            (_visuals, _filters) = _backend.ParseReport(path);
            AppLog.Write($"ParseVisuals devolvió {_visuals.Count} filas; ParseFilters {_filters.Count} filtros.");

            Visuals.SetItems(_visuals);
            Filters.SetItems(_filters);

            if (!HasModel) _sourceName = Path.GetFileNameWithoutExtension(path);

            if (_visuals.Count == 0)
            {
                ShowVisualsEmpty("Ese archivo no tiene visuales legibles",
                    "Se probaron los dos formatos de reporte (PBIR y Layout clásico) y ninguno devolvió visuales.");
            }
            else
            {
                Visuals.SetEmpty(false);
            }

            RebuildUsage();
            RefreshAll();

            if (_visuals.Count > 0)
            {
                var pages = _visuals.Select(v => v.Page).Distinct().Count();
                StatusText = $"{Path.GetFileName(path)} · {pages} páginas · {_visuals.Count} usos de campos";
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR leyendo visuales: {ex}");
            ShowVisualsEmpty("No se pudo leer ese archivo", ex.Message);
        }
    }

    private void ShowVisualsEmpty(string title, string body) => Visuals.SetEmpty(true, title, body);

    [RelayCommand]
    private void ChoosePbix()
    {
        var path = _dialogs.PickOpenFile("Archivos Power BI (*.pbix)|*.pbix");
        if (path is null) return;

        LoadReport(path);
        SelectedSection = Visuals;
    }

    // =====================================================================
    // Análisis derivados
    // =====================================================================

    private void RebuildUsage() => _usage = UsageAnalyzer.Analyze(_model, _visuals);

    private void RebuildPerformance()
    {
        _performance = PerformanceAnalyzer.Analyze(_model);
        Performance.SetItems(_performance);
    }

    [RelayCommand]
    private void ChooseSnapshot()
    {
        var path = _dialogs.PickOpenFile("Snapshot de PBI Context (*.json)|*.json");
        if (path is null) return;

        try
        {
            var older = JsonModelExporter.Load(path);
            var diff = ModelDiffAnalyzer.Compare(older.Model, _model, older.Filters, _filters);
            Compare.SetItems(diff);

            var when = older.ExportedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            StatusText = diff.Count == 0
                ? $"Sin cambios contra el snapshot de {older.SourceName} del {when}."
                : $"{diff.Count} cambios contra el snapshot de {older.SourceName} del {when}.";
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR comparando snapshot: {ex}");
            _dialogs.ShowWarning($"No se pudo leer ese snapshot.\n\n{ex.Message}");
        }
    }

    // =====================================================================
    // Refresco de estados derivados (resumen, vacíos, barra de estado)
    // =====================================================================

    private void RefreshAll()
    {
        Usage.SetItems(_usage);
        RebuildSummary();
        UpdateEmptyStates();
        UpdateStatus();
    }

    private void RebuildSummary()
    {
        if (!HasAnything)
        {
            Summary.SetTiles(Array.Empty<TileViewModel>());
            return;
        }

        var pages = _visuals.Select(v => v.Page).Distinct().Count();
        var visualCount = _visuals.Select(v => (v.Page, v.VisualType, v.Title)).Distinct().Count();
        var hiddenTables = _model.Tables.Count(t => t.IsHidden);
        var hiddenMeasures = _model.Measures.Count(m => m.IsHidden);

        var tiles = new (string Icon, string Label, int Value, string? Sub)[]
        {
            ("", "Tablas",         _model.Tables.Count,        hiddenTables   > 0 ? $"{hiddenTables} ocultas"   : null),
            ("", "Columnas",       _model.Columns.Count,       null),
            ("", "Medidas DAX",    _model.Measures.Count,      hiddenMeasures > 0 ? $"{hiddenMeasures} ocultas" : null),
            ("", "Relaciones",     _model.Relationships.Count, null),
            ("", "Power Query",    _model.PowerQueries.Count,  null),
            ("", "Páginas",        pages,                      null),
            ("", "Visuales",       visualCount,                null),
            ("", "Usos de campos", _visuals.Count,             null),
            ("", "Filtros",        _filters.Count,             null),
            ("", "Sin uso",        _usage.Count(u => u.IsUnused), null),
        };

        Summary.SetTiles(tiles.Select((t, i) => new TileViewModel(t.Icon, t.Label, t.Value, t.Sub, i)));
    }

    private void UpdateEmptyStates()
    {
        // Las secciones del modelo comparten el mismo tipo de estado vacío: "no hay
        // conexión" vs. "esta sección puntual no tiene filas".
        var notConnected = !HasModel;
        SetModelEmpty(Measures, notConnected, _model.Measures.Count == 0, "El modelo conectado no tiene medidas DAX.");
        SetModelEmpty(Tables, notConnected, _model.Tables.Count == 0, "El modelo conectado no tiene tablas visibles.");
        SetModelEmpty(Columns, notConnected, _model.Columns.Count == 0, "El modelo conectado no tiene columnas.");
        SetModelEmpty(Relationships, notConnected, _model.Relationships.Count == 0, "El modelo conectado no tiene relaciones definidas.");
        SetModelEmpty(PowerQuery, notConnected, _model.PowerQueries.Count == 0, "El modelo conectado no tiene consultas de Power Query.");
        SetModelEmpty(Usage, notConnected, _model.Measures.Count == 0 && _model.Columns.Count == 0, "No hay medidas ni columnas para analizar todavía.");

        if (_filters.Count == 0)
            Filters.SetEmpty(true, "Sin filtros detectados",
                "No se encontraron filtros a nivel reporte, página o visual en este archivo — o el tablero de verdad no tiene ninguno configurado.");
        else
            Filters.SetEmpty(false);

        if (notConnected)
            Performance.SetEmpty(true, "Sin modelo conectado", "Conectate a un modelo para analizar su rendimiento.");
        else if (_performance.Count == 0)
            Performance.SetEmpty(true, "Sin alertas detectadas", "No se encontraron patrones conocidos de riesgo en este modelo.");
        else
            Performance.SetEmpty(false);
    }

    private static void SetModelEmpty(SectionViewModel section, bool notConnected, bool sectionIsEmpty, string emptyBody)
    {
        if (notConnected)
            section.SetEmpty(true, "Sin modelo conectado",
                "Abrí tu tablero en Power BI Desktop y lanzá PBI Context desde la pestaña Herramientas externas. Ahí se llenan medidas, tablas, columnas y relaciones.");
        else if (sectionIsEmpty)
            section.SetEmpty(true, "Esta sección está vacía", emptyBody);
        else
            section.SetEmpty(false);
    }

    private void UpdateStatus()
    {
        var section = SelectedSection;

        if (section == Visuals && _visuals.Count > 0)
        {
            var pages = _visuals.Select(v => v.Page).Distinct().Count();
            StatusText = $"{pages} páginas · {_visuals.Count} usos de campos";
        }
        else if (section == Summary)
        {
            StatusText = "Análisis local, basado en reglas — no usa IA ni sale a internet.";
        }
        else if (section == Usage && _usage.Count > 0)
        {
            var unused = _usage.Count(u => u.IsUnused);
            StatusText = $"{unused} de {_usage.Count} objetos sin ningún uso detectado — cruza visuales, DAX y relaciones.";
        }
        else if (section == Filters)
        {
            StatusText = _filters.Count == 0
                ? "Sin filtros detectados en el reporte, la página o los visuales."
                : $"{_filters.Count} filtros — {_filters.Count(f => f.Scope == "Reporte")} de reporte, " +
                  $"{_filters.Count(f => f.Scope == "Página")} de página, {_filters.Count(f => f.Scope == "Visual")} de visual.";
        }
        else if (section == Performance)
        {
            StatusText = _performance.Count == 0
                ? "Heurísticas sobre metadata — sin patrones de riesgo detectados."
                : $"{_performance.Count} alertas — heurísticas sobre metadata, no un profiler real.";
        }
        else if (section == Compare)
        {
            StatusText = "Elegí un snapshot .json exportado antes para ver qué cambió.";
        }
        else if (HasModel && (section == Measures || section == Tables || section == Columns
                              || section == Relationships || section == PowerQuery))
        {
            StatusText =
                $"{_model.Tables.Count} tablas · {_model.Columns.Count} columnas · " +
                $"{_model.Measures.Count} medidas · {_model.Relationships.Count} relaciones";
        }
    }

    // =====================================================================
    // Exportación
    // =====================================================================

    private bool CanExport() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportMarkdown()
    {
        if (!EnsureSomethingLoaded()) return;

        var path = _dialogs.PickSaveFile("Markdown (*.md)|*.md", $"Contexto_IA_{_sourceName}.md");
        if (path is null) return;

        try
        {
            var summary = SummaryBuilder.Build(_model, _visuals);
            AiContextExporter.Export(path, _sourceName, _model, _visuals, summary, _usage, _filters);
            _dialogs.ShowInfo("Listo. Adjuntá o pegá ese .md en el chat con Claude o ChatGPT para que analice todo el tablero.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR generando contexto IA: {ex}");
            _dialogs.ShowWarning($"No se pudo generar el archivo.\n\n{ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportJson()
    {
        if (!EnsureSomethingLoaded()) return;

        var path = _dialogs.PickSaveFile("JSON (*.json)|*.json", $"Snapshot_{_sourceName}.json");
        if (path is null) return;

        try
        {
            JsonModelExporter.Export(path, _sourceName, _model, _visuals, _usage, _filters);
            _dialogs.ShowInfo("Listo. Guardá este archivo — más adelante podés usarlo en \"Comparar versiones\" para ver qué cambió.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR generando JSON: {ex}");
            _dialogs.ShowWarning($"No se pudo generar el archivo.\n\n{ex.Message}");
        }
    }

    private bool EnsureSomethingLoaded()
    {
        if (HasAnything) return true;
        _dialogs.ShowInfo("No hay nada cargado todavía — conectate a un modelo o abrí un .pbix primero.");
        return false;
    }
}
