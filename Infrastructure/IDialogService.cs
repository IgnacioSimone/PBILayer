namespace PBIExplorer.Infrastructure;

/// <summary>Todo lo que abre una ventana o diálogo del sistema. Los ViewModels solo
/// hablan con esta interfaz, así se pueden testear sin UI.</summary>
public interface IDialogService
{
    /// <summary>Diálogo modal; reservado para contenido largo que el usuario pidió ver.</summary>
    void ShowInfo(string message);
    void ShowWarning(string message, string? title = null);
    string? PickOpenFile(string filter);
    string? PickSaveFile(string filter, string suggestedName);

    /// <summary>Copia texto al portapapeles.</summary>
    void CopyText(string text);

    /// <summary>Abre el Explorador de Windows con el archivo seleccionado.</summary>
    void RevealInExplorer(string path);

    /// <summary>Cierra la aplicación y la vuelve a abrir (para aplicar un cambio de idioma).</summary>
    void RestartApplication();

    /// <summary>Lanza el instalador de una actualización (ya verificado) y cierra la aplicación.</summary>
    void RunInstallerAndExit(string path);

    /// <summary>Abre un archivo con su aplicación asociada (ej. el log en el Bloc de notas).</summary>
    void OpenWithShell(string path);
}
