---
title: 設定
description: シークレットの扱いなど、運用に関する設定方法を紹介します。
layout: page
---

エージェント・チーム・グラフそれぞれの作成手順は、[エージェントを作る]({{ '/pages/agents/' | relative_url }})・[チームを作る]({{ '/pages/teams/' | relative_url }})・[グラフを作る]({{ '/pages/graphs/' | relative_url }})を参照してください。このページでは、それらに共通する運用面の設定を紹介します。

## LLMモデルを登録する

Webの `/models` 画面で、エージェントが使うモデルと認証情報を登録します。APIキーは画面上に再表示されず、Local secret storeへ保存されます。

![Models の画面。モデル設定パネル、入力フォーム、保存ボタンに番号付きの注釈があります。]({{ '/assets/images/models.png' | relative_url }})

1. モデル設定パネル
2. Provider、endpoint、モデル名、認証情報の入力欄
3. `Save model` と既定モデルの指定

### Microsoft Foundry

`Microsoft Foundry` を選び、Project endpointとDeployment / model nameを入力します。API keyを入力した場合はOpenAI互換Responses APIを使い、空欄の場合はAzure資格情報を使います。

### OpenAI

`OpenAI` を選び、Deployment / model nameにOpenAIのモデル名(例: `gpt-4.1`)を入力します。標準エンドポイント(`https://api.openai.com/v1`)へ接続するため、Endpointの入力は不要です。API keyを入力し、`Chat Completions` または `Responses` を選択します。

### Amazon Bedrock

`Amazon Bedrock` を選び、AWS region(例: `us-east-1`)とBedrockのモデルID(例: `amazon.nova-lite-v1:0`)を入力します。AWS SDKの標準認証チェーン(AWS CLI profile、環境変数、IAM role等)を使うため、AWS access keyやsecret keyは画面へ入力しません。

### OpenRouter

`OpenRouter` を選び、Deployment / model nameにOpenRouterのモデルID(例: `openai/gpt-4.1`)を入力します。標準エンドポイント(`https://openrouter.ai/api/v1`)を使うためEndpointの入力は不要です。OpenRouterのAPI keyを入力します。

### Anthropic (Claude)

