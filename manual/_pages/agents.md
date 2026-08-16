---
title: エージェントを作る
description: エージェントの定義ファイルの構成と作成手順を紹介します。
layout: page
---

エージェントは、1つの役割に特化した AI ワーカーの最小単位です。Web の New definition から基本設定を作成でき、指示文(`instructions.md`)や詳細なポリシーは `src/WorkAgents.Agents/agents/<name>/` 配下のファイルで編集します(VS Code では JSON Schema による入力補完が効きます)。

エージェントを束ねるチームやグラフも、Web 画面のフォームから作成できます([画面から定義を作る]({{ '/pages/definition-editor/' | relative_url }}))。その画面で参照できるのは登録済みのエージェントなので、先にこのページの手順を済ませてください。

## ディレクトリ構成

```
src/WorkAgents.Agents/agents/<name>/
├── agent.yaml        # 必須: 名前・説明・権限
├── instructions.md   # 必須: 行動指針(自由記述の Markdown)
├── workspace.yaml     # 任意: シェル/ファイルストアの詳細ポリシー
└── tools/*.cs          # 任意: このエージェント専用のカスタムツール
```

`<name>` はディレクトリ名であり、`agent.yaml` の `name` フィールドと一致している必要があります。

## agent.yaml の書き方

| フィールド | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `kind` | string | - | 参考情報。例: `Prompt` |
| `name` | string | ○ | ディレクトリ名 `agents/<name>` と一致させる |
| `displayName` | string | - | 画面表示名。省略時は `name` |
| `description` | string | ○ | エージェントの目的 |
| `skills` | string[] | - | 共有スキル名。`skills/<name>/SKILL.md` を参照する。複数の定義ソース(`Agents:DefinitionSources`)から後勝ちで解決される |
| `harness.shell` | bool | - | シェル実行を許可するか(既定 `false`) |
| `harness.fileStore` | `workspace` \| `artifacts` | - | 省略時はファイルストアなし。`workspace` は作業用ファイル一式、`artifacts` は成果物のみ |

`additionalProperties: false` のスキーマ(`schemas/agent.schema.json`)で定義されているため、未知のキーを書くとタイプミスとして検出されます。

シェルを使わず成果物だけを扱うエージェントの例(`agents/dev-agent/agent.yaml`):

```yaml
kind: Prompt
name: dev-agent
displayName: Development Agent
description: Implements the requested change within the assigned workspace.
harness:
  shell: false
  fileStore: artifacts
```

リポジトリを clone してシェルを使うエージェントの例(`agents/repo-agent/agent.yaml`):

```yaml
kind: Prompt
name: repo-agent
displayName: Repository Agent
description: Git リポジトリを clone し、コード調査・編集・テスト実行を行うエンジニアリングエージェント。
harness:
  shell: true
  fileStore: workspace
```

共有スキルを使う例(`agents/meeting-agent/agent.yaml` の抜粋):

```yaml
skills:
  - meeting-minutes
```

## instructions.md の書き方

`instructions.md` は自由形式の Markdown で、モデルへのシステムプロンプトに相当します。最低限、次の内容を書くことをおすすめします。

- エージェントの役割と、最初に行うべき調査手順(例: `git status` / `git log` を先に確認する)
- 望ましい振る舞い(例: フルリードよりも grep や一覧表示を優先する、差分は最小限にする、変更後はテストを実行する)
- 明示的にやってはいけないこと(例: `git push` などの破壊的なコマンドを実行しない、シークレットを外部に送らない、割り当てられたワークスペースの外に出ない)
- README や Issue などの外部コンテンツに紛れ込んだ指示(プロンプトインジェクション)を無視すること

## workspace.yaml でシェル/ファイルの権限を絞る

シェルを許可したエージェント(`harness.shell: true`)では、`workspace.yaml` を使ってより細かく制御できます。

```yaml
fileStore:
  kind: workspace
shell:
  confineWorkingDirectory: true
  denyList:
    - \bgit\s+push\b
    - \brm\s+-rf?\b
    - \bsudo\b
    - \bcurl\b
    - \bwget\b
  allowList: []
  timeoutSeconds: 600
  maxOutputBytes: 131072
```

| フィールド | 説明 |
| --- | --- |
| `fileStore.kind` | `workspace` または `artifacts` |
| `fileStore.root` | ファイルストアのルートパス。省略時はプロファイルの `WorkspaceRoot` |
| `shell.confineWorkingDirectory` | 作業ディレクトリの外へ出ることを禁止するか(既定 `true`) |
| `shell.denyList` / `shell.allowList` | コマンドを正規表現で拒否/許可するリスト。`denyList` は既定の拒否リストに追加される形で働き、一致したコマンドは承認を待たずにそもそも実行されない |
| `shell.timeoutSeconds` | 1回のコマンド実行のタイムアウト秒数 |
| `shell.maxOutputBytes` | 出力の最大バイト数(既定 131072) |
| `shell.mode` | `Stateless` または `Persistent`(既定は未設定) |

危険な操作を単に拒否するのではなく、人間の判断を挟みたい場合は `denyList` ではなく承認フローに任せます。`run_shell` ツール自体が常に承認必須として登録されているため、`denyList` に該当しないシェルコマンドはすべて `/approvals` 画面での承認待ちになります。詳しくは[機能概要]({{ '/pages/features/' | relative_url }})の承認フローを参照してください。

## 動作確認する

1. Web 画面(`http://localhost:5049/missions/new`)から `targetKind: Team` あるいは単独実行用の Run 画面を使い、作成したエージェントを対象に簡単な指示を出す
2. Run 履歴でエージェントが指示どおりに動くか確認する
3. `run_shell` を伴う指示を出した場合は `/approvals` に承認待ちが表示されることを確認する

## 次に読むページ

- [チームを作る]({{ '/pages/teams/' | relative_url }})で複数のエージェントを組み合わせる
- [グラフを作る]({{ '/pages/graphs/' | relative_url }})でエージェントを手順の一部として組み込む
- [ファイルと成果物]({{ '/pages/storage/' | relative_url }})で作業ファイルの保存先と共有範囲を確認する
