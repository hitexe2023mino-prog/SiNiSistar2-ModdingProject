# SPEC003 implementation traceability

Normative source: [`docs/specifications/SPEC003.md`](../specifications/SPEC003.md). This document records implementation and verification only; it does not change the specification.

In-game steps live in [`docs/testing/SPEC003-test-scenarios.md`](../testing/SPEC003-test-scenarios.md).

## Stage

**v1.2（2026-08-09）。** 敵の同定モデルを改訂した。拘束相手は `GalleryEnemyID` → `EnemyID` → 正規化オブジェクト名の順で名指し、`None` は識別子として扱わない。編集画面は両列挙体を列挙し、拘束時に取得した表示名を併記する。不具合報告「大口のワームの性的／非性的攻撃が設定不可」の修正であり、`m_GalleryEnemyID` が未設定の敵がすべて `None` の1行を共有していたのが原因である。

**CHG-017（同日、レビュー指摘）。** 拘束相手を `Bind.BinderEnemy` だけで見ていたため、`EnemyObject` でない拘束者（`ParasiteTentacle`、`ParasiteBullet`、`StoneEye`）の拘束は依然として名指せなかった。`Bind.Binder`（`IBinder`）を入口に加えて解決する。同型の不具合が SPEC001 側にもあり、そちらは CHG-037 で直した。

**v1.2 で実機確認が要る2点。** A-53（拘束相手がどの順で解決されるか。とくに `m_EnemyID` が設定されているか）と A-54（表示名を取得できるか）。どちらも `[probe] binder-source-*` の1行に出る。順3まで落ちてもカタログは機能する。

**v1.1（2026-08-09）。** 拘束中の敗北条件の置き換え方を改訂した。性的被弾はHPを削らず、絶頂限界の到達がその場でHPを `0` にする。v1.0 の `RemainHp1Msv` 寄与は撤去した。快楽ゲージ、絶頂、堕落・淫紋、母乳ゲージ、サイドカーは v1.0 のまま動いている。

**v1.1 で実機確認が要る2点。** A-50（`BattleMainParameter.DontSub` が減算を止めるか）と A-51（HPを `0` にできるか、そこからゲームの死亡経路が走るか）。どちらも実装側にフォールバックがあり、どの手段が効いたかは `[probe]` 行に出る。

Every tuning value ships at no-change (FR-233) **without exception**: `SuppressSexualHpDamage` は既定で真だが、`PleasureGainPerHit` が `0` の間は抑止そのものが働かない（FR-278）。したがって新規導入した時点ではゲームの挙動が何も変わらない。

## Status meanings

- **Tested** — implemented and covered by an automated test.
- **Implemented / unverified** — code exists, in-game evidence outstanding.
- **Probe** — the code present measures the question rather than answering it.
- **Not started** — deliberately deferred to the next stage.

## Automated coverage

`dotnet test SiNiSistar2.Edi.sln -c Release` → 118 for this MOD, 89 for SPEC002, 143 for SPEC001, 0 failures.

## 要件台帳

