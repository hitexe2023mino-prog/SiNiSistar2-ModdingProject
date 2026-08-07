# SPEC002 implementation traceability

Normative source: [`docs/specifications/SPEC002.md`](../specifications/SPEC002.md). This document records implementation and verification only; it does not change the specification.

In-game verification steps live in [`docs/testing/SPEC002-test-scenarios.md`](../testing/SPEC002-test-scenarios.md). Every row marked *Implemented / unverified* is covered there.

## Status meanings

- **Tested** — implemented and covered by an automated test.
- **Implemented / unverified** — code exists, but the required in-game evidence is outstanding.
- **Design-time** — satisfied by not doing something. Held in place by an automated source scan.
- **Gated** — the contingency depends on a 付録A measurement that has not been taken.

## Automated coverage

`dotnet test SiNiSistar2.Edi.sln -c Release` → 73 tests for this MOD, 143 for the EDI MOD, 0 failures.

`ForbiddenSurfaceTests` scans `src/SiNiSistar2.Difficulty.Plugin/**/*.cs` with comments stripped and fails the build if a forbidden member is named. That is what keeps the "must not touch" requirements from decaying silently.

## 要件台帳

| ID | 要件（要約） | 実装箇所 | 検証 | 状態 |
|---|---|---|---|---|
| FR-101 | BepInEx IL2CPP プラグイン、GUID `community.sinisistar2.difficulty` | `DifficultyPlugin` | ビルドと `BepInEx/plugins/community.sinisistar2.difficulty` への配置 | Tested |
| FR-102 | ゲームバイナリ・アセット・セーブを書き換えない | 全体（実行時パッチのみ） | `ForbiddenSurfaceTests`、AC-101 実機 | Implemented / unverified |
| FR-103 | `Hard` として報告 | `HardModeReportPatches`（`IsHardMode` の postfix）、`DifficultyObserver.EnsureHardReported`（`s_GameDifficultyForCheck` の値上書き） | 起動ログの self-check 行、AC-102 実機 | Implemented / unverified |
| FR-104 | 保存値へ書き込まない | 検査側 static アクセサのみをパッチ | `ForbiddenSurfaceTests.TheSavedDifficultyIsNeverWritten`、AC-104 実機 | Design-time / unverified |
| FR-105 | 波及が確認されたら差し替えを行わない縮退 | 対応済み設計（保存経路を一切書かない） | A-1 実測 | Gated |
| FR-106 | 強化はプレイヤー受けのみ | `DifficultyRuntime.IsPlayerReceiving`、`AbnormalRatePatches`、`AbnormalLevelPatches` | AC-106 実機 | Implemented / unverified |
| FR-107 | 付与率の一時上書きを例外経路でも復元 | `AbnormalRatePatches.OneDamageFinalizer`（Harmony finalizer） | `ReentrantScopeTests`、AC-107 実機 | Implemented / unverified |
| FR-108 | 再入時は最外の1回だけ | `ReentrantScope`、`DifficultyRuntime.RateScope` | `ReentrantScopeTests` | Tested |
| FR-109 | `MaxLevel` を超えず通知経路を通る | `AbnormalLevelPatches`（`_IncrementLevel`、`MaxLevel` 判定） | AC-109 実機 | Implemented / unverified |
| FR-110 | 1回の付与に対する追加進行は1回 | `DifficultyRuntime.LevelScope` | `ReentrantScopeTests` | Tested |
| FR-111 | 快楽系が有効な拘束中だけ無力化窓 | `NullificationScheduler`、`DifficultyObserver.UpdateHold`、`NullificationPatches` | `NullificationSchedulerTests`（8件）、AC-111 実機 | Tested / unverified |
| FR-112 | 穢れ軸を読み書きしない | 参照なし | `ForbiddenSurfaceTests.TheDefilementEscapeAxisIsNeverReferenced` | Design-time |
| FR-113 | 拘束ゲージの数値を書き換えない | 参照なし | `ForbiddenSurfaceTests.TheStruggleMeterNumbersAreNeverWritten` | Design-time |
| FR-114 | `Defilement` を快楽系として拒否 | `DifficultyProfileFactory.BuildPleasure`、`AbnormalTypeSet.Parse` | `ProfileValidationTests.DefilementIsRefused...` | Tested |
| FR-115 | 減衰を変えず表示を隠さない | `NullificationPatches` は `Execution` を飛ばすだけ | AC-115 実機 | Implemented / unverified |
| FR-116 | 強制成功を妨げない | `IsForceSuccessRequest` を参照しない | AC-116 実機 | Design-time / unverified |
| FR-135 | 無力化窓の間だけゲージを着色し、4契機で必ず戻す | `DifficultyObserver.ApplyGaugeTint` / `ResolveGaugeFill`、`HexColor`、`InterventionLedger` | `GaugeTintTests`（10件）、AC-132〜134 実機 | Tested / unverified |
| FR-117 | 占有率の期待値を警告 | `PleasureTuning.ExpectedDutyCycle`、`BuildPleasure` | `ProfileValidationTests.AHighDutyCycleWarns...` | Tested |
| FR-118 | 身重系が有効な離脱直後だけ復帰遅延窓 | `RecoveryPenaltyScheduler`、`DifficultyObserver.UpdateRecovery` | `RecoveryPenaltySchedulerTests`（7件）、AC-118 実機 | Tested / unverified |
| FR-119 | 寄与キーで登録し3契機で必ず解除 | `DifficultyObserver.RegisterMoveSlow` / `ReleaseMoveSlow`、`InterventionLedger` | `RecoveryPenaltySchedulerTests`、`InterventionLedgerTests`、AC-119 実機 | Tested / unverified |
| FR-120 | 二重登録せず新しい窓で置換 | `RecoveryPenaltyScheduler.Begin`、`InterventionLedger.Register` | `RecoveryPenaltySchedulerTests`、`InterventionLedgerTests` | Tested |
| FR-121 | 拘束成立条件を書き換えない | 参照なし | `ForbiddenSurfaceTests.TheHoldPredicatesAreNeverTouched` | Design-time |
| FR-122 | 敵側へ介入しない | `IsPlayerReceiving`、`PlayerAbnormals` 経由のみ | AC-106 実機 | Implemented / unverified |
| FR-123 | 判別不能なら介入しない | `ShouldNullify`、`IsPlayerReceiving` が false を返す | AC-122 実機 | Implemented / unverified |
| FR-124 | 台帳で追跡し全契機で解除、失敗を記録 | `InterventionLedger`、`DifficultyPlugin.Unload`、`DifficultyObserver.Suspend` | `InterventionLedgerTests`（5件） | Tested |
| FR-125 | `Off` ではパッチを適用しない | `DifficultyPlugin.Load` の早期 return、`DifficultyProfileFactory` | `ProfileValidationTests.OffProducesAnInactiveProfile...` | Tested |
| FR-126 | 未知の種別名を無視し提示 | `AbnormalTypeSet.Parse` | `ProfileValidationTests.AnUnknownStatusName...` | Tested |
| FR-127 | 負値は当該機構を無効化 | `DifficultyProfileFactory` の各 Build | `ProfileValidationTests`（2件） | Tested |
| FR-128 | 未実測の既定値は無変更相当 | `DifficultyOptions` の既定値 | `ProfileValidationTests.ShippedDefaultsHaveNoEffect...` | Tested |
| FR-129 | EDI が依存する面を変更しない | 参照なし | `ForbiddenSurfaceTests.TheSurfacesTheEdiModDependsOn...`、AC-127 実機 | Design-time / unverified |
| FR-130 | 偽の状態異常を追加しない | 状態異常の追加・削除を呼ばない | `ForbiddenSurfaceTests.StatusesAreNeverAddedOrRemoved...` | Design-time |
| FR-131 | 他 MOD へ依存宣言しない | `BepInDependency` なし | `ForbiddenSurfaceTests.NoDependencyOnAnotherPlugin...` | Tested |
| FR-132 | メインスレッドで完結、待機しない | `DifficultyObserver`（同期処理のみ） | AC-129 実機 | Implemented / unverified |
| FR-133 | 起動ログに構成を記録 | `DifficultyPlugin.Load` の最終ログ | AC-130 実機 | Implemented / unverified |
| FR-134 | 窓判定・検証をゲーム非依存層へ | `SiNiSistar2.Difficulty.Core`（ゲーム参照ゼロ） | 59件がゲーム起動なしで実行 | Tested |

