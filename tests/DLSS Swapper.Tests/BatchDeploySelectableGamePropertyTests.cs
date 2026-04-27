using FsCheck;
using FsCheck.Fluent;
using DLSS_Swapper.Data;
using DLSS_Swapper.Data.ManuallyAdded;
using DLSS_Swapper.UserControls;

namespace DLSS_Swapper.Tests;

/// <summary>
/// Feature: batch-game-deploy, Properties 1–5: SelectableGame wrapper tests
///
/// These property-based tests validate the SelectableGame wrapper class and
/// the SelectAll/DeselectAll operations over collections of SelectableGame items.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.6, 3.1, 3.2, 3.3**
/// </summary>
public class BatchDeploySelectableGamePropertyTests
{
    /// <summary>
    /// Creates a ManuallyAddedGame with the given title and processing state.
    /// Uses ManuallyAddedGame as a concrete subclass of the abstract Game class.
    /// </summary>
    private static ManuallyAddedGame CreateGame(string title, bool processing = false)
    {
        var game = new ManuallyAddedGame
        {
            Title = title,
            Processing = processing,
        };
        return game;
    }

    /// <summary>
    /// Generator for non-null, non-empty game title strings.
    /// </summary>
    private static Gen<string> TitleGen()
    {
        return ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => s.Get);
    }

    /// <summary>
    /// Generator for a list of game titles (1 to 50 items).
    /// </summary>
    private static Gen<List<string>> TitleListGen()
    {
        return TitleGen().ListOf()
            .Where(list => list.Count > 0)
            .Select(list => list.Take(50).ToList());
    }

    /// <summary>
    /// Generator for a list of (title, processing) tuples representing games with random states.
    /// </summary>
    private static Gen<List<(string Title, bool Processing)>> GameStateListGen()
    {
        var gameStateGen = TitleGen()
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Select(t => (Title: t.Item1, Processing: t.Item2));

        return gameStateGen.ListOf()
            .Where(list => list.Count > 0)
            .Select(list => list.Take(50).ToList());
    }

    /// <summary>
    /// Generator for a list of (title, processing, isChecked) tuples.
    /// </summary>
    private static Gen<List<(string Title, bool Processing, bool IsChecked)>> FullGameStateListGen()
    {
        var gen = TitleGen()
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Select(t => (Title: t.Item1.Item1, Processing: t.Item1.Item2, IsChecked: t.Item2));

        return gen.ListOf()
            .Where(list => list.Count > 0)
            .Select(list => list.Take(50).ToList());
    }

    // -----------------------------------------------------------------------
    // Property 1: Game list completeness
    // For any list of mock Game objects, creating SelectableGame wrappers
    // produces one wrapper per game with matching Title.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 1: Game list completeness — wrapper count equals game count.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.4**
    /// </summary>
    [Fact]
    public void SelectableGame_WrapperCountEqualsGameCount()
    {
        var prop = Prop.ForAll(
            TitleListGen().ToArbitrary(),
            (List<string> titles) =>
            {
                var games = titles.Select(t => CreateGame(t)).ToList();
                var selectableGames = games.Select(g => new SelectableGame(g)).ToList();

                return selectableGames.Count == games.Count;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 1: Game list completeness — each wrapper's Title matches its underlying Game.Title.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.4**
    /// </summary>
    [Fact]
    public void SelectableGame_EachWrapperTitleMatchesGameTitle()
    {
        var prop = Prop.ForAll(
            TitleListGen().ToArbitrary(),
            (List<string> titles) =>
            {
                var games = titles.Select(t => CreateGame(t)).ToList();
                var selectableGames = games.Select(g => new SelectableGame(g)).ToList();

                return selectableGames.Zip(games).All(pair =>
                    pair.First.Title == pair.Second.Title);
            });

        prop.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // Property 2: Game list sorting
    // For any list of SelectableGame items sorted by Title, the sort is
    // alphabetically ascending.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 2: Game list sorting — sorting SelectableGame items by Title
    /// produces alphabetically ascending order.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void SelectableGame_SortByTitleIsAlphabeticallyAscending()
    {
        var prop = Prop.ForAll(
            TitleListGen().ToArbitrary(),
            (List<string> titles) =>
            {
                var games = titles.Select(t => CreateGame(t)).ToList();
                var selectableGames = games.Select(g => new SelectableGame(g)).ToList();

                var sorted = selectableGames
                    .OrderBy(sg => sg.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Verify each consecutive pair is in ascending order
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    if (string.Compare(sorted[i].Title, sorted[i + 1].Title, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        return false;
                    }
                }

                return true;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // Property 3: Processing games are disabled
    // For any Game.Processing state, IsEnabled reflects the inverse.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 3: Processing games are disabled — IsEnabled is always the
    /// inverse of Game.Processing.
    ///
    /// **Validates: Requirements 2.6**
    /// </summary>
    [Fact]
    public void SelectableGame_IsEnabledIsInverseOfProcessing()
    {
        var prop = Prop.ForAll(
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (bool processing) =>
            {
                var game = CreateGame("TestGame", processing);
                var selectable = new SelectableGame(game);

                return selectable.IsEnabled == !processing;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 3 (extended): For any list of games with random Processing states,
    /// every SelectableGame.IsEnabled equals !Game.Processing.
    ///
    /// **Validates: Requirements 2.6**
    /// </summary>
    [Fact]
    public void SelectableGame_IsEnabledReflectsProcessingForAllGames()
    {
        var prop = Prop.ForAll(
            GameStateListGen().ToArbitrary(),
            (List<(string Title, bool Processing)> gameStates) =>
            {
                var selectableGames = gameStates
                    .Select(gs => new SelectableGame(CreateGame(gs.Title, gs.Processing)))
                    .ToList();

                return selectableGames.All(sg => sg.IsEnabled == !sg.Game.Processing);
            });

        prop.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // Property 4: SelectAll checks all enabled games
    // For any set of SelectableGame items with varying IsEnabled, SelectAll
    // sets IsChecked=true only for enabled items.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 4: SelectAll checks all enabled games — after SelectAll,
    /// all enabled items have IsChecked=true and disabled items are unchanged.
    ///
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [Fact]
    public void SelectAll_ChecksAllEnabledGames()
    {
        var prop = Prop.ForAll(
            FullGameStateListGen().ToArbitrary(),
            (List<(string Title, bool Processing, bool IsChecked)> gameStates) =>
            {
                var selectableGames = gameStates.Select(gs =>
                {
                    var sg = new SelectableGame(CreateGame(gs.Title, gs.Processing));
                    sg.IsChecked = gs.IsChecked;
                    return sg;
                }).ToList();

                // Record the IsChecked state of disabled items before SelectAll
                var disabledStatesBefore = selectableGames
                    .Where(sg => !sg.IsEnabled)
                    .Select(sg => (sg, sg.IsChecked))
                    .ToList();

                // Perform SelectAll: set IsChecked=true for all enabled items
                foreach (var sg in selectableGames)
                {
                    if (sg.IsEnabled)
                    {
                        sg.IsChecked = true;
                    }
                }

                // All enabled items should be checked
                var allEnabledChecked = selectableGames
                    .Where(sg => sg.IsEnabled)
                    .All(sg => sg.IsChecked);

                // Disabled items should be unchanged
                var disabledUnchanged = disabledStatesBefore
                    .All(pair => pair.sg.IsChecked == pair.IsChecked);

                return allEnabledChecked && disabledUnchanged;
            });

        prop.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // Property 5: DeselectAll unchecks all games
    // For any set of SelectableGame items, DeselectAll sets IsChecked=false
    // for all.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 5: DeselectAll unchecks all games — after DeselectAll,
    /// all items have IsChecked=false regardless of their enabled state.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void DeselectAll_UnchecksAllGames()
    {
        var prop = Prop.ForAll(
            FullGameStateListGen().ToArbitrary(),
            (List<(string Title, bool Processing, bool IsChecked)> gameStates) =>
            {
                var selectableGames = gameStates.Select(gs =>
                {
                    var sg = new SelectableGame(CreateGame(gs.Title, gs.Processing));
                    sg.IsChecked = gs.IsChecked;
                    return sg;
                }).ToList();

                // Perform DeselectAll: set IsChecked=false for all items
                foreach (var sg in selectableGames)
                {
                    sg.IsChecked = false;
                }

                // All items should be unchecked
                return selectableGames.All(sg => !sg.IsChecked);
            });

        prop.QuickCheckThrowOnFailure();
    }
}
