# SPEC005 implementation traceability

Normative source: [`docs/specifications/SPEC005.md`](../specifications/SPEC005.md). This document records implementation and verification only; it does not change the specification.

実装先は SPEC003 の既存プラグイン `community.sinisistar2.pleasure` である（FR-401、DEC-401）。新規プラグインは追加していない。

## Stage

**v1.0（2026-08-10）。** SPEC005 の4機構を実装した。

- **淫魔化バフ（5.1）** — 昇華済みの絶頂を契機とするHP/MPの緩速回復。加算型・上限なし、セーブポイントで破棄。
- **淫紋の快楽補正（5.2）** — 昇華済みの間だけ掛かる固定倍率。既定 1.25。
- **堕落の蓄積速度の段階化（5.5）** — SPEC003 FR-267 の平坦な倍率を、呪いストック数に比例する軽微な加速と、昇華時の不連続な崖に置き換えた。
- **進行演出（5.4）** — ストック増加と昇華でピンクの靄。絶頂演出の描画系を流用。
- **MP0ペナルティ（5.3）** — 規則は実装・テスト済みだが、硬直モーションの再生経路（A-401）が未確定のため既定無効。

**出荷時の挙動。** `CrestPleasureGainScale`（1.25）だけが利用者確定値であり、導入した時点から昇華後の快楽上昇に作用する。それ以外の新規調整値はすべて挙動不変相当で出荷する（FR-415）。

**堕落の段階化による既存挙動の変化。** SPEC003 は呪いストック1つでも `CorruptionCrestGainScale`（2.0）を掛けていた。本実装では `CorruptionCurseGainMax` の既定 `0` により、**呪い段階の加速がなくなり**、昇華後のみ 2.0 が掛かる。これは意図した方向であり（呪いは引き返せる警告であるべき）、CHG-513 の承認範囲である。

## 実機フリーズ(2026-08-10)— 原因未特定・調査継続中

**事象。** 実機テスト中、ゲームが応答不能になり強制終了された。Windows イベントログ(Application Hang 1002 / WER AppHangB1)より**ハング記録は 17:31:15**。淫紋の昇華(`LustMarkCurse` レベル4の初適用)は 17:30:02、EDI セッションログの最終イベントは 17:30:07。

**確定した事実。**
- ハングは昇華の約1分後の時間帯に発生した(イベントログ)。ただし EDI ログの沈黙はアイドル時にも正常に起きるため、正確な発生時刻は 17:30:07〜17:31:15 の間としか言えない。
- **利用者の直接観察: レベル4付与の時点ではフリーズしていない。** 付与→ステータスUI変化→状況メッセージ表示→フェードまで正常に進行した。
- レベル4の実機適用はこのセッションが初(過去ログに到達記録なし)。
- MOD側の per-frame 全経路にループ・デッドロック要因なし(精査済み)。
- ゲーム側で `LustMarkCurse` を参照するコードは7箇所のみ(定数スキャン)。うちレベル4分岐は `PortraitOverwriteSprite.IsMatch` の立ち絵切替のみで、ハング可能な構造なし。
- WER レポートはハッシュ署名のみでダンプなし(Cab ID 0)。スタック情報は得られていない。

**棄却された仮説。** 「レベル4の書き込みがフリーズを引き起こす」— 利用者の直接観察により棄却。この仮説に基づいて一時導入した「ゲームへはレベル3までしか書かない」抑止(`ApplyFinalCrestStock`)は、淫紋の4段階目進行を壊したため**全面撤回**した。現在はレベル4を従来どおり書き、ゲーム側の「淫紋」表示・立ち絵切替も機能する。この過ちが「推測に基づく修正の厳禁」という利用者指示の背景である。

**次の発生時の証拠計画。** 原因は未特定のまま。再発した場合は、強制終了する**前に**タスクマネージャーで `SiNiSistar2.exe` を右クリック→「ダンプ ファイルの作成」を実行してほしい。ダンプのメインスレッドスタックが、ハングがゲーム側・MOD側・どのモジュールにあるかを確定させる唯一の証拠になる。あわせて発生時刻(何をしていた瞬間か)の記録があれば、`LogOutput.log`・EDI セッションログ・イベントログと突合できる。

