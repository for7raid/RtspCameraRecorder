using CameraRecorder.Settings;
using CameraRecorderAndroidApp.Services;
using Java.Nio.FileNio;
using Microsoft.Extensions.DependencyInjection;

namespace CameraRecorderAndroidApp;

[Activity(Label = "Настройки")]
public class SettingsActivity : Activity
{

    private readonly ISettingsStorageService _settingsStorage;
    private EditText? etRtspMain, etRtspSub, etRtspLogin, etRtspPassword;
    private Switch? swLocalEnabled, swFtpEnabled, swFtps;
    private EditText? etLocalPath, etFtpHost, etFtpLogin, etFtpPassword, etFtpDir;
    private SeekBar? sbPreMotion, sbPostMotion;
    private TextView? tvPreMotion, tvPostMotion;

    public SettingsActivity()
    {
        _settingsStorage = ServiceCollectionConfigurator.Instance.GetRequiredService<ISettingsStorageService>();
    }

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_settings);

        // Bind views
        etRtspMain = FindViewById<EditText>(Resource.Id.etRtspMainStreamUrl)!;
        etRtspSub = FindViewById<EditText>(Resource.Id.etRtspSubStreamUrl)!;
        etRtspLogin = FindViewById<EditText>(Resource.Id.etRtspLogin)!;
        etRtspPassword = FindViewById<EditText>(Resource.Id.etRtspPassword)!;
        swLocalEnabled = FindViewById<Switch>(Resource.Id.swLocalStorageEnabled)!;
        etLocalPath = FindViewById<EditText>(Resource.Id.etLocalRecordingsPath)!;
        var btnPickFolder = FindViewById<Button>(Resource.Id.btnPickFolder)!;
        btnPickFolder.Click += (_, _) =>
        {
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
            StartActivityForResult(intent, 42);
        };
        swFtpEnabled = FindViewById<Switch>(Resource.Id.swFtpEnabled)!;
        etFtpHost = FindViewById<EditText>(Resource.Id.etFtpHost)!;
        etFtpLogin = FindViewById<EditText>(Resource.Id.etFtpLogin)!;
        etFtpPassword = FindViewById<EditText>(Resource.Id.etFtpPassword)!;
        etFtpDir = FindViewById<EditText>(Resource.Id.etFtpDirectory)!;
        swFtps = FindViewById<Switch>(Resource.Id.swUseFtps)!;
        sbPreMotion = FindViewById<SeekBar>(Resource.Id.sbPreMotion)!;
        sbPostMotion = FindViewById<SeekBar>(Resource.Id.sbPostMotion)!;
        tvPreMotion = FindViewById<TextView>(Resource.Id.tvPreMotionValue)!;
        tvPostMotion = FindViewById<TextView>(Resource.Id.tvPostMotionValue)!;
        var btnSave = FindViewById<Button>(Resource.Id.btnSave)!;

        // SeekBar listeners
        sbPreMotion.ProgressChanged += (_, e) => tvPreMotion.Text = e.Progress.ToString();
        sbPostMotion.ProgressChanged += (_, e) => tvPostMotion.Text = e.Progress.ToString();

        // Load settings
        var settings = await _settingsStorage.LoadAsync();
        Populate(settings);

        // Save
        btnSave.Click += async (_, _) =>
        {
            //File.WriteAllText(etLocalPath?.Text + "/log.txt", "hello!");
            await _settingsStorage.SaveAsync(Collect());
            Toast.MakeText(this, "Настройки сохранены", ToastLength.Short)!.Show();
            Finish();
        };
    }

    private void Populate(CameraRecorderSettings s)
    {
        etRtspMain.Text = s.RtspMainStreamUrl;
        etRtspSub.Text = s.RtspSubStreamUrl;
        etRtspLogin.Text = s.RtspLogin;
        etRtspPassword.Text = s.RtspPassword;
        swLocalEnabled.Checked = s.LocalStorageEnabled;
        etLocalPath.Text = s.LocalRecordingsPath;
        swFtpEnabled.Checked = s.FtpEnabled;
        etFtpHost.Text = s.FtpHost;
        etFtpLogin.Text = s.FtpLogin;
        etFtpPassword.Text = s.FtpPassword;
        etFtpDir.Text = s.FtpDirectory;
        swFtps.Checked = s.UseFtps;
        sbPreMotion.Progress = s.PreMotionDurationSec;
        sbPostMotion.Progress = s.PostMotionDurationSec;
        tvPreMotion.Text = s.PreMotionDurationSec.ToString();
        tvPostMotion.Text = s.PostMotionDurationSec.ToString();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == 42 && resultCode == Result.Ok && data?.Data != null)
        {

            var uri = data.Data;
            var path = Android.Net.Uri.Decode(uri.LastPathSegment ?? "");
            if (path.StartsWith("primary:"))
                path = path.Substring("primary:".Length);
            if (path.StartsWith("/tree/"))
                path = path.Substring("/tree/".Length);
            

            if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                path = System.IO.Path.Combine(Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath, path);
            }

            if (etLocalPath != null)
                etLocalPath.Text = path.TrimEnd('/');
        }
    }

    private CameraRecorderSettings Collect()
    {
        return new CameraRecorderSettings
        {
            RtspMainStreamUrl = etRtspMain?.Text ?? "",
            RtspSubStreamUrl = etRtspSub?.Text ?? "",
            RtspLogin = etRtspLogin?.Text ?? "",
            RtspPassword = etRtspPassword?.Text ?? "",
            LocalStorageEnabled = swLocalEnabled?.Checked ?? true,
            LocalRecordingsPath = etLocalPath?.Text ?? "",
            FtpEnabled = swFtpEnabled?.Checked ?? false,
            FtpHost = etFtpHost?.Text ?? "",
            FtpLogin = etFtpLogin?.Text ?? "",
            FtpPassword = etFtpPassword?.Text ?? "",
            FtpDirectory = etFtpDir?.Text ?? "",
            UseFtps = swFtps?.Checked ?? false,
            PreMotionDurationSec = sbPreMotion?.Progress ?? 10,
            PostMotionDurationSec = sbPostMotion?.Progress ?? 10,
        };
    }
}
