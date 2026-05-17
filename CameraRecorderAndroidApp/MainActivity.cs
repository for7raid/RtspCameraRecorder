using Android.Views;
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
        private H265Decoder _decoderDetector;
        private readonly H265Decoder _decoderScreen;
        private TextView? txtRecordingStatus;
        private TextView? txtView2;
        public TextView? txtView3 { get; private set; }
        private ILogger<MainActivity> _logger;

        public MainActivity()
        {
            var serviceProvider = ServiceCollectionConfigurator.Instance;

            _rtspRecorder = serviceProvider.GetRequiredService<RtspRecorder>();
            _rtspMotionDetector = serviceProvider.GetRequiredService<RtspMotionDetector>();
            _decoderDetector = (H265Decoder)serviceProvider.GetRequiredKeyedService<IH26xDecoder>("OnBufferDecoder");
            _decoderScreen = (H265Decoder)serviceProvider.GetRequiredKeyedService<IH26xDecoder>("OnScreenDecoder");
            _logger = serviceProvider.GetRequiredService<ILogger<MainActivity>>();

            _rtspMotionDetector.DetectionLog += (log) =>
            {
                RunOnUiThread(() => { txtView2!.Text = log; });
            };

            _rtspRecorder.RecordingDurationChanged += (duration) =>
            {
                string text = duration != TimeSpan.Zero
                    ? ((int)duration.TotalSeconds % 2 == 0 ? "🔴" : "⚫") + duration.ToString(@"hh\:mm\:ss")
                    : string.Empty;
                RunOnUiThread(() => { txtRecordingStatus!.Text = text; });
            };
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            txtRecordingStatus = FindViewById<TextView>(Resource.Id.txtRecordingStatus);
            txtView2 = FindViewById<TextView>(Resource.Id.textView2);
            txtView3 = FindViewById<TextView>(Resource.Id.textView3);

            // Отрисовка напрямую аппаратным декодером → ждём готовности Surface
            var surfaceView = FindViewById<SurfaceView>(Resource.Id.surfaceView1)!;
            surfaceView.Holder!.AddCallback(new SurfaceCb(() =>
            {
                _decoderScreen.Initialize(surfaceView.Holder!.Surface);
                _decoderDetector.Initialize();

                _rtspRecorder.Start();
                _rtspMotionDetector.Start();
            },
            () => _decoderScreen.Dispose()));


            var btnSettings = FindViewById<Button>(Resource.Id.btnSettings);
            btnSettings!.Click += (_, _) => StartActivity(new Android.Content.Intent(this, typeof(SettingsActivity)));

            var btnLogs = FindViewById<Button>(Resource.Id.btnLogs);
            btnLogs!.Click += (_, _) => StartActivity(new Android.Content.Intent(this, typeof(LogsActivity)));

            var btnStart = FindViewById<Button>(Resource.Id.btnStartRecord)!;
            btnStart.Click += (_, _) => _rtspRecorder.StartRecord();

            var btnStop = FindViewById<Button>(Resource.Id.btnStopRecord)!;
            btnStop.Click += async (_, _) => await _rtspRecorder.StopRecordAsync();


        }
    }

    class SurfaceCb : Java.Lang.Object, ISurfaceHolderCallback
    {
        private readonly Action _onCreated;
        private readonly Action _onDestroyed;
        public SurfaceCb(Action onCreated, Action onDestroyed)
        {
            _onCreated = onCreated;
            _onDestroyed = onDestroyed;
        }
        public void SurfaceCreated(ISurfaceHolder? holder) => _onCreated();
        public void SurfaceDestroyed(ISurfaceHolder? holder) => _onDestroyed();
        public void SurfaceChanged(ISurfaceHolder? holder, Android.Graphics.Format format, int w, int h) { }
    }
}
