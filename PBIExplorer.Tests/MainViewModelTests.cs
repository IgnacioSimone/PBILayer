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

        public Task<ModelSnapshot> ReadModelAsync(string server, string database) =>
            ReadError is null ? Task.FromResult(Model) : Task.FromException<ModelSnapshot>(ReadError);

        public string? GetOpenReportName() => ReportName;
        public Task<string?> FindOpenPbixAsync(TimeSpan budget) => Task.FromResult<string?>(null);

        public (List<VisualFieldUsage>, List<FilterInfo>) ParseReport(string path) => (new(), new());
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Warnings { get; } = new();
        public List<string> Infos { get; } = new();
        public string? OpenFileResult { get; set; }

        public void ShowInfo(string message) => Infos.Add(message);
        public void ShowWarning(string message, string? title = null) => Warnings.Add(message);
        public string? PickOpenFile(string filter) => OpenFileResult;
        public string? PickSaveFile(string filter, string suggestedName) => null;
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
        var vm = new MainViewModel(new FakeBackend(), new FakeDialogs());

        Assert.Equal(11, vm.Sections.Count);
        Assert.Same(vm.Summary, vm.SelectedSection);
        Assert.True(vm.Measures.IsEmpty);
        Assert.Equal("Sin modelo conectado", vm.Measures.EmptyTitle);
        Assert.False(vm.Summary.HasContent);
    }

    [Fact]
    public async Task Connecting_fills_sections_and_clears_empty_state()
    {
        var vm = new MainViewModel(new FakeBackend { Model = SampleModel() }, new FakeDialogs());

        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        Assert.Equal(2, vm.Measures.TotalCount);
        Assert.False(vm.Measures.IsEmpty);
        Assert.True(vm.Relationships.IsEmpty);
        Assert.Equal("Esta sección está vacía", vm.Relationships.EmptyTitle);
        Assert.True(vm.Summary.HasContent);
    }

    [Fact]
    public async Task Search_text_filters_the_view_and_survives_a_reload()
    {
        var backend = new FakeBackend { Model = SampleModel() };
        var vm = new MainViewModel(backend, new FakeDialogs());
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        vm.Measures.SearchText = "margen";
        Assert.Single(vm.Measures.View.Cast<object>());

        await vm.ConnectToLiveModelAsync("localhost:1", "db");
        Assert.Single(vm.Measures.View.Cast<object>());
    }

    [Fact]
    public async Task Connection_failure_warns_and_updates_status()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(new FakeBackend { ReadError = new InvalidOperationException("boom") }, dialogs);

        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        Assert.Single(dialogs.Warnings);
        Assert.Contains("boom", dialogs.Warnings[0]);
        Assert.Equal("No se pudo conectar al modelo en vivo.", vm.StatusText);
    }

    [Fact]
    public async Task Initialize_without_open_report_explains_it_in_visuals()
    {
        var vm = new MainViewModel(new FakeBackend(), new FakeDialogs());

        await vm.InitializeAsync(new[] { "PBIContext.exe" });

        Assert.True(vm.Visuals.IsEmpty);
        Assert.Equal("No se detectó ningún tablero abierto", vm.Visuals.EmptyTitle);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Exporting_with_nothing_loaded_shows_info_instead_of_a_dialog()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(new FakeBackend(), dialogs);

        vm.ExportMarkdownCommand.Execute(null);

        Assert.Single(dialogs.Infos);
    }

    [Fact]
    public void Export_commands_are_disabled_while_busy()
    {
        var vm = new MainViewModel(new FakeBackend(), new FakeDialogs());

        vm.IsBusy = true;

        Assert.False(vm.ExportJsonCommand.CanExecute(null));
        Assert.False(vm.ExportMarkdownCommand.CanExecute(null));
    }

    [Fact]
    public async Task Usage_filter_only_unused_narrows_the_list()
    {
        var vm = new MainViewModel(new FakeBackend { Model = SampleModel() }, new FakeDialogs());
        await vm.ConnectToLiveModelAsync("localhost:1", "db");

        var all = vm.Usage.View.Cast<ObjectUsage>().Count();
        vm.Usage.OnlyUnused = true;
        var unused = vm.Usage.View.Cast<ObjectUsage>().Count();

        Assert.True(all > 0);
        Assert.True(unused <= all);
        Assert.All(vm.Usage.View.Cast<ObjectUsage>(), u => Assert.True(u.IsUnused));
    }
}