## MP0ペナルティが観測不能だった件(2026-08-10)

**一次原因は設定。** デプロイ済みの `community.sinisistar2.pleasure.cfg` は `MpPenalty.Enabled = false` かつ `StunChance = 0` だった(実ファイルで確認)。仕様どおりの出荷既定であり、機構は一度も実行されていない。「観測できない」は正しい挙動だが、**それを画面から知る手段が無かった**ことが問題である。

**二次原因は不可観測性。** 有効化しても、発火はログ1行のみ(A-401 によりアニメーションなし)。条件が7つのANDで、どれが偽なのかも、キーが読めているのかも、外からは分からなかった。

**対策。**

1. **F4: SPEC005 統合デバッグパネル**(新規)。4機構の生状態を1画面で表示する。
   - 堕落値/上限/割合、淫紋レベル/最大/昇華済み、堕落係数(現在・昇華後)、快楽係数
   - 淫魔化バフの残り秒・毎秒回復量、MP回復経路の可否(3値)
   - MP0ペナルティ: 有効/無効・確率・クールダウン・対象入力、**7条件それぞれの真偽**、集約結果
   - **`keys now:` 行** — 既知入力すべての現在の押下状態。これが `Input.GetKey` が IL2CPP 上で機能しているかを直接答える(A-403 の残課題)。設定外の入力も表示し `(off)` を付す
   - 押下数/抽選数/発火数/クールダウン残、**直前の押下がどのゲートで止まったかの説明文**、`key source:`(どちらの入力源が答えているか、ポーリング失敗時はその例外)
   - 描画位置は**画面右上**。左側はゲーム自身の立ち絵とゲージ群があり重なって読めなかったため(利用者REVIEW)。背景は半透明の塗り、文字は3色(見出し/機構を止めている状態/正常)で色分けする
2. **F2: 強制発火**。押下エッジ・確率・クールダウンのみ短絡し、**7条件は一切迂回しない**(SPEC004 DEC-316 と同じ線引き)。条件が揃わない場合はどれが偽かをログに出す。当初 Shift+F4 に割り当てたが機能しないと報告されたため、実績のある単独ファンクションキーへ変更した(入力の信頼性を調べる道具が修飾キーの信頼性に依存するのは筋が悪い)。
3. **`MpZeroStunScheduler` に観測用カウンタ**を追加(`PressCount`/`RollCount`/`FireCount`/`LastOutcome`/`CooldownRemainingAt`)。押下ごとに「無効」「条件不成立」「クールダウン中」「抽選で外れ」「発火」のどれかを記録する。テスト3件。
4. **スケジューラを常時生成**(無効時も)。パネルが「scheduler: absent」としか言えない状態を無くす。無効時は更新経路が手前で return するため挙動は不変。
5. **検証用に設定を有効化**: `Enabled = true`、`StunChance = 1`(条件成立時の押下で必ず発火)。**コード側の既定値は FR-415 のとおり無効のまま**変更していない。

## 実測で解決した項目

