using PBIExplorer.Models;

namespace PBIExplorer.Services;

/// <summary>Acceso a Power BI Desktop (modelo en vivo y archivo .pbix). Abstraído para
/// que los ViewModels no dependan de ADOMD ni del disco.</summary>
public interface IPowerBiBackend
{
    Task<ModelSnapshot> ReadModelAsync(string server, string database);
    string? GetOpenReportName();
    Task<string?> FindOpenPbixAsync(TimeSpan budget);
    (List<VisualFieldUsage> Visuals, List<FilterInfo> Filters) ParseReport(string path);
}

public sealed class PowerBiBackend : IPowerBiBackend
{
    // El modelo puede tener miles de medidas/columnas: la lectura pesada va a un hilo
    // de fondo para no congelar la ventana.
    public Task<ModelSnapshot> ReadModelAsync(string server, string database) =>
        Task.Run(() => new AdomdModelReader(server, database).ReadAll());

    public string? GetOpenReportName() => PbixLocator.GetOpenReportName();

    public Task<string?> FindOpenPbixAsync(TimeSpan budget) =>
        Task.Run(() => PbixLocator.FindOpenPbix(budget));

    public (List<VisualFieldUsage>, List<FilterInfo>) ParseReport(string path)
    {
        var parser = new PbixReportParser();
        return (parser.ParseVisuals(path), parser.ParseFilters(path));
    }
}