## 判断記録

| 項目 | 内容 |
|---|---|
| 論点 | `Hard` の報告に、どのアクセサをパッチすれば保存値を汚染しないか（付録A A-1、A-2） |
| 選択 | `PlayerStatusManager.IsHardMode` と `s_GameDifficultyForCheck` の2つだけをパッチする |
| 根拠 | interop の実シグネチャ上、両者は **static** アクセサであり、セーブへ載る `m_GameDifficultyRP`（`UniRx.ReactiveProperty<GameDifficulty>`）とは別経路である。`s_GameDifficultyForCheck` は名前自体が検査用の複製であることを示す |
| 影響 | 保存値は書かれない。A-1 の実測が波及を示した場合でも、パッチ対象を狭める余地が残る |
| 代替案 | インスタンス側の `get_GameDifficulty` もパッチする案。保存経路が同じプロパティを読む可能性を排除できないため不採用 |

| 項目 | 内容 |
|---|---|
| 論点 | `s_GameDifficultyForCheck` の getter が Harmony でパッチできない |
| 選択 | getter のパッチをやめ、静的フィールドの値そのものを `Hard` へ上書きし、初回に観測した値を台帳へ登録して `Unload` で戻す |
| 根拠 | 実機ログが `Method ... get_s_GameDifficultyForCheck() is a field accessor, it can't be patched` を出した。Harmony は例外を投げないため、旧実装は当たっていないパッチを `patches=2` として成功報告していた。フィールドであれば値として書ける |
| 影響 | SPEC002 4.4 の「読み取りの差し替え」から「一時上書き」へ形が変わる。いずれも 4.4 が許す形であり、要件（FR-103）と可逆性（FR-124）は変わらない。静的フィールドはセーブ読込で書き戻されるため、毎フレーム再表明する |
| 代替案 | `s_GameDifficultyForCheck` を諦めて `IsHardMode` だけに頼る案。ゲーム側の分岐がどちらを読むか未測（A-2）のため、片方だけでは Hard データが部分的にしか有効にならない |

