using TurfLab.Jv;
using TurfLab.Jv.Com;

// 使い方:
//   TurfLab.Cli probe [--max 200]                 全データ種別を少しずつ試し、何が取れるか表にして記録する（保存は --store の中に）
//   TurfLab.Cli fetch --spec RACE --from 20260101000000 --option 1 [--store 場所]
//   TurfLab.Cli all   [--from 19900101000000]      一覧の全データ種別をセットアップ取得（時間がかかる）
//   TurfLab.Cli daily                              前回の続きから差分取得（全種別）
//   TurfLab.Cli count                              保存済みレコードの種別ごとの件数

var opts = ParseArgs(args);
if (opts.Cmd is null or "-h" or "--help") { PrintHelp(); return 0; }

var storePath = opts.Get("store") ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TurfLab", "jv.sqlite");
using var store = new RecordStore(storePath);

if (opts.Cmd == "count")
{
    foreach (var (k, n) in store.CountByKind()) Console.WriteLine($"{k}\t{n}");
    return 0;
}

using var jv = new ComJvLink();
int rc = jv.Init("TURFLAB");
if (rc != 0) { Console.Error.WriteLine($"JVInit に失敗 rc={rc}"); return 2; }
var fetcher = new Fetcher(jv, store);

switch (opts.Cmd)
{
    case "probe":
    {
        int max = int.Parse(opts.Get("max") ?? "200");
        Console.WriteLine("データ種別\t取得方法\tJVOpen\t読めた件数\t記録の種類");
        foreach (var s in SpecCatalog.All)
        {
            var from = s.SetupOption == 1 ? Now().AddDays(-30).ToString("yyyyMMdd") + "000000" : "20260101000000";
            var r = fetcher.Run(s.DataSpec, from, s.SetupOption, null, max);
            Console.WriteLine($"{s.DataSpec}\toption{s.SetupOption}\t{Describe(r)}\t{r.Stored + r.Duplicates}\t{Kinds(r)}");
        }
        return 0;
    }
    case "fetch":
    {
        var spec = opts.Get("spec") ?? throw new ArgumentException("--spec が必要です");
        var from = opts.Get("from") ?? "19000101000000";
        var option = int.Parse(opts.Get("option") ?? "1");
        var r = fetcher.Run(spec, from, option);
        Console.WriteLine($"{spec}\t{Describe(r)}\t新規{r.Stored}\t重複{r.Duplicates}\t{Kinds(r)}");
        return r.Ok || r.OpenRc == -1 ? 0 : 1;
    }
    case "all":
    {
        var from = opts.Get("from") ?? "19900101000000";
        foreach (var s in SpecCatalog.All)
        {
            var r = fetcher.Run(s.DataSpec, s.SetupOption == 1 ? "19000101000000" : from, s.SetupOption);
            Console.WriteLine($"{s.DataSpec}\t{Describe(r)}\t新規{r.Stored}\t重複{r.Duplicates}\t{Kinds(r)}");
        }
        return 0;
    }
    case "daily":
    {
        foreach (var s in SpecCatalog.All.Where(s => s.DataSpec is not ("DIFN" or "BLDN" or "SNPN")))
        {
            var from = store.GetCursor(s.DataSpec) ?? Now().AddDays(-7).ToString("yyyyMMdd") + "000000";
            var r = fetcher.Run(s.DataSpec, from, s.DailyOption);
            Console.WriteLine($"{s.DataSpec}\t{Describe(r)}\t新規{r.Stored}\t重複{r.Duplicates}");
        }
        return 0;
    }
    default:
        PrintHelp();
        return 1;
}

static DateTime Now() => DateTime.Now;
static string Describe(FetchReport r) =>
    r.Error is not null ? $"失敗({r.Error})" : r.OpenRc == -1 ? "新しいデータなし(-1)" : r.OpenRc == 0 ? "OK" : $"rc={r.OpenRc}";
static string Kinds(FetchReport r) => string.Join(" ", r.KindCounts.Select(k => $"{k.Key}={k.Value}"));
static void PrintHelp() => Console.WriteLine("使い方: probe | fetch --spec X --from T --option N | all | daily | count   （--store 保存先）");

static Opts ParseArgs(string[] a)
{
    var o = new Opts { Cmd = a.Length > 0 ? a[0] : null };
    for (int i = 1; i < a.Length; i++)
        if (a[i].StartsWith("--") && i + 1 < a.Length) o.Values[a[i][2..]] = a[++i];
    return o;
}

sealed class Opts
{
    public string? Cmd;
    public Dictionary<string, string> Values = [];
    public string? Get(string k) => Values.TryGetValue(k, out var v) ? v : null;
}
