# SPEC001 implementation traceability

Normative source: [`docs/specifications/SPEC001.md`](../specifications/SPEC001.md) (revised 2026-08-09, CHG-014 〜 CHG-040). This document records implementation and verification only; it does not change the specification.

**CHG-040（2026-08-09）。** 敗北の観測を `Lelia.IsHP0` から `Lelia.LeliaDeadState`（`Live`/`HP0`/`GameOver`）へ移した。利用者のカタログでは `game-over` 11件がすべて `actorId=lelia` で、`Dungeon_Swamp_Boss` の3件（`Damage3_Wall` 0.73s、`Damage3_Wall_Down` 0.68s、`Idle_Injured` 2.32s）はいずれも被弾クリップだった。同じ敵の敗北take `GaID_SwampLeech_Adult/Go_End1` はギャラリーに5.85秒として登録されている。つまり**演出そのものはトリガーになっておらず**、演出の間にプレイヤーのアニメータが移り変わるたびに別の鍵が生まれていた。あわせて敗北を拘束より先に判定するようにした（拘束されたまま敗北する経路では `hold` が続き敗北が観測されない）。`RedAlert.IsStartRed` も候補だったが、赤アラートは敗北を伴わずに出ることがあるため採らず、Harmonyパッチも要さなかった。

**CHG-039（2026-08-09）。** ある段階の波形を複数の他段階へ適用できるようにした。既定は共有（リンク）で、適用先のトリガーが元の段階のギャラリー名を指すだけであり、`.funscript` は複製されない。再生側は元から `outputs[].gallery` を読むだけなので、再生経路の変更はない。GUIの編集・試聴・保存の対象ギャラリーは `AuthoringStore.ResolveGallery` の結果へ切り替えた（FR-063）。段階ごとに動きを変えたい場合のために複製も選べる。

**CHG-037（2026-08-09）。** 拘束トリガーの `actorId` を6.2.1の順序で解決するようにした。それまでは `Bind.BinderEnemy.GalleryEnemyID` を直接使っており、(1) 未登録の敵がすべて `hold/None/…` を共有し（利用者のカタログで6件）、(2) `EnemyObject` でない拘束者（`ParasiteTentacle`、`ParasiteBullet`、`StoneEye`）の拘束はトリガーが1件も出ていなかった。既存カタログの `hold` 行21件は新しい鍵で作り直される。`.funscript` は未作成のため移行不要。

Manual and hardware verification steps live in [`docs/testing/SPEC001-test-scenarios.md`](../testing/SPEC001-test-scenarios.md). Rows below marked as pending device or in-game evidence are the ones those scenarios cover.

## User-directed implementation constraints

| ID | Direction received after SPEC001 | Implementation |
|---|---|---|
| UDR-001 | Distribution packaging is out of scope | Removed the release builder and `dist` source-of-truth; the repository runtime layout is authoritative |
| UDR-002 | Keep every required runtime file in the repository | Placed BepInEx 6 IL2CPP, CoreCLR, generated game interop, plugin DLLs, mappings, EDI configuration, authoring GUI assets, and funscripts at their runtime paths |
| UDR-003 | Users cannot manually enter event identifiers | Trigger capture is always enabled; `trigger-catalog.json` is written automatically and the authoring GUI offers stages by name, so no identifier is ever typed |
| UDR-005 | The game has no bones; detect triggers and let the user author funscripts in a GUI | Withdrew measured-motion generation (supersedes UDR-004); added the trigger catalog, the loopback authoring server, the editor GUI, `Preview` playback, and the save/upload/map pipeline |

Status meanings:

- **Tested**: implemented and covered by an automated test or a completed runtime check.
- **Implemented / unverified**: code exists, but the required physical-device or in-game evidence is outstanding.
- **Blocked**: external evidence or an exercised game path is still required.

## Requirement ledger

