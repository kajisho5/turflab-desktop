namespace TurfLab.Jv;

/// <summary>確からしさの区分。「わからないものをわかったことにしない」ための印。</summary>
public enum Evidence
{
    /// <summary>実機（JV-Link）で、実際に返ってくることを確認した。</summary>
    MeasuredOnDevice,
    /// <summary>公式または資料に名前は出ているが、このプロジェクトでは実機確認していない。</summary>
    Documented,
    /// <summary>名前の記憶だけで根拠が取れていない。試して結果を記録する。</summary>
    Unverified,
}

/// <summary>取得するデータ種別（JVOpenの第1引数）と、その取り方。</summary>
public sealed record SpecEntry(string DataSpec, string Meaning, int SetupOption, int DailyOption, Evidence Evidence);

/// <summary>
/// 取りにいくデータ種別の一覧。ここに載せたものを全部試し、返ってきたレコード種別を全部保存する。
/// 対応表（どの種別がどのレコードを返すか）は決め打ちしない。返ってきたものを記録して後から確定する。
/// 出典: docs/DATA_SOURCES.md（実測）、JRA-VAN Developer の JVOpen 解説、nao_develop_note の実装例。
/// </summary>
public static class SpecCatalog
{
    public static IReadOnlyList<SpecEntry> All { get; } =
    [
        // ---- 名前が有効（-111にならない）と実測済み（docs/DATA_SOURCES.md 2026-07-19）。「意味」欄の※は推測で、返ったレコード種別で確定する ----
        new("RACE", "レース・出走馬・払戻・オッズ・票数など", 4, 1, Evidence.MeasuredOnDevice),
        new("TOKU", "特別登録馬", 1, 1, Evidence.MeasuredOnDevice),
        new("DIFF", "馬・騎手・調教師などのマスタ（差分）", 1, 1, Evidence.MeasuredOnDevice),
        new("BLOD", "血統（繁殖馬・産駒・系統）※意味は推測", 1, 1, Evidence.MeasuredOnDevice),
        new("MING", "データマイニング予想 ※意味は推測", 1, 1, Evidence.MeasuredOnDevice),
        new("SNAP", "出走別着度数 ※意味は推測", 1, 1, Evidence.MeasuredOnDevice),
        new("SLOP", "坂路調教", 4, 1, Evidence.MeasuredOnDevice),
        new("WOOD", "ウッドチップ調教", 4, 1, Evidence.MeasuredOnDevice),
        new("YSCH", "開催スケジュール ※意味は推測", 1, 1, Evidence.MeasuredOnDevice),
        new("HOSE", "競走馬市場取引価格 ※意味は推測", 1, 1, Evidence.MeasuredOnDevice),
        new("HOYU", "馬名の意味由来 ※意味は推測", 1, 1, Evidence.MeasuredOnDevice),
        new("COMM", "コース情報 ※意味は推測", 1, 1, Evidence.MeasuredOnDevice),

        // ---- セットアップ用の名前（資料に名前あり。DIFN は実機で UM 221,697件を取得済み） ----
        new("DIFN", "マスタ（セットアップ用）", 4, 1, Evidence.MeasuredOnDevice),
        new("BLDN", "血統（セットアップ用）", 4, 1, Evidence.Documented),
        new("SNPN", "出走別着度数（セットアップ用）", 4, 1, Evidence.Documented),

        // ---- 速報系は別の呼び出し（JVRTOpen）で、当日にしか取れない。v1の対象外。 ----
    ];
}
