using PBIExplorer.Models;

namespace PBIExplorer.Services;

/// <summary>Acceso a Power BI Desktop (modelo en vivo y archivo .pbix). Abstraído para
/// que los ViewModels no dependan de ADOMD ni del disco.</summary>
public interface IPowerBiBackend
{
    Task<ModelSnapshot> ReadModelAsync(string server, string database);
    string? GetOpenReportName();
    Task<string?> FindOpenPbixAsync(TimeSpan budget);

    /// <summary>Modelo abierto en Power BI Desktop para ese reporte (o el único abierto si no se indica nombre).</summary>
    Task<DesktopModel?> FindDesktopModelAsync(string? reportName);
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

    public Task<DesktopModel?> FindDesktopModelAsync(string? reportName) =>
        Task.Run(() =>
        {
            try { return DesktopModelLocator.FindFor(reportName); }
            catch (Exception ex)
            {
                // WMI o permisos: sin esto la app igual funciona, solo que sin autodetección.
                Infrastructure.AppLog.Write($"No se pudo buscar el modelo de Power BI Desktop: {ex.Message}");
                return null;
            }
        });

    public (List<VisualFieldUsage>, List<FilterInfo>) ParseReport(string path)
    {
        var parser = new PbixReportParser();
        return (parser.ParseVisuals(path), parser.ParseFilters(path));
    }
}
