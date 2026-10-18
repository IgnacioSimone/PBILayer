using PBIExplorer.Infrastructure;
using PBIExplorer.Models;
using PBIExplorer.Services;
using PBIExplorer.ViewModels;

namespace PBIExplorer.Tests;

public class MainViewModelTests
{
    private sealed class FakeBackend : IPowerBiBackend
    {
        public ModelSnapshot Model { get; set; } = new();
        public string? ReportName { get; set; }
        public Exception? ReadError { get; set; }
        public Task<ModelSnapshot>? PendingRead { get; set; }

        public Task<ModelSnapshot> ReadModelAsync(string server, string database) =>
            PendingRead ??
            (ReadError is null ? Task.FromResult(Model) : Task.FromException<ModelSnapshot>(ReadError));

        public string? GetOpenReportName() => ReportName;
        public Task<string?> FindOpenPbixAsync(TimeSpan budget) => Task.FromResult<string?>(null);

        /// <summary>Lo que "Power BI Desktop" tiene abierto en este test (null = nada).</summary>
        public DesktopModel? Desktop { get; set; }

        /// <summary>Si es mayor que 0, el modelo de Desktop recién "aparece" en ese sondeo (simula la carga lenta).</summary>
        public int DesktopAppearsAfter { get; set; }
        public Action? OnDesktopPoll { get; set; }
        private int _polls;

        public Task<DesktopModel?> FindDesktopModelAsync(string? reportName)
        {
            if (DesktopAppearsAfter > 0)
            {
                OnDesktopPoll?.Invoke();
                if (++_polls >= DesktopAppearsAfter && reportName is not null)
                    Desktop = new DesktopModel("localhost:5000", "guid-1", reportName);
            }

            var match = Desktop is not null &&
                        (reportName is null || string.Equals(reportName, Desktop.ReportName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match ? Desktop : null);
        }

        public (List<VisualFieldUsage>, List<FilterInfo>) ParseReport(string path) => (new(), new());
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Warnings { get; } = new();
        public List<string> Infos { get; } = new();
        public List<string> Revealed { get; } = new();
        public string? OpenFileResult { get; set; }

        public void ShowInfo(string message) => Infos.Add(message);
        public void ShowWarning(string message, string? title = null) => Warnings.Add(message);
        public string? PickOpenFile(string filter) => OpenFileResult;
        public string? PickSaveFile(string filter, string suggestedName) => null;
        public void RevealInExplorer(string path) => Revealed.Add(path);
        public List<string> Copied { get; } = new();
        public void CopyText(string text) => Copied.Add(text);

        public List<string> Opened { get; } = new();
        public Action<string>? OnOpenWithShell { get; set; }

        public void OpenWithShell(string path)
        {
            Opened.Add(path);
            OnOpenWithShell?.Invoke(path);
        }
    }

    private static MainViewModel Create(FakeBackend? backend = null, FakeDialogs? dialogs = null, AppSettings? settings = null)
    {
        // Sin auto-descarte: los tests inspeccionan los avisos y no deben competir con un timer.
        return new MainViewModel(backend ?? new FakeBackend(), dialogs ?? new FakeDialogs(), settings)
        {
            ToastDuration = Timeout.InfiniteTimeSpan,
            // "Power BI Desktop" que nunca abre el archivo no debe colgar la prueba (en la app son 4 minutos).
            DesktopPollInterval = TimeSpan.FromMilliseconds(5),
            DesktopWaitTimeout = TimeSpan.FromMilliseconds(60)
        };
    }

    private static ModelSnapshot SampleModel() => new()
    {
        Tables = { new TableInfo { Name = "Ventas" } },
        Measures =
        {
            new MeasureInfo { Table = "Ventas", Name = "Total", Expression = "SUM(Ventas[Monto])" },
            new MeasureInfo { Table = "Ventas", Name = "Margen", Expression = "[Total] * 0.2" },
        },
        Columns = { new ColumnInfo { Table = "Ventas", Name = "Monto" } },
    };

    [Fact]
    public void Starts_with_all_eleven_sections_and_model_sections_empty()
    {
        var vm = Create();

        Assert.Equal(11, vm.Sections.Count);
        Assert.Same(vm.Summary, vm.SelectedSection);
        Assert.True(vm.Measures.IsEmpty);
        Assert.Equal("Sin modelo conectado", vm.Measures.EmptyTitle);
        Assert.False(vm.Summary.HasContent);
    }

