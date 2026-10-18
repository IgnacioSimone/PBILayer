using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PBIExplorer.ViewModels;

public sealed record TileViewModel(string Icon, string Label, int Value, string? Sub, int Order);

public sealed partial class SummaryViewModel : SectionViewModel
{
    public SummaryViewModel() : base("Resumen", "") { }

    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    [ObservableProperty] private bool _hasContent;

    public void SetTiles(IEnumerable<TileViewModel> tiles)
    {
        Tiles.Clear();
        foreach (var t in tiles) Tiles.Add(t);
        HasContent = Tiles.Count > 0;
    }
}
