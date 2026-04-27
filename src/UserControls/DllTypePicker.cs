using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DLSS_Swapper.Data;

namespace DLSS_Swapper.UserControls;

public partial class DllTypePicker : ObservableObject
{
    public GameAssetType AssetType { get; }

    public string DisplayName { get; }

    public List<DLLRecord> AvailableRecords { get; }

    [ObservableProperty]
    public partial DLLRecord? SelectedRecord { get; set; }

    public DllTypePicker(GameAssetType assetType, IEnumerable<DLLRecord> records)
    {
        AssetType = assetType;
        DisplayName = DLLManager.Instance.GetAssetTypeName(assetType);
        AvailableRecords = records.Where(r => r.LocalRecord?.IsDownloaded == true).ToList();
    }
}
