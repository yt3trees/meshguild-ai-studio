---
title: グラフを作る
description: ノードとエッジで手順を組み立てるグラフ(ワークフロー)の作成方法を紹介します。
layout: page
---

グラフは、エージェント・チーム・コード処理・承認・条件分岐・並列実行・合流・ループ・サブグラフをノードとしてつなぎ、決まった手順で実行するワークフローです。`src/WorkAgents.Agents/graphs/<name>/graph.yaml` に定義します。

YAML を直接書かずに済ませたい場合は、Graph Studio のフォームからも作成・編集できます([画面から定義を作る]({{ '/pages/definition-editor/' | relative_url }}))。ノード種別ごとに使う項目だけが出て、参照先はすべて選択式になります。

旧形式の `workflows/<name>/workflow.yaml` は非推奨(スキーマ上も "deprecated" と明記)で、実行時には未移行のファイルは拒否されます。既存の `workflow.yaml` がある場合は `migrate-workflows` で `graph.yaml` へ変換してください。これから新規に作る場合は、必ず `graphs/` 配下の新形式を使います。

## ディレクトリ構成

```
src/WorkAgents.Agents/graphs/<name>/
├── graph.yaml       # 必須: ノード・エッジの定義
└── scripts/*.csx     # kind: code のノードが参照する Roslyn C# スクリプト
```

## graph.yaml のトップレベル構造

| フィールド | 必須 | 説明 |
| --- | --- | --- |
| `version` | ○ | `1` 固定 |
| `name` | ○ | ディレクトリ名 `graphs/<name>` と一致させる |
| `displayName` / `description` | - | 表示名・説明 |
| `defaults.team` | - | `kind: team` ノードで `team` を省略した場合に使う既定チーム |
| `defaults.budget.costLimitUsd` / `timeLimitSeconds` | - | グラフ全体のコスト・時間の上限 |
| `nodes` | ○ | ノードの配列 |
| `edges` | ○ | エッジ(接続)の配列 |
| `subgraphs` | - | サブグラフ ID → `{ nodes, edges }` のマップ。主に `loop.body` から参照する |
| `layout` | - | ノード ID → `{ x, y }` の座標。省略すると接続関係から自動計算される |

## ノードの種類(`nodes[].kind`)

各ノードは `id`(必須)と `kind` を持ち、`kind` に応じて追加フィールドが必要です。

| kind | 追加フィールド | 説明 |
| --- | --- | --- |
| `agent` | `agent`(エージェント名)、`input`(テンプレート文字列) | 1つのエージェントを実行する |
| `team` | `team`(省略時は `defaults.team`)、`goal`(テンプレート文字列) | チームを実行する |
| `code` | `codeFile`(必須。`scripts/` からの相対パス) | Roslyn C# スクリプトを実行する |
| `approval` | `title`、`summary`(テンプレート)、`timeoutSeconds`(既定 900) | 人間の承認を待つ |
| `branch` | (なし) | 経路の分岐点。実際の分岐先はエッジ側の `condition` で決まる |
| `parallel` | (なし) | 出て行くエッジをすべて同時に実行する |
| `join` | `joinPolicy`(必須。`all` \| `any`)、`onPartialFailure`(`fail` \| `continue` \| `alternate`。`alternate` の場合は `alternate` に代替ノード ID が必須) | 並列実行された結果を合流させる |
| `loop` | `stop`(必須) | 条件を満たすまで繰り返す |
| `subgraph` | `graph`(参照する `graphs/<name>`) | 別のグラフを丸ごと呼び出す |

`input` / `goal` / `summary` などのテンプレート文字列では、`${mission.goal}` や `${nodes.<id>.output}` のように `${...}` でミッションの目的や他ノードの出力を参照できます。

`loop` ノードの `stop` には、次のうち少なくとも1つを指定します。

| フィールド | 説明 |
| --- | --- |
| `maxIterations` | 最大反復回数(1〜100、既定 10) |
| `costLimitUsd` / `timeLimitSeconds` | コスト・時間の上限 |
| `scoreThreshold` | 評価スコアがこの値(0〜1)に達したら終了 |

`loop` にはさらに、繰り返す本体を指定する `body`(`subgraphs` の ID)または `agent`、評価方法を指定する `evaluator`(`kind: deterministic | agent`、`node` または `agent`、`metrics[]` に `name`/`target`/`direction: gte | lte`)を指定できます。

`next`(ノード ID の配列)はエディター上の表示用ヒントで、実行エンジンの挙動には影響しません。実際の実行順序は `edges` だけで決まります。

## エッジの書き方

```yaml
edges:
  - id: start-to-route   # 省略時は "<from>-to-<to>" が自動採番される
    from: start
    to: route
    condition: "${mission.id} == 'never'"  # 真の場合だけ辿る。省略時は常に辿る
    loopBack: false        # true にすると、ループの戻りエッジであることを明示する
```

