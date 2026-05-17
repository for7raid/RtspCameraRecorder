using Android.App;
using Android.OS;
using Android.Widget;

namespace CameraRecorderAndroidApp;

[Activity(Label = "Просмотр лога")]
public class LogDetailActivity : Activity
{
    private TextView? tvName, tvContent;
    private Button? btnPrev, btnNext;
    private string[] _files = [];
    private int _index;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_log_detail);

        tvName = FindViewById<TextView>(Resource.Id.tvLogFileName)!;
        tvContent = FindViewById<TextView>(Resource.Id.tvLogContent)!;
        btnPrev = FindViewById<Button>(Resource.Id.btnPrevFile)!;
        btnNext = FindViewById<Button>(Resource.Id.btnNextFile)!;

        _files = Intent!.GetStringArrayExtra("files") ?? [];
        _index = Intent.GetIntExtra("index", 0);

        LoadFile();

        btnPrev.Click += (_, _) => { if (_files.Length == 0) return; _index = (_index - 1 + _files.Length) % _files.Length; LoadFile(); };
        btnNext.Click += (_, _) => { if (_files.Length == 0) return; _index = (_index + 1) % _files.Length; LoadFile(); };
    }

    private void LoadFile()
    {
        if (_files.Length == 0) { tvContent!.Text = "Нет файлов"; return; }
        var path = _files[_index];
        tvName!.Text = Path.GetFileName(path) + " (" + (_index + 1) + "/" + _files.Length + ")";
        try { tvContent!.Text = File.ReadAllText(path); }
        catch { tvContent!.Text = "Ошибка чтения"; }
    }
}
