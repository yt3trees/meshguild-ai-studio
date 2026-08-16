---
title: よくある質問
description: MeshGuild AI Studio に関するよくある質問をまとめています。
layout: page
---

## エージェントが承認待ちのまま止まっているのはなぜですか

Shell を含む危険度の高いツール実行は、実行前に一時停止し `/approvals` 画面での人間による承認を待つ設計になっています。画面から承認または却下してください。

## モデルを登録せずに実行すると何が起きますか

ミッション作成時にチームのオーケストレーターへ使えるモデルがないと、Host は `model_not_configured` を返して実行を受け付けません。
単独 Run では、実行開始後に「No LLM model is configured」というエラーになります。
Web の `/models` で Provider、endpoint、モデル名を登録し、「Use as default model」を選んでから、ミッションをもう一度開始してください。

## appsettings.Development.json を作り忘れるとどうなりますか

アプリは `appsettings.json` の既定値で起動できる場合がありますが、ローカル用に上書きするつもりだったパスや Host URL は反映されません。
その結果、意図していない `C:\work-agents\state\work-agents.db` に履歴が保存されたり、別のワークスペースを見たりします。
Web と Host の両方で `appsettings.example.json` をコピーし、`Runs:DatabasePath` と `Workspace:Root` を確認してから再起動してください。

## 機密情報はどこに保存すればよいですか

API キーやアクセストークンなどの機密情報は、`agent.yaml` やコード、`.env` へ書き込まず、Local secret store にのみ保存してください。

## ローカル環境以外で使えますか

現行の実装は Windows 上の Local プロファイルを前提としており、Web と Host の API に認証機能がないことが既知の制約です。信頼できないネットワークや本番環境へ、追加の外部境界なしに公開しないでください。

## 画面からエージェントやチームを作成できますか

`New definition` 画面から、エージェント、チーム、グラフを作成できます。
チームとグラフはテンプレートや複製から始められ、エージェントは指示文と権限を編集画面で設定します。
高度な設定、コメントを残した YAML、グラフのサブグラフはファイルを直接編集してください。
詳しくは[画面から定義を作る]({{ '/pages/definition-editor/' | relative_url }})、[エージェントを作る]({{ '/pages/agents/' | relative_url }})、[チームを作る]({{ '/pages/teams/' | relative_url }})、[グラフを作る]({{ '/pages/graphs/' | relative_url }})を参照してください。

## workflow.yaml と graph.yaml はどちらを使えばよいですか

新規に作る場合は必ず `graphs/<name>/graph.yaml`(新形式)を使ってください。`workflows/<name>/workflow.yaml` は非推奨の旧形式で、未移行のまま実行しようとすると拒否されます。既存の `workflow.yaml` がある場合は `migrate-workflows` で `graph.yaml` へ変換してください。

## ミッションがずっと Running のままです。どこを読めばよいですか

まず Team Room の右側にある状態と、左側のエージェント状態を見ます。
`Awaiting approval` なら `/approvals`、質問への回答待ちなら会話の最後のメッセージ、`Paused` なら再開操作を確認します。
それ以外は Host のコンソールログと Run/Mission の最新状態を確認し、同じ SQLite を Web と Host が参照しているかを確認してください。

## ミッションが収束せずに終わりました。失敗ですか

`Not converged` は、設定した反復回数、時間、コストなどの上限へ先に到達して終了した状態です。
Web の `Replay & Audit` で結果と停止理由を絞り込み、Team Room の停止条件と Loop Console の評価スコアを確認します。
上限を増やす前に、停止条件、評価エージェント、オーケストレーターの指示が同じ問題を繰り返していないかを見直してください。

## ポートが競合しています。どこを変更しますか

使用中のポートは PowerShell で確認できます。

```powershell
Get-NetTCPConnection -LocalPort 5049,5160 -ErrorAction SilentlyContinue
```

別のポートで直接起動する場合は、Host を `5161`、Web を `5050` として次のように実行します。

```powershell
dotnet run --project src\WorkAgents.Host\WorkAgents.Host.csproj --no-launch-profile --urls http://localhost:5161
dotnet run --project src\WorkAgents.Web\WorkAgents.Web.csproj --no-launch-profile --urls http://localhost:5050
```

この場合は `src/WorkAgents.Web/appsettings.Development.json` の `Orchestration:HostBaseUrl` も `http://localhost:5161` に変更し、`http://localhost:5050` を開きます。
`start-workagents.cmd` を使い続ける場合は、ファイル内の Host/Web の `--urls` と最後に開く URL も同じポートへ変更してください。

## エージェントがファイルを書いたはずなのに見つかりません

作業中のファイルはワークスペース、実行後に残すファイルは成果物です。
ワークスペースへ書いただけでは成果物へ自動登録されないため、Team Room の「成果物」欄や `Artifacts:Root` に表示されないことがあります。
Mission の共有ファイルは `Workspace:Root\missions\<missionId>\work\`、成果物の本体は `Artifacts:Root` 配下を確認してください。
保存先の違いと保持期限は[ファイルと成果物]({{ '/pages/storage/' | relative_url }})を参照してください。
