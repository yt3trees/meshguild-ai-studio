---
title: API から実行する
description: Host の HTTP API からミッションや単独 Run を登録し、状態と成果物を取得する方法を説明します。
layout: page
---

画面を使わずに実行する場合は、Host の HTTP API を呼び出します。
既定の Host は `http://localhost:5160` で待ち受けます。
現行の API は Local 利用を前提として認証を持たないため、ループバック以外のネットワークへ公開しないでください。

## ミッションを登録する

チームまたはグラフを対象にする場合は `POST /missions` を使います。

```powershell
$body = @{
  goal = "短い作業報告を作り、確認結果を成果物にまとめる"
  targetKind = "Team"
  targetName = "demo-team"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5160/missions `
  -ContentType "application/json" `
  -Body $body
```

`targetKind` は `Team` または `Graph`、`targetName` は読み込まれている定義名です。
予算を指定する場合は `budget` に `costLimitUsd`、`timeLimitSeconds`、`maxIterations`、`maxConcurrentAgents` を追加します。

成功すると `201 Created` が返り、レスポンスに `missionId`、`status`、キュー待ちの理由、キュー位置が含まれます。

## ミッションの状態を読む

```powershell
Invoke-RestMethod http://localhost:5160/missions/<missionId>
```

一覧を条件付きで取得する場合は `GET /missions` に `status`、`outcome`、`team`、`limit`、`offset` などのクエリを付けます。
会話、参加エージェント、グラフのノード実行、コスト、成果物はミッション単位のエンドポイントから取得できます。

| 目的 | エンドポイント |
| --- | --- |
| 会話を読む | `GET /missions/{missionId}/messages` |
| エージェントの状態を読む | `GET /missions/{missionId}/agents` |
| グラフのノードとエッジの実行を読む | `GET /missions/{missionId}/graph` |
| コストレポートを読む | `GET /missions/{missionId}/costs` |
| 成果物の一覧を読む | `GET /missions/{missionId}/artifacts` |
| 成果物をダウンロードする | `GET /missions/{missionId}/artifacts/{artifactId}/content` |
| 共有ワークスペースを読む | `GET /missions/{missionId}/workspace/files` |

## エージェントを単独 Run で実行する

チームやグラフを使わず、1体のエージェントへ直接メッセージを渡す場合は `POST /runs` を使います。

```powershell
$body = @{
  agentName = "repo-agent"
  userMessage = "作業ディレクトリの状態を確認し、結果を短く報告してください"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5160/runs `
  -ContentType "application/json" `
  -Body $body
```

`agentName` と `userMessage` は必須です。
会話を続ける場合は同じ `threadId` を指定します。
成功すると `202 Accepted` と `runId`、状態、スレッド ID が返ります。

```powershell
Invoke-RestMethod http://localhost:5160/runs/<runId>
```

実行中の Run を取り消す場合は `POST /runs/{runId}/cancel` を呼び出します。
終了済みの Run は取り消せません。

## 承認を API から確認する

承認要求の一覧は `GET /approvals` で取得できます。
承認または却下は次の形式で行います。

```powershell
$body = @{
  status = "Approved"
  decidedBy = "local-user"
  reason = "内容を確認した"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5160/approvals/<approvalId>/decide `
  -ContentType "application/json" `
  -Body $body
```

承認は危険な操作の安全性を保証するものではありません。
操作の対象と引数を確認し、実行後も結果を Team Room や Run 状態で確認してください。

## ミッションへ人間の指示を送る

実行中の Team ミッションへ追加の指示を送る場合は `POST /missions/{missionId}/messages` を使います。
終端状態の Team ミッションへ送ると、介入メッセージをきっかけに再始動することがあります。

```powershell
$body = @{ body = "報告書の結論を先に確認してから続けてください" } | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5160/missions/<missionId>/messages `
  -ContentType "application/json" `
  -Body $body
```

## エラーの読み方

- `model_not_configured`：対象チームのオーケストレーターに使えるモデルがありません。[設定]({{ '/pages/configuration/' | relative_url }})で登録します。
- `unknown_target`：`targetName` のチームまたはグラフが読み込まれていません。
- `graph_invalid`：グラフの参照、閉路、到達性などの検証に失敗しています。[グラフを作る]({{ '/pages/graphs/' | relative_url }})で検証します。
- `404`：指定したミッション、Run、承認、成果物が存在しないか、利用できません。

API を使う場合も、ワークスペースと成果物の区別は[ファイルと成果物]({{ '/pages/storage/' | relative_url }})の説明に従います。
