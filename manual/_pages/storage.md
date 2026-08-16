---
title: ファイルと成果物
description: AIの作業ファイル、エージェント間の共有、ミッション成果物の保存先を説明します。
layout: page
---

AIの実行中に扱うファイルは、リポジトリにある定義ファイル、実行用のワークスペース、ミッションの成果物に分かれます。
これらは保存場所と共有単位が異なるため、`src/WorkAgents.Agents/` 配下の定義ファイルと、実行時に作られる `C:\work-agents\` 配下のファイルを混同しないでください。

## まず押さえる区別

| 種類 | 主な保存先 | 用途 | 共有単位 |
| --- | --- | --- | --- |
| 定義ファイル | `src/WorkAgents.Agents/agents/`、`src/WorkAgents.Agents/teams/`、`src/WorkAgents.Agents/graphs/` | エージェント、チーム、グラフの設定 | すべての実行から参照されるリポジトリのファイル |
| ワークスペース | `Workspace:Root` 配下 | clone、ソースコードの編集、テスト、一時ファイル | 単独RunはRun単位、チーム/グラフMissionはMission単位 |
| 成果物 | `Artifacts:Root` 配下と SQLite のメタデータ | 実行後も残すファイルと、その要約やハッシュ | `MissionId` に紐づく成果物 |

`harness.fileStore: workspace` のエージェントが作業ファイルを扱います。
`harness.fileStore: artifacts` または `fileStore` を省略した成果物専用エージェント（`harness.shell: false`）には、通常の構成ではワークスペース用のファイル操作ツールは付きません。

作業ファイルをワークスペースに書いただけでは、成果物として登録されたり、別のミッションから見えるようになったりしません。

## ワークスペースの保存先

既定のワークスペースルートは次のパスです。

```text
C:\work-agents\runs
```

これは `ProfileOptions.WorkspaceRoot` の既定値で、設定キー `Workspace:Root` によって上書きできます。
`workspace.yaml` の `fileStore.root` を設定した場合は、その値がエージェントのワークスペースルートとして優先されます。

単独Runでは、作業ディレクトリの決定順は次のとおりです。

1. 実行から明示的に渡された `WorkingDirectory`
2. `workspace.yaml` の `fileStore.root`
3. `Workspace:Root` または `ProfileOptions.WorkspaceRoot`

チームまたはグラフのMissionでは、実行開始時に次のMission共有ワークスペースが自動的に用意されます。

```text
{WorkspaceRoot}\missions\{missionId}\work\
```

このMission共有パスは統括エージェント、メンバー、グラフのエージェントノード、ループ反復、グラフ内Teamへ同じように渡されます。
Mission共有を使う場合は、`workspace.yaml` の個別 `fileStore.root` よりMissionの共有パスが優先されます。ファイル操作やShell操作の権限自体はエージェント定義の設定を維持します。

### 単独 Run の場合

`POST /runs` などの通常の Run 実行では、Run ID ごとに1つのディレクトリを作ります。

```text
{WorkspaceRoot}\{runId}\
```

このディレクトリは Run の開始時に `Directory.CreateDirectory` で作成されます。
既存ディレクトリを再利用する処理ではありません。

### 明示的な作業ディレクトリがない場合

エージェント呼び出しに `WorkingDirectory` が渡されない場合は、エージェント名とランダムな GUID を使ったディレクトリになります。

```text
{root}\{agentName}\{Guid:N}\
```

したがって、単独 Run の `runId` 方式と、フォールバックの GUID 方式ではディレクトリ構成が異なります。

## ファイルストアの実体

エージェントの `file_memory_*` と `file_access_*` が読み書きするファイルストアは、このリポジトリの独自実装ではありません。
外部 NuGet パッケージ Microsoft.Agents.AI.Harness の `FileSystemAgentFileStore` を、決定した作業ディレクトリに対して生成しています。

FileMemoryProvider と FileAccessProvider には同じインスタンスを渡しているため、「メモリ」と「ファイルアクセス」がディスク上で分離されているわけではありません。
同じ作業ディレクトリを参照する呼び出しでは、片方が書いたファイルをもう片方から読めます。

`harness.shell: true` のエージェントでは、Shell のカレントディレクトリにも同じ作業ディレクトリを設定します。
`repo-agent` が `git clone` したリポジトリも、通常はその Run のワークスペース配下に作られます。

## チームとグラフで何が共有されるか

チームまたはグラフの同じMissionに参加するファイル対応エージェントは、同じMission共有ワークスペースを使います。
あるエージェントが `reports/result.md` を作成すると、後続のエージェントやグラフ工程は同じ相対パスでそのファイルを読み取れます。

共有対象は次のとおりです。

- チームのOrchestrator、メンバー、直接質問・回答の参加者
- グラフのAgentノード
- グラフのLoopノードの各反復
- グラフ内で呼び出されるTeamの参加者

異なるMission間、単独Runとの間、チーム定義やグラフ定義の実ファイルとの間でワークスペースは共有されません。
ファイルを扱えないエージェントへ、共有機能によって新しいファイル操作権限が付与されることもありません。

チームの会話コンテキストとグラフのノード出力も従来どおり利用できます。小さい情報はメッセージや `${nodes.<nodeId>.output}` で渡し、ファイルとして扱う成果はMission共有ワークスペースで受け渡します。

Mission詳細画面(`/missions/{missionId}`)の「共有ファイル」欄では、相対パス、種別、サイズ、最終更新時刻、利用可能かどうかを確認できます。
実行中は自動更新されますが、初回表示は読み取り専用のメタデータだけです。ファイル内容の編集・削除・プレビューは画面から行いません。

## 成果物の保存先

成果物ルートの既定値は次のパスです。

```text
C:\work-agents\artifacts
```

設定キー `Artifacts:Root` で上書きできます。
成果物ストアがファイル本体を書き込む場合のパスは、目的とファイル名から次のように組み立てられます。

```text
{ArtifactsRoot}\{purpose}\{fileName}
```

`purpose` と `fileName` はファイル名部分だけが使われるため、指定した値で成果物ルートの外へ出ることはできません。

ファイル本体とは別に、成果物のメタデータは `Runs:DatabasePath` の SQLite に保存されます。
メタデータには `MissionId`、元になったメッセージ、パス、要約、コンテンツハッシュなどが含まれます。

`IMissionArtifactStore.SaveMissionArtifactAsync` は成果物のメタデータを登録する処理です。
ファイル本体の書き込みは `IArtifactStore.SaveAsync`、または成果物を作るコードや連携処理が別途行う必要があります。
ワークスペースにあるファイルを自動的に成果物へコピーする処理はありません。

## 成果物の確認とダウンロード

`GET /missions/{missionId}/artifacts` が返すのは、パス、要約、ハッシュなどのメタデータ JSON です。

ファイル本体は、別のエンドポイント `GET /missions/{missionId}/artifacts/{artifactId}/content` からストリーミングでダウンロードできます。
破棄(discard)済みの成果物、存在しない `artifactId`、他のミッションに属する `artifactId` を指定した場合は、いずれも同じ `404` を返します(存在有無や破棄状態の詳細を外部へ漏らさないためです)。

Team Room の成果物欄にも、各成果物の行に「ダウンロード」リンクが表示されます。
このリンクは Web から Host の上記エンドポイントへ直接張られており、クリックするとブラウザがファイルを直接取得します。

このエンドポイントには、リポジトリ全体に共通する制約(認証なし、ループバック前提)以外の追加の認証層はありません。信頼できないネットワークへ公開しないでください。

## 作成後の掃除

ワークスペースには、完了した Run のディレクトリを自動的に削除する保持期限スイープがあります。

- 既定の保持期間は 7 日間です(設定キー `Workspace:Retention:RetentionPeriod`)。
- スイープの間隔は既定 1 時間です(`Workspace:Retention:SweepInterval`)。
- 保持期限スイープ自体は `Workspace:Retention:Enabled`(既定 `true`)で無効化できます。
- 実行中の Run に対応するディレクトリは、保持期限に関わらず削除対象から除外されます。
- `GET /workspace/usage` から、現在保持されているワークスペースの合計サイズ・件数・直近のスイープ結果を確認できます。

この保持期限スイープは、単独 Run の `{WorkspaceRoot}\{runId}\` と、Mission共有の `{WorkspaceRoot}\missions\{missionId}\work\` およびそのCheckpointを対象とします。
Queued、Running、Paused、AwaitingApprovalのMission/Runは削除されません。終端状態になったMission/Runは、完了時刻から保持期間を過ぎると削除対象になります。
フォールバック方式の `{root}\{agentName}\{Guid}\` ディレクトリは対応するRunを一意に特定できないため、引き続き自動削除の対象外です。不要になったフォールバック方式のディレクトリは、実行中でないことを確認したうえで運用側が削除してください。

チェックポイントで作業ディレクトリのコピーを保存する場合、既定では次の場所にコピーされます。

```text
{WorkspaceRoot}\missions\{missionId}\checkpoints\{checkpointId}\work\
```

コピー対象の上限は既定 512 MB です。Mission共有ワークスペースを保持期限で削除すると、対応するCheckpointのコピーも同時に削除されます。

## repo-agent の clone と認証

`repo-agent` の `git clone` はアプリケーションが自動実行する C# コードではありません。
`instructions.md` の指示を受けたモデルが、`run_shell` で `git clone <url> <dir>` を実行します。

Shell は作業ディレクトリをカレントディレクトリとして起動し、`confineWorkingDirectory: true` を外部 SDK の `LocalShellExecutor` に渡します。
このリポジトリにはコンテナや chroot のような OS レベルのサンドボックスはなく、カレントディレクトリの固定とコマンド拒否リストによるプロセスレベルの制御です。
拒否リストは実行前の UX 用チェックであり、ファイルシステムやネットワークの境界そのものではありません。

Git のトークンを `~/.git-credentials` に書き込む Git 認証(GitHub App のインストールトークン発行)は、シェルを許可されたエージェント(`harness.shell: true`)がシェルを構築する直前に自動的に適用されます。事前に手動でトークンを発行したり、`~/.git-credentials` を用意したりする必要はありません。

自動適用に必要な事前設定は次のとおりです。

- GitHub App の秘密鍵(PEM)を Local secret store へ登録する(既定のキー名は `github-app-private-key`。設定キー `GitAuth:PrivateKeySecretName` で変更可能)
- `GitAuth:AppId` と `GitAuth:InstallationId` を appsettings に設定する

これらが未設定・不正な場合、Git 認証の初期化はベストエフォートで失敗し(シェル自体の起動は妨げません)、詳細はプロセスログにのみ記録されます。この場合、シェルは通常どおり起動しますが、認証情報が用意されないため非公開リポジトリの `git clone` は Git 自身のエラーとして失敗します。

## 目的別の選択

| やりたいこと | 使う場所 |
| --- | --- |
| エージェント、チーム、グラフの定義を変更する | リポジトリの `src/WorkAgents.Agents/` |
| clone、編集、テストを実行する | `harness.fileStore: workspace` の Run 用ワークスペース |
| チーム内で短い情報を渡す | チームのメッセージとコンテキスト |
| グラフの前ノードの結果を渡す | `${nodes.<nodeId>.output}` |
| 別の実行でも残すファイルを保存する | `Artifacts:Root` の成果物ストア |
| 保存済みファイル本体を Web 画面から取得する | Team Room の成果物一覧の「ダウンロード」リンク |
| 古いワークスペースの使用状況を確認する | `GET /workspace/usage` |

## 次に読むページ

- [エージェントを作る]({{ '/pages/agents/' | relative_url }})で `harness.fileStore` と `workspace.yaml` を設定する
- [チームを作る]({{ '/pages/teams/' | relative_url }})で会話によるエージェント間連携を設定する
- [グラフを作る]({{ '/pages/graphs/' | relative_url }})でノード出力を次の処理へ渡す
- [設定]({{ '/pages/configuration/' | relative_url }})でルートパスなどの設定を確認する