| ID | 要件（要約） | 実装箇所 | 検証 | 状態 |
|---|---|---|---|---|
| FR-201 | GUID `community.sinisistar2.pleasure` のプラグイン | `PleasurePlugin` | ビルドと配置 | Tested |
| FR-202 | ゲームバイナリ・アセット・セーブ・フラグ変数を書き換えない | 全体（実行時パッチと寄与のみ） | AC-201 実機 | Implemented / unverified |
| FR-203 | 拘束中の性的被弾はHPを1点も減らさない（被弾の解決の間だけ） | `DamageProbePatches.OneDamagePrefix`、`PlayerHealth.Hold` / `Release` | 3.1.1 の A-50、AC-202 実機 | Implemented / unverified |
| FR-204 | 抑止した状態を完了・例外・脱出・遷移・巻き戻しで必ず戻す | `OneDamagePostfix` 冒頭、`OneDamageFinalizer`、`PleasureObserver.SweepStaleHpHold`、`Suspend` | AC-203 実機 | Implemented / unverified |
| FR-205 | 抑止するのはHPの減算だけ。演出・状態異常・MPは通す | `PlayerHealth` はHPしか触らない | AC-202 実機 | Implemented / unverified |
| FR-206 | 非性的な被弾・攻撃以外の減少・拘束外に介入しない | 前提を `IsBound` と5.3の判別に限定 | AC-204、AC-239、AC-240 実機 | Implemented / unverified |
| FR-207 | 拘束中かつ性的攻撃のときだけ快楽を上げる | `DamageProbePatches.OneDamagePostfix`、`PleasureMeter` | `PleasureMeterTests`（7件）、AC-205 実機 | Tested / unverified |
| FR-208 | 5.3の順序で判別、不明は非性的 | `SexualAttackClassifier`、`EnemyAttackCatalog` | `SexualAttackClassifierTests`（6件）、`EnemyAttackCatalogTests`（4件） | Tested |
| FR-209 | 感度が快楽の上昇量を増やす | `PleasureMeter.AddSexualHit` | `PleasureMeterTests.SensitivityIncreasesTheGainPerHit` | Tested |
| FR-210 | 拘束外で減衰、拘束中は減衰しない | `PleasureObserver.DecayWhenFree`、`PleasureMeter.Decay` | `PleasureMeterTests`（2件）、AC-208 実機 | Tested / unverified |
| FR-211 | 上限到達で1回だけ絶頂処理 | `PleasureMeter.ConsumeClimax`、`PleasureObserver.ConsumeClimax` | `PleasureMeterTests.AFullGaugeYieldsExactlyOneClimax` | Tested |
| FR-212 | 絶頂演出はMOD描画、`timeScale` を変えない | `PleasureObserver.DrawClimaxFlash` / `DrawVignette`、`PleasureArt` | AC-210 実機 | Implemented / unverified |
| FR-213 | 演出が出なくても状態変化と判定は行う | `PleasureObserver.ConsumeClimax`（演出に依存しない） | AC-211 | Implemented / unverified |
| FR-214 | 限界は基礎値＋耐久から算出、読めなければ基礎値 | `ClimaxLimit.Compute`、`PleasureObserver.IsAtClimaxLimit` | `SensitivityAndClimaxTests`（4件）、3.3 の A-6 | Tested / unverified |
| FR-215 | 限界到達でMODがHPを `0` にする。ゲームオーバー処理は呼ばない | `PleasureObserver.ApplyClimaxLimit`、`PlayerHealth.Kill` | `ClimaxLethalityTests`（8件）、3.1.2 の A-51、AC-213 実機 | Tested / unverified |
| FR-216 | 契機は絶頂の成立時のみ。死亡中・発火済みは行わない | `ClimaxLethality.ShouldBeLethal`、`PleasureRuntime.ClimaxDeathFired` | `ClimaxLethalityTests`、AC-229、AC-243 実機 | Tested / unverified |
| FR-275 | HP操作はゲーム自身のAPI。効かなければ次の手段へ | `PlayerHealth`（`DontSub`→書き戻し、`SubAll`→`SetCurrentValue`→`RequestCommonDead`） | A-50、A-51 実機 | Implemented / unverified |
| FR-276 | `SuppressSexualHpDamage=false` でも快楽・絶頂・堕落・致死化は成立 | `PleasureProfile.BlocksSexualHpDamage` のみを分岐に使う | `PleasureProfileTests.TurningTheSuppressionOffLeavesTheGaugeRunning`、AC-241 実機 | Tested / unverified |
| FR-277 | `RemainHp1Msv` へ寄与しない | 当該経路と寄与キーを撤去 | AC-203 実機 | Implemented / unverified |
| FR-278 | 快楽が上がらない設定では抑止しない | `PleasureProfile.BlocksSexualHpDamage` | `PleasureProfileTests`（3件）、AC-242 実機 | Tested / unverified |
| FR-279 | `EnableClimaxGameOver` が偽なら致死化しない。事実は記録する | `ClimaxLethality.ShouldBeLethal`、`ApplyClimaxLimit` のログ | `ClimaxLethalityTests.TheLimitDoesNothingWhenTheGameOverIsOff`、AC-244 実機 | Tested / unverified |
| FR-217 | セーブポイント／オベリスクで絶頂回数を0へ、感度は戻さない | `SavePointPatches.SetObeliskModePostfix` | `SensitivityAndClimaxTests.ResettingTheCount...`、3.4 の A-7 | Tested / unverified |
| FR-218 | 絶頂と性的被弾で感度が増える | `PleasureObserver.ConsumeClimax`、`DamageProbePatches` | `SensitivityAndClimaxTests` | Tested / unverified |
| FR-219 | 感度を減少させる経路を持たない | `SensitivityTrack.Add`（非正を無視） | `SensitivityAndClimaxTests.SensitivityNeverFalls`、AC-215 実機 | Tested |
| FR-220 | 感度は上限で頭打ち、減少ではない | `SensitivityTrack` | `SensitivityAndClimaxTests.TheCapStopsGrowth...` | Tested |
| FR-221 | 条件成立で `BreastSuper` へ遷移、正規経路で付与 | `BreastEscalation`、`BreastPatches`、`PleasureObserver.ApplyPendingBreastSuper` | `BreastEscalationTests`（7件）、AC-217 実機 | Tested / unverified |
| FR-240 | 最大レベルでの付与のみ計数、上限を読めなければ遷移しない | `BreastEscalation.Record`、`BreastPatches.MaxLevel`（読めなければ0） | `BreastEscalationTests.ApplicationsBelowTheMaximum...`、AC-235 実機 | Tested / unverified |
| FR-241 | 治療手段を新設しない | 治療コードなし。`MakeHaanjaCurable` は `m_HaanjaCanCure` を立てるのみ | 3.8 の A-14、AC-237 実機 | Design-time |
| FR-242 | `AbnormalData` の変更を記録しアンロードで戻す | `PleasureObserver.ApplyHaanjaCurableOverride`、`InterventionLedger` | AC-237 実機 | Implemented / unverified |
| FR-244 | 付与経路を問わず計数、二重計上なし | `AbnormalList` の3経路と `AbnormalConditionLabel.ExecutionOne`、`BreastPatches.ClaimThisFrame`（フレーム単位で1回） | `BreastEscalationTests.TheSourceOfTheApplication...`、AC-238 実機 | Tested / unverified |
| FR-245 | 適用前に `BreastSuper` の読み込みを要求 | `PleasureObserver.RequestBreastSuperLoad`（`AbnormalManager.PreloadResist`） | AC-217 実機 | Implemented / unverified |
| FR-246 | 停止中は付与しない | `PleasureObserver.ApplyPendingBreastSuper`（`Time.timeScale <= 0` で保留） | 3.8 の A-16 | Implemented / unverified |
| FR-247 | IL2CPP同一性はポインタで判定 | `BreastPatches.IsPlayer` | 3.8 実機 | Implemented / unverified |
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
| FR-232 | 判定と直列化をゲーム非依存層へ | `SiNiSistar2.Pleasure.Core`（ゲーム参照ゼロ） | 83件がゲーム起動なしで実行 | Tested |
| FR-233 | 未実測の既定は無変更相当。例外なし | `PleasureOptions` の既定値、`PleasureProfile.BlocksSexualHpDamage` | `PleasureProfileTests.ShippedDefaultsChangeNothing` | Tested |
| FR-234 | `Enabled=false` でパッチも随伴ファイルもなし | `PleasurePlugin.Load` の早期 return | `PleasureProfileTests.DisablingTheMod...` | Tested |
| FR-235 | 敵別分類を独立したカタログファイルへ、既存設定を種に | `EnemyAttackCatalogStore`、`EnemyAttackCatalog.SeedFrom`、`PleasurePlugin.LoadEnemyCatalog` | `EnemyAttackCatalogTests.TheOldConfigListsSeed...` ほか、AC-230 実機 | Tested / unverified |
| FR-236 | ゲーム内編集画面、再起動なしで反映 | `EnemyCatalogEditor`（F10）、分類器がカタログを参照保持 | `EnemyAttackCatalogTests.AnEditAppliesWithoutRebuilding...`、AC-231 実機 | Tested / unverified |
| FR-237 | `GalleryEnemyID` と `EnemyID` の全件（`None` 除く）＋発見した `obj:` 行を列挙し、拘束経験のある敵を先頭に | `PleasurePlugin.KnownEnemyIds`（両列挙体）、`EnemyAttackCatalog.AddMissing` / `Rows` | `EnemyAttackCatalogTests.GalleryDecisionsSurviveTheWiderNamespace`、`MetEnemiesSortAheadOfUnmetOnes`、AC-232 実機 | Tested / unverified |
| FR-280 | 敵識別子を `GalleryEnemyID` → `EnemyID` → `obj:` 正規化名の順に解決。拘束相手は `Bind.Binder` を含める | `BinderIdentityResolver.Resolve` / `Captor`（`Bind.Binder` を `SiNiObject` へ、必要なら `EnemyObject` へ TryCast）、`EnemyIds.FromObjectName`、`PleasureObserver.ResolveBinder` | `EnemyIdFromObjectNameTests`（3件）。順の実測は A-53（`[probe] binder-source-*`）、AC-249 実機 | Implemented / probe |
| FR-281 | `None` を識別子として扱わない。引かない、書かない、既存行は破棄 | `EnemyIds.IsUsable`、`EnemyAttackCatalog.Absorb` / `Set` / `MarkSeen` / `AddMissing`、`PleasurePlugin.LoadEnemyCatalog` の警告 | `EnemyAttackCatalogTests.UnsetIsNotAnIdentifier`、`ALeftoverUnsetRowIsDroppedAndReported`、AC-246 実機 | Tested / unverified |
| FR-282 | 拘束時に表示名を取得しカタログへ保存。取得できなくても分類を妨げない | `BinderIdentityResolver.DisplayNameOf`（`DisplayEnemyNameID` → `LocalizeManager.GetLcText`）、`EnemyAttackCatalog.MarkSeen`、`EnemyCatalogEditor.DrawRow` | `EnemyAttackCatalogTests.TheDisplayNameIsLearnedOnSighting...`、`TheFileHoldsTheFieldsTheSpecificationLists`。実機は A-54、AC-247 | Tested / unverified |
| FR-283 | v1.1 までの `GaID_` 行を無効化・改名しない。`schemaVersion` は `1` のまま | 解決順の先頭が `GalleryEnemyID`（`BinderIdentityResolver`）、`EnemyAttackDocument.CurrentSchemaVersion = 1` | `EnemyAttackCatalogTests.GalleryDecisionsSurviveTheWiderNamespace`、AC-248 実機 | Tested / unverified |
| FR-238 | 保存と取り消しの区別、取り消しで開始時点へ | `EnemyCatalogEditor.Commit` / `Cancel`、`EnemyAttackCatalog.RestoreFrom` | `EnemyAttackCatalogTests.CancellingRestores...`、AC-233 実機 | Tested / unverified |
| FR-243 | 旧スキーマ版の随伴ファイルを読む | `SidecarDocument.Parse`（新しい版のみ拒否） | `SidecarStoreTests.AnOlderSchemaIsRead...` | Tested |
| FR-239 | カタログの原子的書き込み、非対応版を上書きしない、失敗で止めない | `EnemyAttackCatalogStore`、`JsonFile` | `EnemyAttackCatalogStoreTests`（6件） | Tested |

