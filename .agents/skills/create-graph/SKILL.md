---
name: "create-graph"
description: "MeshGuild AI Studio のグラフ定義 (src/WorkAgents.Agents/graphs/name/graph.yaml) を新規作成・編集する。工程をノードとエッジで固定したい、分岐・並列・合流・ループ・承認を含む手順を書きたい、code ノードのスクリプトを追加したい、POST /graphs/name/validate のエラー (undeclared_cycle, unreachable_node, unresolved_reference など) を直したい、というときに使う。手順を固定しない編成は create-team、働き手そのものは create-agent。"
---

# グラフ定義を作る

グラフは、工程をノードとエッジで描いた実行手順書です。毎回同じ順で進めたい仕事、分岐や並列や承認を明示したい仕事に使います。

## グラフでよいか先に判断する

- 毎回同じ順で再現したい、承認や分岐を明示したい → グラフ (このスキル)
- 手順を書き切れない、役割分担だけ決めて探索させたい → チーム (`create-team`)
- 全体はグラフで固定し、一部だけ話し合わせたい → `kind: team` ノードからチームを呼ぶ

## 最初に読むもの

`schemas/graph.schema.json` が形式の唯一の真実です。必ず読んでから書いてください。
このスキルには項目一覧を写していません。ここに書くのは、スキーマに表現できない判断と、バリデーターが弾く条件だけです。

手本は `src/WorkAgents.Agents/graphs/demo-graph/graph.yaml`。branch / parallel / join / loop / loopBack を一通り含んでいます。
バリデーションエラーの対処は `references/validation-errors.md` を参照してください。

## 手順

### 1. 工程を日本語で並べてから YAML にする

いきなり YAML を書かないでください。先に「何を、どの順で、どこで分かれ、どこで人が承認するか」を箇条書きにし、
それからノードへ落とします。ノードの種類は 9 つです。

| kind | 使うとき | 必須項目 |
|---|---|---|
| `agent` | 1 エージェントに作業させる | `agent` |
| `team` | チームをまるごと 1 工程として呼ぶ | `team` (または `defaults.team`) |
| `code` | スクリプトを実行する | `codeFile` |
| `approval` | 人の承認を待つ | (`title` 推奨) |
| `branch` | 条件で経路を選ぶ | 条件なしエッジが 1 本必要 |
| `parallel` | 複数経路へ同時に流す | - |
| `join` | 並列経路を合流させる | `joinPolicy` |
| `loop` | 条件を満たすまで繰り返す | `stop` (1 つ以上) |
| `subgraph` | 別のグラフを入れ子にする | `graph` |

副作用のある処理 (デプロイ、外部への書き込み、`git push`) は `approval` ノードとセットで設計してください。
`code` ノードは Host プロセスの権限で動きます。

### 2. graph.yaml を書く

`src/WorkAgents.Agents/graphs/<name>/graph.yaml` に置きます。

```yaml
version: 1
name: <name>                    # フォルダー名と完全一致させる
displayName: Release Check
description: 調査してから実装し、承認を経てリリースする。

nodes:
  - id: research
    kind: agent
    agent: spec-research-agent
    input: "${mission.goal}"
  - id: gate
    kind: approval
    title: 本番反映の承認
    summary: "${nodes.research.output}"
  - id: apply
    kind: code
    codeFile: scripts/apply.csx

edges:
  - from: research
    to: gate
  - from: gate
    to: apply
```

書かなくてよいものが 3 つあります。手で埋めると事故のもとなので省略してください。

- `edges[].id`: 省略すると `<from>-to-<to>` で自動採番されます
- `nodes[].next`: 表示専用で、どのエンジンからも参照されません
- `layout`: 省略するとエッジの向きから自動で段組みされます。座標を固定したいノードだけ書けます

### 3. 参照と条件を書く

`${...}` で使える参照はバリデーターが許すものだけです。それ以外は `unresolved_reference` になります。

- `${mission.goal}` / `${mission.id}`
- `${nodes.<存在するノード id>....}` (例: `${nodes.research.output}`)
- ループ内: `${loop.iteration}` / `${loop.previous.output}` / `${loop.previous.score}`

参照が検証される場所は `nodes[].input`、`nodes[].goal`、`nodes[].summary`、`edges[].condition` です。

`condition` に書けるのは比較と論理演算だけです。関数呼び出しや算術演算は `invalid_condition` になります。

```yaml
condition: "${nodes.check.output} == 'ok'"
condition: "${loop.iteration} >= 3 && ${nodes.check.output} != 'ng'"
```