| Requirement | Implementation | Verification | Status |
|---|---|---|---|
| FR-001 | `EdiPlugin` BepInEx IL2CPP entry point | Compiles against the generated SiNiSistar2 interop; prior isolated startup loaded plugin 1.0.0 | Implemented; startup re-check pending |
| FR-002 | `RuntimeObserver` normalises to explicit trigger keys | `StagesOfTheSameAnimationAreIndependentTriggers` | Tested |
| FR-003 | `MappingRepository.TryResolve` accepts `mapped` only | `MappedEventUsesSeekAndDuplicateObservationDoesNotReplay`, unknown-trigger test | Tested |
| FR-004 | unmapped triggers suppress playback and warn once | `UnknownEventIsDiagnosedAndNeverPlayed`, `CataloguedButUnmappedStageIsNeverPlayed` | Tested |
| FR-005 | `PlaybackCommand` carries an output; REST always emits `channels=` | `PlayUsesPostAndEncodesGalleryChannelAndSeek`, coordinator tests | Tested |
| FR-006 | one output per device; `OutputBinding` roster; coordinator state is per output | `ParasiteAndSwollenBreastApplyTogetherWithoutOverwritingEachOther`, `ATriggerCanSilenceOneOutputWhileAnotherPlaysAndLeaveTheRestAlone`, `BothBreastSidesBecomeTwoOutputs` | Tested |
| FR-007 | normalized animation time converted to millisecond seek | mapped-event and resume tests | Tested |
| FR-008 | per-output last-command/trigger deduplication | duplicate observation test | Tested |
| FR-009 | `EndEvent` acts only on the matching active key | `StaleEndCannotReplaceNewerEvent` | Tested |
| FR-010 | fillers selected from current status after a trigger ends | status-change test | Tested |
| FR-011 | `Breast` / 膨乳 selects `filler-breast-swollen` | mapping, coordinator, and asset-strength tests | Automated behaviour tested; hardware approval pending |
| FR-012 | outputs a trigger holds are excluded from filler updates | status-change test, `EndingATriggerOnlyRestoresTheOutputsItHeld` | Tested |
| FR-013 | runtime pause/resume and inactive Stop transitions | pause/resume test; `LeavingGameplayStopsAnActivePreview` | Tested except physical devices |
| FR-014 | Unity observation stays on `Update`; HTTP and the authoring server are worker-based | async recovery test; `AuthoringServerTests` run off the game thread | Tested |
| FR-015 | failures are caught, backed off, and isolated from Unity | startup with EDI offline; async recovery test | Tested |
| FR-016 | schemaVersion 2, roster consistency, duplicates, unknown outputs, hashes, and required fields fail closed | `SchemaVersionOneIsRefusedWithAPointerToTheMigration`, `EveryOutputNeedsADefaultFillerKeyEvenIfItIsNull`, `TheRosterRejectsTwoOutputsClaimingTheSameThing`, `AnEmptyRosterIsAConfigurationError`, `MissingStageIdFailsClosed` | Tested |
| FR-017 | `EventCaptureTracker` derives transitions; `DiagnosticRecorder` records candidates and classifications | transition and candidate-output tests | Tested |
| FR-018 | report emits numeric `unclassifiedCount` | coverage report test | Tested; counts update as game paths are exercised |
| FR-019 | `AsyncEdiCommandSink.ShutdownAsync` attempts Stop on both channels | shutdown implementation and teardown test | Implemented; hardware unverified |
| FR-020 | startup log includes hashes, versions, session, catalog path, and the GUI URL | `EdiPlugin.Load` log line | Implemented; startup re-check pending |
| FR-024 | `CaptureTextChanges` records every `UnityEngine.UI.Text`; capability records explain unavailable categories | `SessionLogHoldsTransitionsWithoutPerFrameRecords` | Tested |
| FR-029 | build/session-specific append-only path; catalogs remain separate | writer test; `CatalogFromAnotherGameBuildIsNotReused` | Tested |
| FR-030 | bounded nonblocking queue records dropped sequence/count | writer test; overflow path implemented | Tested except forced overflow in Unity |
| FR-031 | `EventKey` is a 5-tuple; `RuntimeObserver` treats a stage change as its own transition | `StagesOfTheSameAnimationAreIndependentTriggers`, `TransitionRecordsBothSidesOfAStageChange` | Tested |
| FR-032 | `RuntimeObserver.EnumerateTakes` reads `GaTakePlayer.m_TakeDataArray` and registers unreached stages | `EnumeratedStagesAreCataloguedBeforeTheyAreReached`; live GUI shows 未到達 stages | Tested at unit level; in-game enumeration unverified |
| FR-033 | Gallery uses the game's own take array; hold degrades to the animator state name with the reason recorded as a `hold-stage-state-machine` capability | Capability record in `EdiPlugin`; interop probe confirmed no general hold state machine exists | Implemented / in-game verification pending |
| FR-034 | `TriggerCatalog.Register` merges without loss; `SaveAsync` writes atomically | `ObservationCompletesAnEnumeratedEntryWithoutLosingIt`, `CatalogSurvivesReloadAndAddsOnlyNewStages` | Tested |
| FR-035 | `AuthoringServer.ValidateBaseUrl` rejects non-loopback before any socket opens | `AuthoringServerOnlyAcceptsLoopbackAddresses` (6 cases) | Tested |
| FR-036 | `editor.html` / `editor.js` catalog list, canvas editor, variant tabs, duplicate-from-stage | `ServerServesTheGuiAndTheStageCatalog`; live browser run drew a waveform and saved it | Tested |
| FR-037 | `PlaybackCoordinator.BeginPreview` / `EndPreview` ranked below `Inactive`; GUI preview goes through the EDI client | `PreviewOutranksFillerAndRestoresGameStateOnEnd`, `PreviewEndRestoresTheActiveEvent`, `PreviewGoesThroughTheCoordinatorAndCanBeStopped` | Tested except physical devices |
| FR-038 | `AuthoringStore.SaveAsync` writes, uploads, then upserts `mappings.json` | `SavingWritesTheAssetRegistersItAndMapsTheTrigger`, `EdiRegistrationFailureLeavesTheTriggerUnmapped`, `SaveThroughTheApiMapsTheTrigger` | Tested except live EDI reload |
| FR-039 | no generator exists; a side is never mirrored, and a variant left undrawn is removed rather than invented | `OneSidedBreastAuthoringMapsOnlyThatOutput`, `ReauthoringForAnotherOutputRemovesTheUnusedVariant` | Tested |
| FR-040 | `Funscript.Validate` compares the loop end with the clip length; approval is recorded in the manifest | `LoopLengthMismatchRequiresApprovalAndIsRecorded`; live GUI refused a 26 ms mismatch | Tested |
| FR-041 | catalog never resolves playback; only `mappings.json` does | `CataloguedButUnmappedStageIsNeverPlayed` | Tested |
| FR-042 | `OutputGate` forwards per output; `BindingVerifier` checks device, channel, variant, readiness and intruders; `EdiPlugin.VerifyBindingsAsync` opens or suppresses each output within the discovery window | `AFullyWiredSetupBindsEveryOutput`, `APistonOnTheWrongChannelSuppressesBothOutputsAndNamesTheMismatch`, `AnUnknownDeviceOnAnOutputChannelSuppressesThatOutput`, `OnlyBoundOutputsReceiveCommands`, `PreviewingASuppressedOutputIsRejectedWithAReason` | Tested; one-device in-game run pending |
| FR-043 | `StatusRule.Priority`; `SelectFiller` is evaluated per output and takes the highest-priority match; load-time validation rejects an equal-priority tie that names different galleries, `null` included | `HighestPriorityRuleWinsWhenSeveralStatusesAreActiveAtOnce`, `EqualPriorityRulesSelectingDifferentFillersAreAConfigurationError`, `SilencingAndPlayingAtTheSamePriorityIsAmbiguous`, `ParasiteAndSwollenBreastApplyTogetherWithoutOverwritingEachOther` | Tested |
| FR-044 | `GalleryRegistration.ReloadAsync` asks EDI to re-scan its current gallery root and reports missing or stray variants; no files are transferred | `StartupAsksEdiToRereadTheGalleryRoot`, `ReloadPostsToTheRescanEndpointWithoutTransferringFiles`; live EDI returned 5 definitions with the gallery root unchanged | Tested |
| FR-045 | EDI `StopClearsFiller` clears the retained filler and stops the device | `StopClearsFillerLeavesTheDeviceStoppedInsteadOfReplayingTheFiller` (Edi.Core.Tests) | Tested; device stop pending |
| FR-046 | `AsyncEdiCommandSink` groups outputs sharing a payload into one request; `channels` is comma-joined | `OutputsSharingAPayloadAreSentAsOneRequest`, `GroupedOutputsAreSentAsOneCommaSeparatedRequest` | Tested |
| FR-047 | `Silent` state sends `Stop`; no still waveform exists in the repository | `AnOutputWithNoDefaultFillerIsStoppedRatherThanFedAStillWaveform`, `NoAssetExpressesStillnessAsAWaveform` | Tested |
| FR-048 | the output set comes from `MappingDocument.Outputs`; no fixed set remains in code | `MappingSchemaTests`, `VariantsAndOutputsResolveBothWays` | Tested |
| FR-049 | `AuthoringServer.DescribePreviewRejection` refuses a gallery with no variant for the target output | `PreviewGoesThroughTheCoordinatorAndCanBeStopped` (asset required first), `EveryGalleryVariantBelongsToAnOutputInTheRoster` | Tested |
| FR-050 | EDI `StrictVariantResolution` returns null and warns instead of substituting | `StrictVariantResolutionRefusesToSubstituteAnotherVariant`, `VariantFallbackStillAppliesWhenTheSettingIsOff` (Edi.Core.Tests) | Tested |
| FR-051 | E1, E2 and E4 are settings defaulting to the historical behaviour | Edi.Core.Tests 130 pre-existing tests pass unchanged | Tested |
| FR-052 | `EdiCapabilityCheck` gates playback; `EdiPlugin.ReachEdiAsync` separates an outage from a verdict | `MissingCapabilitiesBlockPlaybackEntirely`, `ADisabledCapabilityBlocksPlaybackAndNamesTheRequirement`, `MissingInfoEndpointIsReportedAsNoCapabilitiesRatherThanThrowing`; live `GET /Edi/Info` returned all three settings | Tested |
| FR-053 | one request per distinct payload, never per output | `OutputsSharingAPayloadAreSentAsOneRequest`, `OutputsWithDifferentPayloadsAreSentSeparately` | Tested |
| FR-054 | `BindingVerifier` reports expected and actual, and names a uniquified device | `AUniquifiedDeviceNameIsCalledOutInsteadOfReadingAsMissing`, `AWrongVariantOrAnUnreadyDeviceIsReportedWithBothValues`, `AnAbsentDeviceListsWhatEdiDidReport` | Tested |
| FR-055 | `OutputGate.Suppress` stops the device before it stops forwarding | `SuppressingAnOpenOutputStopsTheDeviceFirst` | Tested |
| FR-056 | every roster output needs a `defaultFillers` key; `null` means silent | `EveryOutputNeedsADefaultFillerKeyEvenIfItIsNull` | Tested |
| FR-057 | `GalleryRegistration.FindStrayVariants` reports a variant no output claims | `EveryGalleryVariantBelongsToAnOutputInTheRoster` | Tested |
| FR-058 | `BinderActorId.Resolve` walks `GalleryEnemyID` → `EnemyID` → `ActorIds.FromObjectName`, reading the binder through `Bind.Binder` as well as `Bind.BinderEnemy`; `ActorIds.IsUsable` rejects `None` | `ActorIdTests.UnsetNamesNoActor`, `UnitySuffixesAreNotPartOfTheActor`, `DifferentBindersGetDifferentKeys`; AC-056 in game | Tested / unverified |
| FR-059 | `RuntimeObserver.ObserveGameplay` no longer requires `BinderEnemy` to observe a hold; an unnameable binder becomes `ActorIds.UnidentifiedBinder` | `ActorIdTests.TheUnidentifiedBinderIsItsOwnActor`; AC-057 in game | Tested / unverified |
| FR-061 | `Authoring.OpenGuiKey` (default `F6`) read in `RuntimeObserver.OnGUI`; `AuthoringGuiLauncher.Open` shells out to the default browser and swallows failures; `Authoring.OpenGuiOnStart` opens it at load; the startup log names the key | Build-time only so far — the key path needs the game; AC-059 in game | Implemented / unverified |
| FR-062 | `AuthoringStore.LinkAsync` / `UnlinkAsync`; `POST /api/link` (`mode: link|copy`) and `POST /api/unlink` in `AuthoringServer`; the GUI's 「この波形を他の段階へ…」 dialog, the shared badge in the catalog list, and the link banner with 「リンク解除」 | `LinkedTriggerTests` (8 cases: sharing without a second asset, editing through a link, clip-length approval, unmappable targets, unsaved source, unlink, unlink of an unlinked stage, two-gallery fallback); GUI behaviour driven through a jsdom harness; AC-060〜AC-063 in game | Tested / unverified in game |
| FR-063 | `AuthoringStore.ResolveGallery` reads the gallery from the mapping; `LoadExisting`, `SaveAsync`, `/api/script`, `/api/catalog`, and preview all use it | `LinkedTriggerTests.EditingALinkedStageWritesTheSharedGallery`, `AnEntryNamingTwoGalleriesResolvesToTheStagesOwnGallery` | Tested |
| FR-064 | `RuntimeObserver.ReadDeadState` / `ObserveGameOver` / `ForgetGameOver`; the defeat branch runs before the hold branch; the key is latched per `LeliaDeadState` stage and cleared by `SetNoEvent` and `Shutdown` | No automated coverage — the plugin layer has no test harness. AC-064 and AC-065 in game (TS-507) | Implemented / unverified |
| FR-065 | The simulation section of `authoring/editor.js` (`simFrame`, `reachablePosAt`, `rotorSpeedNormAt`, `drawSim`) plus the `simPanel` markup; pure GUI, no server or API change. Piston motion reuses the `simulateDevice` reachable trace; rotor speed is segment slope / 450 with 500–2500 ms random direction flips, mirroring `ButtplugDevice.SendCmd` | Verified 2026-08-10 against a stub API server in a browser: slope→intensity values (0.25 / 0.556), drawn-vs-reachable divergence (77.5 vs 62.5 at 600 ms), rotor angle advance (0.3456 rad = theoretical), non-loop end stop, loop wrap, playhead pixels, no console errors, and zero HTTP traffic from the simulation (AC-066〜AC-068). No jsdom harness yet | Tested (stub browser run) |
| FR-060 | `EventKey.IsUnidentifiedActor` / `IsAuthorable`; `AuthoringStore.Save` refuses the key; `MappingRepository.TryResolve` refuses it even when hand-written; `PlaybackCoordinator.ObserveEvent` warns about identification rather than authoring | `ActorIdTests.AnUnidentifiedActorCanNeverCarryAScript`, `AHandWrittenMappingForAnUnidentifiedActorIsNotHonoured`; AC-058 in game | Tested / unverified |

