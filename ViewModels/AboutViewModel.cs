using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Infrastructure;

namespace PBIExplorer.ViewModels;

/// <summary>Contenido de la ventana "Acerca de": quién es la app, qué versión, enlaces y actualizaciones.</summary>
public sealed partial class AboutViewModel
{
    private readonly IDialogService _dialogs;

    public AboutViewModel(UpdateViewModel updates, IDialogService dialogs)
    {
        Updates = updates;
        _dialogs = dialogs;
    }

    public UpdateViewModel Updates { get; }

    public string Name => AppInfo.Name;
    public string VersionLine => $"Versión {AppInfo.VersionText}";
    public string Author => $"© 2026 {AppInfo.Author} · Licencia {AppInfo.LicenseName}";
    public string DataFolder => AppInfo.DataDirectory;

    public string Disclaimer =>
        "Herramienta independiente, sin afiliación a Microsoft. Power BI es marca de Microsoft.";

    public string Privacy =>
        "Solo lee metadata del modelo y del reporte: nunca consulta una fila de datos. No tiene telemetría. " +
        "La única conexión posible es buscar actualizaciones en GitHub, y solo si la activás.";

    /// <summary>Abre un enlace del proyecto. Solo se abren los que apuntan al repositorio de la app.</summary>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrEmpty(url) && url.StartsWith(AppInfo.RepositoryUrl, StringComparison.OrdinalIgnoreCase))
            _dialogs.OpenWithShell(url);
    }

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenWithShell(AppInfo.DataDirectory);
}
