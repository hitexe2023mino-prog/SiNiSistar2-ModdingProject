# SPEC003 テストシナリオ

| 項目 | 内容 |
|---|---|
| 正本 | [`docs/specifications/SPEC003.md`](../specifications/SPEC003.md) |
| 実装状況 | [`docs/implementation/SPEC003-traceability.md`](../implementation/SPEC003-traceability.md) |
| 作成日 | 2026-08-08 |
| 対象 | `community.sinisistar2.pleasure` 0.1.0（プローブ段階） |
| 想定実施者 | 対象ゲームビルドを起動できる担当者 |

この文書は検証手順だけを定める。仕様を変更しない。手順と期待結果が食い違う場合、正しいのは正本であり、この文書か実装のどちらかが誤っている。

## 1. 現在の到達点

自動テストは83件が通っている。快楽の増減、絶頂の一回性、感度の単調性、限界算出、性的攻撃の判別順序、随伴ファイルのスキーマ処理は、ゲームを起動せずに検証済みである。

**このビルドはプローブ段階である。** 挙動として変わるのは拘束中のHP0抑止だけで、快楽・絶頂・感度・`BreastSuper` はすべて無変更相当の既定値で停止している。プローブは付録Aの各項目を1回ずつログへ記録する。

付録A のうち **A-4、A-5、A-7 は静的調査で解消済み**であり、実機確認を要しない。残るのは以下である。

## 2. 準備

```bash
dotnet test SiNiSistar2.Edi.sln -c Release
```

```bash
dotnet build SiNiSistar2.Edi.sln -c Release
```

1. ゲームを一度起動して終了し、`BepInEx/config/community.sinisistar2.pleasure.cfg` を生成させる。
2. 起動ログに次が出ることを確認する。出ない場合、以降の手順は無効である。

```
SiNiSistar2 Pleasure 0.1.0 loaded; suppressHp0=True, gauge=off, sensitivity=off,
climaxGameOver=False, breastSuper=off, probe=True, patches=2.
```

3. 測定用のセーブを用意する。**性的な攻撃を行う敵**と**捕食・暴力のみを行う敵**の両方に到達できる地点が望ましい。

> 本MODはゲームのセーブを書き換えない。ただし他の検証と同様、開始前にバックアップを取ること。

## 3. プローブ実行

`[probe]` を含むログ行を読む。各項目は最初の1回だけ記録される。

### 3.1 A-1 — HP0抑止（最重要）

| 手順 | 期待結果 | 判定 |
|---|---|---|
| 1. 性的・非性的を問わず、敵に拘束される | `[probe] A-1: HP0 suppression registered while bound; RemainHp1 now reads True` が出る | 出ない、または `False` なら抑止が成立していない |
| 2. 拘束されたままHPが尽きるまで待つ | **HPが1で止まり、ゲームオーバーにならない** | **AC-202** |
| 3. ダメージ表示と被弾演出を確認する | 従来どおり発生する | **AC-202**。消えていれば FR-205 違反 |
| 4. 脱出する、またはシーンを移動する | 以後の被弾でHPが0に達し、通常どおりゲームオーバーになる | **AC-203** |

`[probe] A-1 caution: RemainHp1Msv could not be resolved` が出た場合、この機構は成立していない。要求(1)が実現できないため、設計の見直しが必要になる。

### 3.2 A-2／A-3 — 拘束中の被弾と状態異常

| 期待するログ | 意味 |
|---|---|
| `[probe] A-2 answered: a hit taken while bound is visible to the MOD as a damage stack.` | 快楽の上昇契機が取れる。機構が成立する |
| （上記が出ない） | 拘束中の被弾を観測できない。快楽は別の契機（拘束の継続時間など）へ縮退する必要がある |
| `[probe] A-3 answered: ... carries m_AbnormalTypes [...]` | 判別が状態異常で成立する。**列挙された名前を記録すること** |
| `[probe] A-3 caution: ... carried no m_AbnormalTypes` | その攻撃は状態異常で判別できない。`SexualEnemyIds` に敵IDを足す必要がある |

**性的な敵と捕食する敵の両方で実施する。** `[probe] Captor '<id>' classified as Sexual/NonSexual from [...]` の行が、判別が意図どおりかを示す。

- 捕食・暴力のみの敵が `Sexual` と出た → `NonSexualEnemyIds` にその ID を追加する
- 性的な敵が `NonSexual` と出た → `SexualEnemyIds` に追加するか、`SexualAbnormalTypes` を見直す