## Acceptance criteria

| Acceptance criterion | Automated/runtime evidence | Status |
|---|---|---|
| AC-001 | Plugin loads and logs versions, hashes, catalog path, and GUI URL | Implemented; startup re-check pending |
| AC-002 | mapped-trigger HTTP/coordinator tests verify key, gallery, seek, and `main` | Passed |
| AC-003 | `BothBreastSidesBecomeTwoOutputs`; grouped send verified by `OutputsSharingAPayloadAreSentAsOneRequest` | Passed |
| AC-004 | duplicate observation test | Passed |
| AC-005 | stale end test | Passed |
| AC-006 | swollen filler after trigger end test | Passed |
| AC-007 | status update does not interrupt a trigger | Passed |
| AC-008 | pause/resume current-position test | Passed |
| AC-009 | inactive and shutdown Stop paths; `ShutdownAttemptsStopForEveryKnownOutput`; EDI `StopClearsFiller` verified | Passed except physical devices |
| AC-010 | EDI-offline startup plus nonblocking worker test | Passed |
| AC-011 | latest-desired-state recovery test | Passed |
| AC-012 | unmapped trigger diagnostic/suppression test | Passed |
| AC-013 | `DuplicateEventKeyAndUnknownChannelFailClosed` (duplicate key and unknown output) | Passed |
| AC-014 | automatic capture and machine-readable unclassified count | Mechanism passed; exhaustive gameplay coverage depends on which paths are exercised |
| AC-015 | `EveryMappedStatusFillerHasAnAssetAndADefinitionRow`, `EveryGalleryVariantBelongsToAnOutputInTheRoster` (both directions) | Passed for current mappings |
| AC-016 | stronger swollen script properties verified automatically | Passed |
| AC-024 | `EnumeratedStagesAreCataloguedBeforeTheyAreReached` plus the live GUI listing two 未到達 stages | Passed at unit level; in-game enumeration pending |
| AC-025 | `TransitionRecordsBothSidesOfAStageChange`, `SessionLogHoldsTransitionsWithoutPerFrameRecords` | Passed; two-enemy in-game run pending |
| AC-026 | `CatalogSurvivesReloadAndAddsOnlyNewStages` (no `.tmp` left behind) | Passed |
| AC-027 | `AuthoringServerOnlyAcceptsLoopbackAddresses` | Passed |
| AC-028 | `SaveThroughTheApiMapsTheTrigger`; live browser save wrote the funscript, manifest, and mapping | Passed |
| AC-029 | `PreviewOutranksFillerAndRestoresGameStateOnEnd`, `PreviewGoesThroughTheCoordinatorAndCanBeStopped` | Passed except physical devices |
| AC-030 | `EdiRegistrationFailureLeavesTheTriggerUnmapped` | Passed |
| AC-031 | `LoopLengthMismatchRequiresApprovalAndIsRecorded`; live GUI reproduced the refusal and the approval | Passed |
| AC-032 | **Superseded by CHG-022/CHG-023.** One-sided authoring is now a supported trigger shape, so the criterion as written contradicts FR-006. `OneSidedBreastAuthoringMapsOnlyThatOutput` records the new behaviour; the criterion needs revising in SPEC001 | Conflict reported, not implemented |
| AC-033 | `CataloguedButUnmappedStageIsNeverPlayed` | Passed |
| AC-034 | `SessionLogHoldsTransitionsWithoutPerFrameRecords` asserts no `animation-sample` and monotonic ordering | Passed |
| AC-035 | `OnlyBoundOutputsReceiveCommands` | Passed |
| AC-036 | `PreviewingASuppressedOutputIsRejectedWithAReason`; `DescribePreviewRejection` also refuses a gallery without the variant | Passed |
| AC-037 | `ParasiteAndSwollenBreastApplyTogetherWithoutOverwritingEachOther`, `HighestPriorityRuleWinsWhenSeveralStatusesAreActiveAtOnce` | Passed |
| AC-038 | `EqualPriorityRulesSelectingDifferentFillersAreAConfigurationError`, `SilencingAndPlayingAtTheSamePriorityIsAmbiguous` | Passed |
| AC-039 | `StartupAsksEdiToRereadTheGalleryRoot`; live EDI returned 5 `gallery`-type definitions with the root unchanged | Passed |
| AC-040 | `StrictVariantResolutionRefusesToSubstituteAnotherVariant` (Edi.Core.Tests); live `GET /Edi/Info` reports it enabled | Passed; device confirmation pending |
| AC-041 | `APistonOnTheWrongChannelSuppressesBothOutputsAndNamesTheMismatch`, `AnUnknownDeviceOnAnOutputChannelSuppressesThatOutput` | Passed |
| AC-042 | `ATriggerCanSilenceOneOutputWhileAnotherPlaysAndLeaveTheRestAlone` | Passed |
| AC-043 | roster-driven: `MappingSchemaTests` and `TestMappings.Roster` add outputs without code changes | Passed |
| AC-044 | `OutputsSharingAPayloadAreSentAsOneRequest`, `OutputsWithDifferentPayloadsAreSentSeparately` | Passed |
| AC-045 | `NoAssetExpressesStillnessAsAWaveform`; the six padding assets were moved out of the gallery root | Passed |
| AC-046 | `SchemaVersionOneIsRefusedWithAPointerToTheMigration` | Passed |
| AC-047 | Edi.Core.Tests 130 pre-existing tests pass with the settings at their defaults, including `StopKeepsReturningToTheFillerWhenTheSettingIsOff` and `VariantFallbackStillAppliesWhenTheSettingIsOff` | Passed |
| AC-048 | `ADisabledCapabilityBlocksPlaybackAndNamesTheRequirement` | Passed |
| AC-049 | `MissingInfoEndpointIsReportedAsNoCapabilitiesRatherThanThrowing`, `MissingCapabilitiesBlockPlaybackEntirely` | Passed |
| AC-050 | `AUniquifiedDeviceNameIsCalledOutInsteadOfReadingAsMissing` | Passed |
| AC-051 | `EdiPlugin.ReachEdiAsync` retries connection failures and only judges on an HTTP answer | Implemented; covered indirectly by the capability tests, no plugin-level harness exists |
| AC-052 | `SuppressingAnOpenOutputStopsTheDeviceFirst` | Passed |
| AC-053 | `AnUnknownDeviceOnTheHoldingChannelLeavesEveryOutputBound`; EDI `UnassignedDeviceChannel` verified live | Passed |
| AC-054 | `EveryOutputNeedsADefaultFillerKeyEvenIfItIsNull`, `EveryGalleryVariantBelongsToAnOutputInTheRoster` | Passed |
| AC-055 | `MergingOutputsKeepsTheAssignmentsSavedEarlier` | Passed |

