# TurfLab デスクトップ（仮称）

JRA-VANのデータ会員が、自分のPCで中央競馬の予想を見るためのWindowsソフト。
設計は `turf-lab` リポの `docs/design/JRAVAN_APP_DESIGN.md`。

## いまの状態（2026-11-22）

作ったのは **取得部分だけ**（JV-Linkから全データ種別を取って、そのまま保存する）。予想の画面はまだ。

| 項目 | 状態 |
|---|---|
| 取得の流れ（読み取り・待ち・保存・重複除外・失敗の記録・続きの起点） | 作成済み。偽のJV-Linkで自動テスト10件が通る |
| 保存（SQLite、Shift-JISのまま、追記のみ） | 作成済み・テスト済み |
| 本物のJV-Linkを呼ぶ部分（`ComJvLink`） | 作成済み。**実機でまだ一度も動かしていない** |
| 32bit exeのビルド | この環境で作成できることを確認（PE32 x86） |
| 画面 | 未着手 |

## 作りの方針

- データ種別の一覧（`SpecCatalog`）を全部試し、**返ってきた記録は種類を絞らず全部保存**する。
  どの種別がどの記録を返すかは決め打ちせず、`probe` の結果で確定する。
- 「実機で確認済み」「資料にあるだけ」「推測」を `Evidence` で区別している。
- 取得に失敗しても例外で止めず、理由を `fetch_log` に残す。読み取りエラーのときは続きの起点を進めない。

## 使うバージョン（2026-11-22に公式で確認）

- .NET 10（長期サポート、2028年11月まで）— https://learn.microsoft.com/dotnet/core/releases-and-support
- Microsoft.Data.Sqlite 10.0.15（NuGet最新の10.0系）

## コマンド（32bitのWindowsで）

```
TurfLab.Cli probe            全データ種別を少しずつ試して何が取れるか表にする（最初に1回）
TurfLab.Cli all              全データ種別をセットアップ取得（時間がかかる）
TurfLab.Cli daily            前回の続きから差分取得
TurfLab.Cli count            保存済みの記録を種類ごとに数える
TurfLab.Cli fetch --spec RACE --from 20260101000000 --option 1
```

保存先の既定は `%LOCALAPPDATA%\TurfLab\jv.sqlite`（`--store` で変更）。

## ビルド

GitHub Actionsの `build`（Windows）で、テスト→32bit exe→zip。成果物は Actions の Artifacts から取れる。

## JRA-VANの規約について

取得データは本人の私的利用のみ（利用規約第7条）。このソフトはデータを外へ送らない。
ソフトとして売る場合のソフト登録・手数料・表示ルールは、JRA-VANへの確認が済むまで未確定。
