using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PBIExplorer.ViewModels;

/// <summary>Una tarjeta del resumen: un número, qué es, y la sección a la que lleva al presionarla.</summary>
public sealed record TileViewModel(string Icon, string Label, int Value, string? Sub, int Order,
    string Description = "", SectionViewModel? Target = null);

public sealed partial class SummaryViewModel : SectionViewModel
{
    public SummaryViewModel() : base(PBIExplorer.Infrastructure.Loc.T("Export_Summary"), "") { }

    /// <summary>Lo completa la ventana principal: cambia la sección que se está viendo.</summary>
    public Action<SectionViewModel>? Navigate { get; set; }

    [RelayCommand]
    private void OpenTile(TileViewModel? tile)
    {
        if (tile?.Target is { } target) Navigate?.Invoke(target);
    }

    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    [ObservableProperty] private bool _hasContent;

    public void SetTiles(IEnumerable<TileViewModel> tiles)
    {
        Tiles.Clear();
        foreach (var t in tiles) Tiles.Add(t);
        HasContent = Tiles.Count > 0;
    }
}
