namespace CameraRecorder.App.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsPageModel model)
    {
        InitializeComponent();
        BindingContext = model;
    }
}
