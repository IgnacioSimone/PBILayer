using System.Collections.ObjectModel;
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
    private readonly AppSettings _settings;
    private readonly Action? _persist;

    private ModelSnapshot _model = new();
    private List<VisualFieldUsage> _visuals = new();
    private List<FilterInfo> _filters = new();
    private List<ObjectUsage> _usage = new();
    private List<PerformanceWarning> _performance = new();
    private string _sourceName = "modelo";

    // Última conexión y último reporte: los usa F5 para volver a leer.
    private string? _server;
    private string? _database;
    private string? _reportPath;
    private CancellationTokenSource? _cts;

    public MainViewModel(IPowerBiBackend backend, IDialogService dialogs, AppSettings? settings = null, Action? persist = null)
    {
        _backend = backend;
        _dialogs = dialogs;
        _settings = settings ?? new AppSettings();
        _persist = persist;
        _settings.Changed = () =>
        {
            SyncRecentFiles();
            _persist?.Invoke();
        };

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
        _selectedSection = Sections.FirstOrDefault(s => s.Title == _settings.LastSection) ?? Summary;
        _theme = _settings.Theme;

        Visuals.EmptyIcon = "";
        Visuals.EmptyActionText = "Elegir archivo .pbix…";
        Visuals.EmptyActionCommand = ChoosePbixCommand;
        Compare.EmptyActionCommand = ChooseSnapshotCommand;
        ShowVisualsEmpty("Buscando el archivo del reporte…",
            "Los visuales y sus campos viven dentro del archivo .pbix, no en el modelo en memoria.");

        Filters.EmptyIcon = "";
        Performance.EmptyIcon = "";

        SyncRecentFiles();
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
    public ObservableCollection<string> RecentFiles { get; } = new();
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <summary>Cuánto dura un aviso antes de desaparecer solo (los errores no desaparecen).</summary>
    public TimeSpan ToastDuration { get; set; } = TimeSpan.FromSeconds(6);

    [ObservableProperty] private SectionViewModel _selectedSection;
    [ObservableProperty] private string _statusText = "Sin conexión — abrí esta herramienta desde Power BI Desktop.";
    [ObservableProperty] private string _globalSearchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeLabel))]
    private ThemeMode _theme;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportMarkdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportJsonCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public string ThemeLabel => Theme switch
    {
        ThemeMode.Light => "Tema: claro",
        ThemeMode.Dark => "Tema: oscuro",
        _ => "Tema: automático (Windows)"
    };

    private bool HasModel => _model.Tables.Count > 0 || _model.Measures.Count > 0;
    private bool HasAnything => HasModel || _visuals.Count > 0;

    partial void OnSelectedSectionChanged(SectionViewModel value)
    {
        UpdateStatus();
        _settings.LastSection = value.Title;
        _persist?.Invoke();
    }

    partial void OnThemeChanged(ThemeMode value)
    {
        _settings.Theme = value;
        _persist?.Invoke();
    }

    partial void OnGlobalSearchTextChanged(string value)
    {
        foreach (var section in Sections.OfType<ISearchableSection>())
            section.SearchText = value;
    }

    // =====================================================================
    // Arranque y operaciones largas
    // =====================================================================

    /// <summary>Power BI Desktop invoca la herramienta externa como:
    /// PBIContext.exe "localhost:PUERTO" "GUID-de-la-base".</summary>
    public async Task InitializeAsync(string[] args)
    {
        AppLog.Write($"Inicio. Args: {string.Join(" | ", args)}");

        await RunBusyAsync(async ct =>
        {
            if (args.Length >= 3)
            {
                var connected = await ConnectToLiveModelAsync(args[1], args[2], ct);
                if (ct.IsCancellationRequested) return;
                if (!connected) { RefreshAll(); }
            }

            RefreshAll();
            await TryAutoLoadVisualsAsync(ct);
        });
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> work)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operación cancelada.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Espera la tarea pero devuelve el control apenas se cancela. La lectura de
    /// ADOMD es bloqueante y no se puede interrumpir: se abandona su resultado.</summary>
    private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken ct)
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => cancelled.TrySetResult(true));

        if (await Task.WhenAny(task, cancelled.Task) != task)
        {
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new OperationCanceledException(ct);
        }
        return await task;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task RefreshAsync()
    {
        if (_server is null && _reportPath is null)
        {
            Notify(ToastKind.Info, "No hay nada para actualizar: abrí la herramienta desde Power BI Desktop o elegí un .pbix.");
            return;
        }

        await RunBusyAsync(async ct =>
        {
            if (_server is not null && _database is not null)
                if (!await ConnectToLiveModelAsync(_server, _database, ct)) return;

            if (_reportPath is not null) LoadReport(_reportPath);
            Notify(ToastKind.Success, "Datos actualizados.");
        });
    }

    // =====================================================================
    // Modelo en vivo (metadata vía DMVs)
    // =====================================================================

    public async Task<bool> ConnectToLiveModelAsync(string server, string database, CancellationToken ct = default)
    {
        try
        {
            _server = server;
            _database = database;
            StatusText = $"Conectando a {server} …";
            _model = await WithCancellation(_backend.ReadModelAsync(server, database), ct);
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
                var detail = "Algunas secciones no se pudieron leer del motor de este modelo:\n\n" +
                             string.Join("\n\n", _model.Warnings);
                Notify(ToastKind.Warning, "Algunas secciones no se pudieron leer de este modelo.", "Ver detalle",
                    () => _dialogs.ShowWarning(detail, $"{WpfDialogService.AppName} — advertencias"));
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Conexión cancelada.";
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR conectando al modelo: {ex}");
            StatusText = "No se pudo conectar al modelo en vivo.";
            NotifyError($"No se pudo conectar al modelo de Power BI Desktop. {ex.Message}", "Reintentar",
                () => RefreshCommand.Execute(null));
            return false;
        }
    }

    // =====================================================================
    // Visuales del reporte (leídos del archivo .pbix)
    // =====================================================================

    private async Task TryAutoLoadVisualsAsync(CancellationToken ct)
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

        string? path;
        try
        {
            path = await WithCancellation(_backend.FindOpenPbixAsync(TimeSpan.FromSeconds(12)), ct);
        }
        catch (OperationCanceledException)
        {
            ShowVisualsEmpty("Búsqueda cancelada", "Elegí el archivo a mano cuando quieras.");
            throw;
        }
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
            _reportPath = path;
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

            _settings.AddRecentFile(path);
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
            NotifyError($"No se pudo leer {Path.GetFileName(path)}. {ex.Message}");
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

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (!File.Exists(path))
        {
            _settings.RemoveRecentFile(path);
            Notify(ToastKind.Warning, $"{Path.GetFileName(path)} ya no existe en esa ubicación.");
            return;
        }

        LoadReport(path);
        SelectedSection = Visuals;
    }

    [RelayCommand]
    private void ClearRecent() => _settings.ClearRecentFiles();

    private void SyncRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _settings.RecentFiles) RecentFiles.Add(path);
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    /// <summary>Arrastrar un archivo a la ventana: .pbix carga el reporte, .json lo compara.</summary>
    public void OpenFile(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pbix":
                LoadReport(path);
                SelectedSection = Visuals;
                break;
            case ".json":
                CompareWithSnapshot(path);
                SelectedSection = Compare;
                break;
            default:
                Notify(ToastKind.Warning, "Solo se pueden abrir archivos .pbix (reporte) o .json (snapshot).");
                break;
        }
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
        if (path is not null) CompareWithSnapshot(path);
    }

    private void CompareWithSnapshot(string path)
    {
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
            NotifyError($"No se pudo leer ese snapshot. {ex.Message}");
        }
    }

    // =====================================================================
    // Navegación, búsqueda y tema
    // =====================================================================

    [RelayCommand]
    private void GoToSection(string? index)
    {
        if (int.TryParse(index, out var i) && i >= 0 && i < Sections.Count)
            SelectedSection = Sections[i];
    }

    [RelayCommand]
    private void NextSection() => Step(+1);

    [RelayCommand]
    private void PreviousSection() => Step(-1);

    private void Step(int delta)
    {
        var current = Sections.ToList().IndexOf(SelectedSection);
        SelectedSection = Sections[(current + delta + Sections.Count) % Sections.Count];
    }

    [RelayCommand]
    private void ClearSearch() => GlobalSearchText = "";

    [RelayCommand]
    private void CycleTheme() => Theme = Theme switch
    {
        ThemeMode.System => ThemeMode.Light,
        ThemeMode.Light => ThemeMode.Dark,
        _ => ThemeMode.System
    };

    [RelayCommand]
    private void ShowShortcuts() => _dialogs.ShowInfo(
        "Atajos de teclado\n\n" +
        "Ctrl+F\t\tBuscar en todas las secciones\n" +
        "Esc\t\tLimpiar la búsqueda\n" +
        "Ctrl+O\t\tAbrir un .pbix\n" +
        "Ctrl+E\t\tExportar Markdown\n" +
        "Ctrl+Shift+E\tExportar JSON\n" +
        "F5\t\tActualizar los datos\n" +
        "Ctrl+1 … 9, 0\tIr a la sección 1 … 10\n" +
        "Ctrl+Av Pág / Re Pág\tSección siguiente / anterior\n" +
        "Ctrl+T\t\tCambiar tema\n" +
        "F1\t\tEsta ayuda\n\n" +
        "También podés arrastrar un .pbix o un snapshot .json a la ventana.");

    // =====================================================================
    // Avisos (toasts)
    // =====================================================================

    private void Notify(ToastKind kind, string message, string? actionText = null, Action? action = null)
    {
        var toast = new ToastViewModel(kind, message, actionText, action, t => Toasts.Remove(t));
        Toasts.Add(toast);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);

        if (kind != ToastKind.Error) _ = DismissLaterAsync(toast);
    }

    private void NotifyError(string message, string? actionText = null, Action? action = null)
    {
        // Los errores siempre ofrecen una salida: reintentar o abrir el log con el detalle.
        if (actionText is null)
        {
            actionText = "Ver log";
            action = () => _dialogs.OpenWithShell(AppPaths.LogPath);
        }
        Notify(ToastKind.Error, message, actionText, action);
    }

    private async Task DismissLaterAsync(ToastViewModel toast)
    {
        await Task.Delay(ToastDuration);
        Toasts.Remove(toast);
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
            Notify(ToastKind.Success, "Contexto guardado. Adjuntá ese .md en el chat con tu asistente de IA.",
                "Mostrar", () => _dialogs.RevealInExplorer(path));
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR generando contexto IA: {ex}");
            NotifyError($"No se pudo generar el archivo. {ex.Message}");
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
            Notify(ToastKind.Success, "Snapshot guardado. Usalo más adelante en \"Comparar versiones\".",
                "Mostrar", () => _dialogs.RevealInExplorer(path));
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR generando JSON: {ex}");
            NotifyError($"No se pudo generar el archivo. {ex.Message}");
        }
    }

    private bool EnsureSomethingLoaded()
    {
        if (HasAnything) return true;
        Notify(ToastKind.Info, "No hay nada cargado todavía — conectate a un modelo o abrí un .pbix primero.");
        return false;
    }
}