## Verification record (2026-08-05)

- `dotnet test tests/SiNiSistar2.Edi.Core.Tests -c Release`: 56 passed, 0 failed.
- `dotnet build SiNiSistar2.Edi.sln -c Release`: succeeded, 0 warnings, 0 errors with `TreatWarningsAsErrors`, using the generated SiNiSistar2 interop.
- Interop probe (`MetadataLoadContext` over `BepInEx/interop/SiNiSistar2.dll`) confirmed `GaTakePlayer.m_TakeDataArray`, `AnimationTakeData.m_TakeName` / `m_IsAnimatorLoop`, and that `HoldStateRp` exists only on `MeatTentacleCluster` — there is no general hold state machine.
- `HttpListener` binding check: `http://127.0.0.1:5601/` and `http://localhost:5601/` bind without elevation; `http://+:5601/` is denied (and is rejected by `ValidateBaseUrl` anyway).
- Live GUI run against a hosted `AuthoringServer`: the catalog listed four stages including two 未到達 entries; canvas mouse events drew a four-point waveform; saving was refused for a 26 ms loop mismatch, then accepted after approval, producing the `.funscript`, the manifest with `loopMismatchApproved: true`, and a `mapped` entry in `mappings.json`.
- Two serialization defects found in that live run and fixed with regression tests: `durationMilliseconds` leaked into the `.funscript`, and the derived `key` object leaked into `mappings.json`.