**この突合が要求(2)の核心である。** 敵IDと分類の対応表をこの文書へ追記すること。

### 3.3 A-6 — 耐久と絶頂限界

`[probe] A-6: durability is X of Y; HP ... Climax limit would be N` を読む。

- `Y`（耐久の最大値）の実際の値域を記録する
- 育成前後で比較し、増分を記録する
- 記録した値域から `ClimaxLimitBase` と `ClimaxLimitPerDurability` を決める

### 3.4 A-7 — セーブポイントとオベリスク（確認のみ）

静的調査で解消済み。実機で確認するのは**発火のタイミングだけ**である。

| 手順 | 期待結果 |
|---|---|
| セーブポイントを使う | `[probe] A-7 answered: SavePointAsyncLabel.ExecutionOneAsync ran with IsObeliskLabel=False` |
| オベリスク（聖なる像）を使う | 同上、`IsObeliskLabel=True` |
| シーンに入っただけで作動する | **異常。** シーン設定時に走っているため、リセット契機として使えない |

### 3.5 A-9 — セーブスロットの識別

`[probe] A-9: save slot is SelectID=..., LoadedFileName='...', IsAutoSave=..., sidecar key='...'`

- 手動セーブとオートセーブの両方で記録する
- 同じスロットへ複数回セーブし、`LoadedFileName` が安定するか確認する
- 別スロットへセーブし、`sidecar key` が変わることを確認する

安定しない場合、随伴ファイルの紐付けを `SelectID` のみへ縮退する。

### 3.5.1 実測済みの所見（2026-08-08）

```
[probe] A-2 answered: a hit taken while bound is visible to the MOD as a damage stack.
[probe] A-3 answered: a hit taken while bound carries m_AbnormalTypes [Defilement].
[probe] Captor 'GaID_PictureFrameBig' classified as Sexual   from [Defilement].
[probe] A-3 caution: a hit taken while bound carried no m_AbnormalTypes ...
[probe] Captor 'GaID_PictureFrameBig' classified as NonSexual from [].
```

- **A-2 は肯定**。拘束中の被弾はダメージスタックとして観測できる。快楽の上昇契機が成立する。
- **A-3 は部分的**。状態異常を伴う攻撃と、伴わない攻撃が同じ敵に共存する。したがって**状態異常だけでは同一の敵が Sexual と NonSexual に割れる**。敵ID による固定が必須である。
- **絵画は拘束する。** `hold/GaID_PictureFrameBig/...` が EDI 側にも出ている。仕様調査時の「絵画は拘束しない」という推定（`Enemy.Character.Cathedral.CathedralArtGallery.PictureFrame` に hold タスクがない）は、実際に拘束する `PictureFrameBig` と別の型を見ていたための誤りだった。
- `GaID_PictureFrameBig` を `SexualEnemyIds` の既定へ追加済み。以後この敵の全攻撃が性的攻撃になる。

**残る作業** — 捕食・暴力を行う敵に拘束され、`NonSexual` と判定されることを確認する。`Sexual` と出た敵があれば、`F10` の編集画面でその敵を `NonSexual` にする（3.7）。

### 3.7 AC-230〜234／A-13 — 敵別分類カタログと編集画面

分類はもう設定ファイルの文字列ではない。`BepInEx/config/community.sinisistar2.pleasure/enemy-attacks.json` が正本であり、`F10` の画面から編集する。

**AC-230 — 種としての引き継ぎと、以後の独立**

1. カタログファイルを削除し、起動する。起動ログに次が出ることを確認する。

```
Enemy catalogue: 108 enemies, 1 forced sexual, 0 forced non-sexual, at '...\enemy-attacks.json'.
```

2. ファイルを開き、`GaID_PictureFrameBig` が `"kind": "Sexual"` で入っていることを確認する。`SexualEnemyIds` の既定が種として引き継がれている。
3. `.cfg` の `SexualEnemyIds` を空にして再起動する。**カタログの内容が変化しないこと**を確認する。以後カタログが正本であり、設定は参照されない。

**AC-231 — 再起動なしで反映される**

