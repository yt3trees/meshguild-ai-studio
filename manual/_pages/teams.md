---
title: チームを作る
description: 複数のエージェントを束ねるチームの定義方法を紹介します。
layout: page
---

チームは、指示役となるオーケストレーターエージェントと、複数のメンバーエージェントを組み合わせた構成です。ミッションの目的(goal)はまずオーケストレーターに渡され、オーケストレーターがメンバーへ作業を割り振ります。定義の実体は `src/WorkAgents.Agents/teams/<name>/team.yaml` で、このページではその書き方を説明します。

YAML を直接書かずに済ませたい場合は、Web 画面のフォームからも作成・編集できます([画面から定義を作る]({{ '/pages/definition-editor/' | relative_url }}))。書き出される形式はどちらも同じです。

## 前提

チームを作る前に、参照するエージェントがすべて `agents/<name>/` に存在している必要があります([エージェントを作る]({{ '/pages/agents/' | relative_url }})を参照)。存在しないエージェント名を参照すると、読み込み時にエラーになります。

## team.yaml の書き方

| フィールド | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `version` | number | ○ | 現時点では `1` 固定 |
| `name` | string | ○ | ディレクトリ名 `teams/<name>` と一致させる |
| `displayName` / `description` | string | - | 表示名・説明 |
| `orchestrator.agent` | string | ○ | 指示役として使うエージェント名 |
| `orchestrator.maxInstances` | number | - | オーケストレーターの最大同時実行数(既定 1) |
| `members[].agent` | string | ○ | メンバーとして使うエージェント名 |
| `members[].role` | string | - | 表示用のラベル。エージェント自体の振る舞いは変えない |
| `members[].scope` | string | - | メンバーの担当範囲を示すメタ情報 |
| `members[].maxInstances` | number | - | 同時実行数(既定 1、`limits.maxParallelInstances` によっても上限が掛かる) |
| `channels.default` | `via-orchestrator` \| `direct` | - | メンバー間の会話を常にオーケストレーター経由にするか、直接可能にするか(既定 `via-orchestrator`) |
| `channels.allow[]` | object[] | - | `{ from, to, kinds: [question, answer, share] }` の形式で、特定のメンバー間だけ直接会話を許可する |
| `limits.maxDelegationDepth` | number | - | 委譲の最大深さ(1〜10、既定 3) |
| `limits.maxParallelInstances` | number | - | チーム全体での並列実行数の上限(既定 6) |
| `limits.noProgressRoundTrips` | number | - | 進捗のない往復がこの回数続くと中断(既定 5) |
| `limits.askTimeoutSeconds` | number | - | メンバーへの質問のタイムアウト秒数(既定 300) |
| `evaluation.evaluator` | string | - | 評価に使うエージェント名 |
| `evaluation.scoreThreshold` | number | - | 合格とみなすスコアの閾値(0〜1) |

`team.yaml` も `additionalProperties: false` で検証されるため、未知のキーや存在しないエージェント名、メンバーの重複、上限を超えた設定は読み込み時にエラーとして拒否されます。

## 実例: demo-team

`teams/demo-team/team.yaml` は、仕様調査・実装・テストの3人チームをオーケストレーターがまとめる構成です。

```yaml
version: 1
name: demo-team
displayName: Demo Delivery Team
description: A small team used by the orchestration quickstart.
orchestrator:
  agent: orchestrator-agent
members:
  - agent: spec-research-agent
    role: specification research
    maxInstances: 1
  - agent: dev-agent
    role: development
    maxInstances: 1
  - agent: test-agent
    role: testing
    maxInstances: 1
channels:
  default: via-orchestrator
  allow:
    - from: dev-agent
      to: spec-research-agent
      kinds: [question, answer, share]
    - from: test-agent
      to: dev-agent
      kinds: [question, answer, share]
limits:
  maxDelegationDepth: 3
  maxParallelInstances: 4
  noProgressRoundTrips: 5
  askTimeoutSeconds: 300
```

この設定では、通常はすべての会話がオーケストレーター(`orchestrator-agent`)を経由しますが、`channels.allow` により「dev-agent から spec-research-agent への質問」「test-agent から dev-agent への質問」だけは直接やり取りできるようにしています。

## 作成手順のまとめ

1. チームで使うエージェントをすべて用意する(未作成なら先に[エージェントを作る]({{ '/pages/agents/' | relative_url }}))
2. `src/WorkAgents.Agents/teams/<name>/team.yaml` を作成し、`orchestrator` と `members` を最低限記述する
3. 必要に応じて `channels.allow` でメンバー間の直接会話を許可し、`limits` で暴走(無限委譲・無限往復)を防ぐ上限を調整する
4. Web 画面のミッション作成(`/missions/new`)で `targetKind: Team`、`targetName: <name>` を指定して実行し、Run 履歴や Team Room で挙動を確認する

## 次に読むページ

- [グラフを作る]({{ '/pages/graphs/' | relative_url }})で、チームを1ノードとして組み込んだより大きな手順を組む
- [ファイルと成果物]({{ '/pages/storage/' | relative_url }})で、チーム内の会話共有と作業ファイルの範囲を確認する
