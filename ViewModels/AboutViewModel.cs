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
    public string VersionLine => Loc.F("About_Version", AppInfo.VersionText);
    public string Author => Loc.F("About_Author", AppInfo.Author, AppInfo.LicenseName);
    public string DataFolder => AppInfo.DataDirectory;

    public string Disclaimer =>
        Loc.T("About_Disclaimer");

    public string Privacy =>
        Loc.T("About_Privacy");

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