- **A-402（MPの回復経路）＝解決。** interop メタデータより、`PlayerStatusManager.MP` は HP と同じ `BattleMainParameter` 派生であり、`Current` / `Max` / `Recover(int, bool)` を持つ。`PlayerStatusManager.RecoverMP(RecoverCalcType, int, bool, bool, bool)` も存在する。実装は `Recover` を用い、バーが動かない場合は1回だけ記録して以後MP回復を試みない（FR-405）。
- **A-401（硬直モーションの再生経路）＝解決。** 利用者がギャラリーからテイクを特定（`gallery/GaID_Attack/Magic_Sword1_Empty/loop/Magic_Sword1_Empty`）。これを手掛かりに ISIL と interop メタデータで経路を確定した。
  - `Magic_Sword1_Empty` はコードの文字列リテラルではなく **`SiNiSistar2.Obj.Animation.AnimState` の列挙値**。
  - `LeliaAnimation.Update` は毎フレーム、プロシージャ群が決めた状態で **`CurrentAnimState` を上書きする**（ISIL 136行目）。したがって外から `CurrentAnimState` を書いても効かず、`ForcePlayAnimation()` も「同じ状態でも再生し直す」フラグに過ぎない（アニメータへ直接 `Play` するのも同様に翌フレーム上書きされ得る）。**状態を外から指定する道は無い。**
  - 正しい経路は**ゲーム自身の入力注入点**である。`AttackActionBase.Pressed()` / `PressedThisFrame()` は、実際のボタンが書くのと同じ2つのフラグ（`Call.IsPressed` / `Call.WasPressedThisFrame`）を書くだけのメソッドで、`ClearCall()` が毎回それらを戻す。
  - `MagicSword.OnUpdateAction` は `PlayerStatusManager.UnUsedMagic`（MP不足）を読み、MPが無ければ空撃ち分岐へ入る。**MP0 は本ペナルティの条件3そのもの**なので、押下を注入するだけでゲームが空撃ちを選び、`Magic_Sword1_Empty` の再生と行動ロックをゲーム自身が行う。
  - **実装**: `Lelia.MagicSword` に対し `PressedThisFrame()` + `Pressed()` を呼ぶ。アニメータ状態は一切書かない。既に行動中（`IsAction`）なら何もしない。MODが言うのは「ボタンが押された」ことだけで、その意味はゲームが決める。誤って詠唱が成立することはない（実行時点でMPは空）。FR-228（再生するテイクは実際に起きている事象を表すこと）を満たす — 実際に詠唱を試みているため。 ISIL 読解により、バニラの「MP0で魔法攻撃→硬直」の正体は**魔法アクションの空撃ちモード**であることを特定した（`MagicArrow.IsEmptyShot`。MP判定は `MagicArrow.OnUpdateAction` の `UnUsedMagic` 参照、消費は `_CreateArrow` の `SubMPForMagic`）。独立した「硬直モーション」は存在せず、空撃ちの動作そのものが硬直である。これを外部契機で再生するにはプレイヤーが入力していない詠唱を開始することになり、take の真実性（SPEC003 FR-228、SPEC001 のトリガー同定）と衝突するため、再生は保留。ペナルティは既定無効のまま、有効化時は**発火のたびに `[SPEC005] MP0 penalty fired` をログへ記録**し、規則（AND条件・押下エッジ・クールダウン）を実機で検証可能にした（利用者REVIEW 2026-08-10「確認不可」への対応）。
  - **押下先の誤り（実機で発覚、同日修正）。** 上記の実装は「発火ログは毎回出るのにモーションが再生されない」と報告された（利用者REVIEW 2026-08-10）。ISIL でオブジェクトの同定をやり直した結果、`Lelia` は魔法の**実装体**（`MagicArrow` +480 / `MagicSword` +488 / `MagicOwl` +496）と**装備スロット**（`Magic01` +568 / `Magic02` +584）を別に保持しており、`Lelia.OnResponseSection` が `UpdateAction` を呼ぶのは `Melee`(+504) と2つのスロットだけであることが判明した。`MagicSword` の `OnUpdateAction` は**一度も実行されない**ため、そこへ立てたフラグは誰にも読まれなかった。押下先を装備スロット（剣魔法が入っている方を優先、無ければ装備中の一方）へ変更して解消。あわせて `MagicSword.OnUpdateAction` の先頭ゲートが `Call+17`（`WasPressedThisFrame`）を読むことも確認済みで、呼ぶメソッド自体は当初から正しかった。
