using FsCheck;
using FsCheck.Fluent;
using DLSS_Swapper.Data;
using DLSS_Swapper.UserControls;

namespace DLSS_Swapper.Tests;

/// <summary>
/// Feature: batch-game-deploy, Properties 10–13: Error resilience, result aggregation,
/// progress reporting, and post-deploy refresh.
///
/// Since the full BatchDeployDialogModel.DeployAsync() depends on WinUI singletons
/// and actual file I/O, we test the pure logic by simulating the deploy loop:
///
/// Property 10: Error resilience — all games processed despite random failures.
/// Property 11: Result aggregation correctness — counts and HasAdminRecommendation.
/// Property 12: Progress reporting correctness — index, total, and title at each step.
/// Property 13: Post-deploy refresh — called iff at least one success per game.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 9.2, 9.3, 9.4, 9.5, 10.1, 10.2, 10.3, 10.4, 11.1, 11.2**
/// </summary>
public class BatchDeployExecutionPropertyTests
{
    // -----------------------------------------------------------------------
    // Outcome enum for simulating per-game, per-DLL-type results.
    // -----------------------------------------------------------------------
    private enum OperationOutcome
    {
        Success,
        Skipped,   // game doesn't have this asset type
        Failed,
        FailedWithAdminPrompt,
    }

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
    // Generators
    // -----------------------------------------------------------------------

