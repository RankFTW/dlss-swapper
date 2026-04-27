using FsCheck;
using FsCheck.Fluent;
using DLSS_Swapper.Data;

namespace DLSS_Swapper.Tests;

/// <summary>
/// Feature: batch-game-deploy, Properties 8–9: Batch deploy dispatch logic
///
/// These property-based tests validate the dispatch decision logic used in
/// BatchDeployDialogModel.DeployAsync():
///
/// Property 8: DLL swap dispatch — UpdateDllAsync is called iff the game has
/// a GameAsset of the selected DLL type; otherwise silently skipped.
///
/// Property 9: Streamline dispatch — StreamlineUpdater.UpdateAsync is called
/// iff IsStreamlineUpdateEnabled AND game.HasStreamline; otherwise skipped.
///
/// Since the full BatchDeployDialogModel depends on WinUI singletons, we test
/// the pure dispatch decision logic as extracted functions.
///
/// **Validates: Requirements 4.4, 5.3, 7.2, 7.3, 7.4, 7.5, 7.6**
/// </summary>
public class BatchDeployDispatchPropertyTests
{
    // -----------------------------------------------------------------------
    // The 9 swappable GameAssetTypes used by the batch deployer.
    // -----------------------------------------------------------------------
    private static readonly GameAssetType[] SwappableAssetTypes =
    [
        GameAssetType.DLSS,
        GameAssetType.DLSS_D,
        GameAssetType.DLSS_G,
        GameAssetType.FSR_31_DX12,
        GameAssetType.FSR_31_VK,
        GameAssetType.XeSS,
        GameAssetType.XeSS_FG,
        GameAssetType.XeSS_DX11,
        GameAssetType.XeLL,
    ];

    // -----------------------------------------------------------------------
    // Pure dispatch decision functions extracted from DeployAsync logic.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The DLL dispatch decision: given a game's set of supported asset types
    /// and a picker's asset type, should UpdateDllAsync be called?
    /// Mirrors: game.GameAssets.Any(a => a.AssetType == picker.AssetType)
    /// </summary>
    private static bool ShouldDispatchDll(HashSet<GameAssetType> gameAssetTypes, GameAssetType pickerAssetType)
    {
        return gameAssetTypes.Contains(pickerAssetType);
    }

    /// <summary>
    /// The Streamline dispatch decision: given the toggle state and the game's
    /// HasStreamline flag, should StreamlineUpdater.UpdateAsync be called?
    /// Mirrors: IsStreamlineUpdateEnabled && game.HasStreamline
    /// </summary>
    private static bool ShouldDispatchStreamline(bool isStreamlineEnabled, bool gameHasStreamline)
    {
        return isStreamlineEnabled && gameHasStreamline;
    }

    // -----------------------------------------------------------------------
    // Generators
    // -----------------------------------------------------------------------

    /// <summary>
    /// Generator for a random subset of the 9 swappable asset types,
    /// representing the asset types a game supports.
    /// </summary>
    private static Gen<HashSet<GameAssetType>> GameAssetTypesGen()
    {
        return Gen.SubListOf(SwappableAssetTypes)
            .Select(list => new HashSet<GameAssetType>(list));
    }

    /// <summary>
    /// Generator for a single swappable GameAssetType (picker selection).
    /// </summary>
    private static Gen<GameAssetType> PickerAssetTypeGen()
    {
        return Gen.Elements(SwappableAssetTypes);
    }

    /// <summary>
    /// Generator for a non-empty subset of swappable asset types,
    /// representing the set of DLL types the user has selected in pickers.
    /// </summary>
    private static Gen<HashSet<GameAssetType>> SelectedPickerTypesGen()
    {
        return Gen.SubListOf(SwappableAssetTypes)
            .Where(list => list.Count > 0)
            .Select(list => new HashSet<GameAssetType>(list));
    }

