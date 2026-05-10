using CameraRecorder.App.Models;
using CameraRecorder.App.PageModels;

namespace CameraRecorder.App.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}