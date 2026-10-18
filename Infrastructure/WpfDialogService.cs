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
}
