using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace PBIExplorer.Services;

/// <summary>Un modelo que Power BI Desktop tiene abierto ahora, listo para leer vía ADOMD.</summary>
public sealed record DesktopModel(string Server, string Database, string ReportName);

/// <summary>
/// Encuentra los modelos de los tableros abiertos en Power BI Desktop sin que haga falta
/// lanzar la app desde "Herramientas externas". Cada Desktop abierto levanta su propio
/// motor local (msmdsrv.exe) con un puerto al azar; el puerto y el nombre de la base
/// están en archivos de su carpeta de trabajo, y la ventana de Desktop dice qué
/// reporte tiene abierto. Solo mira procesos y archivos de metadata: no toca datos.
/// </summary>
public static partial class DesktopModelLocator
{
    public static IReadOnlyList<DesktopModel> Find()
    {
        var found = new List<DesktopModel>();

        var reportByDesktopPid = new Dictionary<int, string>();
        foreach (var process in Process.GetProcessesByName("PBIDesktop"))
        {
            var name = PbixLocator.ReportNameFor(process);
            if (name is not null) reportByDesktopPid[process.Id] = name;
        }
        if (reportByDesktopPid.Count == 0) return found;

        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'msmdsrv.exe'");

        foreach (var obj in searcher.Get())
        {
            using (obj)
            {
                var parentPid = Convert.ToInt32(obj["ParentProcessId"]);
                if (!reportByDesktopPid.TryGetValue(parentPid, out var reportName)) continue;

                var dataDir = ParseDataDir(obj["CommandLine"] as string);
                if (dataDir is null) continue;

                var model = ReadModel(dataDir, reportName);
                if (model is not null) found.Add(model);
            }
        }

        return found;
    }

    /// <summary>
    /// El modelo del reporte con ese nombre; si no se indica nombre y hay un único
    /// Power BI Desktop con modelo, ese. Null si no hay coincidencia clara.
    /// </summary>
    public static DesktopModel? FindFor(string? reportName)
    {
        var models = Find();

        if (!string.IsNullOrWhiteSpace(reportName))
        {
            return models.FirstOrDefault(m => string.Equals(m.ReportName, reportName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return models.Count == 1 ? models[0] : null;
    }

    public static string? ParseDataDir(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var match = DataDirPattern().Match(commandLine);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static DesktopModel? ReadModel(string dataDir, string reportName)
    {
        try
        {
            var portFile = Path.Combine(dataDir, "msmdsrv.port.txt");
            if (!File.Exists(portFile)) return null;

            // El archivo está en UTF-16: leerlo como UTF-8 intercala caracteres nulos.
            var port = File.ReadAllText(portFile, Encoding.Unicode).Trim();
            if (!int.TryParse(port, out var portNumber)) return null;

            // La base se llama como el GUID del archivo "<guid>.<n>.db.xml" de la carpeta.
            var dbFile = Directory.EnumerateFiles(dataDir, "*.db.xml").FirstOrDefault();
            if (dbFile is null) return null;
            var database = Path.GetFileName(dbFile).Split('.')[0];

            return new DesktopModel($"localhost:{portNumber}", database, reportName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"-s\s+""([^""]+)""")]
    private static partial Regex DataDirPattern();
}
