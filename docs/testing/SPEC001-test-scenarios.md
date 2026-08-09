# SPEC001 テストシナリオ

| 項目 | 内容 |
|---|---|
| 正本 | [`docs/specifications/SPEC001.md`](../specifications/SPEC001.md)（2026-08-09改訂、CHG-014〜CHG-040） |
| 実装状況 | [`docs/implementation/SPEC001-traceability.md`](../implementation/SPEC001-traceability.md) |
| 作成日 | 2026-08-06 |
| 対象 | schemaVersion 2 / 出力単位制御への改訂後の構成 |
| 想定実施者 | 実機を接続できる運用者 |

この文書は検証手順だけを定める。仕様を変更しない。手順と期待結果が食い違う場合、正しいのは正本であり、この文書か実装のどちらかが誤っている。

## 1. この文書の範囲

自動テストは MOD 側 172 件、EDI 側 135 件が通っている。ここに書くのは**自動テストでは原理的に確認できないもの**に限る。

| 確認できないもの | 理由 |
|---|---|
| 物理デバイスが実際に動く/止まる | ハードウェアが要る |
| EDI 側のデバイス割り当てが崩れたときの実挙動 | Intiface と実デバイスの接続状態に依存する |
| ゲーム内でのトリガー発生と状態異常の同時成立 | ゲームプレイが要る |
| 起動順序（EDI 未起動からの復帰） | プラグイン層にテストハーネスがない |
| 移行手順そのもの | 一度きりの作業 |

以下は**再実施しなくてよい**。自動テストが毎回確認している。

| 観点 | 自動テスト |
|---|---|
| filler 選択の優先度と曖昧さ検出 | `HighestPriorityRuleWinsWhenSeveralStatusesAreActiveAtOnce`、`SilencingAndPlayingAtTheSamePriorityIsAmbiguous` |
| 出力単位の独立性（ルール層） | `ParasiteAndSwollenBreastApplyTogetherWithoutOverwritingEachOther` |
| まとめ送信が 1 リクエストになること | `OutputsSharingAPayloadAreSentAsOneRequest`、`GroupedOutputsAreSentAsOneCommaSeparatedRequest` |
| 束縛検証の判定ロジック | `BindingAndCapabilityTests` 全件 |
| 静止波形がリポジトリに無いこと | `NoAssetExpressesStillnessAsAWaveform` |
| ギャラリーとバリアントの対応 | `EveryGalleryVariantBelongsToAnOutputInTheRoster` |
| schemaVersion 1 の拒否 | `SchemaVersionOneIsRefusedWithAPointerToTheMigration` |
| EDI 既定値が改訂前と同じであること | Edi.Core.Tests 130 件（改訂前から無変更） |

## 2. 前提環境

1. `Edi/Edi.exe` が SPEC001 7.4 を適用したビルドである。`GET /Edi/Info` が応答すれば適用済み。
2. `Edi/EdiConfig.json` と `Edi/Gallery/EdiConfig.json` の両方に 7.4 の設定がある。EDI は選択されたフォルダから設定を読むため、両方が要る。
3. Intiface Central が起動し、A10 ピストン SA と U.F.O TW が接続できる。
4. ログの参照先
   - MOD: `BepInEx/LogOutput.log`
   - EDI: `Edi/Edilog{yyyyMMdd}.txt`

### 期待する初期構成

| 出力ID | EDIデバイス名 | バリアント |
|---|---|---|
| `main` | `Vorze Piston` | `a10-main` |
| `breast-left` | `Vorze UFO TW Rotate: 1` | `ufo-left` |
| `breast-right` | `Vorze UFO TW Rotate: 2` | `ufo-right` |

`unassigned` チャンネルは台帳に無い。台帳外デバイスの置き場であり、MOD は一切送信しない。

## 3. 実施順序

TS-101 〜 TS-104（起動と能力）→ TS-201 〜 TS-206（束縛）→ TS-301 〜 TS-306（再生）→ TS-401 〜 TS-403（停止）→ TS-501 〜 TS-504（オーサリング）→ TS-601 〜 TS-602（回帰）。

