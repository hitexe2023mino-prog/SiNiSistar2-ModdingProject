# SPEC002: 難易度上昇MOD

| 項目 | 内容 |
|---|---|
| 状態 | 実装可能 |
| 最終更新日 | 2026-08-08 |
| 対象 | SiNiSistar2 Windows x64版 |
| 対象Unityランタイム | Unity 2022.3.62f2 / IL2CPP |
| `GameAssembly.dll` SHA-256 | `B869493305BBE587598C8709E7FE271F00D79D37803C6A8241946D6A6297499D` |
| `global-metadata.dat` SHA-256 | `A56278D0162B6C148312B56FBE208B54BA9AF2D3BAD609EBF9349B7AE7DDC84B` |
| プラグインGUID | `community.sinisistar2.difficulty` |
| 共存対象 | [SPEC001](SPEC001.md) EDI連動MOD（GUID `community.sinisistar2.edi`） |
| 想定読者 | MOD実装者、テスト担当者、バランス調整担当者 |
| 開発ブランチ | `feature/difficulty-mod` |

## 1. 概要

### 1.1 背景と問題

SiNiSistar2 は `Normal` / `Casual` / `Hard` の3段階の難易度を持つ。`Hard` を選択すると、ゲームが持つ `*HardModeValue` 系のデータと `HardOnly` の敵配置が有効になる。しかし `Hard` はゲームが用意した最上位であり、それ以上に難度を上げる手段はない。

本作の失敗は「体力を失って死ぬ」ことよりも、状態異常を受け、拘束され、そこから抜け出せなくなることとして表現される。したがって難度を上げる操作として意味を持つのは、被ダメージ量の一律増加ではなく、**状態異常の付きやすさと重くなりやすさ**、および**拘束から抜けにくく、抜けても再び捕まりやすくなること**である。

一方、拘束からの脱出困難化には既にゲーム自身の軸が存在する。`穢れ`（`AbnormalType.Defilement`）が蓄積するほど脱出が困難になる仕組みであり、interop 上は `AbnormalList.GachaInputRateDefilement` と `GachaGachaParameter.m_DefilementBind` として存在する。MODがこの軸へ重ねて介入すると、ゲーム設計上の蓄積曲線が二重にかかり、`穢れ` を管理するというプレイ判断そのものが意味を失う。

同一ゲームルートには SPEC001 のEDI連動MODが常駐する。両MODは同じゲーム状態を読むため、難易度MODがゲームの識別子、アニメーション、時間尺度を書き換えると、EDI側のトリガー同定とデバイス制御が静かに壊れる。

### 1.2 目的

- `Hard` の上に位置するMOD独自の難易度階層を追加し、ゲームが用意した `Hard` 用データを活かしたうえでさらに難度を上げる。
- 状態異常の**付与率**と**レベル進行**を強化し、状態異常が早く付き、早く重くなるようにする。
- 快楽系の状態異常が有効な間、拘束からの抵抗入力が断続的に無力化される状況を作る。`穢れ` による既存の脱出困難化とは独立した軸として成立させる。
- 身重系の状態異常が有効な間、拘束から離脱した直後の復帰を遅くし、再拘束されやすくする。
- 上記の強化を、プレイヤーが受ける側にだけ適用する。プレイヤーが敵へ与える状態異常を強化しない。
- 調整値をBepInEx設定で変更可能にし、ゲームのセーブデータを書き換えない。
- SPEC001のEDI連動MODと同一ゲームルートで共存し、EDI側のトリガー同定とデバイス制御を壊さない。

### 1.3 成功条件

1. MOD導入後、プレイヤーがゲーム内で選択した難易度を変更していないにもかかわらず、`Hard` 専用データと `HardOnly` 敵配置が有効になる。MODを取り外すと、選択した難易度のまま元の挙動へ戻る。
2. 同一の敵の同一の攻撃を受けたとき、MOD無効時よりも状態異常が付与される頻度が高く、付与時のレベルが同じか高い。
3. プレイヤーが敵へ与える状態異常の付与率とレベル進行が、MOD無効時と変わらない。
4. 快楽系の状態異常が有効な拘束中に、抵抗入力を継続しても拘束ゲージが上昇しない時間帯が断続的に発生する。快楽系の状態異常が1つも有効でない拘束中には発生しない。
5. `穢れ` に由来する脱出困難化の挙動が、MOD導入前後で変化しない。
6. 身重系の状態異常が有効な状態で拘束から離脱した直後、移動が遅くなる時間帯が発生し、その間に再拘束が成立し得る。身重系が有効でない場合は発生しない。
7. MODを取り外し、または全機構を無効化した設定で起動したとき、ゲームの挙動がMOD導入前と一致する。セーブデータにMOD由来の差分が残らない。
8. EDI連動MODが同時に動作している状態で、EDIのトリガーカタログに既存トリガーの欠落が発生せず、デバイス制御が停止しない。
9. MODの介入がゲームのメインスレッドを停止させず、フレーム時間の悪化が計測可能な範囲に収まる。

## 2. スコープ

### 2.1 対象

- BepInEx IL2CPPプラグインとしてのHarmonyランタイムパッチ。
- MOD独自の難易度階層 `Nightmare` の定義と、ゲームへ `Hard` として振る舞わせる読み取り側の差し替え。
- プレイヤーが受ける状態異常の付与率倍率とレベル進行加算。
- 快楽系状態異常に由来する抵抗入力の無力化窓。
- 身重系状態異常に由来する拘束離脱後の復帰遅延と再拘束容易化。
- 上記の適用対象をプレイヤー受け側へ限定する非対称制御。
- BepInEx `ConfigEntry` による調整値と状態異常種別集合の設定。
- SPEC001のEDI連動MODとの共存条件と、それを破らないための制約。
- 全機構の無効化と、介入の完全な巻き戻し。

### 2.2 非対象

- 被ダメージ量、敵HP、プレイヤー最大HP/MP/耐久の変更。1.1の理由により本仕様の軸ではない。
- `穢れ`（`Defilement`）に由来する脱出困難化への介入。`AbnormalList.GachaInputRateDefilement` および `GachaGachaParameter.m_DefilementBind` を読みも書きもしない。
- 状態異常の自然回復時間（`AbnormalData.m_DeleteTime`、`DeleteTimer`、`m_ToLevel0`）の変更。
- 同時付与数上限（`AbnormalList.MaxAddedAbnormalCount`）の変更。
- スリップダメージ間隔（`AbnormalSlipDamage.m_Duration` 系）の変更。
- ゲームの難易度選択UIへの `Nightmare` の追加。`SiNiSistar2.Manager.GameDifficulty` はIL2CPPのenumであり値を追加できない（DEC-101）。
- ゲーム内設定画面、ホットキー、オーバーレイUI。
- `GameAssembly.dll`、`global-metadata.dat`、ゲームアセット、セーブデータファイルの書き換え。
- 敵AI、行動パターン、出現数、リスポーン間隔の変更。
- 難易度階層の複数プリセット。`Nightmare` 1階層と個別倍率の上書きのみとする。
- 新規の状態異常種別の追加。

### 2.3 前提と制約

- MODはBepInEx 6 IL2CPP環境で動作し、`0Harmony.dll` と `Il2CppInterop.HarmonySupport.dll` を実行基盤として利用できる。いずれも `BepInEx/core` に同梱されている。
- 介入はすべて実行時パッチで行う。ゲームバイナリとアセットを改変しない。これはEDI連動MODが `GameAssembly.dll` と `global-metadata.dat` のSHA-256を照合し、不一致ならデバイス出力を無効化するため、共存の必須条件である。
- 対象ビルドのIL2CPPネイティブ実装は生成されたinterop アセンブリから読めない。本仕様が参照するシンボルは実在するが、その**意味論の一部は推論である**。推論に依存する項目は付録Aに列挙し、調整値の確定前に実測で確認する。
- ゲームは状態異常の付与判定と拘束の抵抗判定をメインスレッドで処理する。MODの介入もメインスレッド上で完結する。
- ゲームは `SiNiSistar2.Obj.MultiSettingValue<T>` により、1つの値へ複数の主体が寄与できる機構を持つ。`ResitValue`、`ReleaseValue`、`AllClear` を公開している。MODはこの機構へ自分のキーで寄与を登録し、解除する。
- 状態異常は `SiNiSistar2.Obj.AbnormalList` として `SiNiObject` に属し、プレイヤーと敵の双方が保持する。適用対象の判別が必要である。
- 快楽系と身重系の状態異常の分類は、`AbnormalType` の列挙子名からの推論であり、ゲーム側にその分類は存在しない。既定値は設定として提供し、ユーザーが変更できる。
- ユーザーは難易度上昇の結果として脱出不能な状況が発生し得ることを許容する。ただしそれは本MODが `穢れ` の軸を置き換えた結果であってはならない。

