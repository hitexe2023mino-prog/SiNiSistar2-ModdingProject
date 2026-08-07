# SPEC003 implementation traceability

Normative source: [`docs/specifications/SPEC003.md`](../specifications/SPEC003.md). This document records implementation and verification only; it does not change the specification.

In-game steps live in [`docs/testing/SPEC003-test-scenarios.md`](../testing/SPEC003-test-scenarios.md).

## Stage

This is the **probe stage**. The only behaviour change is removing the HP0 defeat inside a hold; every tuning value ships at no-change (SPEC003 FR-233) and the plugin records what 付録A still needs. The gauge, the climax overlay, the sidecar and `BreastSuper` are specified and partly built but not yet wired to anything the player sees.

## Status meanings

- **Tested** — implemented and covered by an automated test.
- **Implemented / unverified** — code exists, in-game evidence outstanding.
- **Probe** — the code present measures the question rather than answering it.
- **Not started** — deliberately deferred to the next stage.

## Automated coverage

`dotnet test SiNiSistar2.Edi.sln -c Release` → 54 for this MOD, 89 for SPEC002, 143 for SPEC001, 0 failures.

## 要件台帳

| ID | 要件（要約） | 実装箇所 | 検証 | 状態 |
|---|---|---|---|---|
| FR-201 | GUID `community.sinisistar2.pleasure` のプラグイン | `PleasurePlugin` | ビルドと配置 | Tested |
| FR-202 | ゲームバイナリ・アセット・セーブ・フラグ変数を書き換えない | 全体（実行時パッチと寄与のみ） | AC-201 実機 | Implemented / unverified |
| FR-203 | 拘束中に `RemainHp1Msv` へ寄与しHPを1で止める | `PleasureObserver.UpdateHp0Suppression` | 3.1 の A-1、AC-202 | Implemented / unverified |
| FR-204 | 拘束終了・シーン遷移・巻き戻しで寄与を解除 | `PleasureObserver.Suspend` / `UpdateHp0Suppression`、`InterventionLedger` | AC-203 実機 | Implemented / unverified |
| FR-205 | HPダメージと演出を抑止しない | 寄与のみで値を書かない設計 | AC-202 実機 | Implemented / unverified |
| FR-206 | 拘束外のHP0に介入しない | 寄与の条件を `bound` に限定 | AC-204 実機 | Implemented / unverified |
| FR-207 | 拘束中かつ性的攻撃のときだけ快楽を上げる | `DamageProbePatches.OneDamagePostfix`、`PleasureMeter` | `PleasureMeterTests`（7件）、AC-205 実機 | Tested / unverified |
| FR-208 | 5.3の順序で判別、不明は非性的 | `SexualAttackClassifier` | `SexualAttackClassifierTests`（6件） | Tested |
| FR-209 | 感度が快楽の上昇量を増やす | `PleasureMeter.AddSexualHit` | `PleasureMeterTests.SensitivityIncreasesTheGainPerHit` | Tested |
| FR-210 | 拘束外で減衰、拘束中は減衰しない | `PleasureObserver.DecayWhenFree`、`PleasureMeter.Decay` | `PleasureMeterTests`（2件）、AC-208 実機 | Tested / unverified |
| FR-211 | 上限到達で1回だけ絶頂処理 | `PleasureMeter.ConsumeClimax`、`PleasureObserver.ConsumeClimax` | `PleasureMeterTests.AFullGaugeYieldsExactlyOneClimax` | Tested |
| FR-212 | 絶頂演出はMOD描画、`timeScale` を変えない | — | AC-210 | Not started |
| FR-213 | 演出が出なくても状態変化と判定は行う | `PleasureObserver.ConsumeClimax`（演出に依存しない） | AC-211 | Implemented / unverified |
| FR-214 | 限界は基礎値＋耐久から算出、読めなければ基礎値 | `ClimaxLimit.Compute`、`PleasureObserver.IsAtClimaxLimit` | `SensitivityAndClimaxTests`（4件）、3.3 の A-6 | Tested / unverified |
| FR-215 | 限界到達でHP0抑止を解除し既存経路へ委ねる | `PleasureObserver.UpdateHp0Suppression` | AC-213 実機 | Implemented / unverified |
| FR-216 | 限界以上の間は寄与を再登録しない | 同上（`atLimit` 判定） | AC-229 実機 | Implemented / unverified |
| FR-217 | セーブポイント／オベリスクで絶頂回数を0へ、感度は戻さない | `SavePointPatches.ExecutionOneAsyncPostfix` | `SensitivityAndClimaxTests.ResettingTheCount...`、3.4 の A-7 | Tested / unverified |
| FR-218 | 絶頂と性的被弾で感度が増える | `PleasureObserver.ConsumeClimax`、`DamageProbePatches` | `SensitivityAndClimaxTests` | Tested / unverified |
| FR-219 | 感度を減少させる経路を持たない | `SensitivityTrack.Add`（非正を無視） | `SensitivityAndClimaxTests.SensitivityNeverFalls`、AC-215 実機 | Tested |
| FR-220 | 感度は上限で頭打ち、減少ではない | `SensitivityTrack` | `SensitivityAndClimaxTests.TheCapStopsGrowth...` | Tested |
| FR-221 | 条件成立で `BreastSuper` を正規経路で付与 | — | AC-217 | Not started |
| FR-222 | 感度と絶頂回数をスロット単位で保存しセーブに同期 | `SidecarStore`、`PleasureRuntime.LoadSlot` / `SaveSlot`、`PleasureObserver.ProbeSaveSlot`、`SavePointPatches` | `SidecarStoreTests`（8件）、`SidecarDocumentTests`、AC-218 実機 | Tested / unverified |
| FR-223 | 随伴ファイルの書き込みは原子的 | `SidecarStore.Save`（一時ファイル＋置換） | `SidecarStoreTests.SavingLeavesNoTemporaryFile` ほか | Tested |
| FR-224 | ファイルがなければ初期値 | `SidecarDocument.Parse` | `SidecarDocumentTests` | Tested |
| FR-225 | 非対応スキーマは読まず上書きしない | `SidecarDocument.Parse`、`SidecarStore`（スロットを施錠） | `SidecarStoreTests.ANewerSchemaLocksTheSlot...` | Tested |
| FR-226 | 随伴ファイルの失敗でゲームを止めない | `SidecarStore`（失敗を戻り値で返す）、`PleasureRuntime.SaveSlot` | `SidecarStoreTests.AnUnwritableRoot...` ほか | Tested |
| FR-227 | 他MODへ依存宣言しない | `PleasurePlugin`（`BepInDependency` なし） | AC-222 実機 | Implemented / unverified |
| FR-228 | SPEC001が依存する面を変更しない | 参照なし | AC-223 実機 | Implemented / unverified |
| FR-229 | SPEC002の管理面へ介入しない | 参照なし | AC-223 実機 | Implemented / unverified |
| FR-230 | メインスレッドで完結、待機しない | `PleasureObserver`（同期処理のみ） | AC-224 実機 | Implemented / unverified |
| FR-231 | 起動ログに構成を記録 | `PleasurePlugin.Load` | AC-225 実機 | Implemented / unverified |
| FR-232 | 判定と直列化をゲーム非依存層へ | `SiNiSistar2.Pleasure.Core`（ゲーム参照ゼロ） | 42件がゲーム起動なしで実行 | Tested |
| FR-233 | 未実測の既定は無変更相当、HP0抑止のみ例外 | `PleasureOptions` の既定値 | `PleasureProfileTests.ShippedDefaults...` | Tested |
| FR-234 | `Enabled=false` でパッチも随伴ファイルもなし | `PleasurePlugin.Load` の早期 return | `PleasureProfileTests.DisablingTheMod...` | Tested |