前段が通らないうちに後段を実施しない。束縛が成立していない出力は何も送信されないため、再生シナリオは必ず失敗する。

---

## 4. 起動と能力ネゴシエーション

### TS-101 正常起動で全出力が有効化される

**対応**: FR-042、FR-052、AC-002
**事前条件**: 3 デバイスすべて接続済み、EDI 設定は §2 のとおり。
**手順**
1. Intiface Central → EDI → ゲームの順に起動する。
2. `BepInEx/LogOutput.log` を開く。

**期待結果**
- `EDI reports version=…, strictVariantResolution=True, stopClearsFiller=True, unassignedDeviceChannel=unassigned.` が出る。
- `EDI re-read the gallery root; fillers in the mapping: filler-breast, filler-breast-swollen, filler-main, filler-main-parasite.` が出る。
- 3 行の `Output '…' is bound; playback enabled for it.` が出る。
- `suppressed`、`still unbound`、`does not offer the channel(s)` のいずれも出ない。

**失敗時に記録するもの**: 上記ログ行の実際の文言、`GET /Devices` の応答全文。

---

### TS-102 能力が無効な EDI では出力を有効化しない

**対応**: FR-052、AC-048
**事前条件**: TS-101 が通っている。
**手順**
1. ゲームと EDI を終了する。
2. `Edi/EdiConfig.json` と `Edi/Gallery/EdiConfig.json` の `StopClearsFiller` を `false` にする。
3. EDI とゲームを起動する。

**期待結果**
- `EDI has StopClearsFiller disabled, so Stop replays the retained filler instead of stopping the device. FR-019 and FR-045 cannot hold; enable it in Edi/EdiConfig.json.` が **Error** で出る。
- `Playback stays disabled for this session until EDI provides these capabilities.` が出る。
- 束縛検証のログが**一切出ない**（能力確認で止まっている）。
- デバイスが動かない。ゲームは通常どおり進行する。

**後始末**: `StopClearsFiller` を `true` へ戻す。`StrictVariantResolution` でも同じ手順を実施し、`FR-050` を含むメッセージが出ることを確認する。

---

### TS-103 7.4 未適用の EDI を検出する

**対応**: FR-052、AC-049
**手順**
1. ゲームと EDI を終了する。
2. `Edi/Edi.exe` を `Edi/Edi.exe.bak-pre-spec001-v2` で置き換える（元のファイルは別名で保管する）。
3. EDI とゲームを起動する。

**期待結果**
- `EDI has no GET /Edi/Info, so it does not carry the changes SPEC001 7.4 requires.` が Error で出る。
- デバイス出力が有効化されない。
- 同梱ギャラリーの再走査要求が実行されない。

**後始末**: 7.4 適用版の `Edi.exe` へ戻す。

---

### TS-104 EDI 未起動からの復帰

**対応**: FR-015、AC-051
**手順**
1. EDI を起動せずにゲームを起動する。
2. ログに再試行が出ることを確認する。
3. 30 秒ほど待ってから EDI を起動する。

**期待結果**
- `EDI is not reachable yet; playback stays fail-closed and the MOD keeps retrying: …` が **1 回だけ**出る（毎回は出ない）。
- 到達不能が「能力不足」として扱われない。`EDI has no GET /Edi/Info` は出ない。
- EDI 起動後、能力確認 → 束縛検証の順に進み、出力が有効化される。
- ゲームは待たされない。フレームが止まらないこと。

---

## 5. 束縛検証

### TS-201 一部デバイスのみ接続

**対応**: FR-042、AC-035
**手順**
1. ピストンだけを接続し、U.F.O TW は接続しない。
2. ゲームを起動する。

