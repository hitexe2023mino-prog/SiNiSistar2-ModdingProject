# REFACTOR001: 改善要項

| 項目 | 内容 |
|---|---|
| 種別 | **リファクタ計画。規範仕様ではない** |
| 状態 | 施策確定済み（2026-08-11 のディベートで採否・優先順位を決定） |
| 最終更新日 | 2026-08-11 |
| 想定読者 | 施策を実装する人、施策の採否理由を確認する人 |

実装したMODとシステムの改善施策を、採否・優先順位・受け入れ条件つきで記録する。本書は**挙動を保ったままの内部改善**を確定させる文書である。外部から観測できる挙動を変える施策は 4章へ分離し、設計の確定は規範仕様（`docs/specifications/`）側で行う。

## 観点

実施済みの改善施策については、より優れた施策への置き換えと、既存施策のエンハンスを検討する。未着手の領域については、実際に起きている痛みを起点に施策を起こす。

観点そのものは施策ではない。以下の観点は、2章・3章の各施策がどの軸に効くかを示す分類として使う。

### コーディング

- 可読性 / 冗長性撤廃 / 保守性向上 / 再利用性向上 / 拡張性向上 / パフォーマンス向上

### ゲームシステム

- ゲームの難易度向上
  - 性的攻撃を受けることを促すメリット（[SPEC005](../specifications/SPEC005.md)）
  - 性的攻撃を受けることを促すデメリット（[SPEC002](../specifications/SPEC002.md), [SPEC003](../specifications/SPEC003.md)）
  - エネミーオブジェクトのルール変更（[SPEC004](../specifications/SPEC004.md)）

## 1. 現状の確定

施策を起こす前に確認した事実を記録する。以降の施策はここを根拠とする。

### 1.1 ゲームシステムの3項目は実装済みである

| 観点の項目 | 正本 | 実装状況 |
|---|---|---|
| 性的攻撃を受けるメリット | SPEC005 | 実装済み（`5596987`、`e42f163`）。[SPEC005-traceability](../implementation/SPEC005-traceability.md) |
| 性的攻撃を受けるデメリット | SPEC002 / SPEC003 | 実装済み。SPEC003 は v1.2 + CHG-017 まで反映 |
| 進行に時間をかけると画面外・背後から敵が出現 | SPEC004 5.3（FR-309 / FR-310、AC-303 / AC-304） | 実装済み。`StagnationDetector`、`SpawnPointClassifier`、`AdditionalSpawner` |

したがって本書の役割は、これらを**新規に企画すること**ではなく、実装済みの機構が意図した体験を生んでいるかを確かめ、必要ならエンハンスすることである。

### 1.2 コード規模とテストの分布

| 層 | 行数 | 自動テスト |
|---|---|---|
| `src/*.Core`（純粋ロジック） | 12,247 | 9,831 行 |
| `src/*.Plugin`（IL2CPP 結合） | 15,995 | **0 行** |

数え方（**RF-001 の実施前**の値。`bin`/`obj` 配下の生成ファイルを除外する。除外しないと `AssemblyInfo.cs` と `GlobalUsings.g.cs` が混入し、`src` で 476 行、`tests` で 272 行を過大に数える）。RF-001 で `src/Shared/` が加わり、下の2コマンドはどちらもそこを数えないため、実施後の値は 7章を参照する。

```bash
find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -path '*.Core/*' -exec cat {} + | wc -l
find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -path '*.Plugin/*' -exec cat {} + | wc -l
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec cat {} + | wc -l
```

テストプロジェクト4本（`tests/*.Core.Tests`）は、いずれも対応する `*.Core` だけを `ProjectReference` しており、Plugin を参照するものは1つも無い。Plugin は BepInEx / Il2CppInterop のランタイム初期化を前提とするため、ゲームプロセス外でのテストは前例が無い。実行可能性そのものは検証していないため、「Plugin にテストを書く」は不可能とは断定せず、前例の不在を根拠に見送る（RJ-902）。痛みは **Plugin 層に残っている純粋ロジック**の側にある。

