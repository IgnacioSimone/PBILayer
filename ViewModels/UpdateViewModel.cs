using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Infrastructure;
using PBIExplorer.Services.Updates;

namespace PBIExplorer.ViewModels;

/// <summary>Estado de las actualizaciones: qué versión hay, si buscar solo, y bajar e instalar con verificación.
/// La búsqueda es opcional (por defecto apagada): es la única conexión de red de la aplicación.</summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    /// <summary>Con la búsqueda automática activa, se consulta como mucho una vez cada tantas horas.</summary>
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(20);

    private readonly IUpdateFeed _feed;
    private readonly UpdateInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly Action? _persist;
    private readonly Version _current;
    private readonly Func<DateTime> _utcNow;

    public UpdateViewModel(IUpdateFeed feed, UpdateInstaller installer, IDialogService dialogs, AppSettings settings,
        Action? persist, Version current, Func<DateTime>? utcNow = null)
    {
        _feed = feed;
        _installer = installer;
        _dialogs = dialogs;
        _settings = settings;
        _persist = persist;
        _current = current;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _statusText = $"Versión instalada: {current.Major}.{current.Minor}.{current.Build}";
    }

    [ObservableProperty] private string _statusText;
    [ObservableProperty] private ReleaseInfo? _release;
    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isDownloading;

    public bool IsBusy => IsChecking || IsDownloading;

    /// <summary>Hay una versión más nueva que la instalada.</summary>
    public bool IsUpdateAvailable => Release is not null && Release.Version > _current;
    public bool CanInstall => IsUpdateAvailable && Release!.CanInstall;
    public string Notes => Release?.Notes ?? "";

    partial void OnReleaseChanged(ReleaseInfo? value)
    {
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(Notes));
    }

    /// <summary>Casilla "Buscar actualizaciones automáticamente".</summary>
    public bool AutoCheck
    {
        get => _settings.UpdateChecks == UpdateCheckMode.On;
        set
        {
            var mode = value ? UpdateCheckMode.On : UpdateCheckMode.Off;
            if (_settings.UpdateChecks == mode) return;

            _settings.UpdateChecks = mode;
            _settings.UpdatePromptShown = true;
            _persist?.Invoke();
            OnPropertyChanged();
        }
    }

    /// <summary>Se llama al arrancar: busca solo si el usuario lo activó y pasó el intervalo desde la última vez.</summary>
    public async Task<UpdateCheckResult?> AutoCheckIfDueAsync(CancellationToken ct = default)
    {
        if (_settings.UpdateChecks != UpdateCheckMode.On) return null;

        var last = _settings.LastUpdateCheckUtc;
        if (last is not null && _utcNow() - last.Value < AutoCheckInterval) return null;

        return await RunCheckAsync(manual: false, ct);
    }

    [RelayCommand]
    private Task CheckNowAsync() => RunCheckAsync(manual: true, CancellationToken.None);

    private async Task<UpdateCheckResult> RunCheckAsync(bool manual, CancellationToken ct)
    {
        if (IsBusy) return new UpdateCheckResult(UpdateStatus.Failed, Error: "Ya hay una operación en curso.");

        IsChecking = true;
        StatusText = "Buscando actualizaciones…";
        try
        {
            var result = await UpdateChecker.CheckAsync(_feed, _current, ct);

            // Solo una consulta que llegó a GitHub cuenta como "ya busqué hoy"; un fallo de red se reintenta en el próximo arranque.
            if (result.Status != UpdateStatus.Failed)
            {
                _settings.LastUpdateCheckUtc = _utcNow();
                _persist?.Invoke();
            }

            Release = result.Status is UpdateStatus.Available or UpdateStatus.UpToDate ? result.Release : null;
            StatusText = result.Status switch
            {
                UpdateStatus.Available => $"Hay una versión nueva: {result.Release!.Version.Major}.{result.Release.Version.Minor}.{result.Release.Version.Build}",
                UpdateStatus.UpToDate => "Tenés la última versión.",
                UpdateStatus.NoReleases => "No hay versiones publicadas para consultar (el repositorio puede ser privado).",
                _ => manual ? $"No se pudo buscar: {result.Error}" : StatusText
            };
            return result;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!CanInstall || IsBusy) return;

        IsDownloading = true;
        Progress = 0;
        StatusText = "Descargando la actualización…";
        try
        {
            var path = await _installer.DownloadAndVerifyAsync(Release!, new Progress<double>(p => Progress = p), CancellationToken.None);
            StatusText = "Verificada. Cerrando PBI Context para instalar…";

            // El instalador pide permisos de administrador (registra la herramienta externa) y cierra la app si hace falta.
            _dialogs.RunInstallerAndExit(path);
        }
        catch (Exception ex)
        {
            AppLog.Write($"ERROR actualizando: {ex}");
            StatusText = $"No se pudo actualizar: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = Release?.PageUrl;
        if (!string.IsNullOrEmpty(url) && UpdateInstaller.IsTrustedUrl(url)) _dialogs.OpenWithShell(url);
        else _dialogs.OpenWithShell(AppInfo.RepositoryUrl + "/releases");
    }
}