## 3. 用語

| 用語 | 定義 |
|---|---|
| 難易度階層 | 本MODが定義する難度の段。`Off` と `Nightmare` の2値。ゲームの `GameDifficulty` とは別概念。 |
| `Nightmare` | 本MODが追加する難易度階層。ゲームへ `Hard` として振る舞わせたうえで、5.2から5.4の機構を適用する。 |
| 報告値 | ゲーム内の分岐が難易度を判定するために読む値。`PlayerStatusManager.IsHardMode` などの読み取り側。 |
| 保存値 | セーブデータに永続化される難易度。`PlayerStatusManager.GameDifficultyRP` が保持する値。MODは書き換えない。 |
| 受け側 | 状態異常またはダメージを受ける `SiNiObject`。プレイヤー（`Lelia`）か敵かを区別する。 |
| プレイヤー受け | 受け側がプレイヤーである場合。本MODの強化はこの場合にだけ適用する。 |
| 快楽系状態異常 | 抵抗の意思が損なわれる状況を表す状態異常の集合。設定 `PleasureAbnormalTypes` が定める。 |
| 身重系状態異常 | 体が重くなる状況を表す状態異常の集合。設定 `BurdenAbnormalTypes` が定める。 |
| 無力化窓 | 快楽系状態異常が有効な拘束中に、抵抗入力を拘束ゲージへ反映しない時間帯。 |
| 復帰遅延窓 | 身重系状態異常が有効な状態で拘束から離脱した直後に、移動が遅くなる時間帯。 |
| 寄与キー | MODが `MultiSettingValue<T>` へ寄与を登録するときに使う識別子。MODが解除の責任を持つ。 |
| 一時上書き | ゲームのデータへ値を書き、同一の処理単位の終了時に必ず元の値へ戻す介入。 |
| 巻き戻し | MODが行った全ての介入を解除し、ゲームをMOD無効時と同一の状態へ戻す操作。 |

## 4. 採用設計

### 4.1 全体構成

```
BepInEx (IL2CPP)
  └ community.sinisistar2.difficulty        ← 本MOD
      ├ DifficultyPlugin (BasePlugin)        設定読込、Harmonyパッチ適用、巻き戻し
      ├ Harmonyパッチ群                      ゲームへの介入点（Plugin側のみ）
      └ SiNiSistar2.Difficulty.Core          純粋ロジック（ゲーム参照なし・単体テスト対象）
          ├ DifficultyProfile                設定の検証済み表現
          ├ AbnormalClassifier               状態異常種別 → 快楽系/身重系の判定
          ├ NullificationScheduler           無力化窓の開始・終了の決定
          ├ RecoveryPenaltyScheduler         復帰遅延窓の開始・終了の決定
          └ InterventionLedger               介入の登録と巻き戻しの追跡

  └ community.sinisistar2.edi               ← SPEC001 EDI連動MOD（本MODは依存しない）
```

`Core` はUnityにもIL2CPPにも依存しない。窓の開始判定、期間の決定、倍率の合成、設定の検証はすべて `Core` に置き、時刻と乱数を注入して単体テストで検証する。`Plugin` はゲームの値を読み、`Core` へ渡し、返った決定をゲームへ適用するだけの層とする。SPEC001の `Core` / `Plugin` 分離と同じ構成である。

両MODは相互に参照せず、BepInExのプラグイン依存宣言も行わない。片方だけを配置した構成も正当である。

### 4.2 難易度階層の成立方法

`SiNiSistar2.Manager.GameDifficulty` はIL2CPPのenumであり、`Normal` / `Casual` / `Hard` の3値しか持たない。実行時に第4の値を追加することはできない。したがって `Nightmare` はMOD側の階層として保持し、ゲームに対しては `Hard` として振る舞わせる。

書き換えるのは**報告値だけ**とし、**保存値には触れない**。`PlayerStatusManager.GameDifficultyRP` および `set_GameDifficulty` へは書き込まない。これにより、MODを取り外したセーブデータはプレイヤーが選んだ難易度のまま残る。

```
プレイヤーの選択 (Normal)
        │
        ├─ 保存値 GameDifficultyRP = Normal ──────→ セーブデータ（MODは触れない）
        │
        └─ 報告値 IsHardMode / s_GameDifficultyForCheck
                     │
                     └─ MODが Hard として報告 ──→ *HardModeValue, HardOverwrite,
                                                   DifficultySwitcher(HardOnly)
```

報告値と保存値を分離する以上、両者が食い違う。保存処理が報告値と同じ経路を読む場合、`Normal` で始めたセーブへ `Hard` が書き込まれる危険がある。この危険は実測でしか否定できないため、付録Aの検証項目とし、FR-104とAC-104でセーブデータの不変を検証する。

### 4.3 3つの機構と適用対象

| 機構 | 契機 | 介入点の性質 | 適用対象 |
|---|---|---|---|
| 状態異常の強化（5.2） | ダメージ解決時の状態異常付与判定 | 一時上書きと付与後の加算 | プレイヤー受けのみ |
| 抵抗の無力化（5.3） | 拘束中の抵抗入力 | 入力反映の抑止 | プレイヤーのみ |
| 復帰の遅延（5.4） | 拘束からの離脱 | `MultiSettingValue` への寄与登録 | プレイヤーのみ |

3機構は互いに独立して有効・無効を切り替えられる。ある機構の無効化が他の機構の動作を変えてはならない。

### 4.4 介入の可逆性

ゲームのデータへ書き込む介入は、次のいずれかの形しか取らない。

1. **寄与登録** — `MultiSettingValue<T>` へMOD固有の寄与キーで登録し、条件終了時に `ReleaseValue` で解除する。ゲームの既存値を破壊しない。
2. **一時上書き** — 元の値を記録して書き換え、同一の処理単位の終了時に必ず復元する。復元は例外経路でも実行する。
3. **読み取りの差し替え** — ゲームのデータを書き換えず、読み取り結果だけを変える。

いずれの形でも、`InterventionLedger` が未解除の介入を保持する。プラグインの `Unload`、シーン遷移、例外による中断のいずれでも、台帳に残る介入をすべて解除する。台帳が空でない状態でゲームが終了することを許さない。

### 4.5 代替案とトレードオフ

| 論点 | 採用 | 採用しなかった案 | 理由 |
|---|---|---|---|
| `Nightmare` の成立 | 報告値の差し替え | 保存値へ `Hard` を書き込む | セーブデータが汚染され、MOD取り外し後も `Hard` が残る。不可逆。 |
| 同上 | 報告値の差し替え | ゲームが `Hard` のときだけMODを有効化 | `Normal` で始めた既存セーブでMODが無効になる。「Hardの上」という階層が成立しない。 |
| 脱出困難化 | 快楽系による無力化窓 | 拘束ゲージの閾値・減衰値の直接強化 | `穢れ` による既存の脱出困難化と同じ量へ二重に作用し、`穢れ` を管理するプレイ判断が無意味になる。 |
| 同上 | 快楽系による無力化窓 | 入力レート（`GachaInputRate`）の一律低下 | 同上に加え、`GachaInputRateDefilement` との合成規則が不明なまま干渉する。 |
| 復帰遅延 | `MultiSettingValue` への寄与登録 | 移動速度フィールドの直接書き換え | ゲーム側の寄与と競合し、解除の順序によって値が壊れる。`MultiSettingValue` は多重寄与を前提に設計されている。 |
| 適用の非対称性 | 受け側の判別で限定 | 全 `AbnormalList` へ一律適用 | プレイヤーが敵へ与える状態異常も強化され、難易度が下がる。目的と逆行する。 |
| 状態異常の分類 | 設定による集合定義 | 実装に固定した分類表 | 分類は列挙子名からの推論であり、確度が低い。実測で修正できる形にする。 |

## 5. 制御仕様

### 5.1 難易度階層の適用

- MODは設定 `Tier` を読む。`Off` の場合、5.1から5.4のいずれの介入も行わない。
- `Nightmare` の場合、難易度の報告値を `Hard` として報告する。対象は難易度判定に使われる読み取りに限る。
- 保存値への書き込みを行わない。`set_GameDifficulty`、`GameDifficultyRP` への書き込みをMODは一切発行しない。
- 報告値の差し替えは、ゲームプレイのシーンでのみ有効とする。難易度選択UI、セーブ・ロード処理中は素の値を報告する。対象の判別条件は付録Aの実測で確定する。
- 実測により保存値の汚染が確認された場合、MODは報告値の差し替えを行わず、起動時にその旨をエラーとして提示し、5.2から5.4の機構だけを適用する（縮退動作）。

