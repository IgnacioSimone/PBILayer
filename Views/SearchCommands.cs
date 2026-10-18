using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PBIExplorer.Views;

/// <summary>Comando de la "X" de los buscadores: borra todo el texto de una vez y deja el cursor listo para escribir.</summary>
public static class SearchCommands
{
    public static readonly RoutedUICommand Clear = new("Clear", nameof(Clear), typeof(SearchCommands));

    /// <summary>Se llama una sola vez al arrancar: enseña a todo TextBox a obedecer el comando Clear.</summary>
    public static void Register() =>
        CommandManager.RegisterClassCommandBinding(typeof(TextBox), new CommandBinding(
            Clear,
            (sender, _) =>
            {
                if (sender is not TextBox box) return;
                box.Clear();
                box.Focus();
            },
            (sender, e) => e.CanExecute = sender is TextBox { Text.Length: > 0 }));
}