`Anthropic (Claude)` を選び、Deployment / model nameにClaudeのモデル名(例: `claude-opus-5`)を入力します。標準エンドポイント(`https://api.anthropic.com`)を使うためEndpointの入力欄は表示されません。API keyには[Anthropic Console](https://platform.claude.com/)で発行したキーを入力します。

プロキシ経由などのカスタムベースURLには現時点で対応していません。

### GitHub Models (Copilot)

`GitHub Models (Copilot)` を選び、Resource endpointに `https://models.github.ai/inference`、Deployment / model nameにモデルID(例: `openai/gpt-4.1`)を入力します。GitHub Copilot と同じモデル基盤へOpenAI互換のChat Completions APIで接続します。API keyには `models: read` 権限を持つGitHubのPersonal Access Tokenを入力します。

Responses API相当の呼び出しには対応していないため、Chat Completionsのみ利用できます。

## appsettings を用意する

`src/WorkAgents.Web` と `src/WorkAgents.Host` それぞれに、`appsettings.example.json` をコピーした `appsettings.Development.json` を作成します。両方の `Runs:DatabasePath` が同じ SQLite ファイルを指すようにしてください。異なるパスを指定すると、Web に表示される Run 履歴と Host が実際に書き込む履歴が食い違います。

`appsettings.Development.json` は `.gitignore` で除外されており、リポジトリには含まれません。コミットされる `appsettings.json` と `appsettings.example.json` には、パスの既定値や上限値などの非機密情報だけを置きます。

### appsettings.json と appsettings.Development.json の優先順位

ASP.NET Core の `WebApplication.CreateBuilder` は設定ファイルを次の順で読み込み、後から読んだ値が前の値を上書きします。

1. `appsettings.json`
2. `appsettings.{環境名}.json`(環境名が `Development` のときは `appsettings.Development.json`)
3. 環境変数
4. コマンドライン引数

`src/WorkAgents.Host/Properties/launchSettings.json` と `src/WorkAgents.Web/Properties/launchSettings.json` はいずれも `ASPNETCORE_ENVIRONMENT` を `Development` に設定しているため、IDE や `dotnet run` からの起動では 2 番目の `appsettings.Development.json` が読み込まれ、同名キーは `appsettings.json` 側の値より優先されます。キーが重複しない項目は `appsettings.json` の値のままです。

したがって `appsettings.json` は「環境を問わず常に読まれる既定値」であり、`appsettings.Development.json` は「ローカル開発時にキー単位で上書きするための差分」という役割分担になります。`Workspace:Root` など特定のキーだけローカル環境で変えたい場合は、`appsettings.Development.json` にそのキーだけ書けば上書きされます。

### 主なキーの意味

appsettings に登場する主要なキーの意味は次のとおりです。`appsettings.example.json` にはこれらのキーが一通り含まれているので、`appsettings.Development.json` を作る際はそこからコピーし、値を変える際の参考にしてください。

| キー | 対象 | 意味 | 既定値 |
| --- | --- | --- | --- |
| `Profile` | Web / Host | 実行プロファイル。`Local` 以外は未対応(Azure は将来対応) | `Local` |
| `Workspace:Root` | Web / Host | 単独 Run とミッション共有ワークスペースのルートフォルダ。Web と Host で同じ値にする | `C:\work-agents\runs` |
| `Workspace:Retention:Enabled` | Host | ワークスペースの保持期限スイープを有効にするか | `true` |
| `Workspace:Retention:RetentionPeriod` | Host | 終端状態になった Run/Mission のワークスペースを残す期間 | `7.00:00:00`(7日) |
| `Workspace:Retention:SweepInterval` | Host | 保持期限スイープの実行間隔 | `01:00:00`(1時間) |
| `Runs:DatabasePath` | Web / Host | Run/Mission/Approval など実行状態一式を持つ SQLite ファイルのパス。Web と Host で必ず同じ値にする(異なると履歴が食い違う) | `C:\work-agents\state\work-agents.db` |
| `Runs:QueueCapacity` | Host | 実行待ちの Run を積めるキューの上限件数 | `100` |
| `Artifacts:Root` | Web / Host | 成果物ファイル本体の保存ルート | `C:\work-agents\artifacts` |
| `SecretStore:Root` | Web / Host | Local secret store(DPAPI暗号化ファイル)の保存フォルダ。空文字または未指定ならコード側の既定値が使われる | `%LocalAppData%\work-agents\secrets` |
| `GitAuth:AppId` / `GitAuth:InstallationId` | Host | `repo-agent` の `git clone` を自動認証する GitHub App の ID。秘密鍵そのものはここに置かず、Local secret store に登録する | `0`(未設定) |
| `GitAuth:PrivateKeySecretName` | Host | 上記 GitHub App の秘密鍵を Local secret store から取り出す際のキー名 | `github-app-private-key` |
| `Orchestration:HostBaseUrl` | Web | Web が Host の API を呼び出す際のベース URL | `http://localhost:5160` |
| `Orchestration:Engine:Enabled` | Web / Host | ミッション実行エンジン(バックグラウンドサービス)を起動するか。通常は Host 側だけ `true`、Web 側は `false` | Host: `true` / Web: `false` |
| `Orchestration:Limits:MaxConcurrentMissions` | Host | 同時に実行できるミッション数の上限 | `5` |
| `Orchestration:Limits:MaxConcurrentAgents` | Host | 同時に実行できるエージェント数の上限 | `12` |
| `Orchestration:Limits:AskTimeoutSeconds` | Host | エージェントからの質問(Ask)が承認待ちで放置される際のタイムアウト秒数 | `300` |
| `Orchestration:Checkpoint:MaxWorkspaceBytes` | Host | チェックポイントとして作業フォルダをコピーする際の上限バイト数 | `536870912`(512MB) |
| `Orchestration:Triggers:Webhook:Loopback` | Host | Webhookトリガーの受信をループバック(ローカルホスト)からの接続に限定するか | `true` |
| `Streaming:Enabled` | Host | エージェントの応答を生成中から Team Room へ逐次表示するか。`false` にすると発言が確定してからまとめて表示する | `true` |
| `Mcp:Enabled` | Host | Local MCPサーバのendpointを有効にするか。既定は無効。Remote公開や認証なしの外部公開には使わない | `false` |
| `Agents:DefinitionSources` | Web / Host | 読み込むエージェント/チーム/グラフ/ワークフロー/Skill定義ソースの順序付きリスト(`Label`/`Path`)。後勝ちでマージし、未設定時は開発時の出力フォルダまたは配布時の共通`definitions`を単一標準ソースとして読み込む | `[]`(標準ソースへフォールバック) |
| `Agents:ToolPluginDirectories` | Web / Host | チーム固有ツールのアセンブリ(DLL)を配置するディレクトリのリスト | `[]` |
| `Agents:ToolPlugins:AllowedHosts` | Web / Host | チーム固有ツールが到達してよいホストのallowlist。空の場合は制限なし | `[]` |

`Runs:DatabasePath` と `Workspace:Root` は Web / Host 双方に存在する共通のキーなので、値がずれると画面表示と実行結果が食い違います。`GitAuth:*` と `Orchestration:*` は原則 Host 側だけが参照します。

## 配布用ランチャーから設定する

ソースからビルドせず `WorkAgents.Tray`(常駐トレイランチャー、[インストールと起動]({{ '/pages/getting-started/' | relative_url }})の「配布物を起動する場合」を参照)で使っている場合、`appsettings.Development.json` を直接編集する代わりに、トレイメニューの「設定」からこのページの表にあるキーを含め、appsettings関連の設定をまとめて変更できます。

- Web/Hostのポート番号、`Workspace:Root`、`Artifacts:Root`、`Runs:DatabasePath`、追加のエージェント定義ソースは専用の入力欄があります。設定画面のリスト先頭には本体同梱の標準ソースが常に表示され、追加ソースはその後ろへ複数登録できます。上から順に読み込まれ、後のソースにある同名定義が優先されます。標準ソースは開発時は各プロセスの実行フォルダ、配布物ではHost/Webの兄弟にある共通`definitions`フォルダから自動解決されるため、設定ファイルには保存されません。
- `configuration.md`の表にあるその他のキー(`Runs:QueueCapacity`、`GitAuth:AppId`、`Orchestration:Limits:*` など)も、設定画面の「運用設定」にキーごとの入力欄があります。`Agents:ToolPluginDirectories` と `Agents:ToolPlugins:AllowedHosts` は1行1項目のリストで入力できます。表にない将来のキーは「その他の設定」に `キー=値` の形式で入力します。
- いずれも空欄のままなら、appsettingsの既定値がそのまま使われます(トレイランチャー未使用時と同じ挙動)。
- 保存は `%LocalAppData%\WorkAgents\tray-settings.json` に書き込まれ、次回ランチャー起動時に子プロセスへ環境変数として渡されます(`appsettings.json` 本体は書き換えません)。反映には再起動が必要です。
- APIキーなどの機密情報は、`appsettings.Development.json` と同様にこの画面にも入力しないでください。GitAuthの秘密鍵などは、これまでどおり Local secret store に登録します。

設定ファイル上では、追加定義ソースは `additionalAgentDefinitionPaths` 配列として保存されます。例えば
`["C:\\teams\\sales-agents", "C:\\teams\\shared-agents"]` とした場合、標準ソース、sales、shared の順で読み込まれます。旧版の `additionalAgentDefinitionPath` が残っていても、ランチャー起動時に1件の配列へ移行されます。

内部的な変換ルール(コロン区切りのキーを環境変数の `__` 区切りへ変換する等)は `specs/007-tray-icon-app/contracts/tray-settings-file-contract.md` にまとめています。

## 設定・シークレット・パスの保存先

このアプリの設定関連の情報は、次の4つの場所に分かれて保存されます。どれに何を置くかは実装側で固定されており、利用者が選ぶものではありません。

| 保存先 | 実体 | 保存される内容 |
| --- | --- | --- |
| appsettings(リポジトリ管理 + 端末ローカル上書き) | `appsettings.json` / `appsettings.example.json`(コミット対象)、`appsettings.Development.json`(端末ローカル、gitignore対象) | `Workspace:Root`、`Artifacts:Root`、`Runs:DatabasePath` などパスの既定値、`Runs:QueueCapacity`、`Orchestration` 配下の上限値、`GitAuth:AppId` / `GitAuth:InstallationId` / `GitAuth:PrivateKeySecretName`(秘密鍵そのものではなく参照名) |
| トレイランチャー設定(端末ローカル、配布物向け) | `%LocalAppData%\WorkAgents\tray-settings.json`(`WorkAgents.Tray.exe` 使用時のみ) | Web/Hostのポート番号、`Workspace:Root` / `Artifacts:Root` / `Runs:DatabasePath` の上書き、順序付きの追加エージェント定義ソース、その他appsettingsキーの上書き。詳しくは次節「配布用ランチャーから設定する」を参照 |
| SQLite(`Runs:DatabasePath` が指す単一ファイル) | Run、Mission、Approval などの実行状態一式 | LLM モデルの接続設定(Endpoint、DeploymentName など)、ミッション作業フォルダの相対キーと準備・削除日時、APIキー/クライアントシークレットは値そのものではなく `has_api_key` などの存在フラグのみ |
| Local secret store(端末ローカル、DPAPI暗号化) | 既定 `%LocalAppData%\work-agents\secrets\`(`SecretStore:Root` で変更可) | LLM APIキー本体、サービスプリンシパルのクライアントシークレット本体、GitHub App の秘密鍵、Webhookトリガーの共有シークレット本体 |

この分離により、APIキーやトークンの値そのものはリポジトリのファイルにも SQLite にも書き込まれません。SQLite 側が持つのは「値が登録済みかどうか」と「Local secret store 側の参照名」だけです。

ミッション作業フォルダの絶対パスも同じ考え方で扱われます。SQLite の `mission_workspaces` テーブルにはミッションIDに対する相対キー(`missions/<missionId>/work`)と日時だけを保存し、絶対パスそのものは保存しません。実行時に `Workspace:Root`(appsettings由来)とこの相対キーから都度組み立て、UI や API へは返しません。詳細は[ファイルと成果物]({{ '/pages/storage/' | relative_url }})を参照してください。

一方で、`Workspace:Root` や `Artifacts:Root` といった「ルートパスそのもの」は現状 appsettings 側の設定であり、コミット対象の `appsettings.example.json` には既定値としてローカル絶対パス(`C:\work-agents\runs` など)がそのまま書かれています。これは秘密情報ではありませんが、端末固有になり得るパスがリポジトリのファイルに残る形です。運用上問題があれば、`appsettings.Development.json` で上書きしてください。

## シークレットを扱う

API キーやトークンなどの機密情報は、`agent.yaml` や `workspace.yaml`、`.env`、コードなど、リポジトリにコミットされるファイルへ直接書き込まないでください。必ず Local secret store にのみ保存します。詳細はリポジトリ内の `docs/security-and-secrets.md` を参照してください。

## 承認が必要なツールの範囲

`run_shell` はビルトインの実装上、常に承認必須(`Approval: "required"`)として登録されており、エージェント側の設定で承認を省略することはできません。カスタムツールを追加する場合も、危険度に応じて `Approval` を `"required"` にするかどうかをツール登録側(`AgentToolRegistration`)で判断します。承認を待たせたくない・そもそも実行させたくないコマンドは、[エージェントを作る]({{ '/pages/agents/' | relative_url }})の `workspace.yaml` の `denyList` で拒否してください。

## チーム定義・チーム固有ツールを分離配布する

共通システムのソースコードを変更・再ビルドせずに、チームごとのエージェント/チーム/グラフ定義とカスタムツールを追加できます。手順は `specs/006-team-config-distribution/quickstart.md` を参照してください。概要は次のとおりです。

- `Agents:DefinitionSources` に、共通システム標準ソースの後ろにチーム定義パッケージのディレクトリを追加すると、`agents/`・`skills/`・`teams/`・`graphs/`・`workflows/` サブディレクトリ配下の定義がマージ読み込みされます。同名定義は後に列挙したソース側が優先され、上書きが発生したことは起動ログに記録されます。
- `Mcp:Enabled=true` にすると、Hostの `http://127.0.0.1:<HostPort>/mcp` でMCPクライアントから能力発見、Mission受付、状態観測を行えます。既定は無効で、Shell、定義書き込み、承認決定はMCPから公開されません。危険な操作の承認は `/approvals` で行います。
- チーム固有ツールは、共通システムが提供するツールプロバイダ契約(`IAgentToolProvider` 相当)を実装した別アセンブリ(`.dll`)としてビルドし、`Agents:ToolPluginDirectories` に配置します。グラフ/ワークフローの `kind: code` ノードが使う `.csx`(Roslynスクリプト、本体プロセス内実行)とは別の仕組みで、契約(ツール名・説明・引数スキーマ)を明示的に宣言する点は共通です。到達してよいホストは `Agents:ToolPlugins:AllowedHosts` で制限できます。詳細な契約は `specs/006-team-config-distribution/contracts/tool-plugin-contract.md` を参照してください。
- .NETでのビルドを前提としないチームには、JavaScript/Pythonのスクリプトツールも選べます。スクリプト本体と、契約(ツール名・説明・引数スキーマ・承認要否・到達先ホスト)を宣言するマニフェスト(`<name>.tool.yaml`)を同じフォルダに置き、同じ `Agents:ToolPluginDirectories` へ配置します。呼び出しごとに `node`/`python` の子プロセスが起動され、承認フロー・allowlistはDLL版と同じ扱いです。詳細は `specs/006-team-config-distribution/contracts/script-tool-contract.md` を参照してください。
- いずれかの定義ソースやプラグイン(DLL・スクリプトいずれも)の読み込みに失敗しても、その部分だけがスキップされ、起動ログに記録されたうえで起動は継続します。

### 設定例

`appsettings.json`(または `appsettings.Development.json`)に次のように追加します。`standard` は本体同梱の標準定義(未設定時に暗黙で読み込まれるパスと同じ場所)、`team-sales` が外部に用意したチーム定義パッケージです。

```jsonc
{
  "Agents": {
    "DefinitionSources": [
      { "Label": "standard", "Path": "./agents" },
      { "Label": "team-sales", "Path": "C:/teams/sales-agents" }
    ],
    "ToolPluginDirectories": [
      "C:/teams/sales-agents/tools"
    ],
    "ToolPlugins": {
      "AllowedHosts": [ "intranet-api.example.local" ]
    }
  }
}
```

`team-sales` 側のフォルダは、本体の `agents/` などと同じ構造(1エージェント=1フォルダ)を、必要なサブディレクトリ分だけ用意します。ツールプラグインのDLLは `ToolPluginDirectories` に指定したフォルダ直下に置きます。

```text
C:/teams/sales-agents/
├── agents/
│   └── sales-report-agent/
│       ├── agent.yaml
│       └── instructions.md
├── teams/
│   └── sales-team/
│       └── team.yaml
├── graphs/
│   └── sales-pipeline/
│       └── graph.yaml
├── workflows/                 # 使わない場合はフォルダごと省略可
└── tools/                     # Agents:ToolPluginDirectories が指す場所
    ├── SalesTools.dll         # DLLプラグイン(.NET)
    ├── send_slack.js          # スクリプトツール本体(JavaScript)
    └── send_slack.tool.yaml   # send_slack.js の契約宣言(並置マニフェスト)
```

`agents/`・`teams/`・`graphs/`・`workflows/` は使う分だけ用意すれば十分です(例: エージェント定義だけ追加したいチームは `agents/` のみでも構いません)。`tools/` 配下はDLLプラグインとスクリプトツールを混在させられます。

## ローカル環境以外での利用について

現行の実装は Windows 上のローカルプロファイルを前提としており、Web と Host の API に認証機能がないことが既知の制約です。信頼できないネットワークや本番環境へ、追加の外部境界(リバースプロキシでの認証など)なしに公開しないでください。
