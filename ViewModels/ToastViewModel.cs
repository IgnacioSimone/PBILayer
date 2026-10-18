using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PBIExplorer.ViewModels;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Aviso no modal que aparece en una esquina y se descarta solo (los errores
/// quedan hasta que el usuario los cierra). Reemplaza a los MessageBox.</summary>
public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Action<ToastViewModel> _dismiss;
    private readonly Action? _action;

    public ToastViewModel(ToastKind kind, string message, string? actionText, Action? action, Action<ToastViewModel> dismiss)
    {
        Kind = kind;
        Message = message;
        ActionText = actionText;
        _action = action;
        _dismiss = dismiss;
    }

    public ToastKind Kind { get; }
    public string Message { get; }
    public string? ActionText { get; }

    public string Icon => Kind switch
    {
        ToastKind.Success => "",
        ToastKind.Warning => "",
        ToastKind.Error => "",
        _ => ""
    };

    [RelayCommand]
    private void Dismiss() => _dismiss(this);

    [RelayCommand]
    private void RunAction()
    {
        _action?.Invoke();
        _dismiss(this);
    }
}
