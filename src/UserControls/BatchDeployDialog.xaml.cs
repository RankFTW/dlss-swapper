using System;

namespace DLSS_Swapper.UserControls;

public sealed partial class BatchDeployDialog : FakeContentDialog
{
    public BatchDeployDialogModel ViewModel { get; private set; }

    public BatchDeployDialog()
    {
        this.InitializeComponent();

        var dialogWeakReference = new WeakReference<FakeContentDialog>(this);
        ViewModel = new BatchDeployDialogModel(dialogWeakReference);
        DataContext = ViewModel;
    }
}
