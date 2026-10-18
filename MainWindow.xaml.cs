using System.Windows;
using PBIExplorer.ViewModels;

namespace PBIExplorer;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync(Environment.GetCommandLineArgs());
    }
}
