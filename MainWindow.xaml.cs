using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using PBIExplorer.Infrastructure;
using PBIExplorer.ViewModels;
using PBIExplorer.Views;

namespace PBIExplorer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;

    private readonly IDialogService _dialogs;

    public MainWindow(MainViewModel viewModel, AppSettings settings, IDialogService dialogs)
    {
        _dialogs = dialogs;
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        RestorePlacement();

        // Con "reducir animaciones" activo en Windows no hay entrada animada.
        var animate = SystemParameters.ClientAreaAnimation;
        if (animate) RootShell.Opacity = 0;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.AboutRequested += ShowAbout;
        ThemeService.Changed += () => ThemeService.UseImmersiveDarkTitleBar(this, ThemeService.IsDark);

        Loaded += async (_, _) =>
        {
            ThemeService.UseImmersiveDarkTitleBar(this, ThemeService.IsDark);
            if (animate) PlayIntro();
            await viewModel.InitializeAsync(Environment.GetCommandLineArgs());
        };
        Closing += OnClosing;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Theme))
            ThemeService.Apply(_viewModel.Theme);
    }

    private void PlayIntro()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(450);
        RootShell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        // Transform propio (no uno declarado en XAML, que WPF congela y no se puede animar).
        var shift = new System.Windows.Media.TranslateTransform();
        RootShell.RenderTransform = shift;
        shift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

    // =====================================================================
    // Tamaño y posición de la ventana
    // =====================================================================

    private void RestorePlacement()
    {
        if (_settings.WindowWidth is not { } width || _settings.WindowHeight is not { } height ||
            _settings.WindowLeft is not { } left || _settings.WindowTop is not { } top)
            return;

        // Si el monitor donde estaba ya no existe, mejor abrir centrada que fuera de pantalla.
        var visible = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var saved = new Rect(left, top, width, height);
        if (!visible.IntersectsWith(saved) || width < MinWidth || height < MinHeight) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }

    // =====================================================================
    // Búsqueda, recientes y arrastrar y soltar
    // =====================================================================

    private void Find_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        GlobalSearchBox.Focus();
        GlobalSearchBox.SelectAll();
    }

    private void RecentButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RecentButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        foreach (var path in _viewModel.RecentFiles)
        {
            menu.Items.Add(new MenuItem
            {
                Header = Path.GetFileName(path),
                ToolTip = path,
                Command = _viewModel.OpenRecentCommand,
                CommandParameter = path
            });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Limpiar recientes", Command = _viewModel.ClearRecentCommand });
        menu.IsOpen = true;
    }

    private void HelpMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = HelpMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };

        void Add(string header, ICommand command, string? hint = null) =>
            menu.Items.Add(new MenuItem { Header = header, Command = command, InputGestureText = hint });

        Add("Atajos de teclado", _viewModel.ShowShortcutsCommand, "F1");
        Add("Carpeta de datos y registros", _viewModel.OpenDataFolderCommand);
        menu.Items.Add(new Separator());
        Add("Buscar actualizaciones…", _viewModel.CheckForUpdatesCommand);
        Add($"Acerca de {AppInfo.Name}", _viewModel.ShowAboutCommand);
        menu.IsOpen = true;
    }

    private AboutWindow? _about;

    /// <summary>Abre "Acerca de" (una sola ventana a la vez). Con <paramref name="checkNow"/> arranca buscando actualizaciones.</summary>
    private void ShowAbout(bool checkNow)
    {
        if (_about is not null)
        {
            _about.Activate();
        }
        else
        {
            _about = new AboutWindow(new AboutViewModel(_viewModel.Updates, _dialogs)) { Owner = this };
            _about.Closed += (_, _) => _about = null;
            _about.Show();
        }

        if (checkNow) _viewModel.Updates.CheckNowCommand.Execute(null);
    }

    private void ExportMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ExportMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        void Add(string header, ICommand command, string? hint = null) =>
            menu.Items.Add(new MenuItem { Header = header, Command = command, InputGestureText = hint });

        Add("Documento de Word (.docx)", _viewModel.ExportWordCommand);
        Add("Libro de Excel (.xlsx)", _viewModel.ExportExcelCommand);
        Add("Página web (.html)", _viewModel.ExportHtmlCommand);
        menu.Items.Add(new Separator());
        Add("CSV de esta sección (como la ves)", _viewModel.ExportCsvCommand);
        Add("CSV de todas las secciones (.zip)", _viewModel.ExportCsvZipCommand);
        menu.Items.Add(new Separator());
        Add("Snapshot para comparar (.json)", _viewModel.ExportJsonCommand, "Ctrl+Shift+E");
        Add("Contexto para IA (.md)", _viewModel.ExportMarkdownCommand, "Ctrl+E");
        menu.IsOpen = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await _viewModel.OpenFileAsync(files[0]);
    }
}
