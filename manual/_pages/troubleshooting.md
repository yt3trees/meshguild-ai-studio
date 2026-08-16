---
title: トラブルシューティング
description: よくあるトラブルとその対処方法を紹介します。
layout: page
---

## ビルドや起動でつまずく

初回のセットアップでアプリの画面までたどり着けない場合は、次を上から確認してください。

### `dotnet` が見つからない

`start-workagents.cmd` が `.NET SDK was not found in PATH.` で終了する場合や、`dotnet --version` がエラーになる場合は、.NET SDK が入っていないか PATH に反映されていません。
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) をインストールし、PowerShell を開き直してから `dotnet --version` を実行します。
Runtime だけをインストールしてもビルドできないため、SDK を選んでください。

### `dotnet restore` が失敗する

NuGet パッケージを取得できていません。
ネットワークとプロキシの設定を確認し、社内のパッケージソースを使っている場合は `dotnet nuget list source` で参照先を確認します。
一時的な失敗であれば、`dotnet restore WorkAgents.sln` をもう一度実行すると解消することがあります。

### ビルドは通るのに画面が表示されない

`start-workagents.cmd` は `dotnet watch run` で起動するため、初回はビルドが終わるまで数分かかることがあります。
ブラウザーがエラーになった場合も、Host と Web のコマンドウィンドウにビルドエラーが出ていないことを確認してから、ページを再読み込みしてください。
コマンドウィンドウにエラーが出ている場合は、先に `dotnet build WorkAgents.sln` を実行して内容を確認します。

## Web が起動しない

`start-workagents.cmd` の実行ログを確認し、ポートが他のプロセスに使用されていないか確認してください。
ポート競合の確認と変更手順は[よくある質問]({{ '/pages/faq/' | relative_url }})の「ポートが競合しています」を参照してください。

## モデル未登録でミッションを開始できない

ミッション開始時に `model_not_configured`、または「No LLM model is configured」と表示される場合は、対象チームのオーケストレーターへ使えるモデルがありません。
Web の `/models` でモデルを登録し、既定モデルに設定してから Host と Web を再起動し、もう一度ミッションを開始してください。
登録済みなのに同じエラーになる場合は、モデルを保存した SQLite と実行時の `Runs:DatabasePath` が一致しているかを確認します。

## appsettings.Development.json がありません

このファイルがない場合も、`appsettings.json` の既定値で起動できることがあります。
ただし、ローカルで使うつもりだった `Workspace:Root`、`Artifacts:Root`、`Runs:DatabasePath`、Host の接続先などが反映されません。
Web と Host の両方へ `appsettings.example.json` をコピーし、同じ SQLite とワークスペースを指定してから再起動してください。

## 承認画面に何も表示されない

対象の Run が実際に承認待ち状態(Shell を含む危険度の高いツール実行の直前)であるかを、Run 履歴から確認してください。

## エージェント/チーム/グラフの定義が読み込まれない・エラーになる

以下を順に確認してください。

- ディレクトリ名と `agent.yaml` / `team.yaml` / `graph.yaml` 内の `name` が一致しているか
- YAML のインデントや、スキーマにない未知のキーが混入していないか(`agent.yaml` などは `additionalProperties: false` のため、タイプミスはエラーになります)
- `team.yaml` や `graph.yaml` が参照しているエージェント名・チーム名・グラフ名が実在するか
- グラフの場合は、閉路が `loopBack: true` を付けたエッジ以外に存在しないか、到達不能なノードがないかを画面の「検証」または `POST /graphs/<name>/validate` で確認する

## 実行が承認待ちのまま進まず、承認しても反応がない

Web と Host が同じ SQLite データベース(`Runs:DatabasePath`)を参照しているか、両方の `appsettings.Development.json` を確認してください。パスが食い違っていると、Host が処理した承認が Web の画面に反映されません。

## ミッションがずっと Running のまま、または収束せずに終わる

Team Room の左側でエージェントが `Thinking`、`AwaitingReply`、`AwaitingApproval` のどれになっているかを確認します。
`AwaitingApproval` なら `/approvals` を開き、`AwaitingReply` なら会話の最後の質問へ Team Room から指示を送ります。
`Not converged` で終わった場合は、`Replay & Audit` の結果、Team Room の停止条件、Loop Console の評価スコアと停止理由を順に確認します。
上限を増やすだけでは同じ委譲や反復を繰り返す問題は直らないため、目標、評価条件、オーケストレーターの指示を見直してください。

## エージェントが書いたファイルが表示されない

ワークスペースの作業ファイルと成果物は保存先も表示場所も異なります。
Mission の共有ファイルは `Workspace:Root\missions\<missionId>\work\`、成果物は `Artifacts:Root` 配下です。
ワークスペースへ書いたファイルは自動で成果物にならないため、成果物一覧やダウンロード欄に出ないことがあります。
まず Team Room の「共有ファイル」と「成果物」を分けて確認し、保存先の詳しいルールは[ファイルと成果物]({{ '/pages/storage/' | relative_url }})を参照してください。

## WorkAgents.Tray でエージェント/チーム/グラフを追加したのに一覧に出てこない

配布物(`WorkAgents.Tray`)は起動時に一度だけ定義を読み込むため、ファイルを追加しただけでは反映されません。トレイメニューから「更新」を選んで Host/Web を再起動してください。
配布後に定義を追加する場合は `dist\definitions\agents`、Skillを追加する場合は `dist\definitions\skills` を編集します。Host/Webそれぞれの実行フォルダへコピーする必要はありません。

## `publish-workagents.cmd` を使わず自分で `dotnet publish` した場合、Host が起動しない/Webの設定がおかしい

Host と Web の `appsettings.json` は内容が異なります(`Orchestration:Engine:Enabled` が Host=`true`・Web=`false` など)。両方を同じ出力フォルダへ `dotnet publish` すると、後からpublishした側の `appsettings.json` が上書きしてしまいます。`publish-workagents.cmd` は Host/Web/Tray をそれぞれ別サブフォルダ(`dist\WorkAgents.Host\` 等)へpublishすることでこれを避けているので、独自にpublishする場合も同様に出力先フォルダを分けてください。
また、定義ファイルをHost/Webの出力フォルダへ個別にコピーしてはいけません。配布用スクリプトが生成する `dist\definitions\` を両プロセスが共有します。
