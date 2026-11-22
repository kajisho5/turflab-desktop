namespace TurfLab.Jv;

/// <summary>JVOpen の結果。Rc が 0 なら成功、-1 は「該当データなし」、-111 は「データ種別の指定が不正」。</summary>
public sealed record JvOpenResult(int Rc, int ReadCount, int DownloadCount, string LastFileTimestamp);

/// <summary>
/// JVRead の結果。Rc の意味（実機で確認済みの扱い）:
/// 正の数＝1レコード読めた / 0＝全部読み終わり / -1＝ファイルの切り替わり / -3＝ダウンロード中で待つ / それ以外の負＝エラー。
/// </summary>
public sealed record JvReadResult(int Rc, string Text, string FileName);

/// <summary>JV-Link の呼び出し口。本物（COM）と試験用の偽物を差し替えられるようにしてある。</summary>
public interface IJvLink : IDisposable
{
    int Init(string softwareId);
    JvOpenResult Open(string dataSpec, string fromTime, int option);
    int Status();
    JvReadResult Read();
    void Close();
}
