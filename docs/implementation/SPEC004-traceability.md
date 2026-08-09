# SPEC004 implementation traceability

Normative source: [`docs/specifications/SPEC004.md`](../specifications/SPEC004.md) (v1.2). This document records implementation and verification only; it does not change the specification.

In-game verification steps live in [`docs/testing/SPEC004-test-scenarios.md`](../testing/SPEC004-test-scenarios.md). Every row marked *Implemented / unverified* is covered there. Rows marked *Gated* wait on a 付録A measurement.

## Status meanings

- **Tested** — implemented and covered by an automated test.
- **Implemented / unverified** — code exists, but the required in-game evidence is outstanding.
- **Design-time** — satisfied by not doing something. Held in place by an automated source scan.
- **Gated** — the behaviour depends on a 付録A measurement that has not been taken.

## Automated coverage

`dotnet test .\tests\SiNiSistar2.Spawn.Core.Tests\SiNiSistar2.Spawn.Core.Tests.csproj -c Release` → 99 tests, 0 failures.

`ForbiddenSurfaceTests` scans `src/SiNiSistar2.Spawn.Plugin/**/*.cs` with comments stripped and fails the build if a forbidden member is assigned or named (`*Hard` field assignment, treasure box members, `ForceDeadDamage`, `UnityEngine.Random`, save members, plugin dependencies, the game's UI and `timeScale`). It also asserts that `SpawnHud` never reaches for `ManagerList` or the profile, so FR-327's read-only rule holds structurally rather than by review.

## 要件台帳

| ID | 要件（要約） | 実装箇所 | 検証 | 状態 |
|---|---|---|---|---|
| FR-301 | GUID `community.sinisistar2.spawn` の IL2CPP プラグイン | `SpawnPlugin` | ビルドと `BepInEx/plugins/community.sinisistar2.spawn` への配置 | Tested |
| FR-302 | バイナリ・アセット・セーブを書き換えない | 全体（実行時介入のみ）。報酬付与だけがゲーム自身の在庫経路 | `ForbiddenSurfaceTests.TheSaveIsNeverWritten`、AC-307 実機 | Design-time / unverified |
| FR-303 | エリア入場毎に `CurrentSceneID` でプロファイル解決 | `SpawnObserver.EnterArea`、`SpawnProfile.Resolve` | `SpawnProfileFactoryTests`（override 解決 3件）、AC-301 実機 | Tested / unverified |
| FR-304 | 出現源はシーン内スポナーのプール設定のみ | `AdditionalSpawner.PickWeightedPool`、`MimicBoxPlacement`（外部ロード経路なし） | AC-302 実機 | Implemented / unverified |
| FR-305 | 倍率は Hard 解決後の基準値へ、`*Hard` は書かない | `SpawnerTuningLedger.Tune`（`SafeIsHardMode` で基準選択、書き込みは平フィールドのみ） | `ForbiddenSurfaceTests.TheHardOverrideFieldsAreNeverAssigned`（10件）、AC-309 実機 | Tested / unverified |
| FR-306 | 書き換えの退避と復元 | `SpawnerTuningLedger`（原値記録、`RestoreAll`）、`SpawnObserver.Rollback` | AC-307 実機（シーン内無効化） | Implemented / unverified |
| FR-307 | 出現位置は出現位置集合から。条件を満たさなければ出さない | `AdditionalSpawner.CollectCandidatePoints`（候補ゼロでスキップ） | `SpawnPointClassifierTests`（12件）、AC-303 実機 | Tested / unverified |
| FR-308 | 上限遵守、プール枯渇は静かにスキップ | `SpawnBudget`、`AdditionalSpawner.TrySpawnFrom`（null → false） | `SpawnBudgetTests`（3件）、AC-305 実機 | Tested / unverified |
| FR-309 | 停滞成立中のみ・画面外のみ | `StagnationDetector`、`AdditionalSpawner.TrySpawnPenalty`（唯一の追加スポーン駆動元） | `StagnationDetectorTests`（6件）、AC-303 実機 | Tested / unverified |
| FR-310 | 背後条件は `AmbushChance` 確率、`Dir` 不明時は発動しない | `TrySpawnPenalty`（ambush ロールと `FacingDir.None` フォールバック）、`SpawnPointClassifier.IsBehind` | `ClassifierAndScalingTests.BehindJudgement`、AC-304 実機 | Tested / unverified |
| FR-311 | 拘束中・イベント中・除外エリアで追加スポーンしない | `SpawnObserver.PerFrame`（`IsHold` / `IsCinematicEvent` / `Excluded` ガード）、`DefaultExclusions` | AC-306 実機 | Implemented / unverified |
| FR-312 | ギミック複製は許可リスト型・同一シーン内・既定空 | `GimmickCloner`、`SpawnProfile.GimmickCloningEnabled` | `SpawnProfileFactoryTests.DefaultsValidateCleanly`（既定無効）、AC-310 実機 | Tested / unverified |
| FR-313 | 複製失敗はセッション中再試行しない | `GimmickCloner._sessionDenied` | 実機（例外誘発は再現困難のためコードレビューのみ） | Implemented / unverified |
| FR-320 | 疑似宝箱の実体はシーン内プールの `EnmID_Mimic` のみ | `MimicBoxPlacement.IsMimicPool` / `CollectFromSpawner` | AC-314 実機 | Implemented / unverified |
| FR-321 | 抽選は拘束開始時、当たりは完全無介入 | `MimicBoxPatches.HoldSetupPrefix`（当たり→登録解除して `return true`） | AC-315 実機、付録A A-9 | Gated（介入点は A-9 の実測待ち） |
| FR-322 | 外れは拘束抑止＋非死亡除去＋報酬。除去不成立は縮退 | `MimicBoxPatches`（`ResolvedMiss`）、`MimicBoxPlacement.ProcessPendingMisses`（`SetActive(false)`、失敗時は残置） | `ForbiddenSurfaceTests.TheMissBodyIsNeverKilled`、AC-315 実機、付録A A-11 | Gated |
| FR-323 | 報酬は `AddItem` / `DropLootPool.Play` のみ。ファイル直書き禁止 | `MimicBoxPlacement.GrantReward` | `ForbiddenSurfaceTests.TheSaveIsNeverWritten`、AC-316 実機 | Tested / unverified |
| FR-324 | 介入は登録個体のみ。バニラのミミックと本物の宝箱に触れない | `SpawnRuntime.MimicBoxes`（instance id 登録制）、`HoldSetupPrefix` の未登録 early-return | `ForbiddenSurfaceTests.TheRealTreasureBoxesAreNeverTouched`（4件）、AC-316 実機 | Tested / unverified |
| FR-325 | 疑似宝箱は既定無効 | `SpawnOptions.MimicBoxEnabled = false` | `SpawnProfileFactoryTests.DefaultsValidateCleanly`、AC-310 実機 | Tested |
| FR-315 | MOD専有乱数、シード再現 | `RandomSource`（`SeededRandomSource.ForVisit`）、`SpawnObserver.EnterArea` | `RandomSourceTests`（3件）、`ForbiddenSurfaceTests.TheGameRandomIsNeverConsumed`、AC-301 実機 | Tested / unverified |
| FR-316 | 無効化・アンロードで巻き戻し | `SpawnPlugin.Unload`、`SpawnObserver.Poll`（`Enabled` 監視）/ `Rollback`、`Enabled.SettingChanged` | AC-307 実機 | Implemented / unverified |
| FR-317 | 設定エラーは既定値で続行 | `SpawnProfileFactory`（全検証）、`SpawnPlugin.ValidateRewardItems` | `SpawnProfileFactoryTests`（10件）、`RewardTableTests`（7件）、AC-312 実機 | Tested |
| FR-318 | 診断JSONの在庫出力、挙動を変えない | `SpawnDiagnostics.DumpArea`（読み取りのみ） | AC-314 実機（mimicPoolCount）、A-12 | Implemented / unverified |
| FR-319 | 追加出現の介入ログ | `SpawnRuntime.LogIntervention`（全介入点） | AC-301〜AC-315 の判定材料として実機 | Implemented / unverified |
| FR-326 | HUDの3段階循環、既定 `Off` | `SpawnHud.HandleKeys`、`HudModel.Next`、`SpawnObserver.InitialiseHud`、`SpawnOptions.HudMode` | `HudModelTests.ModeCyclesThroughAllThreeStages`、AC-317 実機 | Tested / unverified |
| FR-327 | HUDは読み取り専用 | `SpawnHud`（`HudSnapshot` のみを読む）、`SpawnObserver.BuildSnapshot` | `ForbiddenSurfaceTests.TheHudDrawsOnlyFromItsSnapshot` | Design-time |
| FR-328 | IMGUI限定、Canvas・`timeScale` に触れない | `SpawnHud`（`GUI.Box` / `GUI.Label` のみ） | `ForbiddenSurfaceTests.TheHudNeverReachesIntoTheGamesUi`（4件）、AC-308 実機 | Design-time / unverified |
| FR-329 | 描画例外でHUDを停止し1回提示 | `SpawnHud.OnGUI` の catch（`Mode=Off`、`_faultLogged`） | 実機（例外誘発は再現困難のためコードレビュー） | Implemented / unverified |
| FR-330 | `Full` は停滞・上限・疑似宝箱・候補数を表示 | `HudModel.Full` | `HudModelTests.FullCoversStagnationBudgetCandidatesAndBoxes` ほか6件 | Tested |
| FR-331 | デバッグ操作は設定有効時のみ、無効時は表示のみ | `SpawnHud.HandleKeys`（`commandsEnabled` ガード）、`HudModel.DebugPanel` | `HudModelTests.DebugPanelStatesWhenCommandsAreDisabled`、AC-319 実機 | Tested / unverified |
| FR-332 | 上限・除外・拘束中・位置条件を迂回しない | `SpawnObserver.Dispatch`（除外・一時停止の事前拒否）、`AdditionalSpawner.TrySpawnPenalty`（`forceAmbush` は条件のみ変更）、`MimicBoxPlacement.PlaceOne`（上限確認）、`StagnationDetector.FastForwardToStagnation`（時間のみ） | `StagnationProgressTests` の FastForward 3件、AC-320 実機 | Tested / unverified |
| FR-333 | 抽選固定は1回で消費し記録 | `SpawnRuntime.PinnedMimicOutcome`、`MimicBoxPatches.HoldSetupPrefix`（消費と `PINNED` 記録）、`HudModel.Full` | `HudModelTests.FullShowsAPinnedLotteryOutcome`、AC-321 実機 | Tested / unverified |

## 実装時に採用した仮定（低影響・可逆）

1. **既定除外集合は SceneID 名の規則で判定する**（`DefaultExclusions`: `Ga_` 接頭辞、`Boss` / `Tutorial` / `Ending` / `Title` 含有、`_GO` 系接尾辞、`Character_Setting`）。enum に「イベント専用」の印がないための近似で、areas.json の `excluded` で個別修正できる。誤検知・見逃しは実機で洗い出す。
2. **停滞の「移動距離」は窓内の経路長**（変位ではない）。その場往復も移動とみなす寛大側の解釈。
3. **`m_SpawnCount` は `Vector2Int`（範囲値）**のため、倍率は両端に適用し「元値未満にならない」丸めを行う（`SpawnScaling`）。間隔・クールタイムの `Vector2` も同様に両端へ適用。
4. **Hard 上書きが有効なスポナーでは平フィールドへの書き込みが無効の可能性がある**（付録A A-2）。その場合の実効挙動は「バニラ Hard のまま」で安全側。SPEC002 併用時に 5.2 が効かない範囲は A-2 の実測で確定する。
5. **装備品の判定は ItemID 名に `Wand` を含むかで行う**（`ValidateRewardItems`）。interop から装備区分を確実に引けないための近似。`Relics` は `ItemID` に存在しないため enum 検証で自然に排除される。
6. **外れ時の非死亡除去は `gameObject.SetActive(false)`**（A-11 の候補経路）。プール返却の正規経路が実測で判明したら置き換える。
7. **ミミック抽選の介入点は `OnlyHoldEnemy.HoldSetup`**（A-9 の候補）。`EnmID_Mimic` の実装クラスが別だった場合は `SpawnPlugin.ApplyMimicPatch` の対象だけを差し替える。
8. **HUDの倍率欄は平均値を表示する**。倍率はスポナー毎に独立に引く（5.2）ため代表値が存在しない。1体のスポナーしかないエリアでは実測値と一致する。表示は `mean` と明記している。
9. **ホットキーは `UnityEngine.Event.current` から読む**。ゲームの `InputManager` と Unity InputSystem を経由しないため、ゲーム側のキー割り当てと競合しない。既定は `F5` / `F6`（SPEC003 が `F7`〜`F11` を使用済み）。
10. **停滞の早送りは `_windowForced` フラグで実現する**。合成サンプルを積む方式は次フレームの窓刈り込みで消えるため採らなかった。移動が検知された時点と実際に窓が満たされた時点でフラグは解除され、判定規則そのものは変更しない。

## 付録A 実測の現況

| # | 項目 | 状態 |
|---|---|---|
| A-1 | `TryGet` の取得・配置手順 | 未実測。実装は `TryGet(action)` 内 `Teleport(point, false)` |
| A-2 | 書き換えの反映タイミングと Hard 解決点 | 未実測。ログに基準値と倍率を記録済み（AC-309 の材料） |
| A-3 | `m_MaxSpawn` の反映時点 | 未実測。無効なら安全側 |
| A-4 | `TemporaryEnemySaver` との干渉 | 未実測 |
| A-5 | 画面外スポーンの活性化挙動 | 未実測。待機は許容仕様 |
| A-7 | `SimpleSpawnArea` への倍率適用可否 | 未実測。適用は実装済み |
| A-8 | 5.2 のセーブ非波及 | 未実測（AC-307 実機で確認） |
| A-9 | ミミック実装クラスと拘束抑止点 | 未実測。`MimicBoxEnabled` が既定無効の根拠 |
| A-10 | `AddItem` / `Play` の反映 | 未実測 |
| A-11 | 非死亡除去の波及なし | 未実測。`SetActive(false)` 候補 |
| A-12 | ミミック保有シーン一覧 | 未実測。`DiagnosticsEnabled` の `mimicPoolCount` で収集する |