- **A-403（入力の観測点）＝実機で解決。** F4パネルの `keys now:` 行が全キーで `--` のまま動かなかった（利用者REVIEW 2026-08-10）。**`UnityEngine.Input` のポーリングはこのビルドでは行動キーを一切報告しない**。interop に `Unity.InputSystem.dll` が同梱されていることと整合する。対策として、**IMGUI のキーイベント（`Event.current` の KeyDown/KeyUp）を入力源に採用**した。これは同ファイルの全デバッグキー（F7/F8/F10/F11）が実際に動いている実績のある経路であり、推測ではない。イベントは観測のみで `Use()` せず、ゲーム側の入力を奪わない。生のキーコードで保持集合を管理するため、片方の矢印を離してももう片方が押されていれば移動は継続と読む。ポーリングも併用し続け、どちらが答えているかをパネルの `key source:` 行に出す。例外は握り潰さず記録する（旧実装は catch して false を返しており、これが「押されていない」と「APIが答えられない」を区別できなくしていた）。
- **A-403（旧記述・部分解決）。** ゲーム自身の入力オブジェクトは「行動が許可されたか」を解決した後の状態を公開するため、MP不足で拒否された押下が確実には見えない。`UnityEngine.Input` の直接ポーリング（`UnityEngine.InputLegacyModule` を新規参照）で押下エッジを取る。キー割り当ては**利用者確認済み（2026-08-10）**: X=通常攻撃、C=剣魔法、V=弓魔法、Z=ジャンプ、←→=左右移動（拘束時は抵抗入力であり、このとき硬直を再生することは厳禁 — 条件集約の `!bound` に加え、発火直前の冗長ガードでも遮断）、↓=しゃがみ（意図的に対象外: 待機を罰しない）。
- **A-407（`LustMarkCurse` の `MaxLevel`）＝未確認。** 値は実行時にゲームから読む（FR-421）ため、実装は段数を書き込んでいない。係数は「最終可逆ストックで `1+CurseGainMax`」となるよう段数に対して正規化してあり、段数が変わっても呪いの上限は動かない。

## Status meanings

- **Tested** — implemented and covered by an automated test.
- **Implemented / unverified** — code exists, in-game evidence outstanding.
- **Deferred** — deliberately shipped inert pending a measurement.

## 実装後レビューで直した不具合

静的レビューで実バグを検出し、いずれも修正済み。回帰テストを添えたものはその旨を記す。

| # | 内容 | 影響した要件 | 修正 |
|---|---|---|---|
| 1 | シーン遷移でバフが破棄されていなかった。`PleasureRuntime.Reset()` は `Shutdown` / `Unload` からしか呼ばれず、遷移経路は `Suspend()` である | FR-416、AC-414 | `Suspend()` で `DiscardRegen`。あわせて `_lastFrameTime` を破棄し、ロード時間分の `delta` を1フレームで払い出す不具合も解消 |
| 2 | イベント再生中もバフが消費・回復していた。`timeScale` は通常のまま進むため停止条件に掛からなかった | FR-406 | `UpdateRegenBuff` に `IsCinematic()` を追加 |
| 3 | 敵が最終ストックで淫紋を付与した場合に昇華が latch されず、堕落係数が呪いのまま・快楽の淫紋係数も掛からなかった（SPEC005 3章は付与経路を問わないと定義） | FR-408、FR-419 | `LatchSublimation` を共通化し、`TrackCrestSublimation` で毎フレーム検出。段数未読を `CrestMaxLevelKnown` で区別 |
| 4 | `UnityEngine.Random.value` を毎フレーム引いていた。ゲームと他MODが共有する大域生成器を掻き回す | 10章 | 抽選を `Func<float>` に遅延。テスト `TheRollIsNotDrawnUnlessAPressGetsThrough` で固定 |
| 5 | シーン遷移をまたいで押しっぱなしのキーが「新規押下」に化けた | FR-410 | `Reset()` 後の1フレームを priming 扱い。テスト `KeyHeldAcrossAResetIsNotAPhantomPress` |
| 6 | `Input.GetAxisRaw` の例外が `Update` まで抜け、観測器ごと毎フレーム停止し得た | 堅牢性 | `IsInputDown` を try/catch で包み、読めない場合は「押されていない」を返す |
| 7 | 靄が `ShowOverlay`（SPEC003 のHUD設定）で抑止されていた | FR-413 | HUDのゲートの外へ移動。制御は `CrestFx.Enabled` のみ |
| 8 | 濃度が飽和し、`M=6` では最終可逆ストックと昇華がどちらも 1.0 で同一に見えた | AC-412 | 段数で正規化。テスト `SublimationIsAlwaysStrictlyTheStrongest` |
| 9 | `CorruptionCrestGainScale < 1` を無言で 1 に丸めていた | 5.5.3 | 設定エラーとして提示 |

## Automated coverage

`dotnet test tests/SiNiSistar2.Pleasure.Core.Tests -c Release` → 202 tests, 0 failures（SPEC003 分 151 + SPEC005 分 51）。

