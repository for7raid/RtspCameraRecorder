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
        private bool _isNavigatedTo;

        private readonly RtspRecorder _rtspRecorder;
        private readonly RtspViewer _rtspViewer;
        private readonly IOptions<CameraRecorderSettings> _options;
        [ObservableProperty]
        bool _isRecording;

        [ObservableProperty]
        TimeSpan _recordDuration;

        [ObservableProperty]
        string _recordDurationText;

        [ObservableProperty]
        private string _today = DateTime.Now.ToString("dddd, MMM d");

        private DateTime? _lastMotionTime;

        [ObservableProperty]
        private string _log = string.Empty;

        [ObservableProperty]
        ObservableCollection<string> _logs = new();

        public MainPageModel(
            RtspRecorder rtspRecorder,
            RtspViewer rtspViewer,
            ILoggerFactory loggerFactory,
            IOptions<CameraRecorderSettings> options)
        {

            _rtspRecorder = rtspRecorder;
            _rtspViewer = rtspViewer;
            _options = options;

            _rtspRecorder.RecordingStarted += () => IsRecording = true;
            _rtspRecorder.RecordingStopped += () => IsRecording = false;
            _rtspRecorder.RecordingDurationChanged += (duration) =>
            {
                RecordDuration = duration;
                RecordDurationText = duration != TimeSpan.Zero ? ((int)duration.TotalSeconds % 2 == 0 ? "🔴" : "⚫") + duration.ToString("hh\\:mm\\:ss") : string.Empty;
            };

            _rtspRecorder.Start();

            MotionDetectorSettings settings = new()
            {
                Width = 640,
                Height = 480,
                BlockSize = 6,                        // Компромисс: 6×6 (106×80 ≈ 8 500 блоков)
                FrameBufferSize = 30,

                // Ключевые параметры
                ChangedBlocksRatioThreshold = 0.01,  // 1.5% (около 130 блоков)
                SigmaThreshold = 2.5,                  // 3 сигмы: баланс чувствительности и помехоустойчивости

                // Фон
                AdaptationSpeed = 0.12,
                MinFramesBeforeDetection = 10,
                StatsRecalculationPeriod = 30,

                // Фильтры
                UseAdaptiveBackground = true,
                EnableSpikeFilter = true,
                MinMotionDuration = 2,                // 2 кадра подряд для подтверждения
                MaxGlobalBrightnessChange = 50
            };

            var detector = new AdaptiveMotionDetector(settings, loggerFactory.CreateLogger<AdaptiveMotionDetector>());
            int counter = 0;
            rtspViewer.FrameReceived += async (rgbBytes) =>
            {
                var result = detector.DetectMotion(rgbBytes);
                Log = Log.Substring(Math.Max(0, Log.Length - 200)) + "-" + (counter++) + (result.HasMotion ? "YES" : "NO");

                //Запускаем запись по движению, если сейчас нет записи вручную
                //if ((!_rtspRecorder.IsRecording || _lastMotionTime.HasValue) && result.HasMotion)
                //{
                //    _rtspRecorder.StartRecord();
                //    _lastMotionTime = DateTime.Now;

                //}
                //if (_lastMotionTime.HasValue && (DateTime.Now - _lastMotionTime.Value).TotalSeconds > 10) //TODO Заменить на настройку
                //{
                //    await _rtspRecorder.StopRecordAsync();
                //    _lastMotionTime = null;
                //}

            };

        }



        [RelayCommand]
        private void NavigatedTo()
        {
            //_rtspViewer.Start();
        }

        [RelayCommand]
        private void NavigatedFrom()
        {
            //_rtspViewer.Stop();
        }

        [RelayCommand]
        private async Task Appearing()
        {
            Log = _options.Value.PostMotionDurationSec.ToString();
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