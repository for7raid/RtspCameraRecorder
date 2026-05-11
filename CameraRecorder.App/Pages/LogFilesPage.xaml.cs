namespace CameraRecorder.App.Pages;

public partial class LogFilesPage : ContentPage
{
    public LogFilesPage(LogFilesPageModel model)
    {
        InitializeComponent();
        BindingContext = model;
    }
}
