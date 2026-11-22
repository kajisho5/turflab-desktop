using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TurfLab.Jv;

/// <summary>
/// 取得したレコードをそのまま（Shift-JISのバイト列で）保存する。
/// 追記だけで上書きしない。同じ内容は二重に入れない（内容のハッシュで判定）。
/// </summary>
public sealed class RecordStore : IDisposable
{
    private readonly SqliteConnection _db;
    private static readonly Encoding Sjis;

    static RecordStore()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Sjis = Encoding.GetEncoding(932);
    }

    public RecordStore(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS records(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              kind TEXT NOT NULL,
              spec TEXT NOT NULL,
              file TEXT NOT NULL,
              fetched_at TEXT NOT NULL,
              hash TEXT NOT NULL UNIQUE,
              raw BLOB NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_records_kind ON records(kind);
            CREATE TABLE IF NOT EXISTS fetch_log(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              spec TEXT NOT NULL, option INTEGER NOT NULL, from_time TEXT NOT NULL,
              started_at TEXT NOT NULL, finished_at TEXT NOT NULL,
              open_rc INTEGER NOT NULL, read_count INTEGER NOT NULL, download_count INTEGER NOT NULL,
              stored INTEGER NOT NULL, duplicates INTEGER NOT NULL,
              kinds TEXT NOT NULL, last_ts TEXT NOT NULL, error TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS cursor(spec TEXT PRIMARY KEY, last_ts TEXT NOT NULL);
            """);
    }

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    /// <summary>レコードをまとめて保存。戻り値は (新規, 重複)。</summary>
    public (int Stored, int Duplicates) AddRange(string spec, IEnumerable<(string Kind, string File, string Text)> records, DateTime fetchedAtUtc)
    {
        int stored = 0, dup = 0;
        using var tx = _db.BeginTransaction();
        using var c = _db.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT OR IGNORE INTO records(kind,spec,file,fetched_at,hash,raw) VALUES($k,$s,$f,$t,$h,$r)";
        var pk = c.Parameters.Add("$k", SqliteType.Text);
        var ps = c.Parameters.Add("$s", SqliteType.Text);
        var pf = c.Parameters.Add("$f", SqliteType.Text);
        var pt = c.Parameters.Add("$t", SqliteType.Text);
        var ph = c.Parameters.Add("$h", SqliteType.Text);
        var pr = c.Parameters.Add("$r", SqliteType.Blob);
        var ts = fetchedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        foreach (var (kind, file, text) in records)
        {
            var raw = Sjis.GetBytes(text);
            pk.Value = kind; ps.Value = spec; pf.Value = file; pt.Value = ts;
            ph.Value = Convert.ToHexString(SHA256.HashData(raw));
            pr.Value = raw;
            if (c.ExecuteNonQuery() == 1) stored++; else dup++;
        }
        tx.Commit();
        return (stored, dup);
    }

    public void AddLog(FetchReport r)
    {
        using var c = _db.CreateCommand();
        c.CommandText = """
            INSERT INTO fetch_log(spec,option,from_time,started_at,finished_at,open_rc,read_count,download_count,stored,duplicates,kinds,last_ts,error)
            VALUES($spec,$opt,$from,$st,$fin,$rc,$rd,$dl,$sto,$dup,$kinds,$ts,$err)
            """;
        c.Parameters.AddWithValue("$spec", r.DataSpec);
        c.Parameters.AddWithValue("$opt", r.Option);
        c.Parameters.AddWithValue("$from", r.FromTime);
        c.Parameters.AddWithValue("$st", r.StartedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        c.Parameters.AddWithValue("$fin", r.FinishedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        c.Parameters.AddWithValue("$rc", r.OpenRc);
        c.Parameters.AddWithValue("$rd", r.ReadCount);
        c.Parameters.AddWithValue("$dl", r.DownloadCount);
        c.Parameters.AddWithValue("$sto", r.Stored);
        c.Parameters.AddWithValue("$dup", r.Duplicates);
        c.Parameters.AddWithValue("$kinds", string.Join(",", r.KindCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        c.Parameters.AddWithValue("$ts", r.LastFileTimestamp);
        c.Parameters.AddWithValue("$err", r.Error ?? "");
        c.ExecuteNonQuery();
    }

    public string? GetCursor(string spec)
    {
        using var c = _db.CreateCommand();
        c.CommandText = "SELECT last_ts FROM cursor WHERE spec=$s";
        c.Parameters.AddWithValue("$s", spec);
        return c.ExecuteScalar() as string;
    }

    public void SetCursor(string spec, string lastTs)
    {
        using var c = _db.CreateCommand();
        c.CommandText = "INSERT INTO cursor(spec,last_ts) VALUES($s,$t) ON CONFLICT(spec) DO UPDATE SET last_ts=$t";
        c.Parameters.AddWithValue("$s", spec);
        c.Parameters.AddWithValue("$t", lastTs);
        c.ExecuteNonQuery();
    }

    /// <summary>保存済みレコードの種別ごとの件数。</summary>
    public IReadOnlyDictionary<string, long> CountByKind()
    {
        var d = new SortedDictionary<string, long>();
        using var c = _db.CreateCommand();
        c.CommandText = "SELECT kind, COUNT(*) FROM records GROUP BY kind";
        using var r = c.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetInt64(1);
        return d;
    }

    public void Dispose() => _db.Dispose();
}
