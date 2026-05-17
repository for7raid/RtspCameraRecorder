using Java.Lang;

namespace CameraRecorderAndroidApp;

[Activity(Label = "Логи")]
public class LogsActivity : Activity
{
    private string[] _files = [];
    private string _dir = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_logs);

        var lv = FindViewById<ListView>(Resource.Id.lvLogFiles)!;
        _dir = Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "logs");

        if (Directory.Exists(_dir))
            _files = Directory.GetFiles(_dir, "*.txt").OrderByDescending(f => f).ToArray();

        if (_files.Length == 0)
        {
            lv.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1,
                new[] { "Нет файлов логов" });
        }
        else
        {
            lv.Adapter = new LogFileAdapter(this, _files);
        }

        lv.ItemClick += (_, e) =>
        {
            if (_files.Length == 0) return;
            var intent = new Android.Content.Intent(this, typeof(LogDetailActivity));
            intent.PutExtra("files", _files);
            intent.PutExtra("index", e.Position);
            StartActivity(intent);
        };
    }
}

class LogFileAdapter : BaseAdapter
{
    private readonly Activity _ctx;
    private readonly string[] _paths;

    public LogFileAdapter(Activity ctx, string[] paths) { _ctx = ctx; _paths = paths; }
    public override int Count => _paths.Length;
    public override Java.Lang.Object GetItem(int pos) => throw new NotImplementedException();
    public override long GetItemId(int pos) => pos;

    public override Android.Views.View GetView(int pos, Android.Views.View? convertView, Android.Views.ViewGroup? parent)
    {
        var view = convertView ?? _ctx.LayoutInflater.Inflate(Resource.Layout.item_log_file, parent, false)!;
        var tvName = view.FindViewById<TextView>(Resource.Id.tvFileName)!;
        var tvSize = view.FindViewById<TextView>(Resource.Id.tvFileSize)!;

        var path = _paths[pos];
        tvName.Text = Path.GetFileName(path);

        long len = new FileInfo(path).Length;
        tvSize.Text = len < 1024 ? len + " B"
                    : len < 1048576 ? (len / 1024.0).ToString("F1") + " KB"
                    : (len / 1048576.0).ToString("F1") + " MB";

        return view;
    }
}