    [Fact]
    public async Task Connecting_fills_sections_and_clears_empty_state()
    {
        var vm = Create(new FakeBackend { Model = SampleModel() });

        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        Assert.Equal(2, vm.Measures.TotalCount);
        Assert.False(vm.Measures.IsEmpty);
        Assert.True(vm.Relationships.IsEmpty);
        Assert.Equal("Esta sección está vacía", vm.Relationships.EmptyTitle);
        Assert.True(vm.Summary.HasContent);
    }

    [Fact]
    public async Task Global_search_filters_every_section_and_survives_a_reload()
    {
        var vm = Create(new FakeBackend { Model = SampleModel() });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        vm.GlobalSearchText = "margen";

        Assert.Single(vm.Measures.View.Cast<object>());
        Assert.Equal("1 de 2", vm.Measures.CountText);
        Assert.Equal("1", vm.Measures.BadgeText);
        Assert.Equal("0", vm.Tables.BadgeText);

        await vm.ConnectToLiveModelAsync("localhost:1", "db");
        Assert.Single(vm.Measures.View.Cast<object>());

        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(2, vm.Measures.View.Cast<object>().Count());
        Assert.Null(vm.Measures.BadgeText);
        Assert.Equal("2 filas", vm.Measures.CountText);
    }

    [Fact]
    public async Task Selecting_a_measure_opens_its_detail_with_code_and_dependencies()
    {
        var vm = Create(new FakeBackend { Model = SampleModel() });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");
        Assert.Null(vm.Measures.Detail);

        vm.Measures.SelectedItem = vm.Measures.View.Cast<MeasureInfo>().Single(m => m.Name == "Margen");

        var detail = Assert.IsType<DetailViewModel>(vm.Measures.Detail);
        Assert.Equal("Margen", detail.Title);
        Assert.Equal("Medida · Ventas", detail.Subtitle);
        Assert.Equal("[Total] * 0.2", detail.Code);
        Assert.Equal(CodeLanguage.Dax, detail.Language);
        Assert.Equal(new[] { "[Total]" }, detail.DependsOn.Select(d => d.Label));
        Assert.Equal("Sin uso — candidata a eliminar", detail.StatusText);

        vm.Measures.CloseDetailCommand.Execute(null);
        Assert.Null(vm.Measures.Detail);
    }

    [Fact]
    public async Task Clicking_a_dependency_jumps_to_that_object_even_with_a_search_active()
    {
        var vm = Create(new FakeBackend { Model = SampleModel() });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");
        vm.Measures.SelectedItem = vm.Measures.View.Cast<MeasureInfo>().Single(m => m.Name == "Margen");
        vm.GlobalSearchText = "margen";   // esconde a "Total"

        vm.Measures.Detail!.DependsOn[0].OpenCommand.Execute(null);

        Assert.Same(vm.Measures, vm.SelectedSection);
        Assert.Equal("Total", vm.Measures.SelectedItem!.Name);
        Assert.Equal("", vm.GlobalSearchText);
        Assert.Equal("Total", vm.Measures.Detail!.Title);
        // y "Total" ahora muestra que lo usa [Margen]
        Assert.Equal(new[] { "[Margen]" }, vm.Measures.Detail!.UsedBy.Select(d => d.Label));
    }

    [Fact]
    public async Task A_dependency_written_with_its_table_can_be_a_measure()
    {
        var model = SampleModel();
        model.Measures.Add(new MeasureInfo { Table = "Ventas", Name = "Calificada", Expression = "Ventas[Total] + 1" });
        var vm = Create(new FakeBackend { Model = model });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");
        vm.Measures.SelectedItem = vm.Measures.View.Cast<MeasureInfo>().Single(m => m.Name == "Calificada");

        vm.Measures.Detail!.DependsOn.Single(d => d.Label == "Ventas[Total]").OpenCommand.Execute(null);

        Assert.Equal("Total", vm.Measures.SelectedItem!.Name);
        Assert.Empty(vm.Toasts);
    }

