using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PBIExplorer.Infrastructure;

public sealed class WpfDialogService : IDialogService
{
    public const string AppName = "PBI Context";

    public void ShowInfo(string message) =>
        MessageBox.Show(message, AppName, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string? title = null) =>
        MessageBox.Show(message, title ?? AppName, MessageBoxButton.OK, MessageBoxImage.Warning);

    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string filter, string suggestedName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = suggestedName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // Otro programa tiene el portapapeles tomado en este instante.
            AppLog.Write($"No se pudo copiar al portapapeles: {ex.Message}");
        }
    }

    public void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo abrir el Explorador: {ex.Message}");
        }
    }

    public void RestartApplication()
    {
        try
        {
            if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo reiniciar la aplicación: {ex.Message}");
        }
    }

    public void RunInstallerAndExit(string path)
    {
        try
        {
            // /SILENT muestra solo una barra de progreso; /CLOSEAPPLICATIONS cierra esta app si sigue abierta.
            Process.Start(new ProcessStartInfo(path, "/SILENT /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // El usuario canceló el aviso de permisos de administrador: la app sigue abierta.
            AppLog.Write($"No se lanzó el instalador: {ex.Message}");
            throw new InvalidOperationException(Loc.T("Install_LaunchFailed"), ex);
        }
    }

    public void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"No se pudo abrir {path}: {ex.Message}");
        }
    }
}
