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

        Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions() { SingleReader = true, SingleWriter = true });
        private RTSPClient _rTSPClient;
        private ILogger<MainActivity> _logger;

        public MainActivity()
        {
            var services = new ServiceCollection();


            services.AddTransient<RingBufferVideoStorage>();
            services.AddTransient<RingBufferAudioStorage>();
            services.AddTransient<RTSPClient>();
            services.AddTransient<RtspRecorder>();
            services.AddTransient<RtspMotionDetector>();
            services.AddTransient<IMp4Logger, Mp4Logger>();
            //services.AddSingleton<StaticSettingsProvider>();
            //            services.AddTransient(sp => Options.Create(sp.GetRequiredService<StaticSettingsProvider>().GetSettings()));

            services.AddTransient<IStorageSink, LocalFileSink_>();
            services.AddTransient<IStorageSink, FtpSink>();

            services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    //.AddFilter("CameraRecorder.MotionAnalyzers.MotionDetectionResult", LogLevel.Debug)
                    .AddDebug()
                    //.AddSimpleConsole(o =>
                    //{
                    //    o.SingleLine = false;
                    //})
                    //.AddFile($@"C:\temp\camera\log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt")
                    ;
            });

            var configuration = new ConfigurationBuilder()
                    .AddJsonFile("camerarecorder.settings.json", optional: true, reloadOnChange: true)
                    .Build();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOptions();


            services.AddTransient(sp => Options.Create(CameraRecorderSettings.Default));


            // Строим провайдер
            var serviceProvider = services.BuildServiceProvider();

            // Получаем сервис
            //recorder = serviceProvider.GetRequiredService<RtspRecorder>();
            //rtspViewer = serviceProvider.GetRequiredService<RtspViewer>();
            _rTSPClient = serviceProvider.GetRequiredService<RTSPClient>();
            _logger = serviceProvider.GetRequiredService<ILogger<MainActivity>>();

            _rTSPClient.SetupMessageCompleted += (_, _) =>
            {
                _logger.LogInformation("Setup Completed");
                _rTSPClient.Play();
            };

            MotionDetectorSettings settings = new()
            {
                PixelFormat = PixelFormat.Y,
                Width = 640,
                Height = 480,
                BlockSize = 6,                        // Компромисс: 6×6 (106×80 ≈ 8 500 блоков)
                FrameBufferSize = 30,

                // Ключевые параметры
                ChangedBlocksRatioThreshold = 0.01,  // 1.5% (около 130 блоков)
                SigmaThreshold = 2.5,                  // 3 сигмы: баланс чувствительности и помехоустойчивости

                MinFramesBeforeDetection = 10,
                StatsRecalculationPeriod = 30,

                // Фильтры
                EnableSpikeFilter = true,
                MinMotionDuration = 4,                // 2 кадра подряд для подтверждения
                MaxGlobalBrightnessChange = 50
            };


            _rTSPClient.SetupVideoPayload("H265", ReceivedVideoData_H265);

            _decoder = new H265Decoder(640, 480);
            _decoder.FrameDecoded += _decoder_FrameDecoded;

            _detector = new AdaptiveMotionDetector(settings, serviceProvider.GetRequiredService<ILogger<AdaptiveMotionDetector>>());


        }

        bool hasMotion = false;
        int decoded = 0;
        private void _decoder_FrameDecoded(object? sender, DecodedFrameEventArgs e)
        {
            decoded++;
            var result = _detector.DetectMotion(e.Frame.ToY(), (ulong)e.Frame.TimestampUs);
            RunOnUiThread(() =>
            {
            //    if (result.HasMotion && !hasMotion)
            //    {
            //        //logger.LogInformation("Есть вдижение");
            //        txtView2.Text = $"Есть вдижение {decoded}";
            //    }
            //    else if (!result.HasMotion && hasMotion)
            //    {
            //        //logger.LogInformation("Движение остановлено");
            //        txtView2.Text = $"Движение остановлено {decoded}";
            //    }

                txtView2.Text = result.ToString();
                txtView3.Text = $"decoded {decoded}, {framescount - decoded} left, detection {result.ProcessingTimeMs} ms";
            });
            hasMotion = result.HasMotion;


            //RunOnUiThread(() => { txtView2.Text = $"frame decoded {e.Frame.TimestampUs} {e.Frame.Format}";});

        }

        int framescount = 0;
        private H265Decoder _decoder;
        private AdaptiveMotionDetector _detector;

        void ReceivedVideoData_H265(RTSPClient client, SimpleDataEventArgs dataArgs)
        {
            foreach (var nalUnitMem in dataArgs.Data)
            {
                var nalUnit = nalUnitMem.Span;
                if (nalUnit.Length > 5)
                {
                    var nal_unit_type = (NalUnitType)((nalUnit[4] >> 1) & 0x3F);
                    //var unit = nalUnitMem.Slice(5);
                    _frames.Writer.TryWrite(nalUnitMem.ToArray());
                    //_logger.LogInformation("frame received " + dataArgs.RtpTimestamp.ToString());
                    RunOnUiThread(() => { txtView.Text = "frame received " + (framescount++).ToString(); });
                    _decoder.DecodeFrame(nalUnitMem.ToArray(), (long)dataArgs.RtpTimestamp, nal_unit_type);
                }
            }
        }
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            txtView = FindViewById<TextView>(Resource.Id.textView1);
            txtView2 = FindViewById<TextView>(Resource.Id.textView2);
            txtView3 = FindViewById<TextView>(Resource.Id.textView3);



            //recorder.Start();
            //rtspViewer.Start();
            _decoder.Initialize();
            _rTSPClient.Connect("rtsp://192.168.1.8:554/stream2", "admin", "123456", RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);

        }
    }
}