### In-game run (2026-08-05, user-reported log)

Two defects were found by running the plugin in the actual game and both are fixed:

1. `[Error : Unity] マネージャーアクセス禁止タイミング` — moving the gallery check ahead of the
   gameplay guards also moved it ahead of the only thing that had been keeping manager reads out of
   scene teardown/setup. `Poll` now gates on the game's own `ManagerList.IsForbiddenManagerAccessState`
   and `HasCompletedFirstInitialize` before any manager access, while `HasDoneSceneSetUp` remains a
   gameplay-only precondition so the gallery branch is not gated on it again.
2. The observed key was `scripted-event/Monastery/Idle_Broken/reaction/Idle_Broken`, duplicating the
   animation id into the stage id. Cinematic and game-over reactions play a single animation, so they
   are single-stage triggers and now use `EventKey.DefaultStageId` (SPEC001 3章). Only `hold` uses the
   animator state name as the stage id.

The same log confirms FR-002/FR-004 in-game: the trigger was normalised to a 5-part key and playback
stayed suppressed because no funscript is authored for it yet.

### Gallery run (2026-08-05, all takes of one enemy played)

Session `20260805T012602320Z-…`, 0.77 MB (the equivalent pre-revision session was 80 MB).
Records: 1 `session-start`, 11 `event-transition`, 8 `catalog-update`, 1703 `text-change`,
3 `capture-warning`, and **no `animation-sample`**.