**期待結果**
- `Output 'main' is bound; playback enabled for it.` が出る。
- `breast-left` と `breast-right` について `is suppressed: EDI reports no device named '…'; it reports 'Vorze Piston'` が **1 回だけ**出る。
- ピストンは filler で動く。
- 60 秒後に `Output(s) breast-left, breast-right were still unbound after 60s.` が出て、再確認が止まる。

---

### TS-202 ピストンを乳房側チャンネルへ誤割当

**対応**: FR-042、AC-041。**改訂前に障害を起こした構成そのもの**
**手順**
1. 3 デバイス接続後、EDI の UI でピストンのチャンネルを `breast-left` に変更する。
2. ゲームを起動する。

**期待結果**
- `main` が抑止され、理由に `device 'Vorze Piston' is on channel 'breast-left' but output 'main' requires channel 'main'` を含む。
- `breast-left` も抑止され、理由に `channel 'breast-left' also holds device(s) 'Vorze Piston' that the roster does not name` を含む。
- `breast-right` は**有効なまま**で、U.F.O TW 右は動作を続ける。
- ピストンは動かない。乳房側の波形で動くことは**ない**。

**後始末**: ピストンのチャンネルを `main` へ戻す。

---

### TS-203 台帳外のデバイスを接続する

**対応**: FR-042、7.4 E4、AC-053
**手順**
1. 3 デバイスに加えて、無関係な Buttplug 対応デバイスを Intiface へ接続する。
2. ゲームを起動する。

**期待結果**
- 追加デバイスが EDI 上で `unassigned` チャンネルへ入る（`GET /Devices` で確認）。
- 台帳の 3 出力すべてが有効なまま。
- 追加デバイスは一切動かない。

**この確認の意味**: `UnassignedDeviceChannel` が無いと追加デバイスは `main` へ入り、TS-202 と同じ理由で `main` が抑止される。設定が効いていることの確認である。

---

### TS-204 デバイス名の一意化を検出する

**対応**: FR-054、AC-050
**手順**
1. ゲーム起動中に U.F.O TW の電源を切り、EDI がデバイスを解放しきる前に入れ直す。
2. `GET /Devices` でデバイス名を確認する。

**期待結果**
- 名前が `Vorze UFO TW Rotate: 1 (1)` のように変わっていれば、EDI ログに `Device 'Vorze UFO TW Rotate: 1' was already loaded, so the new one was renamed to …` が出る。
- MOD ログの抑止理由に `EDI reports no device named 'Vorze UFO TW Rotate: 1', but it does report 'Vorze UFO TW Rotate: 1 (1)'. EDI renames a device when it is re-added before the previous one was released.` を含む。

**注**: 一意化は必ず起きるわけではない。解放が先に完了すると再現しない。再現しなかった場合はその旨を記録し、失敗としない。

---

### TS-205 再生中に束縛を失うとデバイスが止まる

**対応**: FR-055、AC-052。**この確認が本命**
**手順**
1. 全出力が有効な状態で、filler が再生されデバイスが動いていることを確認する。
2. ゲームを起動したまま、EDI の UI で U.F.O TW 左のバリアントを `ufo-right` に変更する（束縛を壊す）。
3. 60 秒の確認期間内に再検証が走るのを待つ。

**期待結果**
- `Output 'breast-left' lost its binding and was stopped: device 'Vorze UFO TW Rotate: 1' has variant 'ufo-right' but output 'breast-left' requires 'ufo-left'` が出る。
- **U.F.O TW 左が実際に停止する。** 直前のギャラリーをループし続けない。
- U.F.O TW 右とピストンは動作を続ける。

**改訂前との差**: 旧実装は送信を止めるだけだったため、デバイスは最後の指示を回し続けた。

---

### TS-206 台帳と EDI チャンネル一覧の不一致

**対応**: 7.1
**手順**
1. `Edi/EdiConfig.json` の `Channels` から `breast-right` を削除する。
2. EDI とゲームを起動する。

**期待結果**
- `EDI does not offer the channel(s) breast-right that the device roster declares, so no device can be assigned to them.` を含む警告が出る。

