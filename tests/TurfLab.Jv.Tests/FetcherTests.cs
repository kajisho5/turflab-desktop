using TurfLab.Jv;

namespace TurfLab.Jv.Tests;

/// <summary>台本どおりに答える偽の JV-Link。実機が無くても取得の流れを試せる。</summary>
sealed class FakeJvLink : IJvLink
{
    public JvOpenResult OpenResult = new(0, 1, 0, "20260101000000");
    public Queue<JvReadResult> Reads = new();
    public int StatusCalls;
    public bool Closed;
    public int Init(string id) => 0;
    public JvOpenResult Open(string s, string f, int o) => OpenResult;
    public int Status() { StatusCalls++; return 99; }
    public JvReadResult Read() => Reads.Count > 0 ? Reads.Dequeue() : new JvReadResult(0, "", "");
    public void Close() => Closed = true;
    public void Dispose() { }
    public FakeJvLink Rec(string text, string file = "f1") { Reads.Enqueue(new JvReadResult(text.Length, text + "\r\n", file)); return this; }
}

public sealed class FetcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "turflab-test-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "jv.sqlite");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // 架空のレコード（先頭2文字が種別）
    private const string RA = "RA7otter-fiction-race-0001";
    private const string SE = "SE7otter-fiction-horse-0001";
    private const string KS = "KS7otter-fiction-jockey-0001";

    [Fact]
    public void 種類を絞らなければ全種類を保存する()
    {
        using var store = new RecordStore(DbPath);
        var jv = new FakeJvLink().Rec(RA).Rec(SE).Rec(KS);
        var r = new Fetcher(jv, store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1);

        Assert.True(r.Ok);
        Assert.Equal(3, r.Stored);
        Assert.Equal(new Dictionary<string, long> { ["KS"] = 1, ["RA"] = 1, ["SE"] = 1 }, store.CountByKind());
        Assert.True(jv.Closed);
    }

    [Fact]
    public void 同じ内容は二重に保存しない()
    {
        using var store = new RecordStore(DbPath);
        var f = new Fetcher(new FakeJvLink().Rec(RA).Rec(SE), store, sleepMs: _ => { });
        f.Run("RACE", "20260101000000", 1);
        var r2 = new Fetcher(new FakeJvLink().Rec(RA).Rec(SE).Rec(KS), store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1);

        Assert.Equal(1, r2.Stored);
        Assert.Equal(2, r2.Duplicates);
    }

    [Fact]
    public void ファイル切替とダウンロード待ちを越えて読み続ける()
    {
        using var store = new RecordStore(DbPath);
        var jv = new FakeJvLink().Rec(RA);
        jv.Reads.Enqueue(new JvReadResult(-1, "", ""));
        jv.Reads.Enqueue(new JvReadResult(-3, "", ""));
        jv.Rec(SE);
        var r = new Fetcher(jv, store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1);

        Assert.True(r.Ok);
        Assert.Equal(2, r.Stored);
    }

    [Fact]
    public void データ種別が不正でも例外にせず理由を残す()
    {
        using var store = new RecordStore(DbPath);
        var jv = new FakeJvLink { OpenResult = new JvOpenResult(-111, 0, 0, "") };
        var r = new Fetcher(jv, store, sleepMs: _ => { }).Run("XXXX", "20260101000000", 1);

        Assert.False(r.Ok);
        Assert.Equal(-111, r.OpenRc);
        Assert.Contains("-111", r.Error);
        Assert.True(jv.Closed);
        Assert.Null(store.GetCursor("XXXX"));
    }

    [Fact]
    public void 新しいデータなし_は失敗扱いにしない()
    {
        using var store = new RecordStore(DbPath);
        var jv = new FakeJvLink { OpenResult = new JvOpenResult(-1, 0, 0, "") };
        var r = new Fetcher(jv, store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1);

        Assert.Null(r.Error);
        Assert.Equal(0, r.Stored);
    }

    [Fact]
    public void 読み取りエラーのときは起点を進めない()
    {
        using var store = new RecordStore(DbPath);
        var jv = new FakeJvLink().Rec(RA);
        jv.Reads.Enqueue(new JvReadResult(-402, "", ""));
        var r = new Fetcher(jv, store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1);

        Assert.False(r.Ok);
        Assert.Equal(1, r.Stored);          // 読めたぶんは残す
        Assert.Null(store.GetCursor("RACE")); // でも続きの起点は動かさない
    }

    [Fact]
    public void 正常終了なら次回の起点を保存する()
    {
        using var store = new RecordStore(DbPath);
        new Fetcher(new FakeJvLink().Rec(RA), store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1);
        Assert.Equal("20260101000000", store.GetCursor("RACE"));
    }

    [Fact]
    public void 日本語を含む記録がシフトJISのままで往復できる()
    {
        using var store = new RecordStore(DbPath);
        const string jp = "UM7ミズノアカリ架空馬";
        new Fetcher(new FakeJvLink().Rec(jp), store, sleepMs: _ => { }).Run("DIFN", "19000101000000", 4);
        Assert.Equal(1, store.CountByKind()["UM"]);
    }

    [Fact]
    public void 試し取りは指定件数で止まる()
    {
        using var store = new RecordStore(DbPath);
        var jv = new FakeJvLink().Rec(RA).Rec(SE).Rec(KS);
        var r = new Fetcher(jv, store, sleepMs: _ => { }).Run("RACE", "20260101000000", 1, maxRecords: 2);
        Assert.Equal(2, r.Stored);
    }

    [Fact]
    public void 一覧の全データ種別が重複なく並ぶ()
    {
        var names = SpecCatalog.All.Select(s => s.DataSpec).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains("RACE", names);
        Assert.Contains("MING", names);
    }
}