最大の該当箇所は [PleasureObserver.cs](../../src/SiNiSistar2.Pleasure.Plugin/PleasureObserver.cs) である。3,118 行、分岐 164（`grep -c 'if (\|else if'`）。RF-003 の効果はこの2つの数値の変化で測る。

### 1.3 ソースの完全重複は1件である

`SiNiSistar2.Difficulty.Plugin/StartupGuard.cs` と `SiNiSistar2.Pleasure.Plugin/StartupGuard.cs` は 193 行で、差分は `namespace` の1行のみだった。**RF-001 で解消済み**であり、現在の正本は [src/Shared/StartupGuard.cs](../../src/Shared/StartupGuard.cs) 1つである（7.5）。以下はその判断の根拠として、変更前の状態を残したものである。

同名だが別物であり統合対象にならないものを区別して記録する。

| ファイル | 判定 |
|---|---|
| `RandomSource.cs`（Difficulty.Core / Spawn.Core） | 別物。前者は `double NextUnit()`、後者は `float NextFloat()` + `int NextInt()` + シーン・訪問回数からの決定的シード |
| `BuildFingerprint.cs`（Edi.Core / Spawn.Plugin） | 別物。前者は引数で検証、後者は期待ハッシュを定数で保持 |

### 1.4 起動時に 51MB の SHA-256 が3回計算される

**RF-001 で解消済み**（7.5）。以下は変更前の状態であり、行番号もその時点のものである。

`StartupGuard` は `AppDomain` スロットで SHA-256 の結果をプロセス内共有し、自身のコメントで「各MODが個別に計算すると起動に1秒近く足す」と述べている。しかしこの共有を使うのは Difficulty と Pleasure だけだった。

| 呼び出し元（変更前） | 経路 | キャッシュ |
|---|---|---|
| `DifficultyPlugin.cs:528` / `PleasurePlugin.cs:1156` | `StartupGuard.Sha256` | あり（共有） |
| `EdiPlugin.cs:93` | `BuildFingerprint.ComputeSha256` | なし |
| `SpawnPlugin.cs:335` | `BuildFingerprint.Sha256` | なし |

4MOD 構成では `GameAssembly.dll`（実測 51.3MB）の SHA-256 が起動時に3回走る。`global-metadata.dat`（実測 11.5MB）も同様である。設計意図が半分しか適用されていない。

計測（2026-08-11、`sha256sum GameAssembly.dll` を3回、OSファイルキャッシュが温まった状態）は 157 / 134 / 132 ミリ秒で、中央値は **134ms** であった。重複2回を削減した場合の見込みは約 270ms（冷キャッシュではこれより大きい）。`StartupGuard` のコメントが述べる「1秒近く」は、この条件では再現していない。RF-001 の効果は、この実測値を基準として主張する。

### 1.5 文書と実装の追跡性に欠陥がある

[SPEC005-traceability.md:119](../implementation/SPEC005-traceability.md:119) は FR-415 を `Tested` とし、根拠に `CrestPleasureAndValidationTests.ShippedDefaultsLeaveEveryNewMechanismInert` を挙げる。**このテストは存在しない**（実在するのは `ShippedDefaultsKeepTheCliff`）。リポジトリ全体でこの名前に一致するのは当該文書の1行だけである。

同種の食い違いを以下に記録する。