**後始末**: `Channels` を戻したうえで、**EDI でこのリポジトリのギャラリーフォルダを選択し直す**。

**重要**: EDI がチャンネル集合を作るのは `Edi.Init` / ゲーム選択のときだけで、`POST /Edi/Reload` は意図的にチャンネルを再構成しない（7.4 E3）。EDI を再起動しても保存済みの選択が復元されるだけで `Channels` は読み直されない。`Channels` を編集したら、必ず一度ゲームフォルダを選択し直すこと。2026-08-06 の実測では、これを行わなかったため EDI が `main` しか持たず、`breast-left` / `breast-right` はデバイスを接続しても束縛できない状態だった。

---

## 6. 再生（元の障害の再現確認）

### TS-301 寄生系デバフ単独

**対応**: FR-010、FR-043
**手順**
1. ゲーム内で寄生系デバフ（`Parasite`、`FrogEgg` など）だけを受ける。
2. トリガーが発生していない待機状態にする。

**期待結果**
- ピストンが `filler-main-parasite` で動く。既定の `filler-main` より**弱く遅い**。
- U.F.O TW 左右は `filler-breast` のまま。動きが変わらない。

---

### TS-307 拘束が取りこぼされない ★

**対応**: FR-058、FR-059、AC-056、AC-057（2026-08-09 の不具合報告「拘束されたタイミングで黄色文字のEDIキャッチ表示がなかった」）

**手順**
1. `BepInEx/diagnostics/community.sinisistar2.edi/catalog/<build>/trigger-catalog.json` の `hold` 行を控える。
2. 次のそれぞれに拘束される。
   - ギャラリー登録のある敵（ミミックなど）
   - ギャラリー登録の無い敵（大口のワーム、触手叢、肉胎児）
   - `EnemyObject` でない拘束者（寄生触手、大聖堂の石の眼）
3. 各拘束の直後にログとカタログを確認する。

**期待結果**
- **どの拘束でも、未登録トリガーの警告が出るか再生が始まるかのどちらかが起きる。** 何も起きない拘束が1件も無い。
- `actorId` が `None` の行が新たに増えていない。ギャラリー登録の無い敵は `EnmID_...` または `obj:...` として、**別々の行**で現れる。
- `EnemyObject` でない拘束者は `obj:...`、拘束者を名指せない場合は `unidentified-binder` として現れる。
- 期待どおりにならない拘束があれば、場所・敵・ログ行を記録する。

---

### TS-302 膨乳デバフ単独

**対応**: FR-011、AC-006
**手順**: 膨乳（`Breast`）だけを受け、待機状態にする。

**期待結果**
- U.F.O TW 左右が `filler-breast-swollen` で動く。通常より強い。
- ピストンは `filler-main` のまま。

---

### TS-303 寄生系と膨乳の同時成立 ★

**対応**: FR-006、FR-043、AC-037。**当初の懸念②の直接確認**
**手順**
1. 寄生系デバフと膨乳を**同時に**成立させる。
2. 待機状態で 30 秒以上観察する。

**期待結果**
- ピストンが `filler-main-parasite`、U.F.O TW 左右が `filler-breast-swollen` を**同時に**保持する。
- どちらも途中で相手の波形へ切り替わらない。周期的に入れ替わることもない。
- EDI ログで、ピストンに送られたギャラリー名が `filler-main-parasite` のみであることを確認する。

**失敗の見え方**: ピストンが止まる、または乳房側の波形で動く。片方が数秒ごとに切り替わる。

---

### TS-304 誤配送しても静止波形で隠れない ★

**対応**: FR-047、FR-050、AC-040。**当初の懸念①の直接確認**
**手順**
1. TS-303 の状態で `Edi/Gallery/a10-main/` の中身を確認する。
2. `filler-breast.funscript` と `filler-breast-swollen.funscript` が**存在しない**ことを確認する。
3. EDI ログを検索する。

