namespace CameraRecorder.App.Pages;

public partial class RecordingsPage : ContentPage
{
    public RecordingsPage(RecordingsPageModel model)
    {
        InitializeComponent();
        BindingContext = model;
    }
}