| 箇所 | 食い違い | 文書の性格 | 担当 |
|---|---|---|---|
| FR-415 の検証根拠 | 実在しないテスト名。`Tested` の裏が取れていない | 非規範 | RF-004 |
| `PlayerVitals.IsMpEmpty` | traceability に記載。実在するシンボルは `IsMpLow` | 非規範 | RF-004 |
| `MpZeroStunScheduler.Evaluate` の doc | 「MP is empty」と記述。実際の条件は `MpPenaltyMpFraction` 未満 | コードコメント | RF-004 |
| `CorruptionCrestGainScale` | SPEC005 6章「実測前 `1.0`、暫定案 `2.0`」／traceability「昇華後のみ 2.0」／コード `= 2f` | **規範** | RF-105 |
| 実測前の例外値 | SPEC005 6章「例外は5項目」／traceability:17「`CrestPleasureGainScale` だけ」 | **規範** | RF-105 |
| `CrestFx` | SPEC005 6章「実測後に確定」「実測前は時間0」／コード `1.2f`・`0.2f`・`Enabled = true` | **規範** | RF-105 |

`Tested` の根拠が実在しないテストである以上、他の `Tested` も同じ確度とは言えない。

規範側の3件は SPEC005 6章の既定値表にあり、規範記述であるため本書では直せない（**本書6章**の非目標）。実装と仕様のどちらが正しいかの判断を含むため、RF-105 として引き継ぐ。

### 1.6 MP0ペナルティの閾値はコードに反映されている

「MPが2割を切ったらペナルティ」への変更（CHG-517）は、コード上は反映済みである。

- [PlayerVitals.cs](../../src/SiNiSistar2.Pleasure.Plugin/PlayerVitals.cs) の `IsMpLow(threshold)` が `fraction < threshold` で判定する。
- [PleasureObserver.cs](../../src/SiNiSistar2.Pleasure.Plugin/PleasureObserver.cs) の `ReadMpPenaltyState` が条件集合へ `PlayerVitals.IsMpLow(tuning.MpFraction)` を入れる（行番号ではなくシンボルで指す。RF-003 の移設で行が動いたため）。
- 実機の `BepInEx/config/community.sinisistar2.pleasure.cfg` は `Enabled = true`、`MpPenaltyMpFraction = 0.2`（出荷既定の `false` から有効化済み）。

発火しない場合の最有力の原因は、同じ条件集合にある `MpPenaltyCorruptionFraction = 1` である。SPEC005 5.3 適用条件2 は「堕落が `CorruptionCap`（既定 12）に達していること」を AND で要求するため、MPを2割まで削っても堕落が満タンでなければ抽選に入らない。ただしこれは仮説であり、実機で確定させる（RF-005）。

観測手段は実装側にあり、規範記述は無い。`PleasureObserver` は `ConditionsMet` を7つの論理積（`Corrupted && CrestWorn && MpLow && !Bound && !Dead && !Paused && !Cinematic`）として持ち、`F4` で `DrawSpec005Panel` が各条件を画面へ、`Shift+F4` で `ForceMpPenaltyForDebugging` が `DescribeMpConditions` の1行を `LogInfo` へ出す。**SPEC005 にはこの診断機構の記述が無い**（5章は 5.1〜5.5 で終わり、「デバッグ」の語が現れない）。SPEC004 は同種の機構を 5.8 / 5.9 として規範化しているため、この非対称は追跡性の欠落である（RF-105）。

## 2. 施策台帳

