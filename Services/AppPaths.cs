using System.IO;

namespace PBIExplorer.Services;

/// <summary>Rutas propias de la app en el perfil del usuario — nada en carpetas
/// compartidas como %TEMP%, donde otro proceso local podría curiosear qué tableros
/// se estuvieron analizando.</summary>
public static class AppPaths
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBILayer");

    public static readonly string LogPath = Path.Combine(DataDir, "debug.log");

    /// <summary>Carpeta de datos de cuando la app se llamaba "PBI Context".</summary>
    private static readonly string PreviousDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBIContext");

    static AppPaths()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* el log es best-effort */ }
        MigratePreferences(PreviousDataDir, DataDir);
    }

    /// <summary>Al cambiar el nombre de la app, trae las preferencias de la carpeta vieja a la nueva.
    /// Copia (no mueve) y solo si todavía no hay preferencias nuevas: nunca pisa nada.</summary>
    public static void MigratePreferences(string previousDir, string newDir)
    {
        try
        {
            var source = Path.Combine(previousDir, "settings.json");
            var target = Path.Combine(newDir, "settings.json");
            if (!File.Exists(source) || File.Exists(target)) return;

            Directory.CreateDirectory(newDir);
            File.Copy(source, target);
        }
        catch
        {
            // Si no se puede, la app arranca con las preferencias por defecto.
        }
    }
}