    [Fact]
    public async Task A_dependency_that_is_not_in_the_model_says_so()
    {
        var model = SampleModel();
        model.Measures.Add(new MeasureInfo { Table = "Ventas", Name = "Rara", Expression = "[No existe]" });
        var vm = Create(new FakeBackend { Model = model });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");
        vm.Measures.SelectedItem = vm.Measures.View.Cast<MeasureInfo>().Single(m => m.Name == "Rara");

        vm.Measures.Detail!.DependsOn[0].OpenCommand.Execute(null);

        Assert.Contains(vm.Toasts, t => t.Kind == ToastKind.Info && t.Message.Contains("[No existe]"));
        Assert.Equal("Rara", vm.Measures.SelectedItem!.Name);
    }

    [Fact]
    public async Task Calculated_columns_show_their_dax_and_plain_columns_show_their_source()
    {
        var model = SampleModel();
        model.Columns.Add(new ColumnInfo { Table = "Ventas", Name = "Doble", Expression = "Ventas[Monto] * 2", ColumnType = "Calculated" });
        model.Columns[0].SourceColumn = "monto_src";
        var vm = Create(new FakeBackend { Model = model });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        vm.Columns.SelectedItem = model.Columns[1];
        Assert.Equal("Ventas[Monto] * 2", vm.Columns.Detail!.Code);
        Assert.Equal(new[] { "Ventas[Monto]" }, vm.Columns.Detail.DependsOn.Select(d => d.Label));

        vm.Columns.SelectedItem = model.Columns[0];
        Assert.Null(vm.Columns.Detail!.Code);
        Assert.Contains(vm.Columns.Detail.Facts, f => f.Label == "Columna de origen" && f.Value == "monto_src");
    }

    [Fact]
    public async Task Power_query_detail_is_m_code_and_copy_goes_to_the_clipboard()
    {
        var model = SampleModel();
        model.PowerQueries.Add(new PowerQueryInfo { Name = "Ventas", Kind = "Tabla", MCode = "let Origen = 1 in Origen" });
        var dialogs = new FakeDialogs();
        var vm = Create(new FakeBackend { Model = model }, dialogs);
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        vm.PowerQuery.SelectedItem = model.PowerQueries[0];
        vm.PowerQuery.Detail!.CopyCommand.Execute(null);

        Assert.Equal(CodeLanguage.M, vm.PowerQuery.Detail.Language);
        Assert.Equal(new[] { "let Origen = 1 in Origen" }, dialogs.Copied);
        Assert.Contains(vm.Toasts, t => t.Kind == ToastKind.Success);
    }

    [Fact]
    public async Task A_search_without_matches_says_so_in_that_section()
    {
        var vm = Create(new FakeBackend { Model = SampleModel() });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        vm.GlobalSearchText = "zzz";

        Assert.Equal("Sin resultados para \"zzz\"", vm.Measures.NoResultsText);
        Assert.Equal("0 de 2", vm.Measures.CountText);

        vm.GlobalSearchText = "";
        Assert.Null(vm.Measures.NoResultsText);
    }

    [Fact]
    public async Task Standalone_launch_attaches_itself_to_the_model_open_in_power_bi_desktop()
    {
        var backend = new FakeBackend
        {
            Model = SampleModel(),
            Desktop = new DesktopModel("localhost:5000", "guid-1", "Ventas 2026")
        };
        var vm = Create(backend);

        await vm.InitializeAsync(new[] { "PBIContext.exe" });   // sin servidor/base: abierta a mano

        Assert.Equal(2, vm.Measures.TotalCount);
        Assert.False(vm.Measures.IsEmpty);
    }

    [Fact]
    public async Task Opening_a_pbix_that_is_open_in_desktop_reads_its_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "Ventas 2026.pbix");
        var backend = new FakeBackend
        {
            Model = SampleModel(),
            Desktop = new DesktopModel("localhost:5000", "guid-1", "Ventas 2026")
        };
        var vm = Create(backend);

        await vm.OpenFileAsync(path);