### 5.2 状態異常の付与率とレベル進行

適用条件は、受け側がプレイヤーであることに限る。判別には `DamageStack.IsReceiverLelia`、または `AbnormalList.Target` がプレイヤーであることを用いる。

**付与率**

- ダメージ解決の過程で状態異常の付与判定が行われる直前に、対象の付与率へ `AbnormalRateMultiplier` を乗じる。
- 乗算後の値は元の値の意味論上の上限を超えない。上限は付録Aの実測で確定する。
- 書き換えは一時上書きとし、同一のダメージ解決の終了時に必ず元の値へ復元する。復元は例外経路でも実行する。
- ダメージ解決が再入し得る場合、上書きは最も外側の1回だけとし、内側では二重に乗じない。
- 受け側がプレイヤーでない場合、上書きを行わない。

**レベル進行**

- 状態異常がプレイヤーへ付与された直後、`LevelBonus` で指定した段数だけレベルを追加で進行させる。
- 追加進行は各状態異常の `MaxLevel` を超えない。上限に達している場合は何もしない。
- 追加進行はゲームのレベル変更通知（`AbnormalData.OnChangeLevel`）を通る経路で行い、レベル変更に伴う演出・UI・派生効果を欠落させない。
- 1回の付与に対する追加進行は1回に限る。追加進行が新たな付与判定を誘発しても、それに対して再度の追加進行を行わない。

### 5.3 快楽系状態異常による抵抗の無力化

適用条件は次のすべてを満たすことである。

1. 拘束が成立している（プレイヤーの拘束ゲージが動作中）。
2. プレイヤーに `PleasureAbnormalTypes` のいずれかの状態異常が有効である。

**窓の生成**

- 拘束の開始時に、無力化窓のスケジュールを初期化する。
- 窓は `NullificationIntervalSeconds` を基準とする間隔で発生する。実際の間隔は基準値へ `NullificationIntervalJitter` の比率で乱数を乗じた値とする。
- 1回の窓の長さは `NullificationDurationSeconds` を基準とし、`NullificationDurationJitter` の比率で乱数を乗じた値とする。
- 有効な快楽系状態異常のレベル合計に応じて、間隔を短く、長さを長くしてよい。係数は `PleasureLevelScaling` が定める。既定は影響なしとする。
- 拘束が終了した時点でスケジュールを破棄する。次の拘束では新しいスケジュールを生成する。

**窓の効果**

- 窓の内側では、抵抗入力を拘束ゲージへ反映しない。入力そのものは受け付け、反映だけを行わない。
- 窓は拘束ゲージの減衰（`DeclineValue` に由来する挙動）を変更しない。したがって窓の内側では、入力を継続してもゲージが減少し得る。
- 窓の内側と外側で、拘束ゲージの表示（`GachaGachaSystem` のホールドUI）を隠さない。窓の内側では入力してもゲージが上昇しないことが、既存の表示から観測できる状態を保つ。
- 窓の内側では拘束ゲージの塗り色を設定色へ変更する。変更するのは色だけであり、ゲージの値、減衰、表示位置、UIの構造を変更しない。窓の終了時、拘束の終了時、シーン遷移時、巻き戻し時に元の色へ戻す。塗り色を持つ要素を解決できない場合は着色を行わず、窓自体は成立させる。
- 窓は拘束の強制成功要求（`IsForceSuccessRequest`）を無効化しない。イベントによる強制解放を妨げない。

**穢れ軸との非干渉**

- MODは `AbnormalList.GachaInputRateDefilement` と `GachaGachaParameter.m_DefilementBind` を読み書きしない。
- MODは `AbnormalList.GachaInputRate`、`GachaGachaParameter.m_SuccessValue`、`m_DeclineValue`、およびそれらの `Hard` 版を書き換えない。
- `PleasureAbnormalTypes` に `Defilement` が含まれている場合、起動時に設定エラーとして提示し、当該要素を無視する。

**設定による連続無力化の警告**

- 設定値の組み合わせにより、窓の占有率が1に接近し、事実上恒久的な無力化が成立し得る。MODはこれを禁止しないが、起動時に占有率の期待値を算出し、`NullificationDutyWarnThreshold` を超える場合は警告として提示する。

### 5.4 身重系状態異常による復帰の遅延

適用条件は次のすべてを満たすことである。

1. プレイヤーが拘束から離脱した。
2. 離脱の時点でプレイヤーに `BurdenAbnormalTypes` のいずれかの状態異常が有効である。

**窓の効果**

- 離脱の直後から `RecoveryPenaltySeconds` の間、移動速度低下をMOD固有の寄与キーで `PlayerStatusManager.MoveSlowRateMsv` へ登録する。低下量は `RecoveryMoveSlowRate` が定める。
- 同じ窓の間、離脱後の無敵時間を短縮する。短縮は `RecoveryInvincibleScale` が定める。既定は短縮なしとする。
- 窓の長さは、有効な身重系状態異常のレベル合計に応じて延長してよい。係数は `BurdenLevelScaling` が定める。既定は影響なしとする。
- 窓の終了時、MODが登録した寄与を必ず解除する。窓の途中で再び拘束が成立した場合も、その時点で寄与を解除する。
- 窓が有効な間に新たな離脱が発生した場合、窓を延長せず、新しい窓で置き換える。寄与は二重に登録しない。

**再拘束への影響**

- MODは拘束の成立条件（`Bind.RequestCheck`、`BindCheck`、`StatusCondition.IsHoldable`、`DisableHoldMsv`）を書き換えない。再拘束されやすくなるのは、移動が遅く無敵が短いことの結果としてのみ成立する。

### 5.5 適用対象の非対称性

- 5.2から5.4のいずれの機構も、受け側または対象がプレイヤーである場合にだけ作用する。
- プレイヤーが敵へ与える状態異常の付与率とレベル進行を変更しない。
- 敵の `AbnormalList` と敵に属する `MultiSettingValue` へ寄与を登録しない。
- 対象がプレイヤーであるかを判別できない場合、介入しない。判別不能を強化の根拠にしない。

### 5.6 巻き戻し

- プラグインの `Unload`、シーン遷移、および介入処理中の例外のいずれでも、`InterventionLedger` に残る全ての介入を解除する。
- 解除は、寄与登録については `ReleaseValue`、一時上書きについては記録した元の値の復元とする。
- 解除に失敗した介入は、失敗した対象と理由を記録する。黙って諦めない。
- 全機構を無効化した設定で起動した場合、Harmonyパッチを適用しない。

## 6. 設定

BepInEx の `ConfigEntry` として `BepInEx/config/community.sinisistar2.difficulty.cfg` に生成する。既定値はプラグイン内に持ち、設定ファイルは初回起動時に生成される。`.gitignore` が `BepInEx/config/*.cfg` を除外するため、設定ファイルはリポジトリで追跡しない。

