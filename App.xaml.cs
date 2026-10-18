using System.Windows;
using System.Windows.Threading;
using PBIExplorer.Infrastructure;
using PBIExplorer.Services;
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
            ThemeService.Apply(settings.Theme);

            var viewModel = new MainViewModel(new PowerBiBackend(), new WpfDialogService(), settings, () => settings.Save());
            new MainWindow(viewModel, settings).Show();
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
        MessageBox.Show($"Ocurrió un error inesperado:\n\n{e.Exception.Message}\n\nDetalle completo en:\n{AppPaths.LogPath}",
            WpfDialogService.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Write($"EXCEPCIÓN NO CONTROLADA (dominio): {e.ExceptionObject}");
    }
}