**期待結果**
- `a10-main` に乳房側 filler のファイルが無い。
- EDI ログに `has no 'a10-main' variant` の警告が**出ていない**（誤配送そのものが起きていない）。
- 仮に出た場合、それは誤配送が起きた証拠であり、ピストンは静止波形ではなく**再生なし**になる。ログから原因を追える。

**改訂前との差**: 旧構成では `a10-main/filler-breast.funscript`（pos=0 固定）が存在し、誤配送はピストンの停止として現れて原因が分からなかった。

---

### TS-305 左右同時開始

**対応**: FR-046、AC-003、AC-044
**手順**
1. 乳房側 2 出力を対象とするトリガーを発生させる。
2. EDI ログの `Starting PlayGallery` の時刻を左右で比較する。

**期待結果**
- 左右の `Starting PlayGallery` が**同一ミリ秒**か、それに準ずる差で並ぶ。
- MOD が送った `Play` は 1 回だけ。`channels=breast-left,breast-right` の形になっている。
- 目視で左右が同時に動き出す。

---

### TS-306 片側だけを動かす

**対応**: FR-006、FR-047、AC-042
**事前条件**: 片側を `gallery: null` にしたトリガーを `mappings.json` に用意する。
**手順**: そのトリガーを発生させる。

**期待結果**
- 指定した側の U.F.O TW が**停止**する。
- もう片側はトリガーのギャラリーで動く。
- ピストンはトリガーに含まれないため、直前の filler を継続する。再送も起きない。

---

## 7. 停止

### TS-401 タイトル遷移でデバイスが実際に止まる ★

**対応**: FR-019、FR-045、AC-009。**旧 EDI では成立しなかった**
**手順**
1. filler が再生されデバイスが動いている状態にする。
2. ゲームをタイトル画面へ戻す。

**期待結果**
- 3 デバイスすべてが**停止する**。
- filler が再生され直さない。
- EDI ログに `Stop` が出て、その後に `Starting PlayGallery` が続かない。

**改訂前との差**: 旧 EDI の `Stop` は保持していた filler を再生し直すため、タイトルへ戻ってもデバイスが動き続けた。

---

### TS-402 ゲーム終了

**対応**: FR-019
**手順**: デバイスが動いている状態でゲームを終了する。

**期待結果**: 3 デバイスすべてが停止する。EDI に 1 回の Stop が届き、`channels` に 3 出力すべてが含まれる。

---

### TS-403 ポーズと解除

**対応**: FR-013、AC-008
**手順**: トリガー再生中にポーズし、解除する。

**期待結果**
- ポーズで 3 出力へ `Pause?untilResume=true` が届く。
- 解除で現在位置から `Play` が再構成される。ポーズ中の経過時間を持ち越さない。

---

## 8. オーサリング GUI

### TS-501 出力状態の表示

**対応**: 6.7-10
**手順**: TS-201（片側のみ接続）の状態で GUI を開く。

**期待結果**: 各出力の状態が表示され、抑止されている出力にはその理由が併記される。

---

### TS-502 試聴の対象出力制約

**対応**: FR-049、AC-036
**手順**
1. `a10-main` バリアントだけを保存済みのトリガーを選ぶ。
2. 試聴を実行する。

**期待結果**
- ピストンだけが鳴る。乳房側へは送信されない。
- 乳房側を対象に含めようとした場合、`'…' has no 'ufo-left' variant, so it is not content for U.F.O TW 左。` の趣旨の理由が GUI に返る。
- 抑止中の出力を対象にした場合も理由が返る。

---

### TS-503 左右を別々に保存しても消えない

**対応**: 6.7-8、AC-055
**手順**
1. あるトリガーに `ufo-left` だけを描いて保存する。
2. 同じトリガーに `ufo-right` を描いて保存する。
3. `mappings.json` の当該エントリを確認する。

**期待結果**: `outputs` に `breast-left` と `breast-right` の**両方**が残る。先に保存した側が消えない。

---

### TS-504 保存から再生まで

