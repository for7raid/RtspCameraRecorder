using Android.Views;

namespace AndroidApp1
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            var textView = FindViewById<TextView>(Resource.Id.textView1);
            textView.Click += (s, e) => StopRecord(null);

            RtspClientExample.Recorder.Init();
        }

        public void StopRecord(View view)
        {
            RtspClientExample.Recorder.Stop();
            System.Environment.Exit(0);
        }
    }
}