| ID | 施策 | 種別 | 対象 | 解決する痛み（根拠） | 受け入れ条件 | 検証 | 優先度 | 状態 |
|---|---|---|---|---|---|---|---|---|
| RF-005 | MP0ペナルティが発火しない件の切り分け | 調査 | `PleasureObserver.ReadMpPenaltyState` / `DrawSpec005Panel`（`F4`）/ `ForceMpPenaltyForDebugging`（`Shift+F4`） | 1.6。閾値は反映済みだが実機で作用が確認できていない。原因を推測で直すと誤った箇所を壊す | `ConditionsMet` の7条件のうちどれが `false` かを名指しできる。原因が `MpPenaltyCorruptionFraction` なら RF-102 へ、実装欠陥なら修正施策を起こす | `Shift+F4` が `LogInfo` へ出す `DescribeMpConditions` の行を `BepInEx/LogOutput.log` から採り、本書へ結果を追記 | 最高 | **ブロック中（実機操作が必要。7章）** |
| RF-001 | `StartupGuard` をソース共有し4MODへ適用 | 振る舞い保存 + 性能 | `src/Shared/StartupGuard.cs`（新設）、4つの `*.Plugin.csproj`、`EdiPlugin` と `SpawnPlugin` のビルド照合 | 1.3（193行の完全重複）、1.4（51MBのSHA-256が3回） | 193行の実体が1つになる。4MODすべてが共有スロット経由で SHA を取る。DLL 依存関係は増えない（`<Compile Link>` によるソース共有） | Plugin 層からのハッシュ計算呼び出しが `StartupGuard.Sha256` へ統一される。次のコマンドが現在の4件から0件になる（`BuildFingerprint` の期待ハッシュ定数と比較ロジックは Spawn に残るため、ファイルの消滅は条件にしない）。削減量は RF-006 の基準値（134ms/回 × 削減2回）から算出する。既存テスト全通過 | 高 | **実施済み（7章）** |
| RF-006 | SHA-256 の重複計算コストの基準値計測 | 性能 | `GameAssembly.dll`、`SiNiSistar2_Data/il2cpp_data/Metadata/global-metadata.dat` | RF-001 の効果を主張する基準値が無い | 両ファイルの SHA-256 単体コストを同一手順で3回計測し中央値を取る。現状の計算回数（各3回）と RF-001 後の回数（各1回）の差から削減量を算出する | 1.4 に記録済み（`GameAssembly.dll` 中央値 134ms）。RF-001 後に同手順で再計測し比較する | 高 | **実施済み（1.4）** |
| RF-002 | 文書参照の検証スクリプトを追加 | 振る舞い保存 | `.github/scripts/`（新設）、`docs/implementation/*.md` | 1.5。実在しないテスト名で `Tested` と記録されていた。人手では再発する | `docs/implementation/*.md` が挙げるテスト名・シンボル名を `tests/` と `src/` へ照合し、不一致を失敗として報告する。`validate-skills.py` と同じ形 | スクリプトが現在の不一致を検出することを確認してから RF-004 で解消 | 高 | **実施済み（7章）** |
| RF-004 | 非規範文書とコードコメントの旧解釈残留を解消 | 振る舞い保存 | `docs/implementation/SPEC005-traceability.md`、`MpZeroStunScheduler` の doc コメント | 1.5 のうち非規範側の3件（実在しないテスト名、`PlayerVitals.IsMpEmpty`、`Evaluate` の doc「MP is empty」）。実装が事実であり、文書側が古い | RF-002 のスクリプトが通る。旧シンボル名が残らない | RF-002 のスクリプトを実行 | 高 | **実施済み（7章）** |
| RF-003 | `PleasureObserver` の純粋ロジックを Core へ抽出 | 振る舞い保存 | `PleasureObserver.cs`（3,118行）→ `SiNiSistar2.Pleasure.Core` | 1.2。リポジトリ最大のファイルがテスト不能な層にあり、判断ロジックを変えるたびに実機確認しか手段が無い | ゲーム型に依存しない判断・状態遷移が Core へ移り、移した分にテストが付く。1.2 の手順で数えた分岐数（現在 164）が減る | 移設したロジックの新規テスト。既存テスト全通過。実機で挙動不変を確認 | 中 | **一部実施（7章）** |

**RF-001 の検証コマンド**

```bash
grep -rnE 'BuildFingerprint\.(ComputeSha256|Sha256)\(' src --include=*.cs | grep -v '/bin/\|/obj/' | grep '\.Plugin/'
```

実施前は `EdiPlugin.cs:93,94` と `SpawnPlugin.cs:335,336` の4件が出た。**実施後は0件**である（7.5）。回帰の検知に使えるため、このコマンドは残す。

**優先度の根拠**

