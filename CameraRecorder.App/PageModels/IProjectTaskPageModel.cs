using CameraRecorder.App.Models;
using CommunityToolkit.Mvvm.Input;

namespace CameraRecorder.App.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}