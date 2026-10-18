using System.Windows;
using System.Windows.Threading;
using PBIExplorer.Infrastructure;
using PBIExplorer.Services;
using PBIExplorer.Services.Updates;
using PBIExplorer.ViewModels;

namespace PBIExplorer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Sin esto, cualquier excepción en el arranque cierra la app en silencio
        // y no queda rastro de por qué.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        Exit += (_, args) => AppLog.Write($"App.Exit — ExitCode={args.ApplicationExitCode}");

        try
        {
            base.OnStartup(e);

            var settings = AppSettings.Load();
            Loc.Initialize(settings.Language);
            Views.SearchCommands.Register();
            ThemeService.Apply(settings.Theme);

            var dialogs = new WpfDialogService();

            // Única conexión de red de la app, y solo si el usuario la activa (ver Acerca de).
            var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"PBILayer/{AppInfo.VersionText}");
            var feed = new GitHubUpdateFeed(http, AppInfo.RepositoryOwner, AppInfo.RepositoryName);
            var installer = new UpdateInstaller(http, System.IO.Path.Combine(AppPaths.DataDir, "updates"));
            var updates = new UpdateViewModel(feed, installer, dialogs, settings, () => settings.Save(), AppInfo.Version);

            var viewModel = new MainViewModel(new PowerBiBackend(), dialogs, settings, () => settings.Save(), updates);
            new MainWindow(viewModel, settings, dialogs).Show();
        }
        catch (Exception ex)
        {
            AppLog.Write($"EXCEPCIÓN en el arranque: {ex}");
            throw;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"EXCEPCIÓN NO CONTROLADA (UI): {e.Exception}");
        MessageBox.Show(Loc.F("App_UnexpectedError", e.Exception.Message, AppPaths.LogPath),
            WpfDialogService.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Write($"EXCEPCIÓN NO CONTROLADA (dominio): {e.ExceptionObject}");
    }
}