使えるトークンは `${...}` 参照、`true` / `false`、数値、シングル/ダブルクォートの文字列、
`==` `!=` `<=` `>=` `<` `>` `&&` `||` `!` `(` `)` だけです。

### 4. 制御ノードの規則を守る

branch
: 出るエッジのうち 1 本は条件なし (既定経路) にします。全部に条件を付けると `missing_default_branch` です。

parallel と join
: `parallel` から複数のエッジを出し、各経路の末尾から同じ `join` へ入れます。`joinPolicy` は
  `all` (全分岐を待つ) か `any` (最初の 1 本で進む)。`onPartialFailure` は `fail` / `continue` / `alternate` で、
  `alternate` を選んだら遷移先ノード ID を `alternate` に書きます。

loop
: `stop` に `maxIterations` (1 から 100) / `costLimitUsd` / `timeLimitSeconds` / `scoreThreshold` (0.0 から 1.0) の
  いずれか 1 つ以上が必要です。繰り返しの経路は `loopBack: true` を付けたエッジで明示します。
  これを忘れると `undeclared_cycle` で保存を拒否されます。逆に、後退エッジ以外に `loopBack` を付けてはいけません
  (到達判定から外れて `unreachable_node` を誘発します)。

### 5. code ノードのスクリプトを置く

`codeFile` はグラフフォルダーからの相対パスです。慣例として `scripts/` 配下に置きます。

拡張子は `.csx` にしてください。`graphs/**/*.cs` は `WorkAgents.Agents.csproj` の Content から除外されており、
ビルド出力へコピーされません。`.cs` で置くと実行時にスクリプトが見つかりません。

中身はトップレベル文と `return` の C# Script 構文です。Roslyn scripting で評価されるため、
拡張子に依存したコンパイルは行われません。手本は `graphs/demo-graph/scripts/`。

### 6. 検証する

保存前に検証できます。Host を起動しておき、YAML 本文を投げます。

```powershell
dotnet run --project src/WorkAgents.Host/WorkAgents.Host.csproj --launch-profile http
```

```powershell
$yaml = Get-Content src/WorkAgents.Agents/graphs/<name>/graph.yaml -Raw
$body = @{ yaml = $yaml } | ConvertTo-Json
Invoke-RestMethod -Uri http://localhost:5160/graphs/<name>/validate -Method Post -Body $body -ContentType 'application/json'
```

グラフがまだディスクに無くても検証できます (本文の YAML を検証するため)。
`valid: true` か、`errors[]` に `code` / `message` / `nodeIds` / `edgeIds` が返ります。
コードごとの原因と直し方は `references/validation-errors.md` にまとめてあります。

このエンドポイントは検証しないものが 1 つあります。Host に登録されている `GraphValidator` は
既知の定義名を渡されていないため、`agent` / `team` / `graph` の名前の誤りを検出しません。
参照先が実在することは自分で確認してください。

```powershell
Get-ChildItem src/WorkAgents.Agents/agents, src/WorkAgents.Agents/teams, src/WorkAgents.Agents/graphs -Directory | Select-Object Name
```

### 7. 反映する

```powershell
dotnet build WorkAgents.sln
```

ホットリロードはありません。定義はビルド時に出力へコピーされるので、編集したら再ビルドと再起動が必要です。

`PUT /graphs/<name>` でも保存できますが、書き込み先は実行中プロセスの定義ルート (開発時はビルド出力) です。
ソースツリーの `src/WorkAgents.Agents/graphs/` は更新されないため、次のビルドで上書きされて消えます。
ソースを直接編集する方が確実です。

## つまずきやすいところ

- `name` とフォルダー名の不一致。`name_mismatch` で即エラーになります
- 未知のキー。スキーマは `additionalProperties: false` なので VS Code で赤線が出ます
- 到達判定は `loopBack: false` のエッジだけを辿ります。入次数 0 のノードが起点になるため、起点が複数あっても構いませんが、どこからも辿れないノードは `unreachable_node` です
- `subgraph` で自分自身を直接・間接に呼ぶと `subgraph_recursion` です
- 旧 `workflow.yaml` は新規に書かないでください。既存のものは `migrate-workflows` で `graph.yaml` へ変換します

## 関連

- 手順の正本: `docs/adding-agents.md`
- 概念の整理: `README.md` の「基本の考え方」
- 検証ロジックの実体: `src/WorkAgents.Orchestration/Graph/GraphValidator.cs`
- 実行の仕組み: `src/WorkAgents.Orchestration/Graph/GraphExecutor.cs`、`Graph/NodeHandlers/`