| セクション | キー | 型 | 既定値 | 意味 |
|---|---|---|---|---|
| `General` | `Tier` | enum | `Nightmare` | 難易度階層。`Off` で全機構を無効化する。 |
| `General` | `ForceHardData` | bool | `true` | 難易度の報告値を `Hard` として差し替えるか。 |
| `Abnormal` | `Enabled` | bool | `true` | 5.2の機構の有効・無効。 |
| `Abnormal` | `AbnormalRateMultiplier` | float | 実測後に確定 | プレイヤー受けの状態異常付与率へ乗じる倍率。1.0で無変更。 |
| `Abnormal` | `LevelBonus` | int | 実測後に確定 | 付与直後に追加で進行させる段数。0で無変更。 |
| `Pleasure` | `Enabled` | bool | `true` | 5.3の機構の有効・無効。 |
| `Pleasure` | `PleasureAbnormalTypes` | string | 6.1参照 | 快楽系とみなす `AbnormalType` 名のカンマ区切り。 |
| `Pleasure` | `NullificationIntervalSeconds` | float | 実測後に確定 | 無力化窓の発生間隔の基準値。 |
| `Pleasure` | `NullificationIntervalJitter` | float | `0.5` | 間隔へ乗じる乱数の振れ幅の比率。0で固定間隔。 |
| `Pleasure` | `NullificationDurationSeconds` | float | 実測後に確定 | 1回の無力化窓の長さの基準値。 |
| `Pleasure` | `NullificationDurationJitter` | float | `0.3` | 長さへ乗じる乱数の振れ幅の比率。 |
| `Pleasure` | `PleasureLevelScaling` | float | `0.0` | 快楽系レベル合計による間隔短縮・長さ延長の係数。0で影響なし。 |
| `Pleasure` | `NullificationDutyWarnThreshold` | float | `0.6` | 起動時に警告する窓の占有率の閾値。 |
| `Pleasure` | `HighlightGauge` | bool | `true` | 無力化窓の間、拘束ゲージを着色するか。 |
| `Pleasure` | `NullificationGaugeColor` | string | `FF3E9D` | 着色に使う色。`RRGGBB` または `RRGGBBAA`。 |
| `Burden` | `Enabled` | bool | `true` | 5.4の機構の有効・無効。 |
| `Burden` | `BurdenAbnormalTypes` | string | 6.1参照 | 身重系とみなす `AbnormalType` 名のカンマ区切り。 |
| `Burden` | `RecoveryPenaltySeconds` | float | 実測後に確定 | 復帰遅延窓の長さの基準値。 |
| `Burden` | `RecoveryMoveSlowRate` | float | 実測後に確定 | 窓の間に適用する移動速度低下量。 |
| `Burden` | `RecoveryInvincibleScale` | float | `1.0` | 離脱後無敵時間へ乗じる係数。1.0で短縮なし。 |
| `Burden` | `BurdenLevelScaling` | float | `0.0` | 身重系レベル合計による窓の延長係数。0で影響なし。 |
| `Diagnostics` | `LogInterventions` | bool | `false` | 介入の登録と解除をログへ記録するか。 |

「実測後に確定」と記した既定値は、付録Aの実測を経て確定する。実測前は全て無変更相当の値（倍率1.0、加算0、窓の長さ0）とし、MODを導入しただけでは挙動が変わらないようにする。

### 6.1 状態異常種別の既定集合

分類は `AbnormalType` の列挙子名からの推論である。ゲーム側にこの分類は存在しない。設定で変更できる。

**快楽系（既定）** — `Lustfull`、`Lustfull_Forever`、`LustMarkCurse`、`MindControl`、`MindIntegration`、`Breast`、`BreastSuper`、`Milk`、`WetNurse`、`Drunk`、`FallSleep`、`Semen`、`Semen_mucus`

**身重系（既定）** — `Pregnant`、`Pregnant_Demi`、`MotherBody`、`FrogEgg`、`FrogLEgg`、`TentacleEgg`、`TentacleEgg_GO`、`SpiderEggSac`、`LeechEgg`、`LeechEgg_Boss`、`LeechInfestation`、`MeatBud`、`MeatBuding`、`Parasite`、`ParasiteLv13`、`LivestockParasite`、`Assimilation_Seed`、`EvilWoodSeed`

`Defilement` はいずれの集合にも含めない。5.3の非干渉規定による。

### 6.2 設定の検証

- 未知の `AbnormalType` 名が指定された場合、当該要素を無視し、無視した名前を提示する。集合全体を無効化しない。
- 倍率と係数が負の場合、設定エラーとして提示し、当該機構を無効化する。
- 両集合に同じ種別が含まれる場合は正当とする。快楽系かつ身重系である状態異常は成立し得る。
- `Tier` が `Off` の場合、他の設定値を検証せずに全機構を無効化する。

## 7. EDI連動MODとの互換性

SPEC001のEDI連動MODと同一ゲームルートで動作する。本MODは次を守る。

### 7.1 破ってはならない前提

| 対象 | 制約 | 理由 |
|---|---|---|
| `GameAssembly.dll`、`global-metadata.dat` | 改変しない | EDI連動MODがSHA-256を照合し、不一致ならデバイス出力を無効化する（SPEC001 EdiPlugin）。 |
| `Time.timeScale` | 変更しない | EDI連動MODがポーズ検出に使用する。 |
| `Animator` の状態、再生クリップ、クリップ名 | 変更しない | EDI連動MODがトリガーの `animationId` と `stageId` をクリップ名と take 名から導出する。 |
| `EnemyData.GalleryEnemyID`、take 名、take 配列 | 変更しない | EDI連動MODが `actorId` と `stageId` の同定に使用する。 |
| `Lelia.IsHold`、`IsHP0`、`Bind.BinderEnemy` | 意味を変えない | EDI連動MODが `hold` と `game-over` のトリガー判定に使用する。 |
| `AbnormalList.Has(AbnormalType)` の結果 | 偽の状態異常を追加しない | EDI連動MODが待機中のfiller選択に使用する。 |
| EDIのREST API、`Edi/Gallery`、`mappings.json` | 触れない | EDI連動MODの正本である。 |

### 7.2 許容される観測可能な差分

本MODは難易度を上げるため、EDI連動MODが観測する内容にも差分が生じる。次は互換性の破壊ではなく、意図した結果である。

- **トリガーカタログへの追加** — `Hard` として報告することで `DifficultySwitcher` が `HardOnly` の敵を配置し、EDI側のカタログに新しい `actorId` の行が追加され得る。既存行を失うものではない。追加であるため、EDI側の網羅性レポートに未分類として現れる。
- **`hold` トリガーの継続時間の延長** — 拘束から抜けにくくなるため、EDI側の `hold` トリガーが有効な時間が延びる。トリガーの同一性は変わらない。
- **状態異常fillerの選択頻度の上昇** — 状態異常が付きやすくなるため、EDI側が `filler-breast-swollen` などを選ぶ頻度が上がる。選択規則そのものは変わらない。

### 7.3 起動順序と独立性

- 両MODは相互に依存しない。BepInExの依存宣言を行わず、ロード順序を仮定しない。
- 一方のMODの起動失敗が他方の動作を妨げてはならない。
- 本MODのHarmonyパッチは `community.sinisistar2.difficulty` のHarmony IDで適用し、`Unload` で当該IDのパッチだけを解除する。

## 8. 機能要件