- RF-005 が最高: 他の施策と違い、これは「作るか」ではなく「今どうなっているか」の問いであり、答えが RF-102 の要否を決める。
- RF-001 が高: 根拠は保守性である。193行の完全重複（1.3）は実測値に依存せず存在し、変更は可逆で影響範囲も閉じている。起動コストの削減（1.4、見込み約270ms）は副次的な利得として扱う。当初はこちらを主根拠にしていたが、実測が `StartupGuard` のコメントの「1秒近く」を再現しなかったため、根拠を置き換えた（2026-08-11 のレビューで確定）。
- RF-002 / RF-004 が高: 追跡性が崩れている状態では、他のどの施策の完了判定も信用できない。RF-002 → RF-004 の順で行い、スクリプトが現在の不一致を実際に検出することを先に確かめる。
- RF-003 が中: 痛みは最大だが、テストの無い 3,118 行を触るリスクも最大である。RF-002 / RF-004 で追跡性を回復した後に着手する。

## 3. 依存と順序

| ID | 先行する施策 | 理由 |
|---|---|---|
| RF-001 | RF-006（実施済み） | 変更前の SHA-256 単体コストが無ければ削減量を主張できない。値は 1.4 にある |
| RF-004 | RF-002 | スクリプトが現在の不一致を検出することを先に確かめる |
| RF-003 | RF-002, RF-004 | 追跡性が回復していない状態で最大のファイルを触らない |
| RF-102 | RF-005 | 適用条件を変える前に、いま何が条件を落としているかを確定させる |

## 4. 仕様変更の引き継ぎ

外部から観測できる挙動を変える施策である。本書では**解決したい体験上の課題と影響する既存仕様の特定まで**を行い、設計の確定は引き継ぎ先で行う。

| ID | 変えたい体験 | 根拠 | 影響する既存仕様 | 引き継ぎ先 | 優先度 |
|---|---|---|---|---|---|
| RF-101 | 透明敵（ステルス個体）を不具合ではなく演出として出す | SPEC004 14.2。2026-08-10 の実機で、蘇生時に `EnemyDead.ResumeAlive` を省いた個体がスプライトを失ったまま行動する事象を**実際に発生させた**実績がある。実現可能性が実証済みで、実装コストが他候補より低い | SPEC004 5.3、A-18 | `refine-specification` | 高 |
| RF-102 | MP0ペナルティが体験として成立する適用条件へ見直す | 1.6。`MpPenaltyCorruptionFraction = 1`（堕落が上限）の AND が、MP2割という条件を実質到達不能にしている可能性がある。RF-005 の結果を前提とする | SPEC005 5.3 適用条件2、DEC-405 | `refine-specification` | 高（RF-005 の後） |
| RF-103 | 追加スポーンの既定値を確定し、難易度向上を体験として届かせる | SPEC004 14.2 の筆頭。確率・上限・停滞しきい値が保守的な既定値のままで、最終調整が未完了 | SPEC004 5.3、6章 | `refine-specification`（実測とプレイフィールが前提） | 中 |
| RF-104 | 段階導入機能（5.4 罠・落下物、5.5 ミミック疑似宝箱）を有効化する | 既定無効のまま出荷されている。A-9〜A-12 が未実測で、否定されれば撤回される設計 | SPEC004 5.4、5.5、付録A A-9〜A-12 | `refine-specification`（実測が前提） | 中 |
| RF-105 | SPEC005 の規範記述を実装と一致させる | 1.5 の規範側3件（`CorruptionCrestGainScale` の既定、実測前の例外値の数、`CrestFx` の既定）と、1.6 の診断機構の記述欠落。既定値表は規範記述であり、本書では直せない | SPEC005 6章、5章（診断機構の節が無い） | `refine-specification` | 高 |

RF-103 / RF-104 は実測が前提であり、実測前に既定値を動かすことは SPEC002/003/004/005 が共有する「未確定値の既定は挙動不変」規約に反する。

## 5. 却下・延期の記録

