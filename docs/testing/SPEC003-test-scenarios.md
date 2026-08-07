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

自動テストは42件が通っている。快楽の増減、絶頂の一回性、感度の単調性、限界算出、性的攻撃の判別順序、随伴ファイルのスキーマ処理は、ゲームを起動せずに検証済みである。

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

### 3.6 A-10／A-11 — 未着手

`BreastSuper` の挙動と絶頂演出の競合は、それぞれの機構を実装してから確認する。現段階では対象外。

## 4. 測定結果の記入

確定した値をここへ追記し、`BepInEx/config/community.sinisistar2.pleasure.cfg` へ反映する。

| 項目 | 測定値 | 決定した設定値 |
|---|---|---|
| A-2 拘束中の被弾の観測 | 未測定 | — |
| A-3 状態異常を伴う攻撃の割合 | 未測定 | — |
| 性的な敵の ID | 未測定 | `SexualEnemyIds` |
| 捕食・暴力の敵の ID | 未測定 | `NonSexualEnemyIds` |
| A-6 耐久の最大値の値域 | 未測定 | `ClimaxLimitPerDurability` |
| 1回の拘束あたりの被弾数 | 未測定 | `PleasureGainPerHit` |
| A-9 スロットキーの安定性 | 未測定 | — |

`PleasureGainPerHit` は「1回の拘束で何発受けるか」から逆算する。1回の拘束でおよそ1回絶頂させたいなら `1 / 被弾数` が目安になる。

## 5. 次の段階

3章がすべて確定したら、次を実装する。この文書の該当節はその時点で追記する。

1. 快楽ゲージと絶頂演出の描画（FR-212、AC-210）
2. 随伴ファイルの読み書きとセーブ同期（FR-222〜FR-226、AC-218〜AC-221）
3. 絶頂限界によるゲームオーバー（FR-215、AC-213、AC-229）
4. `BreastSuper` の通常付与（FR-221、AC-217）
5. 3つのMOD同時動作の確認（AC-223）
