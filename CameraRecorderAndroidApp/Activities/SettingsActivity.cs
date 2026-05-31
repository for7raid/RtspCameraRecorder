using CameraRecorder.Settings;
using CameraRecorderAndroidApp.Services;
using FluentFTP;
using Microsoft.Extensions.DependencyInjection;

namespace CameraRecorderAndroidApp.Activities;

[Activity(Label = "Настройки")]
public class SettingsActivity : Activity
{
    private readonly ISettingsStorageService _settingsStorage;

    private EditText? etRtspMain, etRtspSub, etRtspLogin, etRtspPassword;
    private Switch? swLocalEnabled;
    private EditText? etLocalPath, etLocalMaxAge, etLocalMaxStorage;
    private Switch? swFtpEnabled, swFtps;
    private EditText? etFtpHost, etFtpLogin, etFtpPassword, etFtpDir, etFtpMaxAge, etFtpMaxStorage;
    private Button? btnFtpTest;
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

        // RTSP
        etRtspMain = FindViewById<EditText>(Resource.Id.etRtspMainStreamUrl)!;
        etRtspSub = FindViewById<EditText>(Resource.Id.etRtspSubStreamUrl)!;
        etRtspLogin = FindViewById<EditText>(Resource.Id.etRtspLogin)!;
        etRtspPassword = FindViewById<EditText>(Resource.Id.etRtspPassword)!;

        // Локальное хранилище
        swLocalEnabled = FindViewById<Switch>(Resource.Id.swLocalStorageEnabled)!;
        etLocalPath = FindViewById<EditText>(Resource.Id.etLocalRecordingsPath)!;
        etLocalMaxAge = FindViewById<EditText>(Resource.Id.etLocalMaxFileAge)!;
        etLocalMaxStorage = FindViewById<EditText>(Resource.Id.etLocalMaxStorage)!;
        var btnPickFolder = FindViewById<Button>(Resource.Id.btnPickFolder)!;
        btnPickFolder.Click += (_, _) =>
        {
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
            StartActivityForResult(intent, 42);
        };

        // FTP
        swFtpEnabled = FindViewById<Switch>(Resource.Id.swFtpEnabled)!;
        etFtpHost = FindViewById<EditText>(Resource.Id.etFtpHost)!;
        etFtpLogin = FindViewById<EditText>(Resource.Id.etFtpLogin)!;
        etFtpPassword = FindViewById<EditText>(Resource.Id.etFtpPassword)!;
        etFtpDir = FindViewById<EditText>(Resource.Id.etFtpDirectory)!;
        etFtpMaxAge = FindViewById<EditText>(Resource.Id.etFtpMaxFileAge)!;
        etFtpMaxStorage = FindViewById<EditText>(Resource.Id.etFtpMaxStorage)!;
        swFtps = FindViewById<Switch>(Resource.Id.swUseFtps)!;
        btnFtpTest = FindViewById<Button>(Resource.Id.btnFtpTest)!;
        btnFtpTest.Click += OnFtpTestClick;

