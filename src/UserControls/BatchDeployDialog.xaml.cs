using System;

namespace DLSS_Swapper.UserControls;

public sealed partial class BatchDeployDialog : FakeContentDialog
{
    public BatchDeployDialogModel ViewModel { get; private set; }

    public BatchDeployDialog()
    {
        this.InitializeComponent();

        // Widen the dialog beyond the default ContentDialog max width.
        Resources["ContentDialogMinWidth"] = 900.0;
        Resources["ContentDialogMaxWidth"] = 1100.0;

        var dialogWeakReference = new WeakReference<FakeContentDialog>(this);
        ViewModel = new BatchDeployDialogModel(dialogWeakReference);
        DataContext = ViewModel;
    }
}