## 判断記録

| 項目 | 内容 |
|---|---|
| 論点 | A-50 の手段を実装時に1つへ確定できない（`DontSub` が減算を止めるかは実機でしか分からない） |
| 選択 | どちらかを選ばず**両方を毎回走らせる**。被弾ごとに `DontSub` を立て、解決後に元へ戻し、それでもHPが減っていれば減った分を書き戻す |
| 根拠 | 「最初の1発で測って以後はその結論を信じる」形も書けるが、その1発がたまたま無ダメージだった場合に「`DontSub` は効く」と誤って確定し、以後の被弾が素通りする。比較1回で誤りようがなくなるなら、そちらが安い |
| 影響 | A-50 は実装の分岐ではなくログの内容になった。`[probe] hp-held-off` と `hp-put-back` のどちらが出るかで答えが決まる |
| 代替案 | 初回計測で手段を確定する、`DontSub` だけに賭ける、書き戻しだけにする |

| 項目 | 内容 |
|---|---|
| 論点 | `DontSub` を立てたまま復元に失敗すると、拘束外を含めプレイヤーが一切傷つかなくなる（FR-204 の最悪の失敗） |
| 選択 | 復元を4重にする。postfix の冒頭、Harmony の finalizer、`PleasureObserver` の毎フレーム掃き取り、`Suspend`（シーン遷移・停止） |
| 根拠 | HarmonyX の finalizer が IL2CPP でこのメソッドに効くかは実機で確かめるまで断定できない。効かなかった場合に残るのが「無敵」であるため、効かなくても1フレームを越えられない構えにする |
| 影響 | 掃き取りが発火した場合は警告が出るので、finalizer が効いていないことは黙って進行しない |
| 代替案 | finalizer だけに任せる、prefix で `try` を張る（IL2CPP のパッチでは張れない） |