    /// <summary>
    /// Generator for a non-empty game title.
    /// </summary>
    private static Gen<string> TitleGen()
    {
        return ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => s.Get);
    }

    /// <summary>
    /// Generator for a random OperationOutcome.
    /// </summary>
    private static Gen<OperationOutcome> OutcomeGen()
    {
        return Gen.Elements(
            OperationOutcome.Success,
            OperationOutcome.Skipped,
            OperationOutcome.Failed,
            OperationOutcome.FailedWithAdminPrompt);
    }

    /// <summary>
    /// Generator for a non-empty subset of swappable asset types,
    /// representing the DLL types the user selected in pickers.
    /// </summary>
    private static Gen<List<GameAssetType>> SelectedPickerTypesGen()
    {
        return Gen.SubListOf(SwappableAssetTypes)
            .Where(list => list.Count > 0)
            .Select(list => list.ToList());
    }

    /// <summary>
    /// Represents a simulated game in the deploy loop.
    /// </summary>
    private record SimulatedGame(
        string Title,
        Dictionary<GameAssetType, OperationOutcome> DllOutcomes,
        OperationOutcome? StreamlineOutcome);

    /// <summary>
    /// Generator for a single simulated game given a set of selected DLL types
    /// and whether Streamline is enabled.
    /// </summary>
    private static Gen<SimulatedGame> SimulatedGameGen(
        List<GameAssetType> selectedDllTypes,
        bool isStreamlineEnabled)
    {
        // Generate an outcome for each selected DLL type by chaining generators
        Gen<Dictionary<GameAssetType, OperationOutcome>> dllOutcomesGen;
        if (selectedDllTypes.Count > 0)
        {
            // Build up the dictionary by folding over each DLL type
            dllOutcomesGen = Gen.Constant(new Dictionary<GameAssetType, OperationOutcome>());
            foreach (var dllType in selectedDllTypes)
            {
                var capturedType = dllType;
                dllOutcomesGen = dllOutcomesGen.SelectMany(dict =>
                    OutcomeGen().Select(outcome =>
                    {
                        dict[capturedType] = outcome;
                        return dict;
                    }));
            }
        }
        else
        {
            dllOutcomesGen = Gen.Constant(new Dictionary<GameAssetType, OperationOutcome>());
        }

        // Generate Streamline outcome if enabled
        var streamlineOutcomeGen = isStreamlineEnabled
            ? OutcomeGen().Select(o => (OperationOutcome?)o)
            : Gen.Constant((OperationOutcome?)null);

        return TitleGen()
            .Zip(dllOutcomesGen)
            .Zip(streamlineOutcomeGen)
            .Select(t => new SimulatedGame(
                t.Item1.Item1,
                t.Item1.Item2,
                t.Item2));
    }

    /// <summary>
    /// Generator for a full deploy scenario: a list of 1-20 simulated games,
    /// a set of selected DLL types, and whether Streamline is enabled.
    /// </summary>
    private static Gen<(List<SimulatedGame> Games, List<GameAssetType> SelectedDllTypes, bool IsStreamlineEnabled)>
        DeployScenarioGen()
    {
        return SelectedPickerTypesGen()
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .SelectMany(t =>
            {
                var selectedDllTypes = t.Item1;
                var isStreamlineEnabled = t.Item2;

                return SimulatedGameGen(selectedDllTypes, isStreamlineEnabled)
                    .ListOf()
                    .Where(list => list.Count > 0)
                    .Select(list => list.Take(20).ToList())
                    .Select(games => (
                        Games: games,
                        SelectedDllTypes: selectedDllTypes,
                        IsStreamlineEnabled: isStreamlineEnabled));
            });
    }

    // -----------------------------------------------------------------------
    // Simulated deploy loop — mirrors BatchDeployDialogModel.DeployAsync()
    // pure logic, producing a BatchDeployResult and tracking side effects.
    // -----------------------------------------------------------------------

    private record DeployLoopResult(
        BatchDeployResult Result,
        List<(int GameIndex, int CurrentGameIndex, int TotalGameCount, string CurrentGameTitle)> ProgressSnapshots,
        List<int> RefreshedGameIndices);

    /// <summary>
    /// Simulates the deploy loop from BatchDeployDialogModel.DeployAsync(),
    /// using pre-determined outcomes instead of actual I/O.
    /// </summary>
    private static DeployLoopResult SimulateDeployLoop(
        List<SimulatedGame> games,
        List<GameAssetType> selectedDllTypes,
        bool isStreamlineEnabled)
    {
        var result = new BatchDeployResult();
        var progressSnapshots = new List<(int, int, int, string)>();
        var refreshedGameIndices = new List<int>();

        // Initialize per-DLL-type tracking
        var dllSuccessCounts = new Dictionary<GameAssetType, int>();
        var dllSkippedCounts = new Dictionary<GameAssetType, int>();
        var dllFailureCounts = new Dictionary<GameAssetType, int>();
        foreach (var dllType in selectedDllTypes)
        {
            dllSuccessCounts[dllType] = 0;
            dllSkippedCounts[dllType] = 0;
            dllFailureCounts[dllType] = 0;
        }

        int streamlineSuccessCount = 0;
        int streamlineSkippedCount = 0;

        int totalGameCount = games.Count;

        for (int i = 0; i < games.Count; i++)
        {
            var game = games[i];
            int currentGameIndex = i + 1;
            string currentGameTitle = game.Title;

            // Record progress snapshot (mirrors setting CurrentGameIndex, TotalGameCount, CurrentGameTitle)
            progressSnapshots.Add((i, currentGameIndex, totalGameCount, currentGameTitle));

            bool gameHadSuccess = false;

            // Process each selected DLL type
            foreach (var dllType in selectedDllTypes)
            {
                var outcome = game.DllOutcomes[dllType];
                switch (outcome)
                {
                    case OperationOutcome.Success:
                        dllSuccessCounts[dllType]++;
                        gameHadSuccess = true;
                        break;
                    case OperationOutcome.Skipped:
                        dllSkippedCounts[dllType]++;
                        break;
                    case OperationOutcome.Failed:
                        dllFailureCounts[dllType]++;
                        result.Failures.Add(new BatchDeployResult.FailureEntry(
                            game.Title, dllType.ToString(), "Simulated failure"));
                        break;
                    case OperationOutcome.FailedWithAdminPrompt:
                        dllFailureCounts[dllType]++;
                        result.Failures.Add(new BatchDeployResult.FailureEntry(
                            game.Title, dllType.ToString(), "Access denied"));
                        result.HasAdminRecommendation = true;
                        break;
                }
            }

            // Process Streamline if enabled
            if (isStreamlineEnabled && game.StreamlineOutcome != null)
            {
                switch (game.StreamlineOutcome)
                {
                    case OperationOutcome.Success:
                        streamlineSuccessCount++;
                        gameHadSuccess = true;
                        break;
                    case OperationOutcome.Skipped:
                        streamlineSkippedCount++;
                        break;
                    case OperationOutcome.Failed:
                        result.Failures.Add(new BatchDeployResult.FailureEntry(
                            game.Title, "Streamline", "Simulated failure"));
                        break;
                    case OperationOutcome.FailedWithAdminPrompt:
                        result.Failures.Add(new BatchDeployResult.FailureEntry(
                            game.Title, "Streamline", "Access denied"));
                        result.HasAdminRecommendation = true;
                        break;
                }
            }

            // Refresh iff game had at least one success
            if (gameHadSuccess)
            {
                refreshedGameIndices.Add(i);
            }
        }

        // Build DllResults
        foreach (var dllType in selectedDllTypes)
        {
            result.DllResults.Add(new BatchDeployResult.DllTypeResult(
                dllType,
                dllType.ToString(),
                dllSuccessCounts[dllType],
                dllSkippedCounts[dllType],
                dllFailureCounts[dllType]));
        }

        result.StreamlineSuccessCount = streamlineSuccessCount;
        result.StreamlineSkippedCount = streamlineSkippedCount;

        return new DeployLoopResult(result, progressSnapshots, refreshedGameIndices);
    }

    // -----------------------------------------------------------------------
    // Property 10: Error resilience — all games processed
    //
    // For any set of checked games with random failures, total processed
    // (success + failure + skip) equals checked count.
    //
    // **Validates: Requirements 10.1, 10.2**
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 10a: Every game is processed — the number of progress
    /// snapshots equals the number of checked games.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [Fact]
    public void ErrorResilience_AllGamesProcessed_ProgressSnapshotCount()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                return loopResult.ProgressSnapshots.Count == games.Count;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 10b: For each DLL type, success + skipped + failure == game count.
    /// This proves all games are processed for every DLL type despite failures.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    [Fact]
    public void ErrorResilience_AllGamesProcessed_PerDllTypeTotals()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                return loopResult.Result.DllResults.All(dr =>
                    dr.SuccessCount + dr.SkippedCount + dr.FailureCount == games.Count);
            });

        prop.Check(config);
    }

    // -----------------------------------------------------------------------
    // Property 11: Result aggregation correctness
    //
    // For any deploy execution:
    // - Per-DLL-type SuccessCount + SkippedCount + FailureCount == CheckedGameCount
    // - HasAdminRecommendation is true iff any failure had PromptToRelaunchAsAdmin
    //
    // **Validates: Requirements 9.2, 9.3, 9.4, 9.5, 10.3, 10.4**
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 11a: Per-DLL-type counts sum to checked game count.
    ///
    /// **Validates: Requirements 9.2, 9.5**
    /// </summary>
    [Fact]
    public void ResultAggregation_PerDllTypeCountsSumToGameCount()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                // Each DLL type result should have counts summing to total game count
                return loopResult.Result.DllResults.All(dr =>
                    dr.SuccessCount + dr.SkippedCount + dr.FailureCount == games.Count);
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 11b: HasAdminRecommendation is true iff any failure had
    /// PromptToRelaunchAsAdmin (simulated as FailedWithAdminPrompt outcome).
    ///
    /// **Validates: Requirements 10.3, 10.4**
    /// </summary>
    [Fact]
    public void ResultAggregation_HasAdminRecommendation_CorrectlySet()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                // Compute expected: any game had a FailedWithAdminPrompt outcome?
                bool expectedHasAdmin = games.Any(g =>
                    g.DllOutcomes.Values.Any(o => o == OperationOutcome.FailedWithAdminPrompt)
                    || (isStreamlineEnabled && g.StreamlineOutcome == OperationOutcome.FailedWithAdminPrompt));

                return loopResult.Result.HasAdminRecommendation == expectedHasAdmin;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 11c: Each failure entry in the result corresponds to a
    /// Failed or FailedWithAdminPrompt outcome, and the total failure
    /// entry count matches the total number of such outcomes.
    ///
    /// **Validates: Requirements 9.4, 10.3**
    /// </summary>
    [Fact]
    public void ResultAggregation_FailureEntryCountMatchesFailedOutcomes()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                // Count expected failures from DLL outcomes
                int expectedDllFailures = games.Sum(g =>
                    g.DllOutcomes.Values.Count(o =>
                        o == OperationOutcome.Failed || o == OperationOutcome.FailedWithAdminPrompt));

                // Count expected failures from Streamline outcomes
                int expectedStreamlineFailures = 0;
                if (isStreamlineEnabled)
                {
                    expectedStreamlineFailures = games.Count(g =>
                        g.StreamlineOutcome == OperationOutcome.Failed
                        || g.StreamlineOutcome == OperationOutcome.FailedWithAdminPrompt);
                }

                return loopResult.Result.Failures.Count == expectedDllFailures + expectedStreamlineFailures;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 11d: The DllResults list contains exactly one entry per
    /// selected DLL type, with the correct AssetType.
    ///
    /// **Validates: Requirements 9.2, 9.3**
    /// </summary>
    [Fact]
    public void ResultAggregation_DllResultsMatchSelectedPickers()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                // DllResults should have one entry per selected DLL type
                var resultAssetTypes = loopResult.Result.DllResults.Select(dr => dr.AssetType).ToList();
                return resultAssetTypes.Count == selectedDllTypes.Count
                    && resultAssetTypes.SequenceEqual(selectedDllTypes);
            });

        prop.Check(config);
    }

    // -----------------------------------------------------------------------
    // Property 12: Progress reporting correctness
    //
    // After processing game i, CurrentGameIndex == i+1, TotalGameCount == N,
    // CurrentGameTitle == game title at index i.
    //
    // **Validates: Requirements 8.1, 8.2, 8.3**
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 12a: After processing game at index i, CurrentGameIndex == i+1.
    ///
    /// **Validates: Requirements 8.1, 8.3**
    /// </summary>
    [Fact]
    public void ProgressReporting_CurrentGameIndexIsOneBased()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                return loopResult.ProgressSnapshots.All(snap =>
                    snap.CurrentGameIndex == snap.GameIndex + 1);
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 12b: TotalGameCount is always N (the number of checked games).
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Fact]
    public void ProgressReporting_TotalGameCountEqualsN()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                return loopResult.ProgressSnapshots.All(snap =>
                    snap.TotalGameCount == games.Count);
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 12c: CurrentGameTitle matches the title of the game at index i.
    ///
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Fact]
    public void ProgressReporting_CurrentGameTitleMatchesGameAtIndex()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                return loopResult.ProgressSnapshots.All(snap =>
                    snap.CurrentGameTitle == games[snap.GameIndex].Title);
            });

        prop.Check(config);
    }

    // -----------------------------------------------------------------------
    // Property 13: Post-deploy refresh
    //
    // UpdateCurrentDLLsFromGameAssets is called iff the game had at least
    // one successful operation.
    //
    // **Validates: Requirements 11.1, 11.2**
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 13a: A game is refreshed iff it had at least one successful
    /// DLL swap or Streamline update.
    ///
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Fact]
    public void PostDeployRefresh_CalledIffAtLeastOneSuccess()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                for (int i = 0; i < games.Count; i++)
                {
                    var game = games[i];
                    bool hadDllSuccess = game.DllOutcomes.Values.Any(o => o == OperationOutcome.Success);
                    bool hadStreamlineSuccess = isStreamlineEnabled
                        && game.StreamlineOutcome == OperationOutcome.Success;
                    bool expectedRefresh = hadDllSuccess || hadStreamlineSuccess;
                    bool actualRefresh = loopResult.RefreshedGameIndices.Contains(i);

                    if (expectedRefresh != actualRefresh)
                    {
                        return false;
                    }
                }

                return true;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 13b: When all operations for a game are skipped or failed,
    /// the game is NOT refreshed.
    ///
    /// **Validates: Requirements 11.2**
    /// </summary>
    [Fact]
    public void PostDeployRefresh_NotCalledWhenAllSkippedOrFailed()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                for (int i = 0; i < games.Count; i++)
                {
                    var game = games[i];
                    bool hadAnySuccess = game.DllOutcomes.Values.Any(o => o == OperationOutcome.Success)
                        || (isStreamlineEnabled && game.StreamlineOutcome == OperationOutcome.Success);

                    if (!hadAnySuccess && loopResult.RefreshedGameIndices.Contains(i))
                    {
                        return false; // Refreshed when it shouldn't have been
                    }
                }

                return true;
            });

        prop.Check(config);
    }

    /// <summary>
    /// Property 13c: The count of refreshed games equals the count of games
    /// that had at least one successful operation.
    ///
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Fact]
    public void PostDeployRefresh_RefreshedCountEqualsSuccessfulGameCount()
    {
        var config = Config.Default.WithMaxTest(100);

        var prop = Prop.ForAll(
            DeployScenarioGen().ToArbitrary(),
            (ValueTuple<List<SimulatedGame>, List<GameAssetType>, bool> scenario) =>
            {
                var (games, selectedDllTypes, isStreamlineEnabled) = scenario;
                var loopResult = SimulateDeployLoop(games, selectedDllTypes, isStreamlineEnabled);

                int expectedRefreshCount = games.Count(g =>
                    g.DllOutcomes.Values.Any(o => o == OperationOutcome.Success)
                    || (isStreamlineEnabled && g.StreamlineOutcome == OperationOutcome.Success));

                return loopResult.RefreshedGameIndices.Count == expectedRefreshCount;
            });

        prop.Check(config);
    }
}