Gallery observation now works — the pre-revision session for the same content contained zero gallery
samples. The full stage progression was captured with clip lengths:

| Stage | Clips observed | Clip length |
|---|---|---|
| `VillagerRegion_Hold` | `Hold` | 0.400 s |
| `VillagerRegion_HoldDown` | `Hold_Down`, `Hold_Down_Loop` | 0.900 s, 1.600 s |
| `VillagerRegion_HoldDown_End` | `Hold_Down_End`, `Hold_Down_End_Loop` | 3.183 s, 1.600 s |
| `VillagerRegion_HoldDown_EndJoin` | `Hold_Down_End_Loop` | 1.600 s |

This verifies FR-032 (four stages enumerated before being played), FR-031 (`…_EndJoin` reuses the
clip of the previous stage and is still detected as a separate trigger because the stage id differs),
AC-024, AC-025, and AC-034. The `scripted-event` fix is visible too: the key is now
`…/reaction/default`. `capture-warning` records carried machine-readable reasons
(`scene-setup-incomplete`, `gameplay-objects-unavailable`), which the pre-revision `SetNoEvent`
could not report at all.

Two further defects were found in this data and fixed:

3. `actorId` was `"Root"` — every take player component carries that name, so all enemies would have
   collided into one actor. `ResolveGalleryActorId` now derives the actor from the shared take-name
   prefix (`VillagerRegion`), falling back to the hierarchy path.