| ID | 要件 | 優先度 | 根拠 |
|---|---|---|---|
| FR-101 | MODはBepInEx IL2CPPプラグインとしてロードされ、GUID `community.sinisistar2.difficulty` を持たなければならない。 | Must | EDI連動MODとの識別 |
| FR-102 | MODは `GameAssembly.dll`、`global-metadata.dat`、ゲームアセット、セーブデータファイルを書き換えてはならない。介入は実行時パッチに限らなければならない。 | Must | EDI連動MODとの共存（7.1） |
| FR-103 | `Tier` が `Nightmare` かつ `ForceHardData` が有効なとき、MODは難易度の報告値を `Hard` として報告しなければならない。 | Must | Hard専用データの有効化（1.2） |
| FR-104 | MODは難易度の保存値へ書き込んではならない。MODを取り外したとき、セーブデータの難易度はプレイヤーが選択した値のままでなければならない。 | Must | 不可逆な汚染の防止（DEC-101） |
| FR-105 | 報告値の差し替えが保存値へ波及することが実測で確認された場合、MODは差し替えを行わず、理由を提示して5.2から5.4の機構だけを適用しなければならない。 | Must | 縮退動作（5.1） |
| FR-106 | MODは状態異常の付与率とレベル進行の強化を、受け側がプレイヤーである場合にだけ適用しなければならない。 | Must | 非対称性（5.5）。一律適用は難易度を下げる |
| FR-107 | 付与率の一時上書きは、同一のダメージ解決の終了時に必ず元の値へ復元しなければならない。例外が発生した経路でも復元しなければならない。 | Must | 共有データの汚染防止（4.4） |
| FR-108 | ダメージ解決が再入する場合、付与率の上書きは最も外側の1回だけとし、多重に乗じてはならない。 | Must | 倍率の暴走防止 |
| FR-109 | レベルの追加進行は各状態異常の `MaxLevel` を超えてはならず、ゲームのレベル変更通知を通る経路で行わなければならない。 | Must | 演出・UI・派生効果の欠落防止 |
| FR-110 | 1回の付与に対するレベルの追加進行は1回に限らなければならない。 | Must | 再帰的進行の防止 |
| FR-111 | MODは、快楽系状態異常が有効な拘束中に限り、抵抗入力を拘束ゲージへ反映しない無力化窓を発生させなければならない。快楽系が1つも有効でない拘束中には発生させてはならない。 | Must | 状況依存の脱出困難化（1.2） |
| FR-112 | MODは `AbnormalList.GachaInputRateDefilement` および `GachaGachaParameter.m_DefilementBind` を読み書きしてはならない。 | Must | 穢れ軸との非干渉（1.1、5.3） |
| FR-113 | MODは `AbnormalList.GachaInputRate`、`GachaGachaParameter.m_SuccessValue`、`m_DeclineValue`、およびそれらの `Hard` 版を書き換えてはならない。 | Must | 同上。既存の脱出困難化の量へ二重に作用させない |
| FR-114 | `PleasureAbnormalTypes` に `Defilement` が含まれる場合、MODは設定エラーとして提示し、当該要素を無視しなければならない。 | Must | FR-112の設定経路での回避防止 |
| FR-115 | 無力化窓は拘束ゲージの減衰を変更してはならず、拘束ゲージの表示を隠してはならない。 | Must | 入力が反映されていないことの観測可能性 |
| FR-116 | 無力化窓は拘束の強制成功要求を無効化してはならない。 | Must | イベント進行の保護 |
| FR-135 | MODは無力化窓の間、拘束ゲージの塗り色を設定色へ変更しなければならない。色以外（値、減衰、位置、UI構造）を変更してはならず、窓の終了・拘束の終了・シーン遷移・巻き戻しのいずれでも元の色へ戻さなければならない。塗り色を持つ要素を解決できない場合は着色を行わず、窓自体は成立させなければならない。 | Must | 「意図した停止」と「ゲームが固まった」の区別（DEC-103） |
| FR-117 | MODは起動時に無力化窓の占有率の期待値を算出し、`NullificationDutyWarnThreshold` を超える場合は警告として提示しなければならない。 | Must | 設定ミスによる恒久無力化の可視化（5.3） |
| FR-118 | MODは、身重系状態異常が有効な状態で拘束から離脱した直後に限り、復帰遅延窓を発生させなければならない。 | Must | 状況依存の再拘束容易化（1.2） |
| FR-119 | 復帰遅延窓の移動速度低下は `MultiSettingValue<T>` へMOD固有の寄与キーで登録し、窓の終了時、再拘束時、および巻き戻し時に必ず解除しなければならない。 | Must | ゲーム側の寄与との非破壊共存（4.4） |
| FR-120 | 復帰遅延窓が有効な間に新たな離脱が発生した場合、MODは寄与を二重に登録してはならず、新しい窓で置き換えなければならない。 | Must | 寄与の残留防止 |
| FR-121 | MODは拘束の成立条件を書き換えてはならない。再拘束の容易化は移動速度と無敵時間の結果としてのみ成立させなければならない。 | Must | 拘束判定の意味の保存（7.1） |
| FR-122 | MODは敵の `AbnormalList` および敵に属する `MultiSettingValue` へ介入してはならない。 | Must | 非対称性（5.5） |
| FR-123 | 対象がプレイヤーであるかを判別できない場合、MODは介入してはならない。 | Must | 判別不能を強化の根拠にしない |
| FR-124 | MODは未解除の介入を台帳で追跡し、`Unload`、シーン遷移、例外による中断のいずれでもすべて解除しなければならない。解除に失敗した介入は対象と理由を記録しなければならない。 | Must | 巻き戻しの完全性（5.6） |
| FR-125 | `Tier` が `Off` の場合、MODはHarmonyパッチを適用してはならない。 | Must | 完全な無効化の保証 |
| FR-126 | MODは、未知の `AbnormalType` 名を無視し、無視した名前を提示しなければならない。集合全体を無効化してはならない。 | Must | 設定の部分的な誤りへの耐性 |
| FR-127 | MODは倍率と係数が負である設定を検出した場合、設定エラーとして提示し、当該機構を無効化しなければならない。 | Must | 意図しない反転の防止 |
| FR-128 | 実測で確定していない調整値の既定値は、挙動を変更しない値でなければならない。 | Must | 未検証の値でゲームを壊さない（6章） |
| FR-129 | MODは `Time.timeScale`、`Animator` の状態と再生クリップ、`GalleryEnemyID`、take 名、`Lelia.IsHold` / `IsHP0` / `Bind.BinderEnemy` の意味を変更してはならない。 | Must | EDI連動MODのトリガー同定の保護（7.1） |
| FR-130 | MODは `AbnormalList.Has` が偽の状態異常を返すような追加を行ってはならない。 | Must | EDI連動MODのfiller選択の保護（7.1） |
| FR-131 | MODは他のMODへのBepInEx依存を宣言してはならず、ロード順序を仮定してはならない。 | Must | 片方だけを配置した構成の許容（7.3） |
| FR-132 | MODの介入はメインスレッド上で完結しなければならず、フレーム処理を待機によって停止させてはならない。 | Must | ゲーム性能（9.1） |
| FR-133 | MODは起動時に、階層、有効な機構、確定済み調整値、無視した設定要素、および適用したHarmonyパッチ数をログへ記録しなければならない。 | Must | 構成の診断 |
| FR-134 | 窓の開始・終了の決定、期間の算出、倍率の合成、設定の検証は、ゲームへ依存しない層に置き、単体テストで検証可能でなければならない。 | Must | 実機でしか試せない範囲の縮小（4.1） |

## 9. エラーと復旧

| 条件 | 動作 | 復旧 |
|---|---|---|
| ゲームビルドが対象と一致しない | 全機構を無効化し、期待するSHA-256と実際の値を提示 | 対象ビルドを配置するか、対応版のMODへ更新 |
| Harmonyパッチの対象メソッドが見つからない | 当該機構だけを無効化し、見つからなかったシグネチャを提示。他の機構は継続 | ゲーム更新に追随したパッチ対象の更新 |
| Harmonyパッチの適用が例外で失敗 | 当該機構だけを無効化し、既に適用したパッチを解除 | 原因解消後に再起動 |
| 報告値の差し替えが保存値へ波及 | 差し替えを行わず、理由を提示して他の機構だけを適用（FR-105） | 実測結果に基づくパッチ対象の絞り込み |
| 付与率の一時上書きの復元に失敗 | 失敗した対象と元の値を記録し、以後その機構を無効化 | 再起動により素の値へ戻る |
| 寄与の解除に失敗 | 失敗した対象と寄与キーを記録し、`AllClear` を試みない（ゲーム側の寄与を巻き添えにしない） | 再起動 |
| 未知の `AbnormalType` 名 | 当該要素を無視し、無視した名前を提示 | 設定の修正 |
| 倍率・係数が負 | 当該機構を無効化し、キー名と値を提示 | 設定の修正 |
| `PleasureAbnormalTypes` に `Defilement` | 当該要素を無視し、FR-112の理由を提示 | 設定の修正 |
| 無力化窓の占有率が閾値超過 | 警告を提示して継続。禁止しない | 設定の見直し、または警告の許容 |
| 介入処理中の例外 | 当該フレームの介入を中止し、台帳の介入を解除してゲームを継続 | 継続。反復する場合は当該機構を無効化 |
| シーン遷移 | 台帳の介入をすべて解除し、窓のスケジュールを破棄 | 次のシーンで新しいスケジュールを生成 |
| プラグインの `Unload` | 台帳の介入をすべて解除し、Harmony IDのパッチを解除 | 該当なし |

## 10. 非機能要件

### 10.1 性能

- MODの介入はメインスレッド上で完結し、待機によってフレーム処理を停止させない。
- 毎フレームの全 `SiNiObject` 走査、全 `AbnormalList` 走査を行わない。窓の判定は拘束中および復帰遅延窓の間だけ行う。
- Harmonyパッチは介入点に限定して適用する。ダメージ解決の内側に置くパッチは、受け側がプレイヤーでない場合に最短で復帰する。
- 介入のログ出力は既定で無効とし、有効時も同一状態の繰り返しでログを生成しない。

### 10.2 安全性

- ゲームのデータへの書き込みは、寄与登録・一時上書き・読み取りの差し替えのいずれかに限る（4.4）。
- 一時上書きの復元は例外経路でも実行する。
- `MultiSettingValue.AllClear` を呼ばない。ゲーム側の寄与を巻き添えにする。
- 判別不能な対象へ介入しない（FR-123）。

### 10.3 互換性

- EDI連動MODとの共存条件は7章に従う。
- ゲーム更新でパッチ対象が変わった場合、当該機構だけを無効化し、他の機構とゲーム本体を継続させる。
- 設定ファイルの追加キーは、既定値が無変更相当であれば既存構成の挙動を変えない。

### 10.4 可観測性

- 起動時に、階層、有効な機構、確定済み調整値、無視した設定要素、適用したパッチ数を記録する（FR-133）。
- パッチ対象の欠落、設定エラー、復元失敗、解除失敗を、対象を特定できる形で記録する。
- 窓の発生と終了は `LogInterventions` が有効なときに記録する。

