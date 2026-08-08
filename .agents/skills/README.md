# Agent Skills

この `.agents/skills` ディレクトリを、リポジトリ固有のエージェントスキルの正本とする。Codexはリポジトリ内で起動されたときにこの場所を探索する。Claude Code は [`.claude/skills`](../../.claude/skills) の同名アダプターから正本を読み込む。作業に該当するスキルがある場合は、対象の `SKILL.md` を最後まで読んでから実行する。参照資料は各 `SKILL.md` に記載された条件に従って必要なものだけを読む。

## スキルの選択

| スキル | 使用する状況 | 主な成果物 |
|---|---|---|
| [`create-specification`](create-specification/SKILL.md) | 仕様書がまだなく、アイデアや要求から新しく作る | 新規仕様書、要件、受け入れ条件 |
| [`refine-specification`](refine-specification/SKILL.md) | 既存仕様に曖昧さ、矛盾、欠落、未決の設計判断がある | 改訂仕様書、設計判断、変更台帳 |
| [`implement-from-spec`](implement-from-spec/SKILL.md) | 仕様が実装可能で、コード・設定・テストへ反映する | 実装、テスト、要件との対応記録 |

複数段階を含む依頼では、左から必要な段階だけを順に使用する。仕様作成・改訂を実装スキルへ混ぜず、実装上の都合で規範的な要件を変更しない。

## 共通品質基準

- スキル名とフォルダー名を一致させ、lowercase hyphen-case にする。
- `SKILL.md` の frontmatter は `name` と `description` だけにする。
- `description` に機能と具体的な使用条件を記載し、本文に独立した「いつ使うか」節を置かない。
- 本文は命令形で、入力確認、調査、実行、検証、完了報告の順序と停止条件を明示する。
- 事実、推論、仮定、提案、未決事項を区別し、重大な製品判断を暗黙に補完しない。
- 詳細なテンプレートやチェックリストは `references/` へ分離し、必要な場合に読む条件を本文へ記載する。
- `agents/openai.yaml` の表示名、説明、既定プロンプトを `SKILL.md` と同じ責務・言語に保つ。
- `.claude/skills/<skill-name>/SKILL.md` は正本と同じ `name` と `description` を持ち、正本を読み込むアダプターだけを記載する。
- スキルの本文と参照資料は `.agents/skills` だけで編集し、`.claude/skills` へ複製しない。
- TODO、雛形の説明、存在しない相対参照を残さない。
- 本文を500行未満に保ち、一般知識ではなくリポジトリ固有または手続き上重要な指示を優先する。

## 検証

スキルを変更したら、リポジトリのルートで次を実行する。

```powershell
python .github/scripts/validate-skills.py
```

検証は、配置、frontmatter、命名、TODO、本文長、相対リンク、UIメタデータに加え、Claude Code アダプターの網羅性と正本との一致を確認する。内容面では、上の責務境界と、対象スキルのワークフローが一致しているかも差分レビューする。
