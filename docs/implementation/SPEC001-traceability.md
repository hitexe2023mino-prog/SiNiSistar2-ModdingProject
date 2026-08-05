# SPEC001 implementation traceability

Normative source: [`docs/specifications/SPEC001.md`](../specifications/SPEC001.md). This document records implementation and verification only; it does not change the specification.

> **STALE as of the 2026-08-05 SPEC001 revision (CHG-014 〜 CHG-018).** The measured-motion generation path was withdrawn after the target build was found to have no Humanoid avatar and no bones (SPEC001 付録C). The rows below for FR-021, FR-022, FR-023, FR-025, FR-026, FR-027, FR-028 and AC-017 〜 AC-023 refer to withdrawn requirements; their "Tested" status no longer implies conformance. `UDR-003` and `UDR-004` are likewise superseded. The event key gained a `stageId` element, so the FR-002 row is now partial. Requirements FR-031 〜 FR-041 and AC-024 〜 AC-034 are not yet implemented and have no rows here. This banner is removed when the ledger is rebuilt against the revised specification.

## User-directed implementation constraints

| ID | Direction received after SPEC001 | Implementation |
|---|---|---|
| UDR-001 | Distribution packaging is out of scope | Removed the release builder and `dist` source-of-truth; the repository runtime layout is authoritative |
| UDR-002 | Keep every required runtime file in the repository | Placed BepInEx 6 IL2CPP, CoreCLR, generated game interop, plugin DLLs, mappings, EDI configuration, and funscripts at their runtime paths |
| UDR-003 | Users cannot manually enter event mappings | Event capture is always enabled and writes `coverage.json` plus machine-readable `mapping-candidates.json`; start/current/end transitions are derived automatically |
| UDR-004 | Funscript must reproduce the game animation's motion, not merely share its duration | Added LateUpdate motion capture, measured relative-motion extraction, quality-gated funscript generation, EDI asset upload, and generated mappings; synthetic time-only waveforms are rejected |

Status meanings:

- **Tested**: implemented and covered by an automated test or completed runtime smoke test.
- **Implemented / unverified**: code or assets exist, but the required physical-device or full-playthrough evidence is outstanding.
- **Blocked**: external evidence or an exercised game path is still required.

## Requirement ledger

| Requirement | Implementation | Verification | Status |
|---|---|---|---|
| FR-001 | `EdiPlugin` BepInEx IL2CPP entry point | Isolated game startup log loaded plugin 1.0.0 | Tested |
| FR-002 | `RuntimeObserver` creates explicit context/actor/animation/phase keys | Plugin compiles against generated SiNiSistar2 interop; coordinator tests use normalized keys | Tested; full event census pending |
| FR-003 | `MappingRepository.TryResolve` accepts `mapped` only | `MappedEventUsesSeekAndDuplicateObservationDoesNotReplay`, unknown-event test | Tested |
| FR-004 | unclassified playback suppression and one-time warning | `UnknownEventIsDiagnosedAndNeverPlayed` | Tested |
| FR-005 | `PlaybackCommand` requires a channel; REST always emits `channels=` | HTTP and coordinator tests | Tested |
| FR-006 | one `breast` channel with `ufo-left` / `ufo-right` variants | `RuntimeFilesAndRequiredFillerVariantsExistInRepositoryLayout` | Tested |
| FR-007 | normalized animation time converted to millisecond seek | mapped-event and resume tests | Tested |
| FR-008 | per-channel last-command/event deduplication | duplicate observation test | Tested |
| FR-009 | `EndEvent` acts only on the matching active key | `StaleEndCannotReplaceNewerEvent` | Tested |
| FR-010 | fillers selected from current status after event end | status-change test | Tested |
| FR-011 | `Breast` / 膨乳 selects `filler-breast-swollen` | mapping, coordinator, and asset-strength tests | Automated behavior tested; hardware approval pending |
| FR-012 | event channels are excluded from filler updates | status-change test | Tested |
| FR-013 | runtime pause/resume and inactive Stop transitions | pause/resume test; runtime code inspection | Tested except physical devices |
| FR-014 | Unity observation remains on `Update`; HTTP is worker-based | async recovery test; runtime architecture | Tested |
| FR-015 | failures are caught, backed off, and isolated from Unity | startup with EDI offline; async recovery test | Tested |
| FR-016 | schema, duplicates, channels, hashes, and required fields fail closed | mapping validation tests; startup hash validation | Tested |
| FR-017 | `EventCaptureTracker` derives transitions; `DiagnosticRecorder` records candidates and classifications | transition and candidate-output tests | Tested |
| FR-018 | report emits numeric `unclassifiedCount` | coverage report test | Tested; counts update as game paths are exercised |
| FR-019 | `AsyncEdiCommandSink.ShutdownAsync` attempts Stop on both channels | shutdown implementation and startup smoke teardown | Implemented; hardware unverified |
| FR-020 | startup log includes hashes, plugin/mapping versions, path, and endpoint | isolated game startup log | Tested |
| FR-021 | `AnimationSessionWriter` plus `RuntimeObserver.LateUpdate` | JSONL writer test and repository-root startup created a real session | Tested; gameplay animation sample pending |
| FR-022 | `CaptureAnimationFrame` enumerates every Animator layer, state, transition, and clip; unsupported parameters carry a reason | Compiles against generated interop | Implemented / gameplay verification pending |
| FR-023 | event start enumerates every active Animator; `CaptureTransforms` traverses each descendant and annotates Humanoid bones | Compiles against generated interop | Implemented / gameplay verification pending |
| FR-024 | `CaptureTextChanges` records every `UnityEngine.UI.Text`; capability records explain unavailable parameters and TextMeshPro | Startup session captured title UI text with paths | Tested for Unity UI; event names covered by animation records |
| FR-025 | `MotionScriptGenerator` uses PCA projection of measured relative translation/rotation and measured pair distance | synthetic measured-motion tests | Tested algorithmically; physical correspondence pending |
| FR-026 | generator rejects static, discontinuous, non-dominant, or non-semantic signals and never synthesizes a fallback waveform | rejection and independent-side tests | Tested |
| FR-027 | `GeneratedMotionAssetStore`, `UploadAssetsAsync`, `generated-mappings.json` | multipart upload contract and generated mapping persistence tests | Tested except live EDI reload |
| FR-028 | two observed normalized-time wraps delimit the first full loop; boundary discontinuity is rejected | loop extraction and measured timestamp tests | Tested algorithmically |
| FR-029 | build/session-specific append-only path; catalogs remain separate | writer test and runtime session path | Tested |
| FR-030 | bounded nonblocking queue records dropped sequence/count and marks the interval unusable | writer test; overflow path implemented | Tested except forced overflow in Unity |

