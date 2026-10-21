using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Infrastructure;
using PBIExplorer.Models;
using PBIExplorer.Services;
using PBIExplorer.Services.Export;
using PBIExplorer.Services.Updates;

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
    private string _sourceName = Loc.T("Default_SourceName");

    // Última conexión y último reporte: los usa F5 para volver a leer.
    private string? _server;
    private string? _database;
    private string? _reportPath;
    private string? _modelReportName;   // reporte de Power BI Desktop al que pertenece el modelo cargado
    private CancellationTokenSource? _cts;

    public MainViewModel(IPowerBiBackend backend, IDialogService dialogs, AppSettings? settings = null, Action? persist = null,
        UpdateViewModel? updates = null)
    {
        _backend = backend;
        _dialogs = dialogs;
        _settings = settings ?? new AppSettings();
        _persist = persist;
        Updates = updates ?? new UpdateViewModel(
            new NullUpdateFeed(), new UpdateInstaller(new System.Net.Http.HttpClient(), Path.GetTempPath()),
            dialogs, _settings, persist, AppInfo.Version);
        _settings.Changed = () =>
        {
            SyncRecentFiles();
            _persist?.Invoke();
        };

        Summary = new SummaryViewModel();
        Summary.Navigate = section => SelectedSection = section;
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
        var detailContext = new DetailContext
        {
            FindUsage = (kind, table, name) => _usage.FirstOrDefault(u => u.Kind == kind && u.Table == table && u.Name == name),
            Navigate = NavigateTo,
            Copy = text =>
            {
                _dialogs.CopyText(text);
                Notify(ToastKind.Success, Loc.T("Toast_Copied"));
            }
        };
        Measures.DetailContext = detailContext;
        Columns.DetailContext = detailContext;
        PowerQuery.DetailContext = detailContext;

        _selectedSection = Sections.FirstOrDefault(s => s.Title == _settings.LastSection) ?? Summary;
        _theme = _settings.Theme;

        Visuals.EmptyIcon = "";
        Visuals.EmptyActionText = Loc.T("Visuals_EmptyAction");
        Visuals.EmptyActionCommand = ChoosePbixCommand;
        Compare.EmptyActionCommand = ChooseSnapshotCommand;
        ShowVisualsEmpty(Loc.T("Visuals_SearchingTitle"), Loc.T("Visuals_InsideFileBody"));

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

    public UpdateViewModel Updates { get; }

    /// <summary>Pide abrir la ventana "Acerca de" (el parámetro dice si arranca buscando actualizaciones).</summary>
    public event Action<bool>? AboutRequested;

    public IReadOnlyList<SectionViewModel> Sections { get; }
    public ObservableCollection<string> RecentFiles { get; } = new();
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <summary>Cuánto dura un aviso antes de desaparecer solo (los errores no desaparecen).</summary>
    public TimeSpan ToastDuration { get; set; } = TimeSpan.FromSeconds(6);

    [ObservableProperty] private SectionViewModel _selectedSection;
    [ObservableProperty] private string _statusText = Loc.T("Status_NoConnection");
    [ObservableProperty] private string _globalSearchText = "";

    /// <summary>Verdadero mientras se espera que Power BI Desktop termine de abrir el archivo: la pantalla muestra la tarjeta de espera.</summary>
    [ObservableProperty] private bool _isWaitingForDesktop;
    [ObservableProperty] private string _waitingTitle = "";

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
        ThemeMode.Light => Loc.T("Theme_Light"),
        ThemeMode.Dark => Loc.T("Theme_Dark"),
        _ => Loc.T("Theme_Auto")
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
    /// PBILayer.exe "localhost:PUERTO" "GUID-de-la-base".</summary>
    public async Task InitializeAsync(string[] args)
    {
        AppLog.Write($"Inicio. Args: {string.Join(" | ", args)}");

        await RunBusyAsync(async ct =>
        {
            if (args.Length >= 3)
            {
                // Lanzada desde "Herramientas externas": Power BI Desktop nos pasa servidor y base.
                _modelReportName = _backend.GetOpenReportName();
                var connected = await ConnectToLiveModelAsync(args[1], args[2], ct);
                if (connected && _modelReportName is not null) _sourceName = _modelReportName;
                if (ct.IsCancellationRequested) return;
            }
            else if (StartupFile(args) is null)
            {
                // Abierta por su cuenta: si hay un tablero abierto en Power BI Desktop, se engancha solo.
                StatusText = Loc.T("Status_SearchingDesktop");
                await ConnectToDesktopModelAsync(null, ct);
                if (ct.IsCancellationRequested) return;
            }

            RefreshAll();
            if (StartupFile(args) is null) await TryAutoLoadVisualsAsync(ct);
        });

        // "Abrir con" desde el Explorador, o PBILayer.exe "ruta.pbix": carga ese archivo.
        if (StartupFile(args) is { } file) await OpenFileAsync(file);

        await CheckForUpdatesAtStartupAsync();
    }

    /// <summary>Al arrancar: la primera vez pregunta (una sola vez) si buscar actualizaciones; si ya dijo que sí,
    /// busca como mucho una vez al día y avisa si hay una versión nueva. Sin elegir, no se conecta a nada.</summary>
    private async Task CheckForUpdatesAtStartupAsync()
    {
        try
        {
            if (_settings.UpdateChecks == UpdateCheckMode.Unset && !_settings.UpdatePromptShown)
            {
                _settings.UpdatePromptShown = true;
                _persist?.Invoke();
                Notify(ToastKind.Info, Loc.T("Update_Prompt"),
                    Loc.T("Update_PromptAction"), () =>
                    {
                        Updates.AutoCheck = true;
                        AboutRequested?.Invoke(true);
                    },
                    sticky: true);
                return;
            }

            var result = await Updates.AutoCheckIfDueAsync();
            // (UpdateStatus también es un método de esta clase: por eso el nombre completo del enum.)
            if (result is { Status: PBIExplorer.Services.Updates.UpdateStatus.Available, Release: { } release })
            {
                Notify(ToastKind.Info, Loc.F("Update_NewVersionToast", $"{release.Version.Major}.{release.Version.Minor}.{release.Version.Build}"),
                    Loc.T("Update_NewVersionAction"), () => AboutRequested?.Invoke(false), sticky: true);
            }
        }
        catch (Exception ex)
        {
            // Una falla buscando actualizaciones nunca debe molestar a quien está trabajando.
            AppLog.Write($"Chequeo de actualizaciones: {ex.Message}");
        }
    }

    /// <summary>El idioma elegido (no necesariamente el que se está usando hasta reiniciar).</summary>
    public AppLanguage Language => _settings.Language;

    [RelayCommand]
    private void SetLanguage(AppLanguage language)
    {
        if (_settings.Language == language) return;

        _settings.Language = language;
        _persist?.Invoke();
        OnPropertyChanged(nameof(Language));

        // Los textos se cargan al arrancar: el cambio se ve al reiniciar.
        Notify(ToastKind.Info, Loc.T("Lang_RestartPrompt"), Loc.T("Lang_RestartAction"), () => _dialogs.RestartApplication(), sticky: true);
    }

    [RelayCommand]
    private void ShowAbout() => AboutRequested?.Invoke(false);

    [RelayCommand]
    private void CheckForUpdates() => AboutRequested?.Invoke(true);

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenWithShell(AppInfo.DataDirectory);

    /// <summary>Abre la página para reportar un problema o proponer una mejora (formularios de GitHub Issues).</summary>
    [RelayCommand]
    private void OpenFeedback() => _dialogs.OpenWithShell(AppInfo.FeedbackUrl);

    /// <summary>Un único argumento que es un .pbix existente (Power BI Desktop, en cambio, pasa servidor y base).</summary>
    private static string? StartupFile(string[] args) =>
        args.Length == 2 && args[1].EndsWith(".pbix", StringComparison.OrdinalIgnoreCase) && File.Exists(args[1])
            ? args[1]
            : null;

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
            StatusText = Loc.T("Status_Cancelled");
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
        await RunBusyAsync(async ct =>
        {
            // El puerto del motor cambia cada vez que se abre el tablero, así que primero se
            // vuelve a buscar; solo si no aparece se reintenta con el servidor ya conocido.
            var reportName = _modelReportName ?? (_reportPath is null ? null : Path.GetFileNameWithoutExtension(_reportPath));
            var reconnected = await ConnectToDesktopModelAsync(reportName, ct);
            if (ct.IsCancellationRequested) return;

            if (!reconnected && _server is not null && _database is not null)
                if (!await ConnectToLiveModelAsync(_server, _database, ct)) return;

            if (_reportPath is not null) LoadReport(_reportPath);

            if (!HasAnything)
                Notify(ToastKind.Info, Loc.T("Toast_NothingToRefresh"));
            else
                Notify(ToastKind.Success, Loc.T("Toast_Refreshed"));
        });
    }

    /// <summary>Busca el modelo del tablero abierto en Power BI Desktop (por nombre, o el único
    /// abierto) y lo lee. Devuelve false si no hay ninguno o si falló la lectura.</summary>
    private async Task<bool> ConnectToDesktopModelAsync(string? reportName, CancellationToken ct, bool quiet = false)
    {
        var found = await WithCancellation(_backend.FindDesktopModelAsync(reportName), ct);
        if (found is null) return false;

        AppLog.Write($"Modelo de Power BI Desktop encontrado: \"{found.ReportName}\" en {found.Server}");
        if (!await ConnectToLiveModelAsync(found.Server, found.Database, ct, quiet)) return false;

        _modelReportName = found.ReportName;
        _sourceName = found.ReportName;   // la base se llama con un GUID: para nombrar archivos sirve el nombre del tablero
        return true;
    }

    /// <summary>Después de abrir un .pbix a mano: las tablas, columnas y medidas no se pueden leer del
    /// archivo (el modelo va comprimido adentro), pero sí del motor de Power BI Desktop si ese
    /// mismo archivo está abierto ahí.</summary>
    private async Task EnsureModelForReportAsync(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (HasModel && string.Equals(_modelReportName, name, StringComparison.OrdinalIgnoreCase)) return;

        await RunBusyAsync(async ct =>
        {
            if (await ConnectToDesktopModelAsync(name, ct))
            {
                Notify(ToastKind.Success, Loc.F("Toast_ModelRead", name));
                return;
            }

            // El archivo no está abierto en Power BI Desktop: se abre ahí, porque es la única forma
            // de leer su modelo. Un modelo de otro tablero mezclado con estos visuales daría un
            // "Uso y limpieza" falso, así que se descarta antes.
            if (HasModel) ClearModel();
            await OpenInDesktopAndWaitAsync(path, ct);
        });
    }

    /// <summary>Cuánto se espera entre intentos mientras Power BI Desktop abre el archivo.</summary>
    public TimeSpan DesktopPollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Tiempo máximo de espera: un .pbix de varios cientos de MB puede tardar minutos en abrir.</summary>
    public TimeSpan DesktopWaitTimeout { get; set; } = TimeSpan.FromMinutes(4);

    /// <summary>Reintenta abrir el reporte actual en Power BI Desktop (botón del estado vacío).</summary>
    [RelayCommand]
    private async Task OpenReportInDesktopAsync()
    {
        if (_reportPath is null) return;
        var path = _reportPath;
        await RunBusyAsync(ct => OpenInDesktopAndWaitAsync(path, ct));
    }

    private async Task OpenInDesktopAndWaitAsync(string path, CancellationToken ct)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        StatusText = Loc.F("Status_OpeningInDesktop", name);
        WaitingTitle = Loc.F("Wait_Title", name);
        IsWaitingForDesktop = true;
        try
        {
            _dialogs.OpenWithShell(path);

            var deadline = DateTime.UtcNow + DesktopWaitTimeout;
            while (DateTime.UtcNow < deadline)
            {
                // Mientras Desktop carga, el título de su ventana todavía no tiene el nombre del archivo:
                // el modelo simplemente no aparece todavía y se vuelve a intentar.
                if (await ConnectToDesktopModelAsync(name, ct, quiet: true))
                {
                    Notify(ToastKind.Success, Loc.F("Toast_ModelRead", name));
                    return;
                }
                await Task.Delay(DesktopPollInterval, ct);
            }

            Notify(ToastKind.Warning, Loc.F("Toast_DesktopStillOpening", name), sticky: true);
        }
        finally
        {
            // Pase lo que pase (éxito, tiempo agotado o cancelación) la tarjeta de espera se va.
            IsWaitingForDesktop = false;
        }
    }

    private void ClearModel()
    {
        _model = new ModelSnapshot();
        _server = null;
        _database = null;
        _modelReportName = null;

        Measures.SetItems(_model.Measures);
        Tables.SetItems(_model.Tables);
        Columns.SetItems(_model.Columns);
        Relationships.SetItems(_model.Relationships);
        PowerQuery.SetItems(_model.PowerQueries);
        RebuildPerformance();
        RebuildUsage();
        RefreshAll();
    }

    // =====================================================================
    // Modelo en vivo (metadata vía DMVs)
    // =====================================================================

    public async Task<bool> ConnectToLiveModelAsync(string server, string database, CancellationToken ct = default, bool quiet = false)
    {
        try
        {
            _server = server;
            _database = database;
            StatusText = Loc.F("Status_Connecting", server);
            _model = await WithCancellation(_backend.ReadModelAsync(server, database), ct);
            _sourceName = database;

            Measures.SetItems(_model.Measures);
            Tables.SetItems(_model.Tables);
            Columns.SetItems(_model.Columns);
            Relationships.HiddenTables = _model.Tables.Where(t => t.IsHidden).Select(t => t.Name).ToHashSet();
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
                var detail = Loc.F("Warn_SectionsUnreadableDetail", string.Join("\n\n", _model.Warnings));
                Notify(ToastKind.Warning, Loc.T("Toast_SectionsUnreadable"), Loc.T("Toast_ViewDetail"),
                    () => _dialogs.ShowWarning(detail, Loc.F("Dialog_WarningsTitle", WpfDialogService.AppName)));
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("Status_ConnectionCancelled");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR conectando al modelo: {ex}");
            if (quiet) return false;   // sondeo mientras Power BI Desktop termina de abrir: se reintenta solo

            StatusText = Loc.T("Status_ConnectFailed");
            NotifyError(Loc.F("Toast_ConnectFailed", ex.Message), Loc.T("Toast_Retry"),
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
            ShowVisualsEmpty(Loc.T("Visuals_NoReportTitle"), Loc.T("Visuals_NoReportBody"));
            return;
        }

        ShowVisualsEmpty(Loc.F("Visuals_FindingTitle", reportName), Loc.T("Visuals_InsideFileBody2"));

        string? path;
        try
        {
            path = await WithCancellation(_backend.FindOpenPbixAsync(TimeSpan.FromSeconds(12)), ct);
        }
        catch (OperationCanceledException)
        {
            ShowVisualsEmpty(Loc.T("Visuals_SearchCancelledTitle"), Loc.T("Visuals_SearchCancelledBody"));
            throw;
        }
        AppLog.Write($"Ruta detectada: {path ?? "(no encontrada)"}");

        if (path is null)
        {
            ShowVisualsEmpty(Loc.F("Visuals_NotFoundTitle", reportName), Loc.T("Visuals_NotFoundBody"));
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
                ShowVisualsEmpty(Loc.T("Visuals_NoVisualsTitle"), Loc.T("Visuals_NoVisualsBody"));
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
                StatusText = Loc.F("Status_ReportSummary", Path.GetFileName(path), pages, _visuals.Count);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR leyendo visuales: {ex}");
            ShowVisualsEmpty(Loc.T("Visuals_ReadFailedTitle"), ex.Message);
            NotifyError(Loc.F("Toast_ReadFailed", Path.GetFileName(path), ex.Message));
        }
    }

    private void ShowVisualsEmpty(string title, string body) => Visuals.SetEmpty(true, title, body);

    [RelayCommand]
    private async Task ChoosePbixAsync()
    {
        var path = _dialogs.PickOpenFile(Loc.T("Dialog_PbixFilter"));
        if (path is null) return;

        await OpenReportAsync(path);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (!File.Exists(path))
        {
            _settings.RemoveRecentFile(path);
            Notify(ToastKind.Warning, Loc.F("Toast_RecentMissing", Path.GetFileName(path)));
            return;
        }

        await OpenReportAsync(path);
    }

    private async Task OpenReportAsync(string path)
    {
        LoadReport(path);
        SelectedSection = Visuals;
        await EnsureModelForReportAsync(path);
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
    public async Task OpenFileAsync(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pbix":
                await OpenReportAsync(path);
                break;
            case ".json":
                CompareWithSnapshot(path);
                SelectedSection = Compare;
                break;
            default:
                Notify(ToastKind.Warning, Loc.T("Toast_UnsupportedFile"));
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
        var path = _dialogs.PickOpenFile(Loc.T("Dialog_SnapshotFilter"));
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
                ? Loc.F("Status_NoChanges", older.SourceName, when)
                : Loc.F("Status_ChangesFound", diff.Count, older.SourceName, when);
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR comparando snapshot: {ex}");
            NotifyError(Loc.F("Toast_SnapshotReadFailed", ex.Message));
        }
    }

    // =====================================================================
    // Navegación, búsqueda y tema
    // =====================================================================

    /// <summary>Salta al objeto que nombra una dependencia: "[Medida]" va a Medidas DAX y
    /// "Tabla[Columna]" va a Columnas, con esa fila seleccionada y su detalle abierto.</summary>
    private void NavigateTo(string label)
    {
        if (UsageLabels.TryParseQuery(label, out var queryName))
        {
            var query = _model.PowerQueries.FirstOrDefault(q => q.Name == queryName);
            if (query is not null)
            {
                GlobalSearchText = "";
                SelectedSection = PowerQuery;
                PowerQuery.SelectedItem = query;
                return;
            }
        }

        var open = label.IndexOf('[');
        if (open >= 0 && label.EndsWith(']'))
        {
            var table = label[..open];
            var field = label[(open + 1)..^1];

            // Un filtro de búsqueda activo podría esconder justo el objeto al que se quiere ir.
            GlobalSearchText = "";

            if (table.Length == 0)
            {
                var measure = _model.Measures.FirstOrDefault(m => string.Equals(m.Name, field, StringComparison.OrdinalIgnoreCase));
                if (measure is not null)
                {
                    SelectedSection = Measures;
                    Measures.SelectedItem = measure;
                    return;
                }
            }
            else
            {
                var column = _model.Columns.FirstOrDefault(c =>
                    string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Name, field, StringComparison.OrdinalIgnoreCase));
                if (column is not null)
                {
                    SelectedSection = Columns;
                    Columns.SelectedItem = column;
                    return;
                }

                // DAX permite calificar una medida con su tabla (Tabla[Medida]).
                var qualifiedMeasure = _model.Measures.FirstOrDefault(m =>
                    string.Equals(m.Table, table, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.Name, field, StringComparison.OrdinalIgnoreCase));
                if (qualifiedMeasure is not null)
                {
                    SelectedSection = Measures;
                    Measures.SelectedItem = qualifiedMeasure;
                    return;
                }
            }
        }

        Notify(ToastKind.Info, Loc.F("Toast_NotFound", UsageLabels.Display(label)));
    }

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
    private void ShowShortcuts() => _dialogs.ShowInfo(Loc.T("Shortcuts_Text"));

    // =====================================================================
    // Avisos (toasts)
    // =====================================================================

    /// <param name="sticky">Si es true el aviso queda hasta que el usuario lo cierre (mensajes largos o que piden una acción).</param>
    private void Notify(ToastKind kind, string message, string? actionText = null, Action? action = null, bool sticky = false)
    {
        var toast = new ToastViewModel(kind, message, actionText, action, t => Toasts.Remove(t));
        Toasts.Add(toast);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);

        if (kind != ToastKind.Error && !sticky) _ = DismissLaterAsync(toast);
    }

    private void NotifyError(string message, string? actionText = null, Action? action = null)
    {
        // Los errores siempre ofrecen una salida: reintentar o abrir el log con el detalle.
        if (actionText is null)
        {
            actionText = Loc.T("Toast_ViewLog");
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

        var tiles = new (string Icon, string Label, int Value, string? Sub, string Description, SectionViewModel Target)[]
        {
            ("", Loc.T("Section_Tables"),    _model.Tables.Count,        hiddenTables   > 0 ? Loc.F("Tile_Hidden", hiddenTables)   : null, Loc.T("Tile_Desc_Tables"), Tables),
            ("", Loc.T("Section_Columns"),   _model.Columns.Count,       null, Loc.T("Tile_Desc_Columns"), Columns),
            ("", Loc.T("Section_Measures"),  _model.Measures.Count,      hiddenMeasures > 0 ? Loc.F("Tile_Hidden", hiddenMeasures) : null, Loc.T("Tile_Desc_Measures"), Measures),
            ("", Loc.T("Section_Relationships"), _model.Relationships.Count, null, Loc.T("Tile_Desc_Relationships"), Relationships),
            ("", Loc.T("Tile_PowerQuery"), _model.PowerQueries.Count,  null, Loc.T("Tile_Desc_PowerQuery"), PowerQuery),
            ("", Loc.T("Tile_Pages"),        pages,                      null, Loc.T("Tile_Desc_Pages"), Visuals),
            ("", Loc.T("Tile_Visuals"),       visualCount,                null, Loc.T("Tile_Desc_Visuals"), Visuals),
            ("", Loc.T("Tile_FieldUses"), _visuals.Count,             null, Loc.T("Tile_Desc_FieldUses"), Visuals),
            ("", Loc.T("Section_Filters"),   _filters.Count,             null, Loc.T("Tile_Desc_Filters"), Filters),
            ("", Loc.T("Tile_Unused"),        _usage.Count(u => u.IsUnused), null, Loc.T("Tile_Desc_Unused"), Usage),
        };

        Summary.SetTiles(tiles.Select((t, i) => new TileViewModel(t.Icon, t.Label, t.Value, t.Sub, i, t.Description, t.Target)));
    }

    private void UpdateEmptyStates()
    {
        // Las secciones del modelo comparten el mismo tipo de estado vacío: "no hay
        // conexión" vs. "esta sección puntual no tiene filas".
        var notConnected = !HasModel;
        var fromReport = notConnected && _reportPath is not null;
        SetModelEmpty(Measures, notConnected, fromReport, _model.Measures.Count == 0, Loc.T("Empty_NoMeasures"));
        SetModelEmpty(Tables, notConnected, fromReport, _model.Tables.Count == 0, Loc.T("Empty_NoTables"));
        SetModelEmpty(Columns, notConnected, fromReport, _model.Columns.Count == 0, Loc.T("Empty_NoColumns"));
        SetModelEmpty(Relationships, notConnected, fromReport, _model.Relationships.Count == 0, Loc.T("Empty_NoRelationships"));
        SetModelEmpty(PowerQuery, notConnected, fromReport, _model.PowerQueries.Count == 0, Loc.T("Empty_NoQueries"));
        SetModelEmpty(Usage, notConnected, fromReport, _model.Measures.Count == 0 && _model.Columns.Count == 0, Loc.T("Empty_NothingToAnalyze"));

        if (_filters.Count == 0)
            Filters.SetEmpty(true, Loc.T("Empty_NoFiltersTitle"), Loc.T("Empty_NoFiltersBody"));
        else
            Filters.SetEmpty(false);

        if (notConnected)
            Performance.SetEmpty(true, Loc.T("Empty_NoModelTitle"), Loc.T("Empty_PerfConnect"));
        else if (_performance.Count == 0)
            Performance.SetEmpty(true, Loc.T("Empty_NoAlertsTitle"), Loc.T("Empty_NoAlertsBody"));
        else
            Performance.SetEmpty(false);
    }

    private void SetModelEmpty(SectionViewModel section, bool notConnected, bool fromReport, bool sectionIsEmpty, string emptyBody)
    {
        // Con un .pbix cargado pero sin modelo, la salida es abrirlo en Power BI Desktop desde acá.
        section.EmptyActionText = fromReport ? Loc.T("Empty_OpenInDesktop") : null;
        section.EmptyActionCommand = fromReport ? OpenReportInDesktopCommand : null;

        if (fromReport)
            section.SetEmpty(true, Loc.T("Empty_MissingModelTitle"), Loc.T("Empty_MissingModelBody"));
        else if (notConnected)
            section.SetEmpty(true, Loc.T("Empty_NoModelTitle"), Loc.T("Empty_NoModelBody"));
        else if (sectionIsEmpty)
            section.SetEmpty(true, Loc.T("Empty_SectionEmptyTitle"), emptyBody);
        else
            section.SetEmpty(false);
    }

    private void UpdateStatus()
    {
        var section = SelectedSection;

        if (section == Visuals && _visuals.Count > 0)
        {
            var pages = _visuals.Select(v => v.Page).Distinct().Count();
            StatusText = Loc.F("Status_PagesAndUses", pages, _visuals.Count);
        }
        else if (section == Summary)
        {
            StatusText = Loc.T("Status_Summary");
        }
        else if (section == Usage && _usage.Count > 0)
        {
            var unused = _usage.Count(u => u.IsUnused);
            StatusText = Loc.F("Status_Usage", unused, _usage.Count);
        }
        else if (section == Filters)
        {
            StatusText = _filters.Count == 0
                ? Loc.T("Status_NoFilters")
                : Loc.F("Status_Filters", _filters.Count, _filters.Count(f => f.Scope == "Reporte"),
                    _filters.Count(f => f.Scope == "Página"), _filters.Count(f => f.Scope == "Visual"));
        }
        else if (section == Performance)
        {
            StatusText = _performance.Count == 0
                ? Loc.T("Status_NoPerfRisks")
                : Loc.F("Status_PerfAlerts", _performance.Count);
        }
        else if (section == Compare)
        {
            StatusText = Loc.T("Status_Compare");
        }
        else if (section == Measures || section == Tables || section == Columns
                 || section == Relationships || section == PowerQuery)
        {
            StatusText = HasModel
                ? Loc.F("Status_ModelCounts", _model.Tables.Count, _model.Columns.Count, _model.Measures.Count, _model.Relationships.Count)
                : Loc.T("Status_NoModelNeeded");
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

        var path = _dialogs.PickSaveFile("Markdown (*.md)|*.md", Loc.F("Export_MarkdownSuggested", _sourceName));
        if (path is null) return;

        try
        {
            var summary = SummaryBuilder.Build(_model, _visuals);
            AiContextExporter.Export(path, _sourceName, _model, _visuals, summary, _usage, _filters);
            Notify(ToastKind.Success, Loc.T("Export_MarkdownSaved"),
                Loc.T("Toast_Reveal"), () => _dialogs.RevealInExplorer(path));
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR generando contexto IA: {ex}");
            NotifyError(Loc.F("Toast_FileFailed", ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportJson()
    {
        if (!EnsureSomethingLoaded()) return;

        var path = _dialogs.PickSaveFile("JSON (*.json)|*.json", Loc.F("Export_JsonSuggested", _sourceName));
        if (path is null) return;

        try
        {
            JsonModelExporter.Export(path, _sourceName, _model, _visuals, _usage, _filters);
            Notify(ToastKind.Success, Loc.T("Export_JsonSaved"),
                Loc.T("Toast_Reveal"), () => _dialogs.RevealInExplorer(path));
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR generando JSON: {ex}");
            NotifyError(Loc.F("Toast_FileFailed", ex.Message));
        }
    }

    // ---- Documentos: Word, Excel, HTML y CSV ----

    [RelayCommand]
    private Task ExportWordAsync() =>
        ExportDocumentAsync(Loc.T("Export_WordFilter"), "docx", Loc.T("Export_WordLabel"), WordExporter.WriteFile);

    [RelayCommand]
    private Task ExportExcelAsync() =>
        ExportDocumentAsync(Loc.T("Export_ExcelFilter"), "xlsx", Loc.T("Export_ExcelLabel"), ExcelExporter.WriteFile);

    [RelayCommand]
    private Task ExportHtmlAsync() =>
        ExportDocumentAsync(Loc.T("Export_HtmlFilter"), "html", Loc.T("Export_HtmlLabel"), HtmlExporter.WriteFile);

    [RelayCommand]
    private Task ExportCsvZipAsync() =>
        ExportDocumentAsync(Loc.T("Export_ZipFilter"), "zip", Loc.T("Export_CsvZipLabel"),
            (path, document) => CsvExporter.WriteZip(path, document.Tables));

    /// <summary>CSV de la sección que se está viendo, tal cual se ve: con la búsqueda y el orden de la grilla.</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (IsBusy) { NotifyBusy(); return; }

        if (SelectedSection is not IExportableSection section || !section.HasExportData)
        {
            Notify(ToastKind.Info, Loc.T("Export_CsvNeedsSection"));
            return;
        }

        var path = _dialogs.PickSaveFile(Loc.T("Export_CsvFilter"), $"{ExportText.SafeFileName(section.Title)}_{ExportText.SafeFileName(_sourceName)}.csv");
        if (path is null) return;

        var table = section.ToExportTable(filteredView: true);
        await WriteExportAsync(path, Loc.T("Export_CsvLabel"), () => CsvExporter.WriteFile(path, table),
            Loc.F(string.IsNullOrWhiteSpace(GlobalSearchText) ? "Export_CsvRows" : "Export_CsvRowsFiltered", table.Rows.Count.ToString("N0")));
    }

    private async Task ExportDocumentAsync(string filter, string extension, string label, Action<string, ExportDocument> write)
    {
        if (IsBusy) { NotifyBusy(); return; }
        if (!EnsureSomethingLoaded()) return;

        var path = _dialogs.PickSaveFile(filter, Loc.F("Export_DocSuggested", ExportText.SafeFileName(_sourceName), extension));
        if (path is null) return;

        var document = BuildExportDocument();   // se arma acá (hilo de la interfaz); solo la escritura va a segundo plano
        await WriteExportAsync(path, label, () => write(path, document), Loc.F("Export_SectionsSaved", document.Tables.Count));
    }

    private async Task WriteExportAsync(string path, string label, Action write, string successDetail)
    {
        await RunBusyAsync(async ct =>
        {
            StatusText = Loc.F("Status_Generating", label);
            try
            {
                await Task.Run(write, ct);
                Notify(ToastKind.Success, $"{char.ToUpper(label[0])}{label[1..]}: {successDetail}",
                    Loc.T("Toast_Reveal"), () => _dialogs.RevealInExplorer(path));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Write($"ERROR exportando {label}: {ex}");
                NotifyError(Loc.F("Toast_ExportFailed", label, ex.Message));
            }
        });

        UpdateStatus();
    }

    private ExportDocument BuildExportDocument()
    {
        var tables = Sections.OfType<IExportableSection>()
            .Where(s => s.HasExportData)
            .Select(s => s.ToExportTable(filteredView: false))
            .ToList();

        var facts = Summary.Tiles.Select(t => new ExportFact(t.Label, t.Value.ToString("N0"))).ToList();
        return new ExportDocument(SensitiveTextGuard.Mask(_sourceName), DateTime.Now, facts, tables);
    }

    private void NotifyBusy() =>
        Notify(ToastKind.Info, Loc.T("Toast_Busy"));

    private bool EnsureSomethingLoaded()
    {
        if (HasAnything) return true;
        Notify(ToastKind.Info, Loc.T("Toast_NothingLoaded"));
        return false;
    }
}