        Assert.Equal(2, vm.Measures.TotalCount);
        Assert.Contains(vm.Toasts, t => t.Kind == ToastKind.Success);
    }

    [Fact]
    public async Task Opening_a_pbix_not_open_in_desktop_opens_it_there_and_drops_a_stale_model()
    {
        var backend = new FakeBackend
        {
            Model = SampleModel(),
            Desktop = new DesktopModel("localhost:5000", "guid-1", "Otro tablero")
        };
        var dialogs = new FakeDialogs();
        var vm = Create(backend, dialogs);
        await vm.InitializeAsync(new[] { "PBIContext.exe" });
        Assert.Equal(2, vm.Measures.TotalCount);   // quedó enganchada a "Otro tablero"
        var path = Path.Combine(Path.GetTempPath(), "Ventas 2026.pbix");

        await vm.OpenFileAsync(path);

        Assert.Equal(new[] { path }, dialogs.Opened);   // se pidió abrirlo en Power BI Desktop
        Assert.Equal(0, vm.Measures.TotalCount);        // no se mezclan medidas de otro tablero con estos visuales
        Assert.True(vm.Measures.IsEmpty);
    }

    [Fact]
    public async Task Opening_a_pbix_opens_it_in_desktop_waits_for_its_model_and_reads_it()
    {
        var backend = new FakeBackend { Model = SampleModel() };
        var dialogs = new FakeDialogs();
        // Power BI Desktop "tarda" en abrir: el modelo recién aparece en el tercer sondeo.
        dialogs.OnOpenWithShell = _ => backend.DesktopAppearsAfter = 3;
        var vm = Create(backend, dialogs);

        await vm.OpenFileAsync(Path.Combine(Path.GetTempPath(), "Ventas 2026.pbix"));

        Assert.Single(dialogs.Opened);
        Assert.Equal(2, vm.Measures.TotalCount);
        Assert.False(vm.Measures.IsEmpty);
        Assert.False(vm.IsBusy);
        Assert.Contains(vm.Toasts, t => t.Kind == ToastKind.Success);
    }

    [Fact]
    public async Task Waiting_for_desktop_gives_up_with_a_clear_warning()
    {
        var vm = Create(new FakeBackend());

        await vm.OpenFileAsync(Path.Combine(Path.GetTempPath(), "Ventas 2026.pbix"));

        Assert.Contains(vm.Toasts, t => t.Kind == ToastKind.Warning && t.Message.Contains("todavía no terminó"));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task After_giving_up_the_sections_offer_to_retry_opening_in_desktop()
    {
        var dialogs = new FakeDialogs();
        var vm = Create(new FakeBackend(), dialogs);
        await vm.OpenFileAsync(Path.Combine(Path.GetTempPath(), "Ventas 2026.pbix"));

        Assert.Equal("Falta el modelo de este tablero", vm.Tables.EmptyTitle);
        Assert.Equal("Abrir en Power BI Desktop", vm.Tables.EmptyActionText);

        await vm.OpenReportInDesktopCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogs.Opened.Count);
    }

    [Fact]
    public async Task A_pbix_already_open_in_desktop_is_not_opened_again()
    {
        var dialogs = new FakeDialogs();
        var backend = new FakeBackend
        {
            Model = SampleModel(),
            Desktop = new DesktopModel("localhost:5000", "guid-1", "Ventas 2026")
        };
        var vm = Create(backend, dialogs);

        await vm.OpenFileAsync(Path.Combine(Path.GetTempPath(), "Ventas 2026.pbix"));

        Assert.Empty(dialogs.Opened);
        Assert.Equal(2, vm.Measures.TotalCount);
    }

    [Fact]
    public async Task Connection_failure_raises_a_retryable_error_toast()
    {
        var vm = Create(new FakeBackend { ReadError = new InvalidOperationException("boom") });

        var ok = await vm.ConnectToLiveModelAsync("localhost:1", "db");

        Assert.False(ok);
        var toast = Assert.Single(vm.Toasts);
        Assert.Equal(ToastKind.Error, toast.Kind);
        Assert.Contains("boom", toast.Message);
        Assert.Equal("Reintentar", toast.ActionText);
        Assert.Equal("No se pudo conectar al modelo en vivo.", vm.StatusText);
    }

    [Fact]
    public async Task Cancelling_a_slow_connection_returns_control_and_clears_busy()
    {
        var never = new TaskCompletionSource<ModelSnapshot>();
        var vm = Create(new FakeBackend { PendingRead = never.Task });

        var init = vm.InitializeAsync(new[] { "PBIContext.exe", "localhost:1", "db" });
        Assert.True(vm.IsBusy);

        vm.CancelCommand.Execute(null);
        await init;

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Toasts);
    }

    [Fact]
    public async Task Initialize_without_open_report_explains_it_in_visuals()
    {
        var vm = Create();

        await vm.InitializeAsync(new[] { "PBIContext.exe" });

        Assert.True(vm.Visuals.IsEmpty);
        Assert.Equal("No se detectó ningún tablero abierto", vm.Visuals.EmptyTitle);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Exporting_with_nothing_loaded_shows_a_toast_instead_of_a_dialog()
    {
        var dialogs = new FakeDialogs();
        var vm = Create(dialogs: dialogs);

        vm.ExportMarkdownCommand.Execute(null);

        Assert.Single(vm.Toasts);
        Assert.Empty(dialogs.Infos);
    }

    [Fact]
    public void Export_commands_are_disabled_while_busy()
    {
        var vm = Create();

        vm.IsBusy = true;

        Assert.False(vm.ExportJsonCommand.CanExecute(null));
        Assert.False(vm.ExportMarkdownCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Usage_filter_only_unused_narrows_the_list()
    {
        var vm = Create(new FakeBackend { Model = SampleModel() });
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        var all = vm.Usage.View.Cast<ObjectUsage>().Count();
        vm.Usage.OnlyUnused = true;
        var unused = vm.Usage.View.Cast<ObjectUsage>().Count();

        Assert.True(all > 0);
        Assert.True(unused <= all);
        Assert.All(vm.Usage.View.Cast<ObjectUsage>(), u => Assert.True(u.IsUnused));
    }

    [Fact]
    public void Keyboard_navigation_moves_between_sections_and_wraps()
    {
        var vm = Create();

        vm.GoToSectionCommand.Execute("2");
        Assert.Same(vm.Tables, vm.SelectedSection);

        vm.PreviousSectionCommand.Execute(null);
        vm.PreviousSectionCommand.Execute(null);
        vm.PreviousSectionCommand.Execute(null);
        Assert.Same(vm.Compare, vm.SelectedSection);

        vm.NextSectionCommand.Execute(null);
        Assert.Same(vm.Summary, vm.SelectedSection);

        vm.GoToSectionCommand.Execute("99");
        Assert.Same(vm.Summary, vm.SelectedSection);
    }

    [Fact]
    public void Theme_cycles_system_light_dark_and_is_remembered()
    {
        var settings = new AppSettings();
        var vm = Create(settings: settings);

        Assert.Equal(ThemeMode.System, vm.Theme);
        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Light, settings.Theme);
        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Dark, settings.Theme);
        vm.CycleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.System, settings.Theme);
    }

    [Fact]
    public void Last_section_is_restored_from_settings()
    {
        var vm = Create(settings: new AppSettings { LastSection = "Tablas" });

        Assert.Equal("Tablas", vm.SelectedSection.Title);
    }

    [Fact]
    public async Task Opening_a_file_with_an_unsupported_extension_warns()
    {
        var vm = Create();

        await vm.OpenFileAsync(@"C:\temp\notas.txt");

        var toast = Assert.Single(vm.Toasts);
        Assert.Equal(ToastKind.Warning, toast.Kind);
    }

    [Fact]
    public async Task Opening_a_missing_recent_file_removes_it_and_warns()
    {
        var settings = new AppSettings();
        settings.RecentFiles.Add(@"C:\no-existe\viejo.pbix");
        var vm = Create(settings: settings);
        Assert.Single(vm.RecentFiles);

        await vm.OpenRecentCommand.ExecuteAsync(@"C:\no-existe\viejo.pbix");

        Assert.Empty(vm.RecentFiles);
        Assert.Empty(settings.RecentFiles);
        Assert.Single(vm.Toasts);
    }

    [Fact]
    public void Toast_action_runs_and_dismisses_the_toast()
    {
        var dialogs = new FakeDialogs();
        var vm = Create(dialogs: dialogs);
        vm.ExportJsonCommand.Execute(null);   // sin datos: aviso informativo sin acción
        var toast = Assert.Single(vm.Toasts);

        toast.DismissCommand.Execute(null);

        Assert.Empty(vm.Toasts);
    }
}
