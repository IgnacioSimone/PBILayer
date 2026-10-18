using System.Reflection;
using PBIExplorer.Services;

namespace PBIExplorer.Infrastructure;

/// <summary>Datos fijos de la aplicación (nombre, versión, enlaces). La versión sale del ensamblado,
/// que a su vez la toma de &lt;Version&gt; en el .csproj: hay un único lugar donde cambiarla.</summary>
public static class AppInfo
{
    public const string Name = "PBI Layer";
    public const string Author = "Ignacio Simone";
    public const string RepositoryOwner = "IgnacioSimone";
    public const string RepositoryName = "PBILayer";
    public const string RepositoryUrl = "https://github.com/" + RepositoryOwner + "/" + RepositoryName;
    public const string LicenseUrl = RepositoryUrl + "/blob/master/LICENSE";
    public const string LicenseName = "MIT";

    public static Version Version { get; } = ReadVersion();

    /// <summary>"2.0.0" (sin el cuarto número, que en esta app siempre es 0).</summary>
    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    public static string DataDirectory => AppPaths.DataDir;

    private static Version ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Puede traer un sufijo ("2.0.0+abc123" o "2.0.0-beta"): solo importan los números.
        var numeric = informational?.Split('+', '-')[0];
        if (Version.TryParse(numeric, out var parsed)) return Normalize(parsed);

        return Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