| 項目 | 内容 |
|---|---|
| 論点 | パッチが当たったかどうかを起動時に判定できない |
| 選択 | マネージャ初期化後の最初のフレームで、`IsHardMode`、`s_GameDifficultyForCheck`、および未パッチの `GameDifficulty`（保存値）を1行のログへ出す |
| 根拠 | Harmony が受理したが Il2CppInterop が適用できなかったパッチは、起動ログ上では成功と区別できない。ゲームが実際に何を報告しているかを読み取るしかない |
| 影響 | 付録A の A-1（保存値の不変）と A-2（どのアクセサが効いているか）の一次証拠が、実機プレイなしで得られる |
| 代替案 | パッチ適用数だけを報告する旧案。当たっていないパッチを数えるため誤報になる |

| 項目 | 内容 |
|---|---|
| 論点 | 付与率を読むメソッドが特定できない（付録A A-3） |
| 選択 | `DamageManager.OneDamage(DamageStack)` を prefix / finalizer で挟み、`stack.IsReceiverLelia` が真のときだけ `stack.m_DamageParameter.m_AbnormalRate` を一時上書きする |
| 根拠 | `OneDamage` は1件のダメージ解決の単位を引数に取り、その `DamageStack` から受け側判別と対象の `DamageParameter` の両方に到達できる。上書きの寿命が1回の解決に閉じる |
| 影響 | A-3 の実測で付与判定が `OneDamage` の外だった場合、この機構だけが無効となる。他機構は影響を受けない |
| 代替案 | `AbnormalList.AddAbnormal` の prefix。付与が決まった後にしか呼ばれないため付与率には作用できず、不採用 |

| 項目 | 内容 |
|---|---|
| 論点 | `m_AbnormalRate` の値域が未測（付録A A-4）。倍率適用後の上限をどう置くか |
| 選択 | 上限を 100 と仮定しつつ、**元の値より小さい結果を返さない** |
| 根拠 | 値域が 0-100 でなかった場合、素朴に 100 で丸めると難易度上昇のはずの操作が低下になる。仮定が外れても方向だけは保証する |
| 影響 | 値域が 0-100 以外なら効きが弱くなるが、逆転はしない。A-4 の実測で定数を変えるだけで済む |
| 代替案 | 上限を置かない案。ロール実装が飽和しない場合に極端な値が入る |

| 項目 | 内容 |
|---|---|
| 論点 | `RecoveryInvincibleScale`（離脱後無敵の短縮）の適用先が未測（付録A A-14） |
| 選択 | 設定は受け付けるが適用しない。既定 1.0 のままなら何も起きず、1.0 未満が設定された場合は起動時に未適用である旨を警告する |
| 根拠 | `DamageInvincibleMsv` は真偽値であり「短縮」を表現できない。無敵を丸ごと剥がすのは仕様の意図と異なる |
| 影響 | 5.4 の復帰遅延は移動速度低下のみで成立する。再拘束容易化の一部が未実装 |
| 代替案 | `DamageInvincibleMsv` へ `false` を寄与する案。短縮ではなく無効化になるため不採用 |

| 項目 | 内容 |
|---|---|
| 論点 | `MultiSettingValue<T>.ResitValue` のキーが `Il2CppSystem.Object` 参照であり、文字列ではない |
| 選択 | プラグインが起動時に `Il2CppSystem.Object` を1つ作り、MOD の同一性として保持する |
| 根拠 | 寄与の登録と解除が同一参照でしか対応しない。プラグインの寿命と一致させるのが最も単純で、解除漏れの経路が減る |
| 影響 | `Unload` でキーを破棄する前に台帳を空にする順序が必須。`DifficultyPlugin.Unload` はその順で処理する |
| 代替案 | 呼び出しごとに新しいオブジェクトを作る案。解除できなくなるため不採用 |

## 完了監査

- すべての必須要件（FR-101〜FR-134）に実装箇所がある。
- Design-time の要件は自動ソース走査で固定されており、退行するとテストが落ちる。
- 仕様外の変更は混入していない。EDI 側のファイル（`src/SiNiSistar2.Edi.*`、`Edi/**`、`BepInEx/plugins/community.sinisistar2.edi/**`、`SPEC001*`）は未変更。`SiNiSistar2.Edi.sln` へは3プロジェクトの追加のみ。
- 未検証項目は実機テストシナリオへ引き継ぎ済み。
