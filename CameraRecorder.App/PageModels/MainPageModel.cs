using CameraRecorder.MotionAnalyzers;
using CameraRecorder.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace CameraRecorder.App.PageModels
{
    public partial class MainPageModel : ObservableObject
    {
        private readonly RtspRecorder _rtspRecorder;
        private readonly RtspMotionDetector _rtspMotionDetector;
        private readonly IOptions<CameraRecorderSettings> _options;
        [ObservableProperty]
        bool _isRecording;

        [ObservableProperty]
        TimeSpan _recordDuration = TimeSpan.Zero;

        [ObservableProperty]
        string _recordDurationText = string.Empty;

        [ObservableProperty]
        private string _today = DateTime.Now.ToString("dddd, MMM d");

        [ObservableProperty]
        private string _log = string.Empty;

        [ObservableProperty]
        ObservableCollection<string> _logs = new();

        public MainPageModel(
            RtspRecorder rtspRecorder,
            RtspMotionDetector rtspMotionDetector,
            ILoggerFactory loggerFactory,
            IOptions<CameraRecorderSettings> options)
        {

            _rtspRecorder = rtspRecorder;
            _rtspMotionDetector = rtspMotionDetector;
            _options = options;

            _rtspMotionDetector.MotionDetected += () => { _rtspRecorder.StartRecord(); IsRecording = true; };
            _rtspMotionDetector.MotionEnded += () => { _rtspRecorder.StopRecordAsync(); IsRecording = false; };

            _rtspRecorder.RecordingDurationChanged += (duration) =>
            {
                RecordDuration = duration;
                RecordDurationText = duration != TimeSpan.Zero ? ((int)duration.TotalSeconds % 2 == 0 ? "🔴" : "⚫") + duration.ToString("hh\\:mm\\:ss") : string.Empty;
            };

            _rtspRecorder.Start();
        }


        [RelayCommand]
        private void NavigatedTo()
        {
            _rtspMotionDetector.Start();
        }

        [RelayCommand]
        private void NavigatedFrom()
        {
            _rtspMotionDetector.Stop();
        }

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