- `condition` は比較・論理式で、`${...}` により他ノードの出力などを参照できます
- 通常のグラフはループ(閉路)を含められません。閉路を作りたい場合は必ず `loopBack: true` を付けたエッジにする必要があり、それ以外の閉路は保存時に拒否されます
- どのノードからも到達できないノードや、存在しないノード・エージェント・チームを参照するエッジも保存時に拒否されます
- 画面右上の「検証」ボタン、または API の `POST /graphs/<name>/validate` で、保存前にこれらのルール違反を確認できます

## 実例: demo-graph(分岐・並列・合流・ループ)

`graphs/demo-graph/graph.yaml` は、分岐・並列実行・合流・ループを一通り含むサンプルです。

```yaml
version: 1
name: demo-graph
displayName: Demo Graph
description: A validation graph with branch, parallel, join, and explicit loop back.
nodes:
  - id: start
    kind: agent
    agent: repo-agent
    input: "${mission.goal}"
  - id: route
    kind: branch
  - id: parallel
    kind: parallel
  - id: lint
    kind: code
    codeFile: scripts/lint.csx
  - id: doc
    kind: code
    codeFile: scripts/doc.csx
  - id: join
    kind: join
    joinPolicy: all
    onPartialFailure: continue
  - id: verify
    kind: loop
    agent: repo-agent
    stop:
      maxIterations: 2
      scoreThreshold: 0.9
  - id: fallback
    kind: code
    codeFile: scripts/fallback.csx
  - id: done
    kind: code
    codeFile: scripts/done.csx
edges:
  - from: start
    to: route
  - from: route
    to: parallel
  - from: route
    to: fallback
    condition: "${mission.id} == 'never'"
  - from: parallel
    to: lint
  - from: parallel
    to: doc
  - from: lint
    to: join
  - from: doc
    to: join
  - from: join
    to: verify
  - from: verify
    to: done
  - from: fallback
    to: done
  - from: verify
    to: route
    loopBack: true
```

流れは次のとおりです。

1. `start`: `repo-agent` がミッションの目的(`${mission.goal}`)を受けて着手する
2. `route`: 分岐点。通常は `parallel` へ進むが、`${mission.id} == 'never'` が真になる特殊なケースだけ `fallback` へ逃がす
3. `parallel`: `lint` と `doc` の2つのコードノードを同時に実行する
4. `join`: 両方の結果を `all`(すべて揃うまで待つ)ポリシーで合流させ、片方が失敗しても `continue`(処理を続行)する
5. `verify`: `repo-agent` によるループ。最大2回、またはスコアが0.9に達するまで繰り返し、`route` に戻る(`loopBack: true`)ことで再検証できる構造になっている
6. `done`: 最終的なコードノードで締めくくる

## code ノードのスクリプトの書き方

`kind: code` のノードは、グラフフォルダー内の `scripts/` にある `.csx` ファイル(Roslyn のトップレベル C# スクリプト)を実行します。直前ノードまでの入力文字列は `Inputs["input"]` で受け取れます。

`graphs/demo-graph/scripts/lint.csx`:

```csharp
// lint ノードの codeFile。Inputs["input"] には直前ノードまでの入力文字列が入る。
$"lint ok: {Inputs["input"]}"
```

なお、旧形式 `workflow.yaml` の `code` ステップは `.cs` ファイルで `Inputs["<stepName>"]` から値を取り、`return new { ... }` でオブジェクトを次のステップへ渡す、少し異なる書き方をします。新規にグラフを作る場合はこの旧方式に合わせる必要はありません。

## 画面での編集・検証

Graph Studio(`/graphs/<name>`)では以下ができます。詳しい操作は[画面から定義を作る]({{ '/pages/definition-editor/' | relative_url }})を参照してください。

- 保存済みのグラフをノード・エッジとして可視化する(レイアウトは自動計算)
- ノードの追加・削除・編集、エッジの追加・削除、グラフ全体の設定変更
- 「YAML で表示」で、保存したときに書き出される内容を確認する
- 「検証」ボタンで整合性(未到達ノード・不正な閉路・存在しない参照など)を、原因と直し方つきの日本語で確認する
- 「保存」で `graph.yaml` へ書き戻す(検証を通ったときだけ書き込まれます)

画面から保存すると元の YAML のコメントは失われます。また `subgraphs` の中身は画面から編集できません。コメントで説明を残している定義や、ループ本体を持つ定義は `graph.yaml` を直接編集してください。

## 作成手順のまとめ

1. `src/WorkAgents.Agents/graphs/<name>/` ディレクトリを作成する(または画面の New definition からテンプレートで作る)
2. `graph.yaml` に `nodes` と `edges` を書く。コード処理が必要なら `scripts/*.csx` を用意する
3. 画面の「検証」または `POST /graphs/<name>/validate` で整合性を確認する
4. Web 画面のミッション作成(`/missions/new`)で `targetKind: Graph`、`targetName: <name>` を指定して実行する

## 次に読むページ

- [チームを作る]({{ '/pages/teams/' | relative_url }})でグラフの `team` ノードから参照するチームを用意する
- [ファイルと成果物]({{ '/pages/storage/' | relative_url }})でノード間の出力共有と作業ファイルの保存先を確認する
- [設定]({{ '/pages/configuration/' | relative_url }})でシークレットの扱いを確認する
