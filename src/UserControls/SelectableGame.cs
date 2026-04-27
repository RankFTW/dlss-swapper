using CommunityToolkit.Mvvm.ComponentModel;
using DLSS_Swapper.Data;

namespace DLSS_Swapper.UserControls;

public partial class SelectableGame : ObservableObject
{
    public Game Game { get; init; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = false;

    public bool IsEnabled => !Game.Processing;

    public string Title => Game.Title;

    public SelectableGame(Game game)
    {
        Game = game;
    }
}
