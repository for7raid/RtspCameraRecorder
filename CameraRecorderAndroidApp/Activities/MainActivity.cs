using Android.Views;
using CameraRecorder;
using CameraRecorderAndroidApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CameraRecorderAndroidApp.Activities
{
    [Activity(Label = "@string/app_name",
        MainLauncher = true,
        ScreenOrientation = Android.Content.PM.ScreenOrientation.Landscape,
        Theme = "@style/AppTheme")]
    public class MainActivity : Activity
    {
        private RtspRecorder _rtspRecorder;
        private RtspMotionDetector _rtspMotionDetector;
        private TextView? txtRecordingStatus, txtLastRecording;
        private TextView? txtMotionLog;
        private Button? btnStart, btnStop;
        public TextView? txtView3 { get; private set; }
        private ILogger<MainActivity> _logger;
        private readonly LogWebServer _webServer;

        private readonly CircularBuffer<DateTime> _lastRecords = new(5);

        public MainActivity()
        {
            var serviceProvider = ServiceCollectionConfigurator.Instance;

            _rtspRecorder = serviceProvider.GetRequiredService<RtspRecorder>();
            _rtspMotionDetector = serviceProvider.GetRequiredService<RtspMotionDetector>();

            _logger = serviceProvider.GetRequiredService<ILogger<MainActivity>>();
            _webServer = serviceProvider.GetRequiredService<LogWebServer>();
            _ = serviceProvider.GetService<AlarmWebServer>();


            _rtspMotionDetector.DetectionLog += (log) =>
            {
                RunOnUiThread(() => { txtMotionLog!.Text = log; });
            };
            _rtspMotionDetector.MotionDetected += () => { _rtspRecorder.StartRecord(); };
            _rtspMotionDetector.MotionEnded += (lastMotionTime) => { _rtspRecorder.StopRecord(lastMotionTime); };

            _rtspRecorder.RecordingDurationChanged += (duration) =>
            {
                string text = duration != TimeSpan.Zero
                    ? ((int)duration.TotalSeconds % 2 == 0 ? "🔴 " : "⚫ ") + duration.ToString(@"hh\:mm\:ss")
                    : string.Empty;
                RunOnUiThread(() => { txtRecordingStatus!.Text = text; });
            };

            _rtspRecorder.RecordingStarted += () =>
            {
                RunOnUiThread(() =>
                {
                    if (btnStart != null) btnStart.Visibility = ViewStates.Gone;
                    if (btnStop != null) btnStop.Visibility = ViewStates.Visible;
                    txtLastRecording.Visibility = ViewStates.Gone;
                    txtRecordingStatus.Visibility = ViewStates.Visible;
                });
            };

            _rtspRecorder.RecordingStopped += () =>
            {
                RunOnUiThread(() =>
                {
                    if (btnStart != null) btnStart.Visibility = ViewStates.Visible;
                    if (btnStop != null) btnStop.Visibility = ViewStates.Gone;

                    txtLastRecording.Visibility = ViewStates.Visible;
                    txtRecordingStatus.Visibility = ViewStates.Gone;

                    _lastRecords.Add(DateTime.Now);



                    txtLastRecording.Text = _lastRecords.Ordered.Select(d => "🏃‍ " + d.ToString("HH:mm")).Aggregate((a, v) => a + "    " + v);
                });
            };
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            txtRecordingStatus = FindViewById<TextView>(Resource.Id.txtRecordingStatus);
            txtLastRecording = FindViewById<TextView>(Resource.Id.txtLastRecording);
            txtMotionLog = FindViewById<TextView>(Resource.Id.txtMotionLog);


            // Отрисовка напрямую аппаратным декодером → ждём готовности Surface
            var surfaceView = FindViewById<SurfaceView>(Resource.Id.surfaceView1)!;
            surfaceView.Holder!.AddCallback(new SurfaceCb(() =>
            {

                if (_rtspRecorder.H26XDecoder is H265Decoder h265Decoder)
                {
                    h265Decoder.SetOutputSurface(surfaceView.Holder!.Surface);
                }

            },
            () => _rtspRecorder.Stop()));

            _rtspRecorder.Start();
            _rtspMotionDetector.Start();

            _webServer.Start();

            // Бургер-меню
            var btnMenu = FindViewById<ImageButton>(Resource.Id.btnMenu)!;
            btnMenu.Click += (_, _) =>
            {
                var popup = new PopupMenu(this, btnMenu);
                popup.Menu!.Add("Настройки");
                popup.Menu!.Add("Логи");
                popup.MenuItemClick += (s, e) =>
                {
                    if (e.Item!.TitleFormatted!.ToString() == "Настройки")
                        StartActivity(new Android.Content.Intent(this, typeof(SettingsActivity)));
                    else if (e.Item!.TitleFormatted!.ToString() == "Логи")
                        StartActivity(new Android.Content.Intent(this, typeof(LogsActivity)));
                };
                popup.Show();
            };

            btnStart = FindViewById<Button>(Resource.Id.btnStartRecord)!;
            btnStart.Click += (_, _) => _rtspRecorder.StartRecord();

            btnStop = FindViewById<Button>(Resource.Id.btnStopRecord)!;
            btnStop.Click += (_, _) => _rtspRecorder.StopRecord();


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