1. 快楽が上昇する設定（`PleasureGainPerHit` > 0）で起動する。
2. カタログで `Auto` のままの敵に拘束され、快楽が上昇しないことを確認する。
3. 拘束されたまま `F10` を押す。一覧の先頭付近に**その敵が選択された状態**で開くこと、上部に `Holding you now: GaID_...` が出ることを確認する。
4. `Space` を押して `Sexual` にし、`Enter` で保存する。
5. **拘束を続けたまま**、次の被弾から快楽が上昇することを確認する。ゲームの再起動もセーブのロードも行わない。

**AC-232 — 拘束される前の敵も分類できる**

1. `F10` を押し、`Tab` で全件表示へ切り替える。一度も拘束されたことのない敵が一覧に現れることを確認する。
2. `Tab` で戻す。拘束された経験のある敵だけが並ぶことを確認する。まだ一度も拘束されていない場合は全件が並ぶ（空の一覧は出さない）。

**AC-233 — 取り消しが効く**

1. `F10` を押し、複数の敵の宣言を変更する。
2. `Escape` で閉じる。ログに「the previous settings are restored」が出ることを確認する。
3. 再び `F10` を押し、**すべて元の値へ戻っていること**を確認する。ファイルの更新日時が変わっていないことも確認する。

**AC-234 — 新しいスキーマ版を壊さない**

1. カタログファイルの `"schemaVersion"` を `99` に書き換える。
2. 起動する。警告が出て、108件が0件として扱われることを確認する。
3. `F10` で編集して `Enter` を押す。**ファイルが上書きされないこと**と、拒否が記録されることを確認する。
4. `"schemaVersion"` を `1` へ戻す。

**A-13 — 拘束中の入力の干渉**

IMGUI のイベント消費は IMGUI の内部にとどまり、ゲームは独自に入力を読む。したがって編集画面を開いている間のキー入力とクリックは**ゲーム側にも届く**可能性がある。拘束中に開いて次を確認する。

- `Space` や上下キーが、抵抗入力や回避として同時に解釈されるか。
- 行のクリックが攻撃として同時に解釈されるか。
- 実害があれば、キーボードのみで完結する経路（上下キーと `Space`）で回避できるか、あるいは拘束中は開かない運用にするか。

結果を4章へ記入する。

### 3.8 A-14／AC-217、AC-235〜237 — `BreastSuper` への遷移と治療

**確定済み（2026-08-08 実測）。** `Breast` の `MaxLevel` は `1` である。付与された時点で最大レベルにあるため、以後の付与はすべて計数対象になる。`BreastSuperAfterApplications = 3` は「膨乳した状態でさらに3回」を意味する。

初回の読みは `physicalConditionFlag=Base`、`nameID=None` を返したが、これは**未装着のテンプレート**の値である。付与済みの実体での値は `[probe] A-14: Breast while attached at level ...` が出す。

**A-14 は最優先である。** 治療が届かない状態異常を通常プレイへ出すと、進行を阻害する。起動して少し遊ぶと、プローブが次を出す。

```
[probe] A-14: Breast maxLevel=?, haanjaCanCure=?, physicalConditionFlag=?, ...
[probe] A-14: BreastSuper maxLevel=?, haanjaCanCure=?, physicalConditionFlag=?, ...
```

読み方は次のとおりである。

| 読み | 意味 | 次の手 |
|---|---|---|
| `BreastSuper` の `physicalConditionFlag` が `Breast` と同じ | 治療イベントが `PhysicalCondition` で書かれていれば、**介入なしで既存の治療が届く** | 実機で治療選択肢が出るか確認する。出れば FR-241 は完了 |
| `BreastSuper` の `haanjaCanCure` が既に真 | ハーニャの治療対象として既に登録されている | 同上。確認のみ |
| どちらも否 | 既存の治療は届かない | `MakeHaanjaCurable = true` で再確認する |

**AC-238 — アイテムによる遷移（デバッグ）**

膨乳を付与するアイテムでも遷移する。付与経路は問わない。

1. `BreastSuperAfterApplications = 3` を設定して起動する。
2. アイテムを1回使う。ログに次が出ることを確認する。

```
Breast applied at level 1/1 via AddAbnormal(AbnormalType): 1 counted, 2 more before BreastSuper.
```

3. **1回の使用につき計数が1だけ進むこと**を確認する。3つの経路すべてにpostfixを当てているため、同じ付与が複数回報告される可能性があるが、フレーム単位で1回に畳んである。2以上進む場合は畳み込みが効いていない。
4. 3回目で `Breast escalated to BreastSuper` が出ることを確認する。
5. `[probe] A-15: Breast reached the MOD through ...` で、アイテムがどの経路を通ったかを記録する。

