using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSS_Swapper.Data;
using DLSS_Swapper.Data.Streamline;

namespace DLSS_Swapper.UserControls;

public partial class BatchDeployDialogModel : ObservableObject
{
    WeakReference<FakeContentDialog> _dialogWeakReference;
    List<SelectableGame> _allGames;

    public ObservableCollection<SelectableGame> Games { get; }

    [ObservableProperty]
    public partial bool HideHiddenGames { get; set; } = true;

    partial void OnHideHiddenGamesChanged(bool value)
    {
        RefreshGamesList();
    }

    public string HideHiddenGamesGlyph => HideHiddenGames ? "\uED1A" : "\uE7B3";

    public List<DllTypePicker> DllTypePickers { get; }

    public List<string> StreamlineSourceOptions { get; } = new List<string>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeploy))]
    public partial string? SelectedStreamlineSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeploy))]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    public partial int CheckedGameCount { get; set; }

    public bool CanDeploy => CheckedGameCount > 0
        && (DllTypePickers.Any(p => p.SelectedRecord != null) || SelectedStreamlineSource != null);

    public bool CanRestore => CheckedGameCount > 0;

    [ObservableProperty]
    public partial bool IsDeploying { get; set; }

    [ObservableProperty]
    public partial int CurrentGameIndex { get; set; }

    [ObservableProperty]
    public partial int TotalGameCount { get; set; }

    [ObservableProperty]
    public partial string CurrentGameTitle { get; set; }

    public BatchDeployDialogModel(WeakReference<FakeContentDialog> dialogWeakReference)
    {
        _dialogWeakReference = dialogWeakReference;
        CurrentGameTitle = string.Empty;

        // Populate games sorted alphabetically by title.
        var allGames = GameManager.Instance.GetSynchronisedGamesListCopy();
        _allGames = allGames
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SelectableGame(g))
            .ToList();

        Games = new ObservableCollection<SelectableGame>();

        // Subscribe to IsChecked changes on each game to update CheckedGameCount.
        foreach (var game in _allGames)
        {
            game.PropertyChanged += SelectableGame_PropertyChanged;
        }

        RefreshGamesList();

        // Create 9 DllTypePicker instances, one per swappable GameAssetType.
        DllTypePickers = new List<DllTypePicker>
        {
            new DllTypePicker(GameAssetType.DLSS, DLLManager.Instance.DLSSRecords),
            new DllTypePicker(GameAssetType.DLSS_D, DLLManager.Instance.DLSSDRecords),
            new DllTypePicker(GameAssetType.DLSS_G, DLLManager.Instance.DLSSGRecords),
            new DllTypePicker(GameAssetType.FSR_31_DX12, DLLManager.Instance.FSR31DX12Records),
            new DllTypePicker(GameAssetType.FSR_31_VK, DLLManager.Instance.FSR31VKRecords),
            new DllTypePicker(GameAssetType.XeSS, DLLManager.Instance.XeSSRecords),
            new DllTypePicker(GameAssetType.XeSS_FG, DLLManager.Instance.XeSSFGRecords),
            new DllTypePicker(GameAssetType.XeSS_DX11, DLLManager.Instance.XeSSDX11Records),
            new DllTypePicker(GameAssetType.XeLL, DLLManager.Instance.XeLLRecords),
        };

        // Subscribe to SelectedRecord changes on each picker to notify CanDeploy.
        foreach (var picker in DllTypePickers)
        {
            picker.PropertyChanged += DllTypePicker_PropertyChanged;
        }

        UpdateCheckedGameCount();

        // Populate Streamline source options.
        if (StreamlineManager.Instance.IsStagingReady && !string.IsNullOrWhiteSpace(StreamlineManager.Instance.StagedVersion))
        {
            StreamlineSourceOptions.Add(StreamlineManager.Instance.StagedVersion);
        }
        if (StreamlineManager.Instance.IsCustomReady && !string.IsNullOrWhiteSpace(StreamlineManager.Instance.CustomVersion))
        {
            StreamlineSourceOptions.Add($"Custom ({StreamlineManager.Instance.CustomVersion})");
        }
    }

    void SelectableGame_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableGame.IsChecked))
        {
            UpdateCheckedGameCount();
        }
    }

    void DllTypePicker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DllTypePicker.SelectedRecord))
        {
            OnPropertyChanged(nameof(CanDeploy));
        }
    }

    void UpdateCheckedGameCount()
    {
        CheckedGameCount = Games.Count(g => g.IsChecked);
    }

    void RefreshGamesList()
    {
        Games.Clear();
        var filtered = HideHiddenGames
            ? _allGames.Where(sg => sg.Game.IsHidden != true)
            : _allGames;

        foreach (var game in filtered)
        {
            Games.Add(game);
        }

        UpdateCheckedGameCount();
        OnPropertyChanged(nameof(HideHiddenGamesGlyph));
    }

    [RelayCommand]
    void ToggleHideHiddenGames()
    {
        HideHiddenGames = !HideHiddenGames;
    }

    // Stub commands — implementations will be added in tasks 3.2 and 3.3.

    [RelayCommand]
    void SelectAll()
    {
        foreach (var game in Games)
        {
            if (game.IsEnabled)
            {
                game.IsChecked = true;
            }
        }
    }

    [RelayCommand]
    void DeselectAll()
    {
        foreach (var game in Games)
        {
            game.IsChecked = false;
        }
    }

    [RelayCommand]
    void Close()
    {
        if (_dialogWeakReference.TryGetTarget(out FakeContentDialog? dialog))
        {
            dialog.Hide();
        }
    }

    [RelayCommand]
    async Task DeployAsync()
    {
        IsDeploying = true;

        try
        {
            var checkedGames = Games.Where(g => g.IsChecked).ToList();
            TotalGameCount = checkedGames.Count;

            var result = new BatchDeployResult();

            // Initialize per-DLL-type tracking dictionaries.
            var selectedPickers = DllTypePickers.Where(p => p.SelectedRecord != null).ToList();
            var dllSuccessCounts = new Dictionary<GameAssetType, int>();
            var dllSkippedCounts = new Dictionary<GameAssetType, int>();
            var dllFailureCounts = new Dictionary<GameAssetType, int>();
            var dllBackupPreservedCounts = new Dictionary<GameAssetType, int>();
            foreach (var picker in selectedPickers)
            {
                dllSuccessCounts[picker.AssetType] = 0;
                dllSkippedCounts[picker.AssetType] = 0;
                dllFailureCounts[picker.AssetType] = 0;
                dllBackupPreservedCounts[picker.AssetType] = 0;
            }

            int streamlineSuccessCount = 0;
            int streamlineSkippedCount = 0;
            int streamlineBackupPreservedCount = 0;

            for (int i = 0; i < checkedGames.Count; i++)
            {
                var selectableGame = checkedGames[i];
                var game = selectableGame.Game;

                CurrentGameIndex = i + 1;
                CurrentGameTitle = game.Title;

                bool gameHadSuccess = false;

                // Process each selected DLL type.
                foreach (var picker in selectedPickers)
                {
                    try
                    {
                        // Check if the game has a GameAsset of this type.
                        var hasAssetType = game.GameAssets.Any(a => a.AssetType == picker.AssetType);
                        if (!hasAssetType)
                        {
                            dllSkippedCounts[picker.AssetType]++;
                            continue;
                        }

                        // Track if backup already existed (original preserved).
                        var backupType = DLLManager.Instance.GetAssetBackupType(picker.AssetType);
                        bool hadBackupBefore = game.GameAssets.Any(a => a.AssetType == backupType);

                        var updateResult = await game.UpdateDllAsync(picker.SelectedRecord!);
                        if (updateResult.Success)
                        {
                            dllSuccessCounts[picker.AssetType]++;
                            gameHadSuccess = true;
                            if (hadBackupBefore)
                            {
                                dllBackupPreservedCounts[picker.AssetType]++;
                            }
                        }
                        else
                        {
                            dllFailureCounts[picker.AssetType]++;
                            result.Failures.Add(new BatchDeployResult.FailureEntry(game.Title, picker.DisplayName, updateResult.Message));
                            if (updateResult.PromptToRelaunchAsAdmin)
                            {
                                result.HasAdminRecommendation = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                        dllFailureCounts[picker.AssetType]++;
                        result.Failures.Add(new BatchDeployResult.FailureEntry(game.Title, picker.DisplayName, ex.Message));
                    }
                }

                // Process Streamline update if a source is selected.
                if (SelectedStreamlineSource != null)
                {
                    try
                    {
                        if (game.HasStreamline)
                        {
                            // Skip games on Streamline 1.x — not compatible with 2.x staged DLLs.
                            bool skipStreamline = false;
                            if (!string.IsNullOrWhiteSpace(game.CurrentStreamlineVersion))
                            {
                                try
                                {
                                    var slVer = new System.Version(game.CurrentStreamlineVersion);
                                    if (slVer.Major < 2)
                                    {
                                        skipStreamline = true;
                                        streamlineSkippedCount++;
                                        Logger.Info($"Skipping Streamline update for {game.Title} — game is on Streamline {game.CurrentStreamlineVersion} (1.x not compatible with 2.x).");
                                    }
                                }
                                catch
                                {
                                    // Version parse failed — proceed with update.
                                }
                            }

                            if (!skipStreamline)
                            {
                                // Track if backup already existed (original preserved).
                                bool hadBackupBefore = game.HasStreamlineBackup;
                                bool useCustom = SelectedStreamlineSource?.StartsWith("Custom", StringComparison.OrdinalIgnoreCase) == true;

                                var slResult = await StreamlineUpdater.UpdateAsync(game, useCustom);
                                if (slResult.Success)
                                {
                                    streamlineSuccessCount++;
                                    gameHadSuccess = true;
                                    if (hadBackupBefore)
                                    {
                                        streamlineBackupPreservedCount++;
                                    }
                                }
                                else
                                {
                                    result.Failures.Add(new BatchDeployResult.FailureEntry(game.Title, "Streamline", slResult.Message));
                                    if (slResult.PromptToRelaunchAsAdmin)
                                    {
                                        result.HasAdminRecommendation = true;
                                    }
                                }
                            }
                        }
                        else
                        {
                            streamlineSkippedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                        result.Failures.Add(new BatchDeployResult.FailureEntry(game.Title, "Streamline", ex.Message));
                    }
                }

                // Refresh UI for games with at least one success.
                if (gameHadSuccess)
                {
                    game.UpdateCurrentDLLsFromGameAssets();
                }
            }

            // Build DllResults for the summary.
            foreach (var picker in selectedPickers)
            {
                result.DllResults.Add(new BatchDeployResult.DllTypeResult(
                    picker.AssetType,
                    picker.DisplayName,
                    dllSuccessCounts[picker.AssetType],
                    dllSkippedCounts[picker.AssetType],
                    dllFailureCounts[picker.AssetType],
                    dllBackupPreservedCounts[picker.AssetType]));
            }

            result.StreamlineSuccessCount = streamlineSuccessCount;
            result.StreamlineSkippedCount = streamlineSkippedCount;
            result.StreamlineBackupPreservedCount = streamlineBackupPreservedCount;

            // Show summary dialog.
            await ShowDeploySummaryAsync(result);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    [RelayCommand]
    async Task RestoreAllAsync()
    {
        IsDeploying = true;

        try
        {
            var checkedGames = Games.Where(g => g.IsChecked).ToList();
            TotalGameCount = checkedGames.Count;

            var sb = new System.Text.StringBuilder();
            int totalRestored = 0;
            int totalSkipped = 0;
            var failures = new List<BatchDeployResult.FailureEntry>();
            bool hasAdminRecommendation = false;

            // The 9 swappable DLL types to attempt restore on.
            GameAssetType[] dllTypes =
            [
                GameAssetType.DLSS, GameAssetType.DLSS_D, GameAssetType.DLSS_G,
                GameAssetType.FSR_31_DX12, GameAssetType.FSR_31_VK,
                GameAssetType.XeSS, GameAssetType.XeSS_FG, GameAssetType.XeSS_DX11, GameAssetType.XeLL,
            ];

            for (int i = 0; i < checkedGames.Count; i++)
            {
                var selectableGame = checkedGames[i];
                var game = selectableGame.Game;

                CurrentGameIndex = i + 1;
                CurrentGameTitle = game.Title;

                bool gameHadRestore = false;

                // Restore each DLL type that has a backup.
                foreach (var dllType in dllTypes)
                {
                    try
                    {
                        var backupType = DLLManager.Instance.GetAssetBackupType(dllType);
                        var hasBackup = game.GameAssets.Any(a => a.AssetType == backupType);
                        if (!hasBackup)
                        {
                            continue; // No backup for this type, skip silently.
                        }

                        var resetResult = await game.ResetDllAsync(dllType);
                        if (resetResult.Success)
                        {
                            totalRestored++;
                            gameHadRestore = true;
                        }
                        else
                        {
                            failures.Add(new BatchDeployResult.FailureEntry(game.Title, DLLManager.Instance.GetAssetTypeName(dllType), resetResult.Message));
                            if (resetResult.PromptToRelaunchAsAdmin)
                            {
                                hasAdminRecommendation = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                        failures.Add(new BatchDeployResult.FailureEntry(game.Title, DLLManager.Instance.GetAssetTypeName(dllType), ex.Message));
                    }
                }

                // Restore Streamline if it has a backup.
                try
                {
                    if (game.HasStreamlineBackup)
                    {
                        var slResult = await StreamlineUpdater.RestoreAsync(game);
                        if (slResult.Success)
                        {
                            totalRestored++;
                            gameHadRestore = true;
                        }
                        else
                        {
                            failures.Add(new BatchDeployResult.FailureEntry(game.Title, "Streamline", slResult.Message));
                            if (slResult.PromptToRelaunchAsAdmin)
                            {
                                hasAdminRecommendation = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                    failures.Add(new BatchDeployResult.FailureEntry(game.Title, "Streamline", ex.Message));
                }

                if (gameHadRestore)
                {
                    game.UpdateCurrentDLLsFromGameAssets();
                }
                else
                {
                    totalSkipped++;
                }
            }

            // Build summary text.
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Restored {totalRestored} DLL(s) across {checkedGames.Count - totalSkipped} game(s).");
            if (totalSkipped > 0)
            {
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{totalSkipped} game(s) had no backups to restore.");
            }

            if (failures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failures:");
                foreach (var failure in failures)
                {
                    sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {failure.GameTitle} ({failure.DllTypeName}): {failure.ErrorMessage}");
                }
            }

            if (hasAdminRecommendation)
            {
                sb.AppendLine();
                sb.AppendLine("Some errors may be resolved by running DLSS Swapper as administrator.");
            }

            // Close dialog and show summary.
            if (_dialogWeakReference.TryGetTarget(out FakeContentDialog? batchDialog))
            {
                batchDialog.Hide();
            }

            await Task.Delay(100);

            var dialog = new EasyContentDialog(App.CurrentApp.MainWindow.Content.XamlRoot)
            {
                Title = "Batch Restore Summary",
                CloseButtonText = "OK",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
                Content = new Microsoft.UI.Xaml.Controls.ScrollViewer
                {
                    Content = new Microsoft.UI.Xaml.Controls.TextBlock
                    {
                        Text = sb.ToString(),
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    },
                    MaxHeight = 400,
                },
            };
            await dialog.ShowAsync();
        }
        finally
        {
            IsDeploying = false;
        }
    }

    async Task ShowDeploySummaryAsync(BatchDeployResult result)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var dllResult in result.DllResults)
        {
            var line = $"{dllResult.DisplayName}: {dllResult.SuccessCount} updated, {dllResult.SkippedCount} skipped, {dllResult.FailureCount} failed";
            if (dllResult.BackupPreservedCount > 0)
            {
                line += $" ({dllResult.BackupPreservedCount} original backup(s) preserved)";
            }
            sb.AppendLine(line);
        }

        if (SelectedStreamlineSource != null)
        {
            var slLine = $"Streamline: {result.StreamlineSuccessCount} updated, {result.StreamlineSkippedCount} skipped";
            if (result.StreamlineBackupPreservedCount > 0)
            {
                slLine += $" ({result.StreamlineBackupPreservedCount} original backup(s) preserved)";
            }
            sb.AppendLine(slLine);
        }

        if (result.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Failures:");
            foreach (var failure in result.Failures)
            {
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {failure.GameTitle} ({failure.DllTypeName}): {failure.ErrorMessage}");
            }
        }

        if (result.HasAdminRecommendation)
        {
            sb.AppendLine();
            sb.AppendLine("Some errors may be resolved by running DLSS Swapper as administrator.");
        }

        // Close the batch deploy dialog first, then show the summary on the main window.
        if (_dialogWeakReference.TryGetTarget(out FakeContentDialog? batchDialog))
        {
            batchDialog.Hide();
        }

        // Small delay to let the FakeContentDialog fully close before showing the summary.
        await Task.Delay(100);

        var dialog = new EasyContentDialog(App.CurrentApp.MainWindow.Content.XamlRoot)
        {
            Title = "Batch Deploy Summary",
            CloseButtonText = "OK",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            Content = new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = sb.ToString(),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                },
                MaxHeight = 400,
            },
        };
        await dialog.ShowAsync();
    }
}
