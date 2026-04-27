using System.Collections.Generic;
using DLSS_Swapper.Data;

namespace DLSS_Swapper.UserControls;

public class BatchDeployResult
{
    public record DllTypeResult(GameAssetType AssetType, string DisplayName, int SuccessCount, int SkippedCount, int FailureCount);

    public record FailureEntry(string GameTitle, string DllTypeName, string ErrorMessage);

    public List<DllTypeResult> DllResults { get; } = new();

    public int StreamlineSuccessCount { get; set; }

    public int StreamlineSkippedCount { get; set; }

    public List<FailureEntry> Failures { get; } = new();

    public bool HasAdminRecommendation { get; set; }
}