## Acceptance criteria

| Acceptance criterion | Automated/runtime evidence | Status |
|---|---|---|
| AC-001 | Isolated BepInEx game startup loaded the plugin and logged versions/hashes | Passed |
| AC-002 | mapped-event HTTP/coordinator tests verify key, gallery, seek, and `main` | Passed |
| AC-003 | repository-layout test verifies one `breast` channel and two variants | Passed |
| AC-004 | duplicate observation test | Passed |
| AC-005 | stale end test | Passed |
| AC-006 | swollen filler after event-end test | Passed |
| AC-007 | status update does not interrupt event test | Passed |
| AC-008 | pause/resume current-position test | Passed |
| AC-009 | inactive and shutdown Stop paths | Implemented / device verification pending |
| AC-010 | EDI-offline startup plus nonblocking worker test | Passed |
| AC-011 | latest-desired-state recovery test | Passed |
| AC-012 | unknown event diagnostic/suppression test | Passed |
| AC-013 | duplicate key and unknown channel test | Passed |
| AC-014 | automatic capture and machine-readable unclassified count | Capture/output mechanism passed; exhaustive gameplay coverage depends on which game paths are exercised |
| AC-015 | repository contains galleries and variants referenced by current mappings | Passed for current mappings |
| AC-016 | stronger script properties verified automatically | Passed |
| AC-017 | session schema/sequence plus real title-text capture; event animation path not yet exercised | Partial |
| AC-018 | measured pair-distance test verifies source timestamps and positions | Passed |
| AC-019 | two-wrap loop extraction and boundary validation | Passed algorithmically |
| AC-020 | independent left/right measured-transform selection | Passed algorithmically; physical devices pending |
| AC-021 | static and invalid signals produce no script | Passed |
| AC-022 | automatic mapping persistence passed; live EDI registration and repeated gameplay event pending | Partial |
| AC-023 | incomplete intervals are marked and excluded by `_eventCaptureComplete` | Implemented; Unity overflow test pending |

## Verification record (2026-08-04)

- `dotnet test ... -c Release`: 26 passed, 0 failed.
- `dotnet build SiNiSistar2.Edi.sln -c Release`: succeeded with 0 warnings and 0 errors using generated SiNiSistar2 interop.
- Isolated game launch: BepInEx `6.0.0-be.738`, Unity `2022.3.62f2`, plugin `1.0.0` loaded; EDI was intentionally offline and the plugin stayed fail-closed while retrying. A build/session-specific JSONL was created and captured `UnityEngine.UI.Text` changes with hierarchy paths.
- Repository-root diagnostic smoke: enumerated 71 abnormal-state identifiers and wrote valid `coverage.json` and `mapping-candidates.json`; the latter was successfully read back on the next startup. This was not an exhaustive gameplay traversal.
- Supported hashes: `GameAssembly.dll` `B8694933...499D`; `global-metadata.dat` `A56278D0...C84B`.

## Current operation status

The repository is self-contained for build and game-side execution. Event discovery requires no identifier entry. Unknown events remain fail-closed only while a complete measured interval is unavailable; quality-approved scripts are written, uploaded to EDI, and added to generated mappings automatically. A real gameplay event and physical-device run are still required to validate that the automatically selected 1D stroke is the intended pleasurable motion for each animation.