| ID | 施策 | 決着 | 決め手になった根拠 | 再検討の条件 |
|---|---|---|---|---|
| RJ-901 | `RandomSource` / `BuildFingerprint` を共通化する | 却下 | 1.3。同名だが実装も用途も別物である。統合すると片方の要件を他方へ持ち込むことになる | 両者の要件が実際に一致したとき |
| RJ-902 | Plugin 層に自動テストを追加する | 却下 | 1.2。テストプロジェクト4本はいずれも Core だけを参照しており、Plugin を対象にした前例が無い。Plugin の型は Il2CppInterop のランタイム初期化を前提とするため、ゲームプロセス外で意味のある実行ができるかは**未検証**である。この却下は前例の不在に基づき、不可能性の証明ではない | Plugin 型をテストホストから実行できることを実証したとき。代替として RF-003 を実施する |
| RJ-903 | 共有 DLL（`SiNiSistar2.Common`）へ切り出す | 却下 | MOD の独立性（単体導入・個別更新）と衝突する。README と SPEC004 7.3 が明記する前提である。同じ目的は `<Compile Link>` によるソース共有（RF-001）で、DLL 依存を増やさずに達成できる | 独立性の前提そのものを見直すとき |
| RJ-904 | `StartupGuard` のコピーを Edi / Spawn へも配置する | 却下 | 起動コストは解消するが、193行のコピーが4つになる。RF-001 が同じ効果をコピーを増やさずに達成する | なし |

## 6. 前提とリスク

| 項目 | 内容 |
|---|---|
| 前提 | 実装が事実であり、文書が食い違う場合は文書側を直す（1.5、1.6 の扱い）。 |
| 前提 | MOD は互いに独立して動作し個別に更新できる。この前提を壊す施策は採らない（RJ-903）。 |
| 非目標 | 本書では規範仕様（製品要件、ゲームルール、受け入れ条件）を改訂しない。4章の施策は引き継ぎ先で設計する。 |
| 非目標 | 実測が済んでいない調整値の既定を動かさない。 |
| リスク | RF-003 はテストの無い 3,118 行を触る。移設対象を「ゲーム型に依存しない判断」に限定し、1施策1領域で分割して進める。検知は実機での挙動確認による。 |
| リスク | RF-001 のソース共有は、4つの csproj が同じファイルを取り込む構成になる。片方のMODだけをビルドしても成立することを、各 `*.Plugin.csproj` 単体ビルドで確認する。 |
| リスク | RF-005 の結果によっては RF-102 が不要になる、または実装欠陥の修正施策へ置き換わる。台帳は結果を受けて更新する。 |

## 7. 実装結果（2026-08-11）

### 7.1 変更前の基準

| 項目 | 変更前 |
|---|---|
| ビルド | 8プロジェクトすべて成功。警告0、エラー0 |
| テスト | Difficulty 89、Spawn 118、Pleasure 260 が合格。Edi は 172 合格・**1 失敗** |

既存の失敗は `RepositoryLayoutTests.SwollenFillerIsMechanicallyStrongerThanNormalFiller(variant: "ufo-right")` で、`ufo-right: swollen amplitude 21 should exceed 23` と報告する。funscript アセットの振幅であり、本書のどの施策とも無関係である。**変更前から失敗しており、本作業では触っていない**。そのため RF-001 / RF-003 の受け入れ条件「既存テスト全通過」は、この1件を除いた同一性として判定した。

### 7.2 実施後のコード規模

1.2 の値は RF-001 実施前のものである。RF-001 で `src/Shared/` が加わったため、層は3つになった。

| 層 | 変更前 | 変更後 | 数え方 |
|---|---|---|---|
| `src/Shared`（4MOD 共有ソース） | — | **193** | `find src/Shared -name '*.cs' -exec cat {} + \| wc -l` |
| `src/*.Core` | 12,247 | **12,329** | 1.2 のコマンド |
| `src/*.Plugin` | 15,995 | **15,552** | 1.2 のコマンド |
| `tests` | 9,831 | **9,956** | 1.2 のコマンド |