新規テストファイル: `CrestStagingTests`（8）、`RegenBuffTrackTests`（11）、`MpZeroStunSchedulerTests`（11）、`CrestPleasureAndValidationTests`（10）、`CrestProgressEffectTests`（11）。

ソリューション全体は `SiNiSistar2.Edi.Core.Tests.RepositoryLayoutTests.SwollenFillerIsMechanicallyStrongerThanNormalFiller(variant: "ufo-right")` の1件が失敗する。**本実装以前からの既存不具合**であり、HEAD（`0feeb6c`）を別ワークツリーへ取り出して再現を確認した。SPEC001 の EDI 側データ（`ufo-right` の filler 振幅 21 が 23 を超えていない）の問題で、本実装は EDI のコードもデータも変更していない。

## 要件台帳

| ID | 要件（要約） | 実装箇所 | 検証 | 状態 |
|---|---|---|---|---|
| FR-401 | SPEC003 プラグイン内へ実装。新規プラグインを追加しない | 既存 `community.sinisistar2.pleasure` を拡張 | ビルドと配置 | Tested |
| FR-402 | バフは昇華済みかつ絶頂のときのみ発動 | `PleasureObserver.GrantRegenBuff` | `RegenBuffTrackTests`、AC-401〜403 実機 | Tested / unverified |
| FR-403 | 発動ごとに加算。`RegenDurationCap` が正なら頭打ち、0で上限なし | `RegenBuffTrack.OnQualifyingClimax` | `RegenBuffTrackTests.RepeatedClimaxesAddRatherThanReset` ほか3件 | Tested |
| FR-404 | 致死化する絶頂では発動しない。死亡中は回復・消費せず破棄 | `GrantRegenBuff`（`ClimaxDeathFired` を確認）、`UpdateRegenBuff` | `RegenBuffTrackTests`、AC-405 実機 | Tested / unverified |
| FR-405 | 回復はゲーム自身の操作。MP経路が未確定なら行わず記録 | `PlayerVitals.Restore`、`PlayerHealth.Current.Recover` | A-402（メタデータで解決）、実機で `[probe] mp-recovery` | Implemented / unverified |
| FR-406 | イベント中・敗北演出中・`timeScale=0` で停止。拘束中は継続 | `PleasureObserver.UpdateRegenBuff` | `RegenBuffTrackTests.NotAdvancingDoesNotSpendTheBuff`、実機 | Tested / unverified |
| FR-407 | 永続化しない。スロット読込・新規開始・セーブポイントで破棄 | `PleasureRuntime.DiscardRegen`、`LoadSlot` / `EnterNoSlot` / `SavePointPatches` | `RegenBuffTrackTests.DiscardEnds...`、AC-406 実機 | Tested / unverified |
| FR-408 | 快楽は 5.2.1 の式。淫紋係数は昇華済みのみ、段階連動禁止 | `PleasureMeter.AddSexualHit`、`DamageProbePatches` | `CrestPleasureAndValidationTests`（4件、AC-407・AC-415 を含む） | Tested |
| FR-409 | 装着・堕落閾値・MP0・通常プレイ・生存のAND | `PleasureObserver.UpdateMpPenalty`、`PlayerVitals.IsMpLow` | `MpZeroStunSchedulerTests`、AC-408・409 実機 | Tested / unverified |
| FR-410 | 押下エッジのみ。押しっぱなしで再抽選しない。クールダウン | `MpZeroStunScheduler.Evaluate` | `MpZeroStunSchedulerTests`（4件、AC-410 を含む） | Tested |
| FR-411 | 硬直はゲーム既存モーションの流用。新規モーションを作らない | `PleasureObserver.PlayStagger` → `MagicSword.PressedThisFrame()` / `Pressed()`。アニメータ状態は書かず、ゲームが空撃ち分岐で `AnimState.Magic_Sword1_Empty` を再生する | A-401 解決、実機確認待ち | Implemented / unverified |
| FR-412 | バニラのMP0魔法硬直を変更しない。二重発生させない | `StunInputs.Defaults` から `Magic` を除外。ゲーム側へ一切介入しない | `MpZeroStunSchedulerTests.InputsOutsideTheConfiguredSetNeverTrigger`、AC-411 実機 | Tested / unverified |
| FR-413 | ストック増加・昇華でピンクの靄。濃度は段階に応じる | `PleasureObserver.RaiseCrestProgressEffect` / `DrawCrestProgressFlash`、`CrestProgressEffect` | AC-412 実機 | Implemented / unverified |
| FR-414 | 描画できなくても状態変化を妨げない。保留時は成立時に1回 | 演出は `ApplyPendingLustCrest` の付与成立後にのみ発火。描画は `OnGUI` の try/catch 内 | AC-412 実機 | Implemented / unverified |
| FR-415 | 未確定値の既定は挙動不変。`Enabled` が偽なら介入しない | `PleasureOptions` の既定値、各 `*Tuning.HasEffect` | `CrestPleasureAndValidationTests.ShippedDefaultsLeaveOnlyTheUnmeasuredMechanismsInert`、AC-413 | Tested |
| FR-416 | `Unload`・シーン遷移・例外でバフ・クールダウン・演出を破棄 | `PleasureRuntime.Reset`、`PleasureObserver.Suspend` | `MpZeroStunSchedulerTests.ResetClears...`、AC-414 実機 | Tested / unverified |
| FR-417 | SPEC003 の既存規範を変更しない（FR-267 の適用規則を除く） | 基準増加量・単調非減少・上限・絶頂・サイドカーに変更なし | SPEC003 既存テスト 151 件が無変更で通過 | Tested |
| FR-418 | 起動時に有効な機構・確定値・無効理由を記録 | `PleasurePlugin.LogCorruptionBonus` | 実機ログ | Implemented / unverified |
| FR-419 | 堕落は `基準増加量 × 淫紋蓄積係数`。段階は 5.5.1 のとおり | `CrestStaging.Coefficient`、`CorruptionTuning.ScaleFor`、`PleasureRuntime.GainCorruption` | `CrestStagingTests`（8件、AC-416・417 を含む） | Tested |
| FR-420 | 崖の存在を検証。違反時は段階化を無効化、蓄積は止めない | `PleasureProfileFactory.BuildCorruption` | `CrestPleasureAndValidationTests.ConfigurationWithoutACliff...`（AC-418） | Tested |
| FR-421 | 最終ストック数はゲームから読む。段数を固定値に持たない | `PleasureRuntime.CrestMaxLevel`（`AbnormalData.MaxLevel` から）、`CrestStaging` は段数で正規化 | `CrestStagingTests.TheLastReversibleStockAlwaysReachesTheSameCeiling` | Tested |