最大レベルへ達する手間を省きたい場合は `CountBelowMaxLevel = true` にする。デバッグ専用で、仕様の 5.8 から外れる。

**AC-217／AC-235 — 遷移**

1. `BreastSuperAfterApplications = 2` を設定して起動する。
2. `Breast` を与える敵に繰り返し拘束される。ログの `[probe] A-14: Breast applied; level N of M` で最大レベルを確認する。
3. 最大レベルに達するまでは遷移しないことを確認する（AC-235）。
4. 最大レベル到達後、さらに2回受けると `Breast escalated to BreastSuper` が出ることを確認する。
5. ステータス画面で `BreastSuper` が表示され、`Breast` が消えていることを確認する。

**AC-236 — 計数の保存**

1. 遷移の1回手前まで進める。
2. セーブポイントでセーブし、ゲームを終了して再起動、同じスロットをロードする。
3. さらに1回 `Breast` を受けると遷移することを確認する。初めから積み直しになっていないこと。

**AC-237 — 治療とアンロード**

1. `MakeHaanjaCurable = true` で起動し、ログに「BreastSuper is now marked curable by Haanja」が出ることを確認する。
2. ハーニャの治療を受け、`BreastSuper` が消えることを確認する。
3. 治療が完了しない、または選択肢が出ない場合はその旨を記録する。**この場合 FR-241 は満たせず、延期事項へ移す判断が必要になる。**

### 3.6 A-10／A-11 — 未着手

`BreastSuper` の挙動と絶頂演出の競合は、それぞれの機構を実装してから確認する。現段階では対象外。

## 4. 測定結果の記入

確定した値をここへ追記し、`BepInEx/config/community.sinisistar2.pleasure.cfg` へ反映する。

| 項目 | 測定値 | 決定した設定値 |
|---|---|---|
| A-2 拘束中の被弾の観測 | **観測できる（2026-08-08 実測）** | — |
| A-3 状態異常を伴う攻撃の割合 | **一部のみ。**同一の敵が `[Defilement]` を伴う攻撃と、`m_AbnormalTypes` が空の攻撃の両方を持つ（2026-08-08 実測） | 状態異常だけでは同一の敵が Sexual/NonSexual に割れる。敵ID の固定が必要 |
| 性的な敵の ID | `GaID_PictureFrameBig`（絵画）を確認 | `SexualEnemyIds` の既定へ追加済み |
| 捕食・暴力の敵の ID | 未測定 | `NonSexualEnemyIds` |
| A-6 耐久の最大値の値域 | **`m_MaxDurability` は 100（2026-08-08 実測）。** 現在値の読みは表示の誤りで再測定が必要 | `ClimaxLimitPerDurability` |
| 1回の拘束あたりの被弾数 | 未測定 | `PleasureGainPerHit` |
| A-9 スロットキーの安定性 | 未測定 | — |
| A-13 拘束中の編集画面の入力干渉 | 未測定 | — |
| A-14 `Breast` の `MaxLevel` | **`1`（2026-08-08 実測）** | 付与時点で最大。以後の付与はすべて数える |
| A-14 `BreastSuper` の `MaxLevel` | 未測定 | — |
| A-15 アイテムが通る付与経路 | 未測定 | — |
| A-14 `BreastSuper` の `PhysicalConditionFlag` と `HaanjaCanCure` | 未測定 | `MakeHaanjaCurable` の要否 |

`PleasureGainPerHit` は「1回の拘束で何発受けるか」から逆算する。1回の拘束でおよそ1回絶頂させたいなら `1 / 被弾数` が目安になる。

## 5. 次の段階

3章がすべて確定したら、次を実装する。この文書の該当節はその時点で追記する。

1. 快楽ゲージと絶頂演出の描画（FR-212、AC-210）
2. 随伴ファイルの読み書きとセーブ同期（FR-222〜FR-226、AC-218〜AC-221）
3. 絶頂限界によるゲームオーバー（FR-215、AC-213、AC-229）
4. ~~`BreastSuper` の通常付与~~ **実装済み。** 残るのは A-14 の実測と AC-217、AC-235〜237 の実機確認
5. 3つのMOD同時動作の確認（AC-223）