**対応**: FR-038、FR-044、AC-028
**手順**
1. 未マッピングの段階に `.funscript` を保存する。
2. その段階をゲーム内で再度発生させる。

**期待結果**
- 保存時に `Definitions.csv` が更新され、EDI へ再走査が要求される。
- `%LocalAppData%\Edi\Upload` に**何も書かれない**。
- EDI のギャラリールートが保存前と同じ。
- 次回発生時にそのギャラリーが再生される。

---

### TS-505 波形を他の段階へ共有する ★

**対応**: FR-062、FR-063、AC-060、AC-063
**手順**
1. 拘束中に `Idle_Broken`、`Idle_Injured`、`Walk2`、`Jump_Fall_Loop` のように、同じ敵の複数段階をカタログへ出す。
2. うち1段階に波形を描いて保存する。
3. 「この波形を他の段階へ…」を押し、**共有（リンク）**のまま残りの段階を選んで適用する。
4. `mappings.json` と `Edi/Gallery/a10-main/` を確認する。
5. 適用先の段階を選び、波形を1点動かして保存する。
6. ゲーム内で元の段階と適用先の段階の両方を発生させる。

**期待結果**
- 適用先のエントリが `mapped` になり、`outputs[].gallery` が**元の段階と同じギャラリー名**を指す。
- `.funscript` は**増えていない**。
- 適用先を選ぶと、共有中であることと同じ波形を再生する段階の一覧が編集画面に出る。
- 手順5の編集後、共有している全段階が新しい波形で再生される。

---

### TS-506 リンク解除と承認 ★

**対応**: FR-062、FR-040、AC-061、AC-062
**手順**
1. TS-505 の状態から、共有中の1段階で「リンク解除」を押す。
2. `Edi/Gallery/a10-main/` と `mappings.json` を確認する。
3. 解除した段階の波形を編集して保存し、残りの段階をゲーム内で発生させる。
4. 別途、クリップ長が明らかに違うループ段階（例: 2.5秒の段階へ1秒の波形）を共有として適用する。
5. `unidentified-binder` の段階を適用先に選べるか確認する。

**期待結果**
- 解除した段階だけが自前のギャラリーを持ち、残りは共有していた波形のまま再生される。手順3の編集は他へ波及しない。
- 手順4はループ長差異として拒否され、承認チェックを入れるまで `mapped` にならない。
- 手順5では `unidentified-binder` の段階が**適用先の一覧に現れない**。

---

### TS-507 敗北がトリガーになる ★

**対応**: FR-064、AC-064、AC-065
**手順**
1. 沼のボス（`GaID_SwampLeech_Adult`）に敗北する。
2. 敗北演出が始まってから操作が戻るまでの間、ログの `[game-over]` 行を読む。
3. オーサリングGUIのカタログで `game-over` の行を確認する。
4. 拘束されたまま敗北する敵でも同じことを行う。

**期待結果**
- `[game-over] game-over/…/reaction/HP0` と `…/reaction/GameOver` が**それぞれ1回だけ**記録される。演出の途中で鍵が入れ替わらない。
- 拘束されたまま敗北した場合も、演出の間 `hold` のトリガーが続かない。
- カタログに当該行が現れ、`.funscript` を作成して保存すると次回の敗北で再生される。
- 演出がプレイヤーのアニメータで再生されない敵でも、`animationId` が `GameOver` の行としてカタログに残る（観測なしにならない）。

---

### TS-508 GUI内シミュレーション ★

**対応**: FR-065、AC-066、AC-067、AC-068
**前提**: EDIを起動していなくてもよい（起動していても実機が動かないことを確認対象に含める）。
**手順**
1. オーサリングGUIで波形を持つ段階を開き、波形を一部編集して**保存せずに**「▶ 動作シミュレーション」を押す。
2. ブラウザの開発者ツールの Network タブを開いたまま再生し、シミュレーション中の HTTP 要求を観察する。
3. `a10-main` に 100ms 未満の間隔で点を密に置いた波形、または 60ms で 45 以上動く区間を作って再生する。
4. `ufo-left` / `ufo-right` の波形で、緩い傾きと急な傾きの区間を作って再生する。
5. 「繰り返し再生」を外して最後まで再生する。次にチェックして再生する。