| 項目 | 内容 |
|---|---|
| 論点 | `SubAll(bool)` と `SetCurrentValue(int, bool)` の bool の意味が interop から読めない |
| 選択 | `SubAll(false)` → `SubAll(true)` → `SetCurrentValue(0,false)` → `SetCurrentValue(0,true)` → `RequestCommonDead` の順に試し、**各回のあとで `HP.Current` を見る**。0 になった時点で止める |
| 根拠 | 引数の意味を推測して1つ選ぶより、結果を見て次へ進むほうが確実である。効いた手段は `[probe] climax-death-method` に出るので A-51 はプレイ1回で確定する |
| 影響 | 実装は確定を待たずに出せる。仕様 5.5.3 の3段の手段はそのまま実装の順序になっている |
| 代替案 | 実測まで実装を止める、`RequestCommonDead` を第一手段にする |

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
| 選択 | `SavePointMenu.SetObeliskMode(bool)` の postfix、引数でオベリスクを判別 |
| 根拠 | 当初は `SavePointAsyncLabel.ExecutionOneAsync` の postfix だったが、同メソッドは `UniTask`（構造体）を返し、IL2CPP で構造体返しメソッドへ detour を張ると戻り値が壊れる。実機ではセーブダイアログが開いた瞬間に自動で閉じ、セーブもレベルアップも不能になった。`SetObeliskMode` は void 返しで、メニューが開くたびに走り、オベリスク判別を引数で運ぶ |
| 影響 | 発火時点が「ラベル実行開始時」から「メニュー表示時」へ移ったが、いずれも像の作動と同時であり FR-217 の意味は変わらない。教訓: この実行環境では void 返しのメソッドだけをパッチ対象にする |
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

