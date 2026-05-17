using CameraRecorder;
using CameraRecorderAndroidApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CameraRecorderAndroidApp
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private RtspRecorder _rtspRecorder;
        private RtspMotionDetector _rtspMotionDetector;
        private TextView? txtRecordingStatus;
        private TextView? txtView2;

        public TextView? txtView3 { get; private set; }

        private ILogger<MainActivity> _logger;

        public MainActivity()
        {
           


            // Строим провайдер
            var serviceProvider = ServiceCollectionConfigurator.Instance;

            //Получаем сервис
            _rtspRecorder = serviceProvider.GetRequiredService<RtspRecorder>();
            _rtspMotionDetector = serviceProvider.GetRequiredService<RtspMotionDetector>();

            H265Decoder decoder = serviceProvider.GetService<IH26xDecoder>() as H265Decoder;
            decoder.Initialize();

            _logger = serviceProvider.GetRequiredService<ILogger<MainActivity>>();

            _rtspMotionDetector.DetectionLog += (log) =>
            {
                RunOnUiThread(() =>
                {
                    txtView2.Text = log;
                });
            };

            _rtspRecorder.RecordingDurationChanged += (duration) =>
            {
                string text = duration != TimeSpan.Zero ? ((int)duration.TotalSeconds % 2 == 0 ? "🔴" : "⚫") + duration.ToString("hh\\:mm\\:ss") : string.Empty;
                RunOnUiThread(() =>
                {
                    txtRecordingStatus.Text = text;
                });
            };



        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            txtRecordingStatus = FindViewById<TextView>(Resource.Id.txtRecordingStatus);
            txtView2 = FindViewById<TextView>(Resource.Id.textView2);
            txtView3 = FindViewById<TextView>(Resource.Id.textView3);

            var btnSettings = FindViewById<Button>(Resource.Id.btnSettings);
            btnSettings!.Click += (_, _) => StartActivity(new Android.Content.Intent(this, typeof(SettingsActivity)));

            var btnLogs = FindViewById<Button>(Resource.Id.btnLogs);
            btnLogs!.Click += (_, _) => StartActivity(new Android.Content.Intent(this, typeof(LogsActivity)));

            var btnStart = FindViewById<Button>(Resource.Id.btnStartRecord)!;
            btnStart.Click += (_, _) => _rtspRecorder.StartRecord();

            var btnStop = FindViewById<Button>(Resource.Id.btnStopRecord)!;
            btnStop.Click += async (_, _) => await _rtspRecorder.StopRecordAsync();

            _rtspRecorder.Start();
            _rtspMotionDetector.Start();

        }
    }
}