## 実機で確認が要る点

1. **淫紋進行の復元確認** — F8で上限まで進め、4段階目でゲームのステータスが「淫紋」になり、立ち絵が切り替わり、昇華ログと最大濃度の靄が出ること。フリーズが再発した場合は強制終了前にタスクマネージャーでダンプを採取（上記の証拠計画）。
2. **MP0ペナルティの規則検証（F4パネルで実施）** — 設定は検証用に `Enabled=true`・`StunChance=1` へ変更済み。手順:
   1. F4 でパネルを開く。
   2. **`keys now:` 行を見ながら X / Z / ← → を押す。** ここが `DOWN` に変われば入力読み取りは機能している。変わらなければ `UnityEngine.Input` が IL2CPP 上で機能しておらず、それが根本原因(A-403)。
   3. `conditions:` 行で、何が足りないかを読む。F8 で堕落を閾値(50%)以上へ、魔法を撃って MP を 0 にする。
   4. 全条件が揃った状態(`=> ALL MET`)で X を押す → `fires` が増え、ログに `[SPEC005] MP0 penalty fired` が出る。
   5. 条件を揃えられない場合は **Shift+F4** で強制発火(条件は迂回しない)。条件不成立ならどれが偽かがログに出る。
   6. 拘束中は発火しないこと(←→ は抵抗入力)を確認する。
3. **A-402 の実挙動** — `MP.Recover` がバーを動かすか。`[probe] mp-recovery` の1行で判定できる。
4. **A-406 / A-405** — 調整値の実用域。とくに「呪いを受けてから昇華までに解呪が間に合うか」（正のフィードバックループの利得）。
5. **A-407** — `LustMarkCurse` の `MaxLevel` は実機ログで **4 と確定**（A-45 行）。`M=4` 前提の暫定表はそのまま有効。