## 判断記録

| 項目 | 内容 |
|---|---|
| 論点 | 付録A の A-4（ゲームオーバーの発火手段）と A-5（HP0を経由するか） |
| 選択 | 限界到達時に `RemainHp1Msv` の寄与を登録しないだけとし、ゲームオーバー処理を呼ばない |
| 根拠 | interop 調査で `GameOverLabel.ExecutionOne(GameOverParameter)` と `HpControlType` を確認したが、自前で呼べば復帰処理・ペナルティ・演出の再現責任をMODが負う。抑止を外すだけなら、以降はゲーム自身が普段どおり走る |
| 影響 | SPEC001 の `game-over` トリガーが従来どおり成立する。実測項目が2件減った |
| 代替案 | `Lelia.RequestCommonDead` を立てる、`GameOverLabel` を自前で呼ぶ |

| 項目 | 内容 |
|---|---|
| 論点 | 付録A の A-7（セーブポイントのアクティベート検出） |
| 選択 | `SavePointAsyncLabel.ExecutionOneAsync` の postfix、`IsObeliskLabel` でオベリスクを判別 |
| 根拠 | interop に当該メソッドと `IsObeliskLabel` が実在し、`SavePointMenu.SetObeliskMode(bool)` が裏付けになる。`SavePointSelector.IsObeliskActive` は地点の形態を表すもので作動イベントではない |
| 影響 | 実測は「シーン設定時ではなく作動時に走ること」の確認だけに縮小した |
| 代替案 | セーブ完了（`MainSaveData.IsAutoSave` で手動を判別）を契機にする |

| 項目 | 内容 |
|---|---|
| 論点 | オベリスク限定でリセットすると、難易度によってリセット地点が消え得る |
| 選択 | `ResetAtObeliskOnly` を設定として出し、既定を偽（どのセーブポイントでもリセット）とする |
| 根拠 | `SavePointSelector.m_ChangeObeliskInHardMode` により、難易度でセーブポイントがオベリスクへ置き換わる。オベリスク限定を既定にすると、難易度によってはリセット手段のない構成が生まれる |
| 影響 | ユーザーの「聖なる像など」という表現の範囲に収まる。限定したい場合は設定で切り替えられる |
| 代替案 | オベリスク限定を既定にする、常に両方でリセットし設定を持たない |

| 項目 | 内容 |
|---|---|
| 論点 | 実測前に全機構を実装するか、計測を先に回すか |
| 選択 | HP0抑止と計測だけを実装し、快楽以降の値は無変更相当のまま出す |
| 根拠 | A-2（拘束中の被弾を観測できるか）が否定されると、快楽の上昇契機ごと設計をやり直す必要がある。SPEC002 で `Execution` のパッチが当たらないことを実測ログで一度に特定できた経験と同じ形にした |
| 影響 | 1回のプレイで A-1、A-2、A-3、A-6、A-7、A-9 がまとめて確定する |
| 代替案 | 全機構を実装してから実機で調整する |

## 完了監査

- 未着手の要件（FR-212、FR-221、FR-223、および FR-222 の同期部分）は、状態欄に **Not started** と明記した。仕様が満たされたと誤読される箇所はない。
- SPEC001 と SPEC002 のファイルは未変更。`SiNiSistar2.Edi.sln` へは3プロジェクトの追加のみ。
- 未検証項目は実機テストシナリオ3章へ引き継ぎ済み。
