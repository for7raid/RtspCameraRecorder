using Android.App;
using Android.OS;
using Android.Widget;

namespace CameraRecorderAndroidApp;

[Activity(Label = "Логи")]
public class LogsActivity : Activity
{
    private TextView? tvLogFile, tvLogContent;
    private Button? btnPrev, btnNext, btnDelete;
    private string[] _logFiles = [];
    private int _currentIndex;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_logs);

        tvLogFile = FindViewById<TextView>(Resource.Id.tvLogFile)!;
        tvLogContent = FindViewById<TextView>(Resource.Id.tvLogContent)!;
        btnPrev = FindViewById<Button>(Resource.Id.btnPrev)!;
        btnNext = FindViewById<Button>(Resource.Id.btnNext)!;
        btnDelete = FindViewById<Button>(Resource.Id.btnDelete)!;

        string logsDir = Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "logs");
        if (Directory.Exists(logsDir))
            _logFiles = Directory.GetFiles(logsDir, "*.txt").OrderByDescending(f => f).ToArray();

        if (_logFiles.Length > 0)
        {
            _currentIndex = 0;
            LoadFile();
        }
        else
        {
            tvLogContent.Text = "Нет файлов логов";
        }

        btnPrev.Click += (_, _) => { if (_logFiles.Length == 0) return; _currentIndex = (_currentIndex - 1 + _logFiles.Length) % _logFiles.Length; LoadFile(); };
        btnNext.Click += (_, _) => { if (_logFiles.Length == 0) return; _currentIndex = (_currentIndex + 1) % _logFiles.Length; LoadFile(); };
        btnDelete.Click += (_, _) => {
            if (_logFiles.Length == 0) return;
            var path = _logFiles[_currentIndex];
            new AlertDialog.Builder(this)
                .SetTitle("Удалить?")
                .SetMessage(Path.GetFileName(path))
                .SetPositiveButton("Удалить", (s, e) => {
                    File.Delete(path);
                    _logFiles = _logFiles.Where(f => f != path).ToArray();
                    if (_logFiles.Length == 0) { tvLogContent.Text = "Нет файлов логов"; tvLogFile.Text = "Логи"; }
                    else { _currentIndex = System.Math.Min(_currentIndex, _logFiles.Length - 1); LoadFile(); }
                })
                .SetNegativeButton("Отмена", (s, e) => { })
                .Show();
        };
    }

    private void LoadFile()
    {
        if (_logFiles.Length == 0) return;
        var path = _logFiles[_currentIndex];
        tvLogFile!.Text = System.IO.Path.GetFileName(path) + " (" + (_currentIndex + 1) + "/" + _logFiles.Length + ")";
        try { tvLogContent!.Text = File.ReadAllText(path); }
        catch { tvLogContent!.Text = "Ошибка чтения"; }
    }
}