## 11. 受け入れ条件

| ID | 対応要件 | シナリオと期待結果 |
|---|---|---|
| AC-101 | FR-101, FR-102 | Given MODを配置し `Tier=Nightmare` で起動する / When ゲームを起動して終了する / Then `GameAssembly.dll` と `global-metadata.dat` のSHA-256が起動前と一致し、EDI連動MODがビルド不一致でデバイス出力を無効化しない |
| AC-102 | FR-103 | Given `Normal` で開始したセーブを読み込む / When `ForceHardData` を有効にして起動する / Then `HardOverwrite` を持つ受けコライダーと `DifficultySwitcher` が `Hard` 側の分岐を選ぶ |
| AC-103 | FR-103, FR-125 | Given 同じセーブ / When `Tier=Off` で起動する / Then MOD無効時と同一の分岐が選ばれ、Harmonyパッチが1件も適用されない |
| AC-104 | FR-104 | Given `Normal` で開始したセーブ / When `Nightmare` でプレイし、セーブし、MODを取り外して起動する / Then 難易度が `Normal` のままであり、セーブデータにMOD由来の差分がない |
| AC-105 | FR-105 | Given 報告値の差し替えが保存値へ波及する実測結果 / When MODを起動する / Then 差し替えが行われず、理由がエラーとして提示され、5.2から5.4の機構は動作する |
| AC-106 | FR-106, FR-122 | Given 同一の敵の同一の攻撃 / When プレイヤーが受ける場合と、プレイヤーが同種の状態異常を敵へ与える場合を比較する / Then プレイヤー受けでのみ付与率とレベル進行が強化され、敵受けはMOD無効時と一致する |
| AC-107 | FR-107 | Given 付与率の一時上書き中に例外が発生する / When ダメージ解決が中断する / Then 上書きした付与率が元の値へ復元されており、次のダメージ解決で倍率が二重にかからない |
| AC-108 | FR-108 | Given ダメージ解決が再入する構成 / When 内側の解決が起きる / Then 付与率へ乗じた倍率が1回分だけである |
| AC-109 | FR-109 | Given 最大レベルの状態異常 / When 追加進行の条件が成立する / Then レベルが `MaxLevel` を超えず、レベル変更に伴う演出とUIが欠落しない |
| AC-110 | FR-110 | Given 追加進行が新たな付与判定を誘発する / When 追加進行が実行される / Then 1回の付与に対する追加進行が1回だけである |
| AC-111 | FR-111 | Given 快楽系状態異常が有効な拘束 / When 抵抗入力を継続する / Then 拘束ゲージが上昇しない時間帯が断続的に発生する。Given 快楽系が1つも有効でない拘束 / Then 発生しない |
| AC-112 | FR-112, FR-113 | Given MODが動作している / When 拘束が成立する / Then `GachaInputRateDefilement`、`m_DefilementBind`、`GachaInputRate`、`m_SuccessValue`、`m_DeclineValue` の値が、MOD無効時の同一状況と一致する |
| AC-113 | FR-112 | Given `穢れ` を蓄積させたセーブ / When MOD導入前後で同一の拘束を比較する / Then 穢れに由来する脱出困難化の挙動が一致する |
| AC-114 | FR-114 | Given `PleasureAbnormalTypes` に `Defilement` を含む設定 / When 起動する / Then 設定エラーが提示され、`Defilement` が集合から除かれ、他の要素は有効である |
| AC-115 | FR-115 | Given 無力化窓の内側 / When 抵抗入力を継続する / Then 拘束ゲージの表示が隠れず、ゲージが上昇しないことが画面から観測でき、減衰の速度がMOD無効時と一致する |
| AC-116 | FR-116 | Given 無力化窓の内側 / When イベントが拘束の強制成功を要求する / Then 拘束が解放される |
| AC-117 | FR-117 | Given 占有率の期待値が閾値を超える設定 / When 起動する / Then 警告が提示され、MODは動作を続ける |
| AC-132 | FR-135 | Given 無力化窓が開く / When 拘束ゲージを見る / Then 塗り色が設定色へ変わり、窓の終了時に元の色へ戻る。ゲージの値と減衰は着色の有無で変わらない |
| AC-133 | FR-135 | Given 着色中にシーンが遷移する、または MOD をアンロードする / When ゲージを再度表示する / Then 元の色で表示され、解除失敗のログが出ない |
| AC-134 | FR-135 | Given `NullificationGaugeColor` に不正な文字列を設定する / When 起動する / Then 設定エラーが提示され、着色は行われないが無力化窓は動作する |
| AC-118 | FR-118 | Given 身重系状態異常が有効な拘束 / When 離脱する / Then 移動が遅くなる時間帯が発生する。Given 身重系が有効でない拘束 / Then 発生しない |
| AC-119 | FR-119 | Given 復帰遅延窓が有効 / When 窓が終了する、再拘束される、またはシーンが遷移する / Then MODが登録した寄与が解除され、移動速度がMOD無効時と一致する |
| AC-120 | FR-120 | Given 復帰遅延窓が有効 / When 新たな離脱が発生する / Then 寄与が1件だけ登録されており、窓が新しい期間で置き換わる |
| AC-121 | FR-121 | Given 復帰遅延窓が有効 / When 拘束の成立条件を読み出す / Then `IsHoldable` と `DisableHoldMsv` がMOD無効時と一致する |
| AC-122 | FR-123 | Given 受け側がプレイヤーか判別できない状況 / When 状態異常の付与判定が起きる / Then MODが介入せず、素の挙動になる |
| AC-123 | FR-124 | Given 介入が登録された状態 / When プラグインを `Unload` する / Then 台帳が空になり、解除に失敗した介入があればその対象と理由が記録されている |
| AC-124 | FR-126 | Given 存在しない `AbnormalType` 名を含む設定 / When 起動する / Then 当該名が提示され、他の要素からなる集合が有効である |
| AC-125 | FR-127 | Given 負の倍率を指定した設定 / When 起動する / Then 設定エラーが提示され、当該機構だけが無効化され、他の機構は動作する |
| AC-126 | FR-128 | Given 実測前の既定値のまま / When 起動してプレイする / Then 付与率、レベル進行、窓の長さがMOD無効時と一致する |
| AC-127 | FR-129, FR-130 | Given EDI連動MODと同時に動作させる / When ギャラリー、拘束、ゲームオーバーを一巡する / Then EDIのトリガーカタログから既存の行が失われず、`filler` 選択規則が変わらず、デバイス制御が停止しない |
| AC-128 | FR-131 | Given 本MODだけを配置した構成、およびEDI連動MODだけを配置した構成 / When それぞれ起動する / Then どちらも起動に成功し、他方の不在を理由に失敗しない |
| AC-129 | FR-132 | Given 全機構が有効 / When 拘束と状態異常が同時に多数成立する場面を再生する / Then フレーム時間がMOD無効時に対して計測可能な範囲に収まり、待機によるフレーム停止が発生しない |
| AC-130 | FR-133 | Given 任意の設定 / When 起動する / Then 階層、有効な機構、確定済み調整値、無視した設定要素、適用したパッチ数がログに現れる |
| AC-131 | FR-134 | Given `Core` の単体テスト / When 窓の開始・終了、期間算出、倍率合成、設定検証を検証する / Then ゲームを起動せずに合否が判定できる |

## 12. リポジトリ成果物と運用

### 12.1 成果物

| 成果物 | 位置 |
|---|---|
| プラグイン実装 | `src/SiNiSistar2.Difficulty.Plugin` |
| ゲーム非依存ロジック | `src/SiNiSistar2.Difficulty.Core` |
| 単体テスト | `tests/SiNiSistar2.Difficulty.Core.Tests` |
| 実行プラグイン | `BepInEx/plugins/community.sinisistar2.difficulty` |
| 設定ファイル（追跡しない） | `BepInEx/config/community.sinisistar2.difficulty.cfg` |
| 本仕様 | `docs/specifications/SPEC002.md` |
| 実装トレーサビリティ | `docs/implementation/SPEC002-traceability.md` |
| 実機テストシナリオ | `docs/testing/SPEC002-test-scenarios.md` |

3プロジェクトは `SiNiSistar2.Edi.sln` へ追加する。EDI側の3プロジェクトと同一ソリューションに置き、`dotnet build` の一回で両MODが実行位置へ配置される。EDI側のプロジェクトへ参照を追加しない。

