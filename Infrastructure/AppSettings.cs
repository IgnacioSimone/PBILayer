using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PBIExplorer.Services;

namespace PBIExplorer.Infrastructure;

/// <summary>Preferencias del usuario, persistidas en %LOCALAPPDATA%\PBIContext\settings.json.</summary>
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

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Título de la última sección abierta.</summary>
    public string? LastSection { get; set; }

    public List<string> RecentFiles { get; set; } = new();

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
