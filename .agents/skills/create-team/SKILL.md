---
name: "create-team"
description: "MeshGuild AI Studio のチーム定義 (src/WorkAgents.Agents/teams/name/team.yaml) を新規作成・編集する。複数エージェントで進める仕事を編成したい、統括エージェントとメンバーを決めたい、会話経路 (channels) やガードレール (limits) を設定したい、team.yaml のバリデーションエラーを直したい、というときに使う。工程が固定なら create-graph、働き手そのものは create-agent。"
---

# チーム定義を作る

チームは「誰と誰が、どう話してよいか」だけを決めた集団です。
進め方は固定せず、統括エージェント (orchestrator) がミッションを読んでその場で委譲します。

## チームでよいか先に判断する

- 手順を書き切れない、役割分担だけ決めて探索させたい → チーム (このスキル)
- 毎回同じ順で再現したい、承認や分岐を明示したい → グラフ (`create-graph`)
- 全体はグラフで固定し、一部だけ話し合わせたい → グラフの `team` ノードからチームを呼ぶ (両方作る)

判断がつかないなら、まずチームで書いてください。手順が固まってからグラフへ写す方が、
最初から工程図を描くより手戻りが小さくなります。

## 最初に読むもの

`schemas/team.schema.json` が形式の唯一の真実です。必ず読んでから書いてください。
このスキルには項目一覧を写していません。ここに書くのは、スキーマに表現できない判断と、ローダーが弾く条件だけです。

手本は `src/WorkAgents.Agents/teams/demo-team/team.yaml`。

## 手順

### 1. 参照するエージェントを確認する

`orchestrator.agent` と `members[].agent` は既存のエージェント名です。存在しない名前は読み込み時に例外になります。

```powershell
Get-ChildItem src/WorkAgents.Agents/agents -Directory | Select-Object Name
```

足りない役割があれば先に `create-agent` で作ります。
なお、役割名を変えたいだけなら新しいエージェントは不要です。`members[].role` がそのための項目で、
エージェント定義自体のプロンプトや動作は変えません。

### 2. team.yaml を書く

`src/WorkAgents.Agents/teams/<name>/team.yaml` に置きます。

```yaml
version: 1
name: <name>                    # フォルダー名と完全一致させる
displayName: Review Team
description: 変更内容をレビューして指摘をまとめる。
orchestrator:
  agent: orchestrator-agent
members:
  - agent: dev-agent
    role: 実装
  - agent: test-agent
    role: 検証
channels:
  default: via-orchestrator
  allow:
    - from: test-agent
      to: dev-agent
      kinds: [question, answer, share]
limits:
  maxDelegationDepth: 3
  maxParallelInstances: 4
  noProgressRoundTrips: 5
  askTimeoutSeconds: 300
```

決めるのは次の三点です。

編成
: `orchestrator` は分解と委譲と進行管理を担う 1 体。`members` は 1 件以上。同じエージェントを 2 回書けません
  (重複はエラー)。同じ役割を並列に走らせたいときは `maxInstances` を上げます。

会話経路
: 既定の `via-orchestrator` はすべて統括を経由します。`channels.allow` には、直接話させたい組み合わせだけを
  列挙してください。全員を `direct` にすると会話が発散し、`noProgressRoundTrips` で打ち切られやすくなります。
  「検証担当が実装担当へ質問する」のように、実際に往復が要る 1 本か 2 本から始めるのが確実です。

ガードレール
: 省略時は `maxDelegationDepth: 3`、`maxParallelInstances: 6`、`noProgressRoundTrips: 5`、`askTimeoutSeconds: 300`。
  既定で困らないなら書かなくて構いません。書くなら、メンバーの `maxInstances` の合計が
  `maxParallelInstances` を超えないようにします (超えると読み込み時にエラー)。

### 3. 検証して反映する

チームには専用の validate エンドポイントがありません。`FileBasedTeamLoader` が読み込み時に検証し、
違反があると `TeamValidationException` で起動が失敗します。

```powershell
dotnet build WorkAgents.sln
dotnet run --project src/WorkAgents.Host/WorkAgents.Host.csproj --launch-profile http
curl http://localhost:5160/teams/<name>
```

書く前にこれだけは自分で確かめてください。ローダーが例外にする条件です。

- `version` が 1 以外
- `name` がフォルダー名と不一致 (大文字小文字も含めた完全一致)
- `orchestrator` が無い、または `orchestrator.agent` が空
- `members` が空
- `orchestrator.agent` / `members[].agent` に未知のエージェント名
- `members` に同じエージェントが重複
- `channels.default` が `via-orchestrator` / `direct` 以外
- `channels.allow[].from` / `to` がチーム外のエージェント
- `kinds` に `question` / `answer` / `share` 以外
- `maxDelegationDepth` が 1 未満または 10 超
- メンバーの `maxInstances` 合計が `maxParallelInstances` 超過
- `evaluation.scoreThreshold` が 0.0 未満または 1.0 超
- 未知のキー (`unknown key: Property '...' not found`)。キー名のつづり間違いか、インデントがずれて別の階層に入っている

例外メッセージを日本語の原因と直し方へ対応付けた表が `src/WorkAgents.Core/Authoring/ValidationMessageCatalog.cs` の
`ForTeam` にあります。読めないメッセージが出たらそこを引いてください。

ホットリロードはありません。編集したら再ビルドと再起動が必要です。

## つまずきやすいところ

- 未知のキー。スキーマは `additionalProperties: false` なので VS Code で赤線が出ます。実行時に無視される項目もあるので、赤線を無視しないでください
- `channels.allow` を書いても `default: direct` にはなりません。既定経路と個別許可は別の設定です
- `evaluation` はループ実行などで使う既定の評価設定です。通常のチーム実行に必須ではありません
- チームは会話経路とガードレールしか決めません。「まず調査、次に実装」のような順序をチーム定義に書く場所はありません。順序を固定したいならグラフです

## 関連

- 手順の正本: `docs/adding-agents.md`
- 概念の整理: `README.md` の「基本の考え方」
- 実行の仕組み: `src/WorkAgents.Orchestration/Teams/TeamExecutor.cs`、`ConversationPolicy.cs`、`RosterManager.cs`