### 12.2 導入順序

1. `dotnet test` と `dotnet build` を実行し、プラグインDLLを実行位置へ配置する。
2. ゲームを一度起動し、設定ファイルを生成させる。この時点の既定値は挙動を変えない（FR-128）。
3. 付録Aの実測を行い、調整値を確定する。
4. 設定ファイルへ確定した調整値を記入し、起動ログで有効な機構と値を確認する。

### 12.3 ロールバック

`BepInEx/plugins/community.sinisistar2.difficulty` を削除する。または `Tier=Off` を設定する。いずれの場合もセーブデータとゲームアセットへの変更は残らない（FR-102、FR-104）。EDI連動MODの動作は影響を受けない。

## 13. 設計判断

| ID | 判断 | 理由 | 採用しなかった案 |
|---|---|---|---|
| DEC-101 | `Nightmare` をMOD側の階層として保持し、ゲームへは `Hard` として報告する。保存値は書き換えない。 | `SiNiSistar2.Manager.GameDifficulty` はIL2CPPのenumであり、実行時に第4の値を追加できない。保存値へ `Hard` を書くと、MODを取り外した後もセーブデータが `Hard` のまま残り、不可逆になる。読み取り側だけの差し替えなら、MODの有無が難易度の永続状態を変えない。 | 保存値へ `Hard` を書き込む、ゲームが `Hard` のときだけMODを有効化する |
| DEC-102 | 脱出困難化を、拘束ゲージの閾値・減衰・入力レートではなく、時間帯としての無力化窓で実現する。 | `穢れ` による脱出困難化が `GachaInputRateDefilement` として既に存在する。同じ量へ重ねて作用させると、蓄積曲線が二重にかかり、`穢れ` を管理するというプレイ判断が意味を失う。窓は直交する軸であり、既存の量に一切触れずに難度を上げられる。 | `SuccessValue` の引き上げ、`DeclineValue` の引き上げ、`GachaInputRate` の一律低下 |
| DEC-103 | 無力化窓の内側でも拘束ゲージの表示を維持し、減衰も変更しない。 | 入力が反映されていないことを、プレイヤーがゲージの停止として観測できる必要がある。表示を隠すと、MODの介入とゲームの不具合を区別できない。減衰まで止めると窓が「一時停止」になり、難度が上がらない。 | 窓の間ホールドUIを隠す、窓の間ゲージ全体を停止させる |
| DEC-104 | 復帰遅延を `MultiSettingValue<T>` への寄与登録で実現する。 | `MoveSlowRateMsv` などは複数主体の寄与を前提に設計されており、`ResitValue` / `ReleaseValue` を公開している。フィールドを直接書き換えると、ゲーム側の寄与と競合し、解除の順序によって値が壊れる。寄与登録なら解除の責任範囲がMODの寄与キーに限定される。 | 移動速度フィールドの直接書き換え、`AllClear` による一括解除 |
| DEC-105 | 再拘束の容易化を、拘束の成立条件ではなく移動速度と無敵時間の結果として成立させる。 | `IsHoldable` と `DisableHoldMsv` はEDI連動MODが `hold` トリガーの判定に使う `Lelia.IsHold` の上流にある。ここを書き換えると、拘束が成立していないのに成立したと観測される経路が生まれ得る。結果として成立させれば、拘束判定の意味は変わらない。 | `IsHoldable` の強制、`DisableHoldMsv` の抑制、拘束判定距離の拡大 |
| DEC-106 | 強化の適用をプレイヤー受け側へ限定する。 | `AbnormalList` はプレイヤーと敵の双方が持つ。付与率を一律に強化すると、プレイヤーが敵へ与える状態異常も強化され、難易度が下がる。目的と逆行する。 | 全 `AbnormalList` への一律適用、敵側にも別倍率を用意する |
| DEC-107 | 快楽系・身重系の分類を実装に固定せず、設定で定義する。 | 分類は `AbnormalType` の列挙子名からの推論であり、ゲーム側にこの分類は存在しない。実装に固定すると、推論が外れていたときに再ビルドが必要になる。設定にすれば実測とプレイ感で修正できる。 | 実装内の固定分類表、`AbnormalOne` の既存フィールドからの自動分類 |
| DEC-108 | 実測で確定していない調整値の既定値を、挙動を変更しない値とする。 | 意味論の一部が推論であるため（付録A）、根拠のない数値を既定値にすると、導入しただけで未検証の変更がゲームへ入る。無変更を既定にすれば、導入と調整を分離できる。 | 推定値を既定にする、実測完了まで仕様を未完成とする |
| DEC-109 | 状態異常の軸を付与率とレベル進行に限り、自然回復時間・同時付与数上限・スリップダメージ間隔を非対象とする。 | 付与率とレベル進行は「早く付き、早く重くなる」という一つの因果に収まる。自然回復時間の延長は治療手段の設計に、同時付与数上限の緩和はUI表示の限界に、それぞれ別種の検証を要求する。最小の軸で目的を達成できる。 | 4軸すべての同時導入、自然回復の無効化 |
| DEC-110 | 介入を寄与登録・一時上書き・読み取りの差し替えの3形態に限り、台帳で追跡する。 | 実行時パッチは、解除されない介入が残ると次のシーンや次の起動で原因不明の挙動になる。形態を限定すれば解除方法が形態ごとに一意に決まり、台帳が空であることを巻き戻しの完了条件にできる。 | 介入形態を限定しない、解除をシーン遷移時のみ行う |
| DEC-111 | 両MODを同一ソリューションに置き、相互参照とBepInEx依存宣言を行わない。 | 同一ソリューションなら一度のビルドで両方が実行位置へ配置され、EDI互換の受け入れ条件（AC-127）を同じ手順で検証できる。一方で参照や依存宣言を持たせると、片方だけを配置した構成が起動しなくなる。 | 別ソリューションへ分離、EDI側へ依存宣言を追加 |
| DEC-112 | 窓の判定と設定検証をゲーム非依存の `Core` に置く。 | 本MODの正しさの大半は「いつ窓が開き、いつ閉じ、どの倍率がかかるか」であり、これはゲームを起動しなくても検証できる。実機でしか試せない範囲を、ゲームの値の読み書きだけに縮小できる。SPEC001が同じ分離で運用実績を持つ。 | Plugin へ一体化する、実機テストのみで検証する |

## 14. 前提、延期事項、残存リスク

### 14.1 明示的な前提

- `0Harmony.dll` と `Il2CppInterop.HarmonySupport.dll` により、対象ビルドのマネージド呼び出し経路へパッチを適用できる。
- 状態異常の付与判定と拘束の抵抗判定はメインスレッドで処理され、MODの介入もメインスレッドで完結する。
- `MultiSettingValue<T>` の `ResitValue` / `ReleaseValue` が、キー単位の登録と解除として機能する。
- `DamageStack.IsReceiverLelia` が、受け側がプレイヤーであるかの判別として信頼できる。
- 快楽系・身重系の分類は列挙子名からの推論であり、実機のプレイ感で見直す。
- ユーザーは脱出不能な状況が発生し得ることを許容する。ゲームオーバー経由の復帰がゲーム設計上の正規経路である。

### 14.2 延期事項

- 被ダメージ量、敵HP、プレイヤー最大ステータスの調整。
- 状態異常の自然回復時間、同時付与数上限、スリップダメージ間隔の調整。
- 敵の行動パターン、出現数、リスポーンの調整。
- 難易度階層の複数プリセット化と、階層ごとの調整値セット。
- 拘束ゲージの着色以外の、無力化窓を明示する演出（効果音、画面効果、専用アイコン）。
- ゲーム内設定画面からの難易度階層の切り替え。
- 状態異常種別の分類をゲーム側データから自動導出する方法。

### 14.3 残存リスク