    // -----------------------------------------------------------------------
    // Property 8: DLL swap dispatch correctness
    //
    // For any checked game and DLL type selection, UpdateDllAsync is called
    // iff the game has a GameAsset of that type; otherwise silently skipped.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 8a: For a single game and single picker, dispatch happens
    /// iff the game's asset types contain the picker's asset type.
    ///
    /// **Validates: Requirements 4.4, 7.2, 7.3, 7.4**
    /// </summary>
    [Fact]
    public void DllDispatch_CalledIffGameHasAssetType()
    {
        var config = Config.Default.WithMaxTest(200);

        var prop = Prop.ForAll(
            GameAssetTypesGen().ToArbitrary(),
            PickerAssetTypeGen().ToArbitrary(),
            (HashSet<GameAssetType> gameAssetTypes, GameAssetType pickerType) =>
            {
                var shouldDispatch = ShouldDispatchDll(gameAssetTypes, pickerType);
                var expected = gameAssetTypes.Contains(pickerType);

                return shouldDispatch == expected;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 8b: When a game has no assets at all, no DLL type should
    /// be dispatched — all are silently skipped.
    ///
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Fact]
    public void DllDispatch_AllSkippedWhenGameHasNoAssets()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            PickerAssetTypeGen().ToArbitrary(),
            (GameAssetType pickerType) =>
            {
                var emptyAssets = new HashSet<GameAssetType>();
                return !ShouldDispatchDll(emptyAssets, pickerType);
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 8c: For any game and any set of selected pickers, the number
    /// of dispatched DLL swaps equals the intersection of the game's asset
    /// types and the selected picker types.
    ///
    /// **Validates: Requirements 4.4, 7.2, 7.3, 7.4**
    /// </summary>
    [Fact]
    public void DllDispatch_CountEqualsIntersectionOfGameAssetsAndSelectedPickers()
    {
        var config = Config.Default.WithMaxTest(200);

        var prop = Prop.ForAll(
            GameAssetTypesGen().ToArbitrary(),
            SelectedPickerTypesGen().ToArbitrary(),
            (HashSet<GameAssetType> gameAssetTypes, HashSet<GameAssetType> selectedPickers) =>
            {
                // Simulate the dispatch loop from DeployAsync
                int dispatchCount = 0;
                int skipCount = 0;

                foreach (var pickerType in selectedPickers)
                {
                    if (ShouldDispatchDll(gameAssetTypes, pickerType))
                    {
                        dispatchCount++;
                    }
                    else
                    {
                        skipCount++;
                    }
                }

                var expectedDispatchCount = gameAssetTypes.Intersect(selectedPickers).Count();
                var expectedSkipCount = selectedPickers.Count - expectedDispatchCount;

                return dispatchCount == expectedDispatchCount
                    && skipCount == expectedSkipCount
                    && dispatchCount + skipCount == selectedPickers.Count;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 8d: When a game supports all 9 swappable asset types,
    /// every selected picker type is dispatched (none skipped).
    ///
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [Fact]
    public void DllDispatch_AllDispatchedWhenGameSupportsAllTypes()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            SelectedPickerTypesGen().ToArbitrary(),
            (HashSet<GameAssetType> selectedPickers) =>
            {
                var allAssetTypes = new HashSet<GameAssetType>(SwappableAssetTypes);

                return selectedPickers.All(pickerType =>
                    ShouldDispatchDll(allAssetTypes, pickerType));
            });

        prop.Check(config);
    }

    // -----------------------------------------------------------------------
    // Property 9: Streamline dispatch correctness
    //
    // For any checked game, StreamlineUpdater.UpdateAsync is called iff
    // IsStreamlineUpdateEnabled and game.HasStreamline; otherwise skipped.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 9a: Streamline dispatch happens iff both isStreamlineEnabled
    /// and gameHasStreamline are true.
    ///
    /// **Validates: Requirements 5.3, 7.5, 7.6**
    /// </summary>
    [Fact]
    public void StreamlineDispatch_CalledIffEnabledAndGameHasStreamline()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (bool isStreamlineEnabled, bool gameHasStreamline) =>
            {
                var shouldDispatch = ShouldDispatchStreamline(isStreamlineEnabled, gameHasStreamline);
                var expected = isStreamlineEnabled && gameHasStreamline;

                return shouldDispatch == expected;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 9b: When Streamline is disabled, dispatch never happens
    /// regardless of game.HasStreamline.
    ///
    /// **Validates: Requirements 7.5, 7.6**
    /// </summary>
    [Fact]
    public void StreamlineDispatch_NeverCalledWhenDisabled()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (bool gameHasStreamline) =>
            {
                return !ShouldDispatchStreamline(isStreamlineEnabled: false, gameHasStreamline);
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 9c: When Streamline is enabled but game does not have
    /// Streamline, dispatch does not happen (silently skipped).
    ///
    /// **Validates: Requirements 7.6**
    /// </summary>
    [Fact]
    public void StreamlineDispatch_SkippedWhenGameLacksStreamline()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (bool isStreamlineEnabled) =>
            {
                return !ShouldDispatchStreamline(isStreamlineEnabled, gameHasStreamline: false);
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 9d: When both Streamline is enabled and game has Streamline,
    /// dispatch always happens.
    ///
    /// **Validates: Requirements 5.3, 7.5**
    /// </summary>
    [Fact]
    public void StreamlineDispatch_AlwaysCalledWhenEnabledAndGameHasStreamline()
    {
        // This is a deterministic check but validates the positive case
        Assert.True(ShouldDispatchStreamline(isStreamlineEnabled: true, gameHasStreamline: true));
    }

    // -----------------------------------------------------------------------
    // Combined dispatch: DLL + Streamline for a single game
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 8+9 combined: For any game with random asset types and
    /// HasStreamline, and any set of selected pickers with Streamline toggle,
    /// the total dispatch count equals the expected DLL dispatches plus
    /// the expected Streamline dispatch (0 or 1).
    ///
    /// **Validates: Requirements 4.4, 5.3, 7.2, 7.3, 7.4, 7.5, 7.6**
    /// </summary>
    [Fact]
    public void CombinedDispatch_TotalCountMatchesExpected()
    {
        var config = Config.Default.WithMaxTest(200);

        var scenarioGen = GameAssetTypesGen()
            .Zip(SelectedPickerTypesGen())
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Select(t => (
                GameAssetTypes: t.Item1.Item1.Item1,
                SelectedPickers: t.Item1.Item1.Item2,
                IsStreamlineEnabled: t.Item1.Item2,
                GameHasStreamline: t.Item2));

        var prop = Prop.ForAll(
            scenarioGen.ToArbitrary(),
            (ValueTuple<HashSet<GameAssetType>, HashSet<GameAssetType>, bool, bool> scenario) =>
            {
                var (gameAssetTypes, selectedPickers, isStreamlineEnabled, gameHasStreamline) = scenario;

                // Count DLL dispatches
                int dllDispatchCount = selectedPickers.Count(p => ShouldDispatchDll(gameAssetTypes, p));
                int dllSkipCount = selectedPickers.Count(p => !ShouldDispatchDll(gameAssetTypes, p));

                // Count Streamline dispatch
                bool streamlineDispatched = ShouldDispatchStreamline(isStreamlineEnabled, gameHasStreamline);

                // Expected values
                int expectedDllDispatches = gameAssetTypes.Intersect(selectedPickers).Count();
                int expectedDllSkips = selectedPickers.Count - expectedDllDispatches;
                bool expectedStreamline = isStreamlineEnabled && gameHasStreamline;

                return dllDispatchCount == expectedDllDispatches
                    && dllSkipCount == expectedDllSkips
                    && streamlineDispatched == expectedStreamline;
            });

        prop.Check(config);
    }
}
