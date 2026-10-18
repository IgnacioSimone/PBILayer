using System.Windows;
using PBIExplorer.Infrastructure;
using PBIExplorer.ViewModels;

namespace PBIExplorer.Views;

public partial class AboutWindow : Window
{
    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) => ThemeService.UseImmersiveDarkTitleBar(this, ThemeService.IsDark);
    }
}