| リスク | 影響 | 軽減策 |
|---|---|---|
| 報告値の差し替えが保存値へ波及する | `Normal` で始めたセーブが `Hard` になり、MOD取り外し後も残る | 付録Aで波及の有無を先に実測する。波及する場合はFR-105の縮退動作へ移り、差し替えを行わない。AC-104でセーブの不変を検証する |
| 付与率を読むメソッドが特定できない | 5.2の付与率強化が実現できない | 付録Aの実測項目とする。特定できない場合、当該機構だけを無効化し、レベル進行の強化は独立に成立させる（FR-109は `AddAbnormal` 経路のみに依存する） |
| 一時上書きが共有アセットへ残留する | 敵へ与える状態異常の付与率まで恒久的に変わり、難易度が下がる | FR-107の例外経路を含む復元、FR-108の再入防止、AC-107とAC-108で検証する。復元失敗時は当該機構を無効化する（9章） |
| 無力化窓が `穢れ` の脱出困難化と体感上区別できない | `穢れ` を管理する意味が薄れ、DEC-102の目的が達成されない | FR-112とFR-113で値への非干渉を規定し、AC-112とAC-113で穢れ由来の挙動の一致を検証する。窓は時間帯として発生するため、ゲージの停止と再開が観測できる |
| 設定次第で恒久的な無力化が成立する | 進行不能に近い状態になり、原因が設定だと気付けない | FR-117で起動時に占有率の期待値を警告する。ユーザーの選択として禁止はしない |
| 寄与キーの解除漏れ | 移動速度低下が拘束外でも残り続ける | FR-119で3つの解除契機を規定し、FR-124の台帳で未解除を追跡する。AC-119とAC-123で検証する |
| `Hard` 報告により `HardOnly` の敵が出現し、EDIのカタログに未分類行が増える | EDI側の網羅性レポートが未分類を報告し、リリースゲートに引っかかる | 7.2で意図した差分として明示する。EDI側は追加であり既存行を失わない。AC-127で既存行の保持を検証する |
| ゲーム更新でパッチ対象が変わる | 機構が無言で効かなくなる | 9章でパッチ対象の欠落を当該機構の無効化と提示として扱う。FR-133の起動ログに適用パッチ数を出す |
| 快楽系・身重系の分類が実態と合わない | 意図しない状況で窓が発生する、または意図した状況で発生しない | DEC-107により設定で修正可能にする。既定集合を6.1に明示し、実機で見直す |
| EDI連動MODと同時動作でフレーム時間が悪化する | 両MODの原因切り分けが難しい | 10.1でMOD単体の性能要件を規定し、AC-129で計測する。介入ログを既定で無効にする |

## 付録A: 実測で確定する項目

対象ビルドのIL2CPPネイティブ実装は生成されたinterop アセンブリから読めない。次の項目は**推論であり、調整値を確定する前に実機で確認する**。手順は `docs/testing/SPEC002-test-scenarios.md` に記録する。

| # | 確認項目 | 確認できないと影響する箇所 |
|---|---|---|
| A-1 | 難易度の報告値を差し替えたとき、保存値へ波及するか。波及する場合、どの経路か | 5.1、FR-105、AC-104。波及するなら縮退動作へ移る |
| A-2 | 難易度の判定に実際に使われる読み取りが `PlayerStatusManager.IsHardMode` か `s_GameDifficultyForCheck` か、その両方か | 5.1、FR-103 |
| A-3 | `DamageParameter.m_AbnormalRate` を読み、`AbnormalList.AddAbnormal` を呼ぶメソッド | 5.2、FR-106 |
| A-4 | `m_AbnormalRate` の値域と意味（確率か閾値か、上限値） | 5.2、`AbnormalRateMultiplier` の既定値 |
| A-5 | `AddAbnormalConditionType`（`Rate` / `HP1`）による付与経路の分岐 | 5.2。`HP1` 経路が確率を持たない場合、倍率が作用しない |
| A-6 | ダメージ解決が再入するか。`DamageManager.IsUpdatingDamage` の意味 | FR-108 |
| A-7 | `GachaGachaSystem.Execution` が抵抗入力1回あたりの反映であるか | 5.3、FR-111 の介入点 |
| A-8 | `GachaGachaSystem.CurrentValue` の上昇量が `AbnormalList.GachaInputRate` と `GachaInputRateDefilement` のどちらから合成されるか | FR-112、FR-113 の非干渉が成立するかの根拠 |
| A-9 | `GachaGachaSystem.Update` の減衰が `Execution` と独立か | 5.3、DEC-103 |
| A-10 | `PauseHoldUIAndGachaMsv` / `IsPauseBindProcess` が減衰も止めるか | 無力化窓の実装手段の選択 |
| A-11 | 拘束からの離脱を検出できる通知（`Bind.OnReleaseResponse`、`GachaBind.OnReleaseResponseFromBind`、`AbnormalSlipDamage.ReleaseAll`）のうち、プレイヤーの離脱すべてを捉えるもの | 5.4、FR-118 |
| A-12 | `MultiSettingValue<T>.ResitValue` / `ReleaseValue` のキーの型と、同一キーの再登録時の挙動 | FR-119、FR-120 |
| A-13 | `PlayerStatusManager.MoveSlowRateMsv` の値の意味（倍率か減算か、値域） | 5.4、`RecoveryMoveSlowRate` の既定値 |
| A-14 | 離脱後の無敵時間を保持する対象と、`DamageInvincibleMsv` との関係 | 5.4、`RecoveryInvincibleScale` |
| A-15 | `AbnormalData.MaxLevel` の取得可否と、`OnChangeLevel` を通る追加進行の手段 | 5.2、FR-109 |
| A-16 | `AbnormalList.MaxAddedAbnormalCount` に達している状態での追加付与の挙動 | 5.2の境界値 |
| A-17 | 快楽系・身重系の既定集合（6.1）が実際のゲーム内表現と合致するか | DEC-107、既定集合の見直し |

## 付録B: 参照した interop シンボル

本仕様が参照するシンボルは `BepInEx/interop/SiNiSistar2.dll` に実在することを確認済みである。意味論の確度は付録Aによる。

| 領域 | シンボル |
|---|---|
| 難易度 | `SiNiSistar2.Manager.GameDifficulty` (`Normal`/`Casual`/`Hard`)、`PlayerStatusManager.GameDifficulty`、`GameDifficultyRP`、`IsHardMode`、`s_GameDifficultyForCheck`、`GetCurrentGameModeSetting`、`Enemy.Character.DifficultyCondition` (`Both`/`HardOnly`/`NonHardOnly`)、`DifficultySwitcher` |
| Hard専用データ | `SiNiObject.m_MaxHPHasHardModeOverride`、`m_MaxHPHardModeValue`、`DamageParameter.m_PowerHasHardModeOverride`、`m_PowerHardModeValue`、`DamageReceiverCollider.m_HardOverwrite`、`GachaGachaParameter.m_HardOverwrite` |
| 状態異常 | `SiNiSistar2.Obj.AbnormalList` (`AddAbnormal`、`Has`、`GetAbnormalLevel`、`MaxAddedAbnormalCount`、`Target`、`UpdateGachaRate`)、`AbnormalData` (`m_Level`、`MaxLevel`、`OnChangeLevel`、`GachaInputRate`)、`AbnormalType`（71種）、`Damage.DamageParameter.m_AbnormalRate`、`m_AbnormalTypes`、`m_AddAbnormalConditionType`、`Damage.AddAbnormalConditionType` (`Rate`/`HP1`) |
| 拘束 | `SiNiSistar2.GachaGachaSystem` (`Execution`、`Update`、`CurrentValue`、`Rate`、`IsSuccessGacha`、`IsForceSuccessRequest`、`IsPauseBindProcess`、`PauseHoldUIAndGachaMsv`)、`GachaGachaParameter` (`m_SuccessValue`、`m_DeclineValue`、`m_SuccessValueHard`、`m_DeclineValueHard`、`m_DefilementBind`)、`Obj.Bind` (`ReleaseBind`、`OnReleaseResponse`、`IsBoundRP`、`BinderEnemy`)、`Obj.GachaBind` (`OnReleaseResponseFromBind`、`Execution`)、`Obj.AbnormalSlipDamage` (`ReleaseAll`、`HoldAction`、`m_GachaBindParameter`) |
| 穢れ軸（非干渉対象） | `AbnormalList.GachaInputRateDefilement`、`GachaGachaParameter.m_DefilementBind`、`AbnormalType.Defilement` |
| 多重寄与値 | `SiNiSistar2.Obj.MultiSettingValue<T>` (`ResitValue`、`ReleaseValue`、`AllClear`、`Value`)、`PlayerStatusManager.MoveSlowRateMsv`、`InitJumpSlowRateMsv`、`DamageInvincibleMsv`、`DisableHoldMsv`、`Obj.StatusCondition.IsHoldable` |
| 受け側判別 | `Damage.DamageStack.IsReceiverLelia`、`Obj.StatusCondition.IsPlayer`、`IsEnemy`、`Obj.Lelia` |
| EDI連動MODが依存する面 | `Lelia.IsHold`、`IsHP0`、`Bind.BinderEnemy`、`UI.Gallery.EnemyData.GalleryEnemyID`、`Manager.Gallery.GaTakePlayer`、`AbnormalList.Has` |