        // Запись
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
            await _settingsStorage.SaveAsync(Collect());
            Toast.MakeText(this, "Настройки сохранены", ToastLength.Short)!.Show();
            Finish();
        };
    }

    private async void OnFtpTestClick(object? sender, EventArgs e)
    {
        var host = etFtpHost?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            Toast.MakeText(this, "Укажите хост FTP", ToastLength.Short)!.Show();
            return;
        }

        var login = etFtpLogin?.Text ?? "";
        var password = etFtpPassword?.Text ?? "";
        var dir = etFtpDir?.Text?.Trim() ?? "";
        var useFtps = swFtps?.Checked ?? false;

        if (btnFtpTest == null) return;
        btnFtpTest.Enabled = false;
        btnFtpTest.Text = "Проверка...";

        try
        {
            using var client = new AsyncFtpClient(host, login, password);

            if (useFtps)
            {
                client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                client.Config.ValidateAnyCertificate = true;
            }

            client.Config.ConnectTimeout = 10_000;
            client.Config.DataConnectionConnectTimeout = 10_000;
            client.Config.DataConnectionReadTimeout = 10_000;

            await client.AutoConnect();

            // Создаём временную папку и файл для проверки прав на запись
            var testDir = string.IsNullOrEmpty(dir) ? "/" : dir.TrimEnd('/');
            var tempDir = $"{testDir}/_camerarecorder_test_{Guid.NewGuid():N}";
            var tempFile = $"{tempDir}/test.txt";

            await client.CreateDirectory(tempDir);
            await client.UploadBytes("ok"u8.ToArray(), tempFile);

            // Удаляем за собой
            await client.DeleteFile(tempFile);
            await client.DeleteDirectory(tempDir);

            await client.Disconnect();

            RunOnUiThread(() =>
                Toast.MakeText(this, "FTP подключение успешно ✅", ToastLength.Short)!.Show());
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
                Toast.MakeText(this, $"Ошибка FTP: {ex.Message}", ToastLength.Long)!.Show());
        }
        finally
        {
            if (btnFtpTest != null)
            {
                btnFtpTest.Enabled = true;
                btnFtpTest.Text = "Проверить подключение";
            }
        }
    }

    private void Populate(CameraRecorderSettings s)
    {
        etRtspMain.Text = s.RtspMainStreamUrl;
        etRtspSub.Text = s.RtspSubStreamUrl;
        etRtspLogin.Text = s.RtspLogin;
        etRtspPassword.Text = s.RtspPassword;

        var local = s.LocalStorage;
        swLocalEnabled.Checked = local?.Enabled ?? false;
        etLocalPath.Text = local?.Path ?? "";
        etLocalMaxAge.Text = local?.MaxFileAgeDays.ToString() ?? "10";
        etLocalMaxStorage.Text = local?.MaxStorageSizeMb.ToString() ?? "2048";

        var ftp = s.Ftp;
        swFtpEnabled.Checked = ftp?.Enabled ?? false;
        etFtpHost.Text = ftp?.Host ?? "";
        etFtpLogin.Text = ftp?.Login ?? "";
        etFtpPassword.Text = ftp?.Password ?? "";
        etFtpDir.Text = ftp?.Directory ?? "";
        etFtpMaxAge.Text = ftp?.MaxFileAgeDays.ToString() ?? "10";
        etFtpMaxStorage.Text = ftp?.MaxStorageSizeMb.ToString() ?? "2048";
        swFtps.Checked = ftp?.UseFtps ?? false;

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
            var path = Android.Net.Uri.Decode(uri.LastPathSegment ?? "") ?? string.Empty;
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
        var localEnabled = swLocalEnabled?.Checked ?? false;
        var ftpEnabled = swFtpEnabled?.Checked ?? false;

        return new CameraRecorderSettings
        {
            RtspMainStreamUrl = etRtspMain?.Text ?? "",
            RtspSubStreamUrl = etRtspSub?.Text ?? "",
            RtspLogin = etRtspLogin?.Text ?? "",
            RtspPassword = etRtspPassword?.Text ?? "",
            LocalStorage = new LocalStorageSettings
            {
                Enabled = true,
                Path = etLocalPath?.Text ?? "",
                MaxFileAgeDays = int.TryParse(etLocalMaxAge?.Text, out var age) ? age : 10,
                MaxStorageSizeMb = int.TryParse(etLocalMaxStorage?.Text, out var size) ? size : 2048,
            },

            Ftp = new FtpSettings
            {
                Enabled = true,
                Host = etFtpHost?.Text ?? "",
                Login = etFtpLogin?.Text ?? "",
                Password = etFtpPassword?.Text ?? "",
                Directory = etFtpDir?.Text ?? "",
                UseFtps = swFtps?.Checked ?? false,
                MaxFileAgeDays = int.TryParse(etFtpMaxAge?.Text, out var ageF) ? ageF : 10,
                MaxStorageSizeMb = int.TryParse(etFtpMaxStorage?.Text, out var sizeF) ? sizeF : 2048,
            },

            PreMotionDurationSec = sbPreMotion?.Progress ?? 10,
            PostMotionDurationSec = sbPostMotion?.Progress ?? 10,
        };
    }
}