4. Enumerated stages used the take name as the animation id, but observation uses the clip name, so
   the placeholder never merged and every stage kept a permanent ghost row. Enumerated stages now use
   `EventKey.UnobservedAnimationId`, which `TriggerCatalog` retires on first observation (across a
   differing phase, and for takes that queue several clips). `AuthoringStore` refuses to map a
   placeholder because its clip and length are unknown.

Because `actorId` changed, catalog rows written before this fix cannot merge with new ones; the
existing `trigger-catalog.json` was deleted (user-approved) so it is rebuilt from a fresh run.

### Gallery identity and display names (2026-08-05)

The take-name prefix (`VillagerRegion`) is an internal code name, so the GUI could not tell which
enemy or scene a stage belonged to. `GalleryUI.CharacterSelectUI.CategoryData` exposes the gallery's
own data, which resolves this properly:

- `EnemyData.GalleryEnemyID` → `actorId` (`Region`). This is the same stable identifier the `hold`
  context already uses, so an enemy now has one actor id in both contexts.
- `EnemyData.SelectText` → `ActorDisplayName` (`構造の落とし子`).
- `AnimationTake.SelectText` → `DisplayName` per stage (`拘束`, `押し倒し`).
- `EnemyData.m_AnimationTakeArray` lets the whole category be enumerated, so stages appear with real
  names before being played rather than only after.

The take-name prefix remains the fallback when the UI chain is unavailable, and that degradation is
recorded as a `gallery-actor-identity` capture warning.

Two follow-on defects were found while verifying this against a hosted GUI and fixed:

5. Retiring a placeholder discarded the game's display names, because only the stage array knows
   them. `TriggerCatalog.Register` now hands them to the observed trigger.
6. A take that queues several clips produces several triggers but has only one placeholder, so the
   second clip had no names. `InheritNamesFromSiblings` gives the stage label to entries of the same
   stage and the actor label to any entry of the same actor.

Verified end to end in the browser: all five rows render as `構造の落とし子 — 拘束 / 押し倒し / …`
with the unreached stages marked 未再生.

A separate defect was found in the GUI asset itself: `editor.js` contained four literal NUL bytes
used as key separators, so the file was not valid text. They are now `|`.

### Identifying which stage is on screen (2026-08-05)

Naming the enemy was not enough: the in-game viewer selects stages by numbered tabs, while the GUI
listed internal take names (`GO`, `Hold`, `MagicSuccessHold`), so the two could not be matched.

An attempt to infer the tab number from `AnimationTake.SelectID` failed — this build leaves it at 0
for every take, and because 0 is a valid `int` the array-position fallback never ran, so every row
rendered as `#0`. `DisplayNumber` now treats a non-positive `SelectID` as unset. The number orders
the list; it is explicitly **not** claimed to match the gallery's tabs, and the GUI says so.

Identification is instead solved by asking the game. `LiveTriggerState` hands the currently observed
trigger from the Unity main thread to the authoring server, `/api/current` returns it, and the editor
polls once a second to highlight the matching row, show a banner with a jump button, and optionally
scroll to it. A reading older than three seconds is discarded so no row stays highlighted after the
game pauses or exits.

Verified in a browser against a hosted server with a cycling live trigger: the banner, the row
highlight, and the `▶ ゲームで再生中` badge all track the reported stage, and rows numbered `#1`–`#4`
confirm the `#0` fix.

### Wrong enemy and uncaptured defeat events (2026-08-05)

Playing 子抱えヒル reported 大羽虫, and defeat performances of bosses and some enemies produced no
trigger at all. Two causes, both fixed:

7. `SelectedEnemy` indexed `CategoryData.m_EnemyDataArray` by `CategoryData.SelectID`. The menu list
   is filtered (`EnemyData.IsHiddenByFilter`), so the position in the visible list is not the
   position in the array and the wrong entry was selected — the same class of mistake as assuming
   `AnimationTake.SelectID` was a tab number. Identification is now content-based: the take names
   the take player actually loaded are matched against each menu entry's take list, the best overlap
   wins, and a tie yields no identification rather than a guess.
8. `TryReadAnimator` only inspected layer 0. Defeat and boss performances are driven from a higher
   layer, so the read returned "nothing playing" and the event was never observed. All layers are
   now scanned, with layer 0 keeping priority so cases that already worked are unchanged, and a take
   that plays but yields no readable clip is recorded as a `take-animator-unreadable` warning
   instead of being dropped silently.

Both fixes are in the IL2CPP plugin layer and cannot be covered by the automated suite; they need an
in-game run to confirm. The catalog written before this fix contains 34 observed rows with
unreliable actor attribution (including `Hold`, `Gallery`, and `VillagerRegion` fallback ids) and
must be rebuilt.

### EventPlayer takes and a destroyed-animator crash (2026-08-05)

An in-game run confirmed the actor highlight was fixed and produced three more findings:

