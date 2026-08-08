# SiNiSistar2 EDI Integration

SiNiSistar2 のゲーム内イベントを自動捕捉し、EDI 上のデバイスを 1 台ずつ独立した出力として制御する BepInEx 6 IL2CPP プラグインです。既定構成は `main`（A10 ピストン SA）、`breast-left` / `breast-right`（U.F.O TW 左右）の 3 出力で、出力の集合は `mappings.json` のデバイス台帳が決めます。[SPEC001](docs/specifications/SPEC001.md) を実装しています。

このリポジトリはゲームルートとして、そのまま動作確認できる構成を正本とします。BepInEx 6 IL2CPP、CoreCLR、SiNiSistar2 interop、プラグイン DLL、マッピング、EDI 設定、funscript をリポジトリ内に保持します。配布パッケージの生成は行いません。

## ドキュメント

| 文書 | 役割 |
|---|---|
| [SPEC001](docs/specifications/SPEC001.md) | 正本の仕様 |
| [実装トレーサビリティ](docs/implementation/SPEC001-traceability.md) | 要件と実装・テストの対応 |
| [テストシナリオ](docs/testing/SPEC001-test-scenarios.md) | 実機とゲーム内でしか確認できない手順 |

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

1. SPEC001 7.4 の改訂を適用した EDI（`Edi/Edi.exe`）を `http://127.0.0.1:5000` で起動します。
2. EDI がこのリポジトリの `Edi/EdiConfig.json` と `Edi/Gallery` を使用するようにします。
3. EDI 上で各デバイスを台帳どおりのチャンネルとバリアントへ 1 対 1 で割り当てます。
4. `SiNiSistar2.exe` を起動します。

プラグインは起動時に次の順で確認し、通らない限り出力しません。

1. **到達確認** — EDI が応答するまでバックオフ付きで再試行します。EDI 未起動は能力不足として扱いません。
2. **能力確認** — `GET /Edi/Info` で `StrictVariantResolution` と `StopClearsFiller` が有効かを確認します。無効なら誤配送が無警告になり停止も成立しないため、出力を有効化しません。
3. **束縛検証** — `GET /Devices` で、各出力のデバイス名・チャンネル・バリアント・準備状態が台帳と一致し、そのチャンネルに台帳外のデバイスが居ないことを確認します。成立しない出力だけを抑止し、理由を提示します。

一部のデバイスだけを接続した構成は正当です。抑止は出力単位で、他の出力の動作を止めません。EDI が停止してもゲーム側は継続し、復帰後は各出力の最新状態だけへ収束します。

デバイスを動かさないことは波形ではなく制御状態で表現します。「待機中で静か」と「他の出力のギャラリーを受け取っている」を区別できるようにするためで、静止波形のアセットはリポジトリに置きません。

## トリガー検出と funscript オーサリング

イベント識別子をユーザーが入力する必要はありません。捕捉は常時有効で、`hold`、`gallery`、`game-over`、`scripted-event` のトリガー遷移を記録します。

本作は 2D ピクセル表現で Humanoid アバターもボーンも持たず、演出は部位別スプライトのテクスチャ差し替えで構成されます。運動を計測して funscript を自動生成する前提が成立しないため、MOD は段階トリガーの検出に専念し、`.funscript` はオーサリング GUI でユーザーが作成します。根拠は [SPEC001 付録C](docs/specifications/SPEC001.md) を参照してください。

### トリガーカタログ

敵ごとに段数の異なる演出段階を、`context / actorId / animationId / phase / stageId` の5組で識別します。ギャラリーの take 配列は到達時点で全段階を列挙するため、まだ再生していない段階も先にオーサリングできます。

- 一次資料（追記専用 JSON Lines）: `BepInEx/diagnostics/community.sinisistar2.edi/sessions/{gameBuildId}/{sessionId}.animation.jsonl`
- トリガーカタログ: `BepInEx/diagnostics/community.sinisistar2.edi/catalog/{gameBuildId}/trigger-catalog.json`
- 網羅性レポート: `BepInEx/diagnostics/community.sinisistar2.edi/coverage.json`