**期待結果**
- 保存していない編集内容がそのまま動く。シミュレーション中に HTTP 要求が発生せず、実機も EDI も反応しない（AC-066）。
- 台帳の3出力が同じタイムラインで同時に動き、波形の無い出力は「波形なし — 動きません」と表示される。波形キャンバス上を再生ヘッドが移動する。
- ピストンは橙の破線（描いた線）と青のキャリッジ（実機の到達位置）が乖離し、密な点や急な区間で「描いたとおりに動かない」ことが見える（AC-067）。
- UFO は傾きが急な区間ほど速く回り、強度リングと％表示が変化する。回転方向はおおむね 0.5〜2.5 秒ごとに不規則に反転する（AC-068）。
- 「繰り返し再生」オフでは終端で停止してデバイスが止まったままになり、オンでは先頭へ戻って続く。

---

## 9. 回帰（他ゲームの保護）

### TS-601 既定値の EDI が改訂前と同じ挙動をする

**対応**: FR-051、AC-047
**手順**
1. 本 MOD 以外のゲーム構成（`EdiConfig.json` に 7.4 の設定を書いていないもの）を EDI で選択する。
2. その構成で従来どおり再生・停止する。

**期待結果**
- `Stop` が filler へ復帰する（改訂前の挙動）。
- バリアントが無いときに別バリアントへ退避する（改訂前の挙動）。
- 新規デバイスが先頭チャンネルへ入る（改訂前の挙動）。

**この確認の意味**: 7.4 の 3 設定は既定で現行挙動を保つ。他ゲームへ影響しないことがフラグ化の根拠である。

---

### TS-602 ロールバック

**対応**: 12.3
**手順**
1. `Edi/EdiConfig.json` と `Edi/Gallery/EdiConfig.json` の `StrictVariantResolution` と `StopClearsFiller` を `false`、`UnassignedDeviceChannel` を空にする。
2. EDI とゲームを起動する。
3. 続いて `Edi.exe` を改訂前バイナリへ差し戻し、同じ確認をする。

**期待結果**
- 段階 1: EDI は改訂前の挙動へ戻る。MOD は 7.4.3 により出力を有効化しない。ゲームは EDI 連動なしで進行する。
- 段階 2: `GET /Edi/Info` が 404 になり、MOD は 7.4 未適用と判定する。
- どちらもゲーム本体とセーブデータに影響しない。

---

## 10. 記録

各シナリオについて次を残す。結果は [`SPEC001-traceability.md`](../implementation/SPEC001-traceability.md) の該当 AC 行へ反映する。

```
TS-xxx  実施日:        実施者:
判定: Pass / Fail / 再現せず / 未実施
環境: Edi.exe SHA-256 =            接続デバイス =
観測: （ログ行の実際の文言、デバイスの挙動）
差異: （期待結果と違った点）
```

`Edi.exe` の SHA-256 は次で取得する。

```bash
python -c "import hashlib;print(hashlib.sha256(open('Edi/Edi.exe','rb').read()).hexdigest())"
```

## 11. 既知の制約

- TS-204 は再現条件が EDI 側のデバイス解放タイミングに依存するため、必ず再現するとは限らない。
- TS-305 の同時性は EDI のログ時刻で判定する。ミリ秒未満の差は測定できない。
- TS-303 と TS-304 は、寄生系デバフと膨乳を同時に成立させられるゲーム内状況に到達できることが前提になる。到達できない場合は `mappings.json` の `statusRules` を一時的に別のデバフへ割り当てて代替する。
- `Edi/Gallery-static-asset-backup-20260806/` は移行時に退避した静止アセットである。TS-304 が通ったら削除してよい。
