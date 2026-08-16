---
title: 概要
description: MeshGuild AI Studio の主な機能を紹介します。
layout: page
---

MeshGuild AI Studio は、目標をミッションとして受け取り、エージェントの作業を観測しながら成果物へつなげます。
用語同士の関係を先に確認したい場合は、[概念を絵で理解する]({{ '/pages/concepts/' | relative_url }})と[用語集]({{ '/pages/glossary/' | relative_url }})を参照してください。

## 全体の構成要素

```mermaid
flowchart TD
    mission["ミッション<br/>達成したい目標"]
    choice{"実行方法を選ぶ"}
    agent["エージェント<br/>1体の役割"]
    team["チーム<br/>オーケストレーターとメンバー"]
    graph["グラフ<br/>ノードとエッジの手順"]
    run["Run<br/>1回の実行"]
    workspace["ワークスペース<br/>作業中のファイル"]
    artifact["成果物<br/>残すファイル"]

    mission --> choice
    choice -->|"単一の作業"| agent
    choice -->|"相談・委譲"| team
    choice -->|"手順・分岐・承認"| graph
    agent --> run
    team --> run
    graph --> run
    run --> workspace
    workspace --> artifact
```

エージェント・チーム・グラフはいずれも `src/WorkAgents.Agents/` 配下のディレクトリにファイルとして置かれ、名前(ディレクトリ名)で相互に参照し合います。

## どれを使うか

作業の進め方が決まっているか、エージェント同士の相談が必要かで選びます。

| やりたいこと | 使うもの | 理由 |
| --- | --- | --- |
| 単一の作業を1体に任せる | [エージェント]({{ '/pages/agents/' | relative_url }}) | 1つの役割と権限を持つ担当者として実行する |
| 役割分担して相談させたい。手順は実行中に決めたい | [チーム]({{ '/pages/teams/' | relative_url }}) | オーケストレーターが状況に応じてメンバーへ委譲する |
| 手順・分岐・承認位置が決まっている | [グラフ]({{ '/pages/graphs/' | relative_url }}) | ノードとエッジで決めた順序を再現する |

## エージェント

`agents/<name>/agent.yaml` と `instructions.md` によって、エージェント1体の振る舞いをファイルベースで定義します。`agent.yaml` にはシェル実行の可否やファイルストアの種類などの権限(harness)を、`instructions.md` には具体的な行動指針を記述します。詳しくは[エージェントを作る]({{ '/pages/agents/' | relative_url }})を参照してください。

## チーム

`teams/<name>/team.yaml` によって、指示役となるオーケストレーターエージェントと、複数のメンバーエージェントの組み合わせを定義します。メンバー間で直接会話させるか、必ずオーケストレーター経由にするかもここで制御します。詳しくは[チームを作る]({{ '/pages/teams/' | relative_url }})を参照してください。

## グラフ(ワークフロー)

`graphs/<name>/graph.yaml` によって、複数のノード(エージェント/チーム/コード/承認/分岐/並列/合流/ループ/サブグラフ)とエッジ(接続と条件)を組み合わせた手順を定義します。旧形式の `workflows/<name>/workflow.yaml` は非推奨で、実行時には `graph.yaml` への移行(`migrate-workflows`)が必要です。詳しくは[グラフを作る]({{ '/pages/graphs/' | relative_url }})を参照してください。

## 定義エディタ

チームとグラフは、YAML を直接書くほかに Web 画面のフォームからも作成・編集できます。用途別の雛形から始められ、エージェント名やノード ID といった参照先はすべて選択式で、保存前の検証結果は原因と直し方つきの日本語で返ります。編集中の定義が結局どう動くのかを日本語で読み返せる説明も常に表示されます。
画面や YAML の直接編集のほかに、同梱の Agent Skill(`create-agent`、`create-team`、`create-graph`、`graph-design`)を使って、Claude Code のようなコーディングエージェントに定義を作らせることもできます。詳しくは[画面から定義を作る]({{ '/pages/definition-editor/' | relative_url }})を参照してください。

## 承認フロー

Shell 実行(`run_shell`)など危険度の高いツール呼び出しは、ツール登録側で承認必須(`Approval: "required"`)として扱われており、実行前に必ず一時停止して `/approvals` 画面で人間が承認または却下します。加えて、エージェントの `workspace.yaml` に設定した `denyList`(正規表現)に一致するコマンドは、承認を待つ以前にそもそも実行が拒否されます。グラフやワークフローに明示的な承認ノード(`kind: approval` / `kind: approve`)を組み込めば、ツールの危険度に関わらず任意の地点で人間の判断を挟むこともできます。

## Team ミッションの再始動

完了済み(成功・収束せず・失敗・中止などの終端状態)の Team ミッションに、人からメッセージを送ると実行を再始動できます。チームの作業結果を確認したうえで「ここを直して続けてほしい」といった追加の指示を伝え、同じチーム構成で続きから再実行させる用途を想定しています。

- 対象は終端状態の Team ミッション(`targetKind: Team`)だけです。グラフミッションや、実行中・停止中のミッションは再始動の対象ではありません
- 再始動のきっかけは、ミッションへの投稿(`POST /missions/{missionId}/messages`)で、人の介入メッセージが届くと Running へ戻って Team 実行が再開されます
- これは「停止状態から再開する」のではなく、「完了済みのミッションを人の介入で再始動する」機能です。詳細は[チームを作る]({{ '/pages/teams/' | relative_url }})を参照してください

## Run 履歴

実行された Run(エージェント単独実行)やミッション(チーム/グラフ実行)の状態遷移や結果は履歴として記録され、Web 画面から後から確認できます。承認待ちの Run もここから状態を追跡できます。

## 次に読むページ

- [概念を絵で理解する]({{ '/pages/concepts/' | relative_url }})で、1つのミッションが成果物になるまでの流れを見る
- [インストールと起動]({{ '/pages/getting-started/' | relative_url }})でローカル環境を準備する
- [はじめてのミッション(15分)]({{ '/pages/first-mission/' | relative_url }})で画面を使って一通り実行する