セッションログはトリガー遷移と変化した UI テキストだけを記録します。フレーム単位の Transform・Animator 収集は行いません。カタログは再生可否を決めません。再生されるのは `mappings.json` で `mapped` になったトリガーだけです。

### オーサリング GUI

プラグインはループバック限定のローカル HTTP で GUI を配信します。既定は `http://127.0.0.1:5601/` で、`BepInEx/config/community.sinisistar2.edi.cfg` の `[Authoring] BaseUrl` で変更できます。GUI は認証を持たないため、ループバック以外を指定すると設定エラーになり GUI は起動しません（ゲームとデバイス制御は継続します）。

ゲームを起動したままブラウザで開き、段階を選んで波形を作成します。

- クリックで点を追加、ドラッグで移動、右クリックで削除
- 「クリップ長に合わせる」で編集長をゲームのクリップ長へ合わせる
- 「別の段階から複製」で作成済みの波形を複製して編集を開始
- 「▶ 試聴」は MOD の EDI クライアント経由でチャンネルを明示して再生し、ゲーム側の再生に優先します。停止するとその時点のゲーム状態へ復帰します
- 「保存して EDI へ登録」で `.funscript` を保存し、`Definitions.csv` を更新し、EDI へ再走査を要求し、`mappings.json` を原子的に更新します。ファイル転送は行いません

### filler の編集

GUI 上部の「filler（待機）」タブで、待機中に流れる波形を編集できます。filler はトリガーではなく `mappings.json` の `defaultFillers` と `statusRules` から名前で参照される固定ギャラリーなので、カタログとは別の一覧になります。

- `膨乳` などの状態異常が有効な間は、対応する専用 filler へ切り替わります
- 「別の段階から複製」で同じ出力を対象とする filler を複製できます。通常版を複製して強くするのが `filler-breast-swollen` の作り方です
- 長さを変更して保存すると `Edi/Gallery/Definitions.csv` の `EndTime` も自動更新されます。EDI は再生長をこの表から読むため、両方が揃っている必要があります
- 同一チャンネルの全バリアント（`ufo-left` と `ufo-right`）は同じ長さである必要があります。異なる場合は保存を拒否します
- 波形の長さと CSV の `EndTime` がずれている filler は一覧に警告を表示します

MOD は波形を生成・補間・推測しません。`ufo-left` と `ufo-right` は両方を作成する必要があり、片側の反転で他側を埋めることはありません。ループ対象の段階では、波形の終端とクリップ長の差が 1 フレーム相当（17ms）を超えると保存時に検証エラーになります。意図した差異はチェックボックスで承認でき、承認はマニフェストへ記録されます。

保存先は `Edi/Gallery/a10-main`、`Edi/Gallery/ufo-left`、`Edi/Gallery/ufo-right`、マニフェストは `BepInEx/diagnostics/community.sinisistar2.edi/generated/{gameBuildId}` です。EDI への登録が失敗した場合はマッピングを `mapped` に更新しないため、再生されることはありません。

カタログとマッピングはゲームビルドごとに分離します。ゲーム更新でハッシュが変わった場合は以前のカタログを混在させません。

## リポジトリ内の正本

- 実行プラグイン: `BepInEx/plugins/community.sinisistar2.edi`
- イベント／状態マッピング: `BepInEx/config/community.sinisistar2.edi/mappings.json`
- トリガーカタログ: `BepInEx/diagnostics/community.sinisistar2.edi/catalog`
- オーサリング GUI: `BepInEx/plugins/community.sinisistar2.edi/authoring`
- セッション時系列と保存マニフェスト: `BepInEx/diagnostics/community.sinisistar2.edi`
- EDI 設定: `Edi/EdiConfig.json`
- ギャラリー定義と funscript: `Edi/Gallery`
- 実装とテスト: `src`、`tests`
- 要件対応: [SPEC001 traceability](docs/implementation/SPEC001-traceability.md)
- 同梱ランタイムの出所: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
