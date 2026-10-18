using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PBIExplorer.Services;

namespace PBIExplorer.Infrastructure;

/// <summary>Si la app busca versiones nuevas en GitHub: sin elegir todavía, sí o no.</summary>
public enum UpdateCheckMode
{
    Unset,
    On,
    Off
}

/// <summary>Preferencias del usuario, persistidas en %LOCALAPPDATA%\PBILayer\settings.json.</summary>
public sealed class AppSettings
{
    public const int MaxRecentFiles = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDir, "settings.json");

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Idioma de la interfaz (se aplica al reiniciar). En automático sigue el de Windows.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Título de la última sección abierta.</summary>
    public string? LastSection { get; set; }

    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Buscar actualizaciones es opcional: sin elegir (Unset) no se hace ninguna conexión.</summary>
    public UpdateCheckMode UpdateChecks { get; set; } = UpdateCheckMode.Unset;

    /// <summary>Ya se le preguntó una vez al usuario si quiere buscar actualizaciones (no se insiste).</summary>
    public bool UpdatePromptShown { get; set; }

    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Se llama cuando cambia algo que conviene guardar enseguida (ej. recientes).</summary>
    [JsonIgnore]
    public Action? Changed { get; set; }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        Changed?.Invoke();
    }

    public void RemoveRecentFile(string path)
    {
        if (RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            Changed?.Invoke();
    }

    public void ClearRecentFiles()
    {
        RecentFiles.Clear();
        Changed?.Invoke();
    }

    /// <summary>Un settings.json corrupto o ausente nunca debe impedir abrir la app.</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo leer settings.json, uso valores por defecto: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo guardar settings.json: {ex.Message}");
        }
    }
}
