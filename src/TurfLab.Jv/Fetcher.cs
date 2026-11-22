namespace TurfLab.Jv;

/// <summary>1回の取得（1つのデータ種別）の結果。失敗しても例外にせず、ここに理由を残す。</summary>
public sealed record FetchReport(
    string DataSpec, int Option, string FromTime,
    DateTime StartedAtUtc, DateTime FinishedAtUtc,
    int OpenRc, int ReadCount, int DownloadCount,
    int Stored, int Duplicates,
    IReadOnlyDictionary<string, int> KindCounts,
    string LastFileTimestamp, string? Error)
{
    public bool Ok => Error is null && OpenRc == 0;
}

public sealed class Fetcher(IJvLink jv, RecordStore store, Func<DateTime>? clock = null, Action<int>? sleepMs = null)
{
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly Action<int> _sleep = sleepMs ?? (ms => Thread.Sleep(ms));

    /// <summary>1バッチに溜める件数（SQLiteへまとめて書く）。</summary>
    private const int BatchSize = 5000;
    /// <summary>ダウンロード待ちの最大回数（約15分）。</summary>
    private const int MaxDownloadWaits = 900;
    /// <summary>読み取り中に「ダウンロード中」が続く最大回数。</summary>
    private const int MaxReadWaits = 7200;

    /// <summary>
    /// kinds が null なら全種類を保存する（この製品の既定）。
    /// maxRecords は試し取り用（0で無制限）。
    /// </summary>
    public FetchReport Run(string dataSpec, string fromTime, int option, ISet<string>? kinds = null, int maxRecords = 0, CancellationToken ct = default)
    {
        var started = _now();
        var counts = new SortedDictionary<string, int>();
        int stored = 0, dup = 0, readCount = 0, dlCount = 0;
        string lastTs = "";
        string? error = null;
        int openRc = 0;

        try
        {
            var open = jv.Open(dataSpec, fromTime, option);
            openRc = open.Rc;
            readCount = open.ReadCount;
            dlCount = open.DownloadCount;
            lastTs = open.LastFileTimestamp;
            if (open.Rc != 0)
            {
                // -1 は「新しいデータが無い」。失敗ではないが保存するものも無い。
                error = open.Rc == -1 ? null : $"JVOpen rc={open.Rc}";
                return Finish();
            }

            for (int i = 0; i < MaxDownloadWaits && dlCount > 0; i++)
            {
                ct.ThrowIfCancellationRequested();
                int st = jv.Status();
                if (st < 0 || st >= dlCount) break;
                _sleep(1000);
            }

            var batch = new List<(string Kind, string File, string Text)>(BatchSize);
            int waits = 0, total = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var r = jv.Read();
                if (r.Rc == 0) break;
                if (r.Rc == -1) continue;
                if (r.Rc == -3)
                {
                    if (++waits > MaxReadWaits) { error = "ダウンロード待ちが長すぎるため中断"; break; }
                    _sleep(500);
                    continue;
                }
                if (r.Rc < 0) { error = $"JVRead rc={r.Rc}"; break; }

                waits = 0;
                var text = (r.Text ?? "").TrimEnd('\r', '\n');
                if (text.Length < 2) continue;
                var kind = text[..2];
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                if (kinds is { Count: > 0 } && !kinds.Contains(kind)) continue;

                batch.Add((kind, r.FileName, text));
                total++;
                if (batch.Count >= BatchSize) { Flush(batch); }
                if (maxRecords > 0 && total >= maxRecords) break;
            }
            Flush(batch);

            void Flush(List<(string Kind, string File, string Text)> b)
            {
                if (b.Count == 0) return;
                var (s, d) = store.AddRange(dataSpec, b, _now());
                stored += s; dup += d;
                b.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            error = "中断されました";
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
        }
        finally
        {
            try { jv.Close(); } catch { /* 閉じ損ねは無視（次回 Open で解消） */ }
        }
        return Finish();

        FetchReport Finish()
        {
            var rep = new FetchReport(dataSpec, option, fromTime, started, _now(), openRc, readCount, dlCount,
                stored, dup, counts, lastTs, error);
            store.AddLog(rep);
            // 正常に最後まで読めたときだけ、次回の差分取得の起点を進める。
            if (rep.Ok && !string.IsNullOrEmpty(lastTs)) store.SetCursor(dataSpec, lastTs);
            return rep;
        }
    }
}