Plugin 層は 443 行減った。内訳は、重複していた `StartupGuard` 2本（386行）、`PleasureObserver` からの移設（62行）、`Spawn.Plugin/BuildFingerprint` の未使用メソッド除去（5行）で計 453 行の削除であり、差の 10 行は追加した `using` と説明コメントである。共有ソースの 193 行は3つのコマンドのいずれにも入らないため、上の表に独立した行として置いた。

### 7.3 RF-002 — 文書参照の検証スクリプト

[validate-doc-references.py](../../.github/scripts/validate-doc-references.py) を追加した。`docs/implementation/*.md` の中で `` `Type.Member` `` の形をした参照と、`` `SomethingTests` `` の形をしたテストクラス名を、`src/` と `tests/` の宣言と照合する。

このリポジトリが所有する型だけを検査する。`Type` が `src/` または `tests/` で宣言されている場合にだけ照合し、ゲーム側や BepInEx の型（`PlayerStatusManager.MP`、`AbnormalData.MaxLevel`）は素通しする。これが誤検出を防ぐ要である。

もう一つの規約を実装に反映した。これらの文書は長いテスト名を先頭一致で省略する（`ProfileValidationTests.ShippedDefaultsHaveNoEffect` は実際には `...OnTheGame` で終わる）。この省略は欠陥ではないため先頭一致を合格とし、**何も指さない名前だけ**を報告する。区別しなかった初回実行では29件が出たが、そのうち22件はこの省略だった。

### 7.4 RF-004 — 旧解釈残留の解消

スクリプトの報告は **8件**である（先頭一致の規則を入れた時点で7件、テストクラス名の規則を足して1件）。このうち 1.5 で既に把握していたのは2件（実在しないテスト名、`PlayerVitals.IsMpEmpty`）で、**6件は新規発見**である。1.5 の非規範側3件目はコードコメントであり、スクリプトの対象外なので報告には現れない。

訂正は `git diff --stat docs/implementation/` で **3文書・7行**、行内の参照としては **11件**である（報告された8件に加え、同じ行に載っていた3件を巻き込みで直した）。

| 文書 | 誤 | 正 |
|---|---|---|
| SPEC003-traceability | `PleasureMeterTests.SensitivityIncreasesTheGainPerHit` | `.CorruptionIncreasesTheGainPerHit` |
| SPEC003-traceability | `PleasureObserver.IsAtClimaxLimit` | `.ApplyClimaxLimit` |
| SPEC003-traceability | `SensitivityAndClimaxTests`（2箇所） | `CorruptionAndClimaxTests` |
| SPEC004-traceability | `HudModel.DebugPanel` と `HudModelTests.DebugPanel*`（3件） | `HudModel.DebugHeader`、`HudModelTests.DebugHeader*` |
| SPEC005-traceability | `PlayerVitals.IsMpEmpty` | `.IsMpLow` |
| SPEC005-traceability | `CrestPleasureAndValidationTests.ShippedDefaultsLeaveEveryNewMechanismInert` | `.ShippedDefaultsLeaveOnlyTheUnmeasuredMechanismsInert` |

最後の1件について 1.5 の記述を訂正する。1.5 は「実在するのは `ShippedDefaultsKeepTheCliff`」と書いたが、これは誤りだった。FR-415 を実際に検証しているテストは `ShippedDefaultsLeaveOnlyTheUnmeasuredMechanismsInert` であり、名前が `Every` から `OnlyTheUnmeasured` へ改称された際に文書が追随しなかったのが原因である。改称は意味の変更を伴っており（利用者確定値は不活性ではない）、文書だけが旧い意味のまま残っていた。

コード側では `MpZeroStunScheduler.Evaluate` の doc コメントを直した。「MP is empty」は CHG-517 より前の解釈である。

### 7.5 RF-001 — StartupGuard のソース共有

