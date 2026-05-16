using CameraRecorder;
using CameraRecorder.MotionAnalyzers;
using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMP4.Common;
using System.Threading.Channels;
using Android.Widget;
using CameraRecorderAndroidApp.Services;

namespace CameraRecorderAndroidApp
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private RtspRecorder recorder;
        private RtspMotionDetector rtspViewer;
        private TextView? txtView;
        private TextView? txtView2;

        public TextView? txtView3 { get; private set; }

        private ILogger<MainActivity> _logger;

        public MainActivity()
        {



            // Строим провайдер
            var serviceProvider = ServiceCollectionConfigurator.Instance;

            // Получаем сервис
            //recorder = serviceProvider.GetRequiredService<RtspRecorder>();
            //rtspViewer = serviceProvider.GetRequiredService<RtspViewer>();

            _logger = serviceProvider.GetRequiredService<ILogger<MainActivity>>();


        }
       
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            txtView = FindViewById<TextView>(Resource.Id.textView1);
            txtView2 = FindViewById<TextView>(Resource.Id.textView2);
            txtView3 = FindViewById<TextView>(Resource.Id.textView3);

            var btnSettings = FindViewById<Button>(Resource.Id.btnSettings);
            btnSettings!.Click += (_, _) => StartActivity(new Android.Content.Intent(this, typeof(SettingsActivity)));


        }
    }
}