9. **The observer was crashing.** `TryReadAnimator` threw
   `NullReferenceException` from `Behaviour.get_isActiveAndEnabled`. A destroyed Unity object is not
   a C# null but throws on every member access, so the `is null` guard did not catch it and the
   whole observer went down its fail-closed path. It now checks `WasCollected` and treats a throwing
   animator as `animator-destroyed` rather than failing the poll.
10. **Defeat and boss performances have no animator by design.** `AnimationTakeData.m_PlayType` is
    `Animator` or `EventPlayer`; an `EventPlayer` take is a scripted performance with no animator and
    no clip, which is exactly what the `animator-null` warnings for `GO` and `MagicSuccessHold`
    reported. These are now observed as clip-less triggers identified by the take itself, so they
    reach the catalog and can be authored. They carry no clip length, so the loop-length check in
    FR-040 does not apply to them.
11. **Generic take names defeated the enemy matching.** Counting matching take names tied constantly
    because names like `Hold` are shared, and the tie fell back to a take-name prefix — visible in
    the log as `gallery/Hold/...`.

### Identifying the enemy authoritatively (2026-08-05)

Name-based matching was then found to be wrong at its root: 外なる者の呪い#2 was reported as 巨大豚,
and every game-over take collapsed into a single `GO` row. A take player covers a whole background
scene and several enemies share one, so the loaded take list does not belong to a single enemy —
no scoring over it can identify one.

`GalleryUI.ButtonGuideUI.EnemyData` is the enemy the viewer is actually showing, held by the UI that
draws the gallery's own tab bar (it also exposes `NextTake` / `PrevTake`, matching the on-screen
`a Previous` / `d Next`). The actor is now read from there, and the entire matching heuristic was
deleted rather than kept as a fallback: it produced confidently wrong attributions, and wrong data
is worse than missing data here.

When the enemy cannot be read, the actor id is now `unidentified:<prefix>` with a warning, so a
suspect row is visibly suspect and distinct enemies still cannot collapse into one.

This also explains the collapsed game-over rows: an EventPlayer take carries no clip, so its key is
`gallery/<actor>/GO/reaction/GO` and the actor is the only thing separating one enemy's defeat from
another's. With the actor correct, they separate again.

**Confirmed in-game (2026-08-05).** A rebuilt catalog (237 rows, 13 observed) contains zero
`unidentified:` rows and zero legacy fallback ids. The five observed game-over takes each sit under a
distinct, correctly named enemy:

| Enemy | actorId | stage | clip |
|---|---|---|---|
| 外なる者の呪い | `GaID_NoItem` | `GO` | 0 (EventPlayer) |
| 外なる者 | `GaID_OuterOne` | `GO` | 0 (EventPlayer) |
| 豚人間 | `GaID_VillagerMiddleMale_A_Pig` | `GO4` | 0 (EventPlayer) |
| 子抱えヒル | `GaID_SwampLeech_Adult` | `Go_End1` | 5.850 s and 8.183 s (Animator, two clips) |

Both previously reported misattributions are gone (子抱えヒル was 大羽虫, 外なる者の呪い was 巨大豚),
which closes findings 7, 10, and 11 and verifies FR-031 and FR-032 in-game.

### EDI device-send thread-safety, revision E7 (2026-08-09)

`Edilog20260809.txt` (21:28–21:33) recorded the piston's `LinearAsync` failing 26 times in a row
with `InvalidOperationException` inside `ButtplugMessage.GetName`, starting the moment three
devices began one gallery together, while both UFO devices kept playing. Buttplug 4.0.0 caches
message metadata in an unlocked static `Dictionary`, and EDI's per-device playback tasks serialize
concurrently, so racing first-time lookups can corrupt the cache; the affected message type then
fails on every send until the process restarts. This is the field failure previously perceived as
"A10 and UFO timing is unstable" — the channel model itself routed correctly throughout the log
(`Rotate: 1 → ufo-left`, `Rotate: 2 → ufo-right`, strict variant resolution refused every
cross-variant request).

Fixed as SPEC001 7.4.2 E7: `ButtplugMessageMetadataPrewarm` resolves every message type once,
single-threaded, in the `ButtplugProvider` constructor, making the cache read-only for the life of
the process. Covered by `Edi.Core.Tests/Devices/ButtplugMessageMetadataPrewarmTests` (16 threads ×
1000 lookups over all message types; `LinearCmd`/`RotateCmd`/`StopDeviceCmd` proven pre-resolved).
All 137 Edi.Core tests pass. Deployed `Edi.exe` SHA-256
`D5B47F31442D598B881E10C104FAF1DA7CB82D6601F25FA6BDDF12434C50178D`; the prior binary is kept as
`Edi/Edi.exe.bak-pre-spec001-e7`. The fix takes effect on the next `Edi.exe` start.

## Outstanding verification

The following require an actual game session and are not covered by the automated suite:

1. Gallery stage enumeration against a real `GaTakePlayer` (FR-032, AC-024).
2. Hold-stage identification across two enemies with different stage counts (FR-033, AC-025).
3. The corrected guard order actually producing gallery observations, which the 2026-08-05 session log showed were absent entirely.
4. Physical device behaviour for preview, filler strength, and shutdown Stop.