[src/Shared/StartupGuard.cs](../../src/Shared/StartupGuard.cs) を正本とし、4つの `*.Plugin.csproj` が `<Compile Include>` で取り込む。名前空間は `SiNiSistar2.Shared` に統一した。**本体は1文字も変えていない**（元ファイルとの差分は `namespace` 行のみであることを確認済み）。

| 受け入れ条件 | 結果 |
|---|---|
| Plugin 層のハッシュ計算呼び出しが0件 | 4件 → **0件** |
| `StartupGuard.cs` の実体が1つ | 2つ → **1つ** |
| 4MOD すべてが共有スロット経由 | `DifficultyPlugin`、`PleasurePlugin`、`EdiPlugin`、`SpawnPlugin` の6箇所すべて `StartupGuard.Sha256` |
| DLL 依存関係が増えない | `ProjectReference` は各MOD の `*.Core` のみ。変更なし |
| 単体ビルドが成立（6章のリスク） | 4つの `*.Plugin.csproj` を個別にビルドし、すべて成功 |
| 既存テスト | 7.1 と同一 |

`Spawn.Plugin/BuildFingerprint.cs` からは、未使用になった `Sha256` を除いた。期待ハッシュの定数と比較ロジックは残した。これは各MODが「自分は何のビルドに対して計測されたか」を述べる部分であり、共通化の対象ではない（RJ-901）。

**削減量**: `GameAssembly.dll` の SHA-256 は起動あたり3回から1回になった。1.4 の基準値 134ms/回から、削減見込みは約 270ms である。実起動での再計測は未実施。

### 7.6 RF-003 — 純粋ロジックの Core への抽出（一部）

`MpPenaltyState`（7条件の論理積）と条件の説明文生成を [MpPenaltyState.cs](../../src/SiNiSistar2.Pleasure.Core/MpPenaltyState.cs) へ移した。ゲーム型に一切依存しない領域を選んだ。

この領域を最初に選んだのは、RF-005 が「7条件のうちどれが `false` か」を問うものであり、その答えを組み立てるのがまさにこのコードだからである。移設によって、実機を起動せずに検証できるようになった。

| 指標 | 変更前 | 変更後 |
|---|---|---|
| `PleasureObserver.cs` の行数 | 3,118 | **3,056** |
| `PleasureObserver.cs` の分岐数 | 164 | **160** |
| Pleasure.Core.Tests の合格数 | 260 | **272**（新規12件） |

追加したテストは [MpPenaltyStateTests.cs](../../tests/SiNiSistar2.Pleasure.Core.Tests/MpPenaltyStateTests.cs) にある。7条件のいずれか1つが欠けると成立しないこと、読めないMPが「低いMP」と混同されないこと、抑止要因が該当時のみ表示されることを固定した。

**未完了**: RF-003 は `PleasureObserver` の他の領域を残している。また受け入れ条件の「実機で挙動不変を確認」は未実施である（7.7）。

### 7.7 未実施と引き継ぎ

| 項目 | 状態 | 必要なこと |
|---|---|---|
| RF-005 | **ブロック中** | 実機操作が要る。ゲームを起動し、堕落を上限まで進めた状態で `Shift+F4` を押し、`BepInEx/LogOutput.log` の `Shift+F4:` で始まる行を採取する。その行が7条件のどれが `false` かを名指しする |
| RF-001 / RF-003 の実機確認 | 未実施 | ゲームを起動し、起動ログと MP0 パネル（`F4`）が変更前と同じ内容を出すことを確認する |
| RF-003 の残り | 未実施 | `PleasureObserver` の他の純粋領域。1施策1領域の方針を維持する |
| RF-101〜RF-105 | 未着手 | 4章のとおり `refine-specification` へ引き継ぐ |
| `ufo-right` の既存テスト失敗 | 未対応 | 本書の対象外。funscript アセットの振幅の問題であり、別途扱う |
