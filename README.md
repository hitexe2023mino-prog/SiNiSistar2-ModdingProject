# SiNiSistar2 EDI Integration

SiNiSistar2 のゲーム内イベントを自動捕捉し、EDI の `main` / `breast` チャンネルを制御する BepInEx 6 IL2CPP プラグインです。[SPEC001](docs/specifications/SPEC001.md) を実装しています。

このリポジトリはゲームルートとして、そのまま動作確認できる構成を正本とします。BepInEx 6 IL2CPP、CoreCLR、SiNiSistar2 interop、プラグイン DLL、マッピング、EDI 設定、funscript をリポジトリ内に保持します。配布パッケージの生成は行いません。

## ビルド

.NET 8 SDK から次のコマンドだけでビルドできます。外部の `BepInExRoot` は不要です。

```powershell
dotnet test .\tests\SiNiSistar2.Edi.Core.Tests\SiNiSistar2.Edi.Core.Tests.csproj -c Release
dotnet build .\SiNiSistar2.Edi.sln -c Release
```

プラグインのビルド完了時に、次の DLL が自動的に実行位置へ更新されます。

- `BepInEx/plugins/community.sinisistar2.edi/SiNiSistar2.Edi.Plugin.dll`
- `BepInEx/plugins/community.sinisistar2.edi/SiNiSistar2.Edi.Core.dll`

## 実行

1. EDI を `http://127.0.0.1:5000` で起動します。
2. EDI がこのリポジトリの `Edi/EdiConfig.json` と `Edi/Gallery` を使用するようにします。
3. `SiNiSistar2.exe` を起動します。

プラグインは EDI に `main` と `breast` の両方が存在するまで出力しません。EDI が停止していてもゲーム側は継続し、復帰後は各チャンネルの最新状態だけへ収束します。

## 実測アニメーションの自動捕捉とfunscript生成

イベント識別子をユーザーが入力する必要はありません。捕捉は常時有効で、`hold`、`gallery`、`game-over`、`scripted-event` の各イベント中にAnimator評価後の全フレームを記録します。全レイヤー、クリップ、状態、遷移、root motion、全子Transform、Humanoidボーン、状態異常、取得可能なUIテキストが対象です。

一次資料は次の追記専用JSON Linesです。ゲームビルドと起動セッションごとに蓄積し、自動削除しません。

- `BepInEx/diagnostics/community.sinisistar2.edi/sessions/{gameBuildId}/{sessionId}.animation.jsonl`

次の2ファイルは検索・網羅性確認用の派生カタログです。funscript生成の時系列正本ではありません。

- `BepInEx/diagnostics/community.sinisistar2.edi/coverage.json`
- `BepInEx/diagnostics/community.sinisistar2.edi/mapping-candidates.json`

完全なイベント区間が得られると、対象部位と基準部位の相対変位を主運動軸へ射影し、実測時刻と実測位置だけから0〜100の `.funscript` を生成します。normalizedTimeから正弦波・三角波を作ることや、左右の片側信号を反転して捏造することはありません。静止、欠番、低い主軸集中度、ループ境界不連続などがある場合は生成を拒否し、マニフェストへ理由を残します。

合格した生成物は `Edi/Gallery/a10-main`、`Edi/Gallery/ufo-left`、`Edi/Gallery/ufo-right` に保存され、EDI Assets APIへ登録後、`BepInEx/config/community.sinisistar2.edi/generated-mappings.json` に自動登録されます。未知ループは完全な1ループを初回計測した後の反復から、未知非ループは次回発生から再生できます。EDIは未来の動的コンテンツを再生できないため、未観測の初回区間を予測波形で代用しません。

同じゲームビルドの候補は起動をまたいで自動的に累積されます。ゲーム更新でハッシュが変わった場合は以前の候補を混在させません。

## リポジトリ内の正本

- 実行プラグイン: `BepInEx/plugins/community.sinisistar2.edi`
- イベント／状態マッピング: `BepInEx/config/community.sinisistar2.edi/mappings.json`
- 実測から作られた自動マッピング: `BepInEx/config/community.sinisistar2.edi/generated-mappings.json`
- セッション時系列と生成マニフェスト: `BepInEx/diagnostics/community.sinisistar2.edi`
- EDI 設定: `Edi/EdiConfig.json`
- ギャラリー定義と funscript: `Edi/Gallery`
- 実装とテスト: `src`、`tests`
- 要件対応: [SPEC001 traceability](docs/implementation/SPEC001-traceability.md)
- 同梱ランタイムの出所: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
