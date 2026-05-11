using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraRecorder.App.PageModels
{
    public partial class MainPageModel : ObservableObject
    {
        private bool _isNavigatedTo;

        private readonly RtspRecorder _rtspRecorder;


        [ObservableProperty]
        bool _isRecording;

        [ObservableProperty]
        TimeSpan _recordDuration;

        [ObservableProperty]
        string _recordDurationText;

        [ObservableProperty]
        private string _today = DateTime.Now.ToString("dddd, MMM d");

        public MainPageModel(
            RtspRecorder rtspRecorder)
        {

            _rtspRecorder = rtspRecorder;


            _rtspRecorder.RecordingStarted += () => IsRecording = true;
            _rtspRecorder.RecordingStopped += () => IsRecording = false;
            _rtspRecorder.RecordingDurationChanged += (duration) =>
            {
                RecordDuration = duration;
                RecordDurationText = duration != TimeSpan.Zero ? ((int)duration.TotalSeconds % 2 == 0 ? "🔴" : "⚫") + duration.ToString("hh\\:mm\\:ss") : string.Empty;
            };

            _rtspRecorder.Start();
        }



        [RelayCommand]
        private void NavigatedTo() =>
            _isNavigatedTo = true;

        [RelayCommand]
        private void NavigatedFrom() =>
            _isNavigatedTo = false;

        [RelayCommand]
        private async Task Appearing()
        {

        }



        [RelayCommand]
        private Task StartRecord()
        {
            _rtspRecorder.StartRecord();
            return Task.CompletedTask;
        }

        [RelayCommand]
        private async Task StopRecord()
        {
            await _rtspRecorder.StopRecordAsync();
        }
    }
}