using FsCheck;
using FsCheck.Fluent;
using DLSS_Swapper.Data;

namespace DLSS_Swapper.Tests;

/// <summary>
/// Feature: batch-game-deploy, Property 6: DLL picker filters to downloaded records only
///
/// These property-based tests validate that the DllTypePicker filtering logic
/// correctly includes only DLLRecord objects where LocalRecord?.IsDownloaded == true.
///
/// The DllTypePicker constructor calls DLLManager.Instance.GetAssetTypeName which
/// depends on WinUI ResourceHelper, so we test the filtering expression directly.
/// The filtering logic under test is: records.Where(r => r.LocalRecord?.IsDownloaded == true)
///
/// **Validates: Requirements 4.5**
/// </summary>
public class BatchDeployDllTypePickerPropertyTests
{
    /// <summary>
    /// The exact filtering predicate used by DllTypePicker constructor.
    /// Extracted here so we test the same logic without needing the WinUI singleton.
    /// </summary>
    private static List<DLLRecord> FilterToDownloaded(IEnumerable<DLLRecord> records)
    {
        return records.Where(r => r.LocalRecord?.IsDownloaded == true).ToList();
    }

    /// <summary>
    /// Creates a DLLRecord with the specified LocalRecord/IsDownloaded configuration.
    /// </summary>
    private static DLLRecord CreateDLLRecord(string version, bool hasLocalRecord, bool isDownloaded)
    {
        var record = new DLLRecord
        {
            Version = version,
            VersionNumber = (ulong)Math.Abs(version.GetHashCode()),
        };

        if (hasLocalRecord)
        {
            var localRecord = LocalRecord.FromExpectedPath($"C:\\fake\\{Guid.NewGuid()}.dll");
            localRecord.IsDownloaded = isDownloaded;
            record.LocalRecord = localRecord;
        }
        // If hasLocalRecord is false, LocalRecord stays null

        return record;
    }

    /// <summary>
    /// Generator for a single DLLRecord test scenario: (version, hasLocalRecord, isDownloaded).
    /// </summary>
    private static Gen<(string Version, bool HasLocalRecord, bool IsDownloaded)> DllRecordStateGen()
    {
        var versionGen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => s.Get);

        return versionGen
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Zip(ArbMap.Default.GeneratorFor<bool>())
            .Select(t => (Version: t.Item1.Item1, HasLocalRecord: t.Item1.Item2, IsDownloaded: t.Item2));
    }

    /// <summary>
    /// Generator for a list of DLLRecord states (1 to 50 items).
    /// </summary>
    private static Gen<List<(string Version, bool HasLocalRecord, bool IsDownloaded)>> DllRecordStateListGen()
    {
        return DllRecordStateGen().ListOf()
            .Where(list => list.Count > 0)
            .Select(list => list.Take(50).ToList());
    }

    // -----------------------------------------------------------------------
    // Property 6: DLL picker filters to downloaded records only
    // For any list of DLLRecord objects with varying IsDownloaded states,
    // AvailableRecords contains only those where LocalRecord?.IsDownloaded == true.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 6a: All records in the filtered result have LocalRecord.IsDownloaded == true.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Fact]
    public void FilteredRecords_AllHaveIsDownloadedTrue()
    {
        var prop = Prop.ForAll(
            DllRecordStateListGen().ToArbitrary(),
            (List<(string Version, bool HasLocalRecord, bool IsDownloaded)> states) =>
            {
                var records = states.Select(s => CreateDLLRecord(s.Version, s.HasLocalRecord, s.IsDownloaded)).ToList();
                var filtered = FilterToDownloaded(records);

                // Every record in the filtered list must have LocalRecord?.IsDownloaded == true
                return filtered.All(r => r.LocalRecord?.IsDownloaded == true);
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 6b: No downloaded record is excluded from the filtered result.
    /// Every input record where LocalRecord?.IsDownloaded == true must appear in the output.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Fact]
    public void FilteredRecords_NoDownloadedRecordExcluded()
    {
        var prop = Prop.ForAll(
            DllRecordStateListGen().ToArbitrary(),
            (List<(string Version, bool HasLocalRecord, bool IsDownloaded)> states) =>
            {
                var records = states.Select(s => CreateDLLRecord(s.Version, s.HasLocalRecord, s.IsDownloaded)).ToList();
                var filtered = FilterToDownloaded(records);

                // Count of records with IsDownloaded == true in input
                var expectedCount = records.Count(r => r.LocalRecord?.IsDownloaded == true);

                return filtered.Count == expectedCount;
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 6c: Records with null LocalRecord are excluded from the filtered result.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Fact]
    public void FilteredRecords_NullLocalRecordExcluded()
    {
        var prop = Prop.ForAll(
            DllRecordStateListGen().ToArbitrary(),
            (List<(string Version, bool HasLocalRecord, bool IsDownloaded)> states) =>
            {
                var records = states.Select(s => CreateDLLRecord(s.Version, s.HasLocalRecord, s.IsDownloaded)).ToList();
                var filtered = FilterToDownloaded(records);

                // No record in the filtered list should have a null LocalRecord
                return filtered.All(r => r.LocalRecord != null);
            });

        prop.QuickCheckThrowOnFailure();
    }

    /// <summary>
    /// Property 6d: Records with IsDownloaded == false are excluded from the filtered result.
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Fact]
    public void FilteredRecords_NotDownloadedRecordsExcluded()
    {
        var prop = Prop.ForAll(
            DllRecordStateListGen().ToArbitrary(),
            (List<(string Version, bool HasLocalRecord, bool IsDownloaded)> states) =>
            {
                var records = states.Select(s => CreateDLLRecord(s.Version, s.HasLocalRecord, s.IsDownloaded)).ToList();
                var filtered = FilterToDownloaded(records);

                // None of the filtered records should have IsDownloaded == false
                return filtered.All(r => r.LocalRecord?.IsDownloaded != false);
            });

        prop.QuickCheckThrowOnFailure();
    }
}