| 項目 | 内容 |
|---|---|
| 論点 | 敵別の性的・非性的の宣言をどこに置き、どこから編集させるか |
| 選択 | 設定文字列から独立したカタログファイルへ移し、`F10` のゲーム内画面から編集する。分類器はカタログを参照で保持する |
| 根拠 | 対象は `GalleryEnemyID` 108件でカンマ区切りの1行に収まらない。さらに、その敵の攻撃が性的かどうかは画面を見て下す判断であり、ゲームを終了して編集し再起動する経路では対象が目の前にない状態で決めることになる |
| 影響 | 編集が次の被弾から効く。`SexualEnemyIds` / `NonSexualEnemyIds` はカタログ新規作成時の種としてのみ残る |
| 代替案 | `ConfigEntry` の文字列を増やす、外部エディタ用のCSVのみ、編集のたびにプロファイル再構築 |

| 項目 | 内容 |
|---|---|
| 論点 | 編集画面をどのIMGUI呼び出しで作るか |
| 選択 | `GUI.Label` と `GUI.Box` だけで描き、行の当たり判定を自前で行う |
| 根拠 | `GUI.Button`、`GUI.BeginScrollView`、`GUI.TextField` はいずれも interop メタデータにあるが、`GUI.DrawTexture` も同様にありながら本ビルドでは実行時に `Method unstripping failed` を投げた。実機で成功が確認できている呼び出しだけに限る |
| 影響 | スクロールと選択を自前で持つ分だけ実装が増えたが、実行時に失敗し得る面が増えない |
| 代替案 | `GUI.Button` と `BeginScrollView` を使う、uGUI のキャンバスを立てる |

| 項目 | 内容 |
|---|---|
| 論点 | アイテムを使っても何も起こらず、プローブも1行も出なかった |
| 原因 | `ReferenceEquals` でプレイヤーの状態異常リストを判定していた。Il2CppInterop はHarmonyのpostfixへ独自のラッパーを渡すため常に偽になり、すべての付与が無言で捨てられていた |
| 二次的な誤り | 診断の出力をその判定の**後ろ**に置いていたため、判定が壊れるとログが完全に沈黙した。「何も付与されていない」と「すべて捨てられた」が区別できない |
| 選択 | ポインタ比較へ変更し（SPEC002 が既にそうしている）、診断は判定の前に出して付与先を注記する |
| 併せて | `AbnormalConditionLabel.ExecutionOne` にもpostfixを追加した。IL2CPPのインライン化で `AddAbnormal` の呼び出しが消えている可能性があり、SPEC002 が `GachaGachaSystem.Execution` で同じ失敗を経験している |

## 完了監査

- **未着手の要件はない。** ただし FR-241（治療）は **Design-time** であり、既存の治療経路が `BreastSuper` へ届くかは実機の A-14 待ちである。届かないと判明した場合に限り `MakeHaanjaCurable` を既定で有効にする。それでも届かない場合は延期事項へ移す。
- `BreastSuperChance`（確率付与）は廃止し、`BreastSuperAfterApplications`（最大レベルでの付与回数）へ置き換えた。要求が「一定回数受けた場合に遷移」であり、確率では回数を表現できない。
- 新規の FR-235〜239 は Core 側を単体テストで、ゲーム内編集画面（`EnemyCatalogEditor`）を **Implemented / unverified** として扱う。実機確認は AC-230〜234 と 付録A A-13。
- 実機で確認済み: HP0 抑止（A-1）、拘束中の被弾の観測（A-2）、状態異常の同伴（A-3）、耐久値（A-6）、セーブポイント検出（A-7）、スロット識別（A-9）。
- SPEC001 と SPEC002 のファイルは未変更。`SiNiSistar2.Edi.sln` へは3プロジェクトの追加のみ。
- 未検証項目は実機テストシナリオ3章へ引き継ぎ済み。
