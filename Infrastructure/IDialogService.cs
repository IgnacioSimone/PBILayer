namespace PBIExplorer.Infrastructure;

/// <summary>Todo lo que abre una ventana o diálogo del sistema. Los ViewModels solo
/// hablan con esta interfaz, así se pueden testear sin UI.</summary>
public interface IDialogService
{
    void ShowInfo(string message);
    void ShowWarning(string message, string? title = null);
    string? PickOpenFile(string filter);
    string? PickSaveFile(string filter, string suggestedName);
}
