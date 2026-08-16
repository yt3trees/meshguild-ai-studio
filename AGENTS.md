# AGENTS.md
This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

MeshGuild AI Studio は、複数の自律型 AI エージェントへミッションを渡し、会話・委譲・反復を観測しながら収束させる Windows ローカル実行基盤です。
.NET 10 / C# のソリューション (`WorkAgents.sln`) と、Playwright E2E 用のルート npm パッケージで構成されています。

## 規約の正本

CLAUDE.md には規約本文を置きません。次のファイルを正本として参照してください。

- `.specify/memory/constitution.md`: プロジェクト憲法。リポジトリ内の他の慣行に優先します。秘密情報の扱い、危険操作の HITL 承認、Local-First の前提、Run/承認のテストファースト、ファイルベース定義の 5 原則。これらに触れる変更の前に必ず読むこと。
- `AGENTS.md`: 設定項目を追加・変更するときの手順の正本。
- `docs/testing.md`: 検証手順の正本。E2E とトレイの手動スモークテスト項目を含みます。
- `.github/copilot-instructions.md`: 旧 `workflow.yaml` 執筆規約。現行のグラフ定義の一次情報にはしないでください。

## 常用コマンド

### ビルド

```powershell
dotnet restore WorkAgents.sln
dotnet build WorkAgents.sln
dotnet build WorkAgents.sln --configuration Release --no-restore
```

### 単体テスト

```powershell
dotnet test tests/WorkAgents.UnitTests/WorkAgents.UnitTests.csproj
```

クラス単位、メソッド単位で絞り込む場合。

```powershell
dotnet test tests/WorkAgents.UnitTests/WorkAgents.UnitTests.csproj --filter "FullyQualifiedName~ApprovalTests"
dotnet test tests/WorkAgents.UnitTests/WorkAgents.UnitTests.csproj --filter "FullyQualifiedName~GraphValidatorTests.Validate_ReportsUndeclaredCycleWithNodesAndEdges"
```

### 起動

```powershell
dotnet run --project src/WorkAgents.Host/WorkAgents.Host.csproj --launch-profile http   # http://localhost:5160
dotnet run --project src/WorkAgents.Web/WorkAgents.Web.csproj --launch-profile http     # http://localhost:5049
```

Host と Web をまとめて起動する場合はリポジトリ直下の `start-workagents.cmd` を使います。
変更の動作確認では `--no-build` を使わないでください (直前のビルド成果物を掴むため)。

### Playwright E2E

```powershell
npm ci
npm run test:e2e:install
npm run typecheck
npm run test:e2e
```

単一ファイル、タイトル絞り込み、後始末。

```powershell
npm run test:e2e -- tests/e2e/navigation.spec.ts
npm run test:e2e -- tests/e2e/models.spec.ts -g "validation"
npm run test:e2e:clean
```

E2E は Playwright の WebServer が `WorkAgents.Web` を `E2E` 環境として `http://127.0.0.1:5049` に自前で起動します。
先に `dotnet run` を立ち上げないでください。各実行は一時 SQLite / SecretStore / Workspace を使い、`C:\work-agents` の開発データには触りません。

### ワークフロー移行 CLI

```powershell
dotnet run --project src/WorkAgents.Host/WorkAgents.Host.csproj -- migrate-workflows --dry-run
```

## アーキテクチャ

### プロセス構成

`WorkAgents.Host` が唯一の実行エンジンです。Mission API、BackgroundService による非同期実行、Triggers、SignalR Hub (`MissionHub`, `RunProgressHub`) を持ちます。
`WorkAgents.Web` は Blazor の観測・操作クライアントで、実行エンジンを持ちません。Host の HTTP API (`Services/MissionApiClient.cs`) と SignalR (`Services/MissionHubClient.cs`) を経由します。

両プロセスは同じ SQLite (`Runs:DatabasePath`) を参照する前提です。この前提を崩さないでください。

### ミッションの実行モデル

ミッション (目標) は `targetKind: Team` か `targetKind: Graph` のどちらかへ渡され、実際に働くのはエージェントです。

- 起点は `src/WorkAgents.Orchestration/MissionEngine.cs`
- Team 経路: `Teams/TeamExecutor.cs`。統括エージェントが実行時に委譲する動的な進行。会話経路の可否は `ConversationPolicy.cs`、配送は `MessageBus.cs`、インスタンス管理は `RosterManager.cs`、待機は `WaitGraph.cs`
- Graph 経路: `Graph/GraphExecutor.cs`。ノードとエッジで固定した静的な工程。ノード種別ごとの処理は `Graph/NodeHandlers/`
- `Graph/GraphValidator.cs` が保存前に循環、未到達ノード、未解決参照を拒否します。循環は `loopBack: true` を付けたエッジだけが許されます (`POST /graphs/<name>/validate`)

Team とグラフの概念的な違い、`agent.yaml` / `team.yaml` / `graph.yaml` の書き方は `README.md` の「基本の考え方」が最も詳しいです。

### 定義のロード

エージェント、チーム、グラフ、スキルはすべてファイルベースで、`src/WorkAgents.Agents/{agents,teams,graphs,skills,workflows}/<name>/` に 1 フォルダー 1 定義として置きます。

- ローダーは `src/WorkAgents.Agents/Loading/FileBased{Agent,Team,Graph,Workflow}Loader.cs`
- 複数の定義ソース (チーム別に別リポジトリで配布する定義パッケージ) の後勝ちマージと診断は `Loading/DefinitionSourceResolver.cs`
- 画面から定義を書き戻す経路は `Loading/*YamlWriter.cs` と `src/WorkAgents.Web/Services/DefinitionAuthoringService.cs`
- ホットリロードはありません。定義ファイルは `WorkAgents.Agents.csproj` の Content 設定でビルド時に各プロセスの出力へコピーされるため、編集したら再ビルドと再起動が必要です。ただし `graphs/**` の `.cs` は Content から除外されているため出力へ届きません (グラフのスクリプトは `.csx` で置くこと)。配布時は `publish-workagents.cmd` が `dist/definitions/` へ集約します。Host と Web は同じ標準定義ルートを探索するため、配布後に定義を変えたらトレイの「更新」で再起動します

### YAML スキーマ

`schemas/*.schema.json` が定義形式の唯一の真実です。
`WorkAgents.Core.csproj` がこれを EmbeddedResource として埋め込み、配置パスに依存せず読めるようにしています。UI のフォーム生成もこのスキーマを読みます。
定義形式を変更するときは、スキーマ、該当する `*YamlWriter.cs`、`.vscode/settings.json` の `yaml.schemas` を揃えて更新してください。

### レガシー: workflow.yaml

`workflow.yaml` は旧世代の逐次ワークフロー定義です。読み込みはできますが、実行前に `migrate-workflows` (CLI) または `POST /migrations/workflows` で `graph.yaml` へ変換する必要があります (`WorkflowMigrationRequiredException`)。
新規の工程定義はグラフで書いてください。

### スクリプト実行

`graphs/**` と `workflows/**` 配下の `.cs` / `.csx` は、プロジェクトの Compile から除外され (`WorkAgents.Agents.csproj`)、実行時に Roslyn scripting で評価されます。
拡張子に依存せずトップレベル文 + `return` の C# Script 構文で書きます。
ただし `graphs/**` の `.cs` は Content からも除外されていてビルド出力へコピーされないため、グラフのスクリプトは `.csx` で置いてください。Host プロセス権限で動くため、副作用のある処理は承認ノードとセットで設計してください。

### プロジェクトの責務

| プロジェクト | 責務 |
|---|---|
| `WorkAgents.Core` | Mission、Team、Graph、Loop、Trigger、承認のドメインモデルと抽象。スキーマの埋め込み |
| `WorkAgents.Orchestration` | MissionEngine、Team/Graph 実行、Loops、Budgets、Checkpoints、Replay、Triggers、Migration |
| `WorkAgents.Agents` | 定義のロード、`AgentRegistry`、`LlmAgentFactory`、ツールと Skills |
| `WorkAgents.Harness` | FileStore、Shell、作業ディレクトリ拘束、Git 認証、承認連携 |
| `WorkAgents.Infrastructure` | SQLite ストア、キュー、Secrets (DPAPI)、Telemetry |
| `WorkAgents.Host` | Mission API、非同期実行、Triggers、SignalR |
| `WorkAgents.Web` | Blazor UI (Mission Control、Team Room、Graph Studio、Approvals、設定) |
| `WorkAgents.Tray` | Host / Web を子プロセスとして起動する常駐ランチャー (`ProcessSupervisor.cs`) |

依存方向は `WorkAgents.Core` を中心に保ちます。Agents の定義型を Harness へ直接持ち込まず、`HarnessAgentConfig` で必要な値だけを渡してください。

`WorkAgents.Tray` は UI 依存のため自動テストの対象外です。変更したら `docs/testing.md` の手動スモークテスト (シナリオ 1 から 8) を確認してください。

### アイコンの実装ルール (WorkAgents.Web)

絵文字は使わず、Google の Material Symbols (Outlined) をリガチャフォントとして使います。フォントは `wwwroot/app.css` の先頭で `@import` 済みです。

- 使い方: `<span class="wa-icon" aria-hidden="true">folder_open</span>` のように、`.wa-icon` (または NavMenu 内では既存の `.nav-icon`) を付けた要素にアイコン名をテキストとしてそのまま書きます。アイコン名は [Material Symbols](https://fonts.google.com/icons) の名前 (snake_case) をそのまま使ってください
- アイコン単体のボタンには必ず `title` と `aria-label` を付け、アイコンの `<span>` 側には `aria-hidden="true"` を付けます (アイコンはテキストのリガチャ描画であり、スクリーンリーダーには意味を持たないため)
- 新しい共通の見た目 (角丸の枠付きボタンなど) が要る場合は、ページ固有 CSS に書かずに `wwwroot/app.css` へ `wa-` プレフィックスのクラスとして追加し、他画面でも再利用できるようにしてください (例: `.wa-field-icon-btn`)

## テストを書くときに知っておくこと

- LLM を伴う実行系のテストは `tests/WorkAgents.UnitTests/Fakes/ScriptedAgentInvoker.cs` を使います。エージェント名へ台本を登録すると、LLM や API キーへ接続せずに発言とツール呼び出しを再現できます
- ストアを使うテストは `Path.GetTempPath()` 配下に一意なデータベースを作り、終了時に削除します。固定パスや実 API キーに依存させないでください

## ドキュメントの所在

- ルートの `README.md` (英語版) と `README-ja.md` (日本語版) は常に同期する。どちらかを変更したときは、構成、リンク、画像、説明、注意書きをもう一方にも反映する。
- `docs/*.md`: 現行の文書 (`README.md`、`testing.md`、`configuration.md`、`adding-agents.md`、`security-and-secrets.md`、`manual-site-development.md`)
- `docs/decisions/`: ADR。0001 は Microsoft Foundry 採用、0002 は追加 LLM プロバイダー、0003 はオーケストレーションのホスティング
- `specs/<NNN>-<name>/`: 機能単位の仕様、data-model、contracts、quickstart
- `manual/`: 利用者向け Jekyll サイト (GitHub Pages)

# 開発時の設定項目追加ルール

## 設定項目を追加・変更する場合

- `manual/_pages/configuration.md` の設定キー表を利用者向けの正本として扱う。
- Host と Web の両方が読む共通設定は、両プロセスへ同じ値を渡す。
- `Workspace:Root`、`Artifacts:Root`、`Runs:DatabasePath` のようなパスは、トレイ設定ファイルの専用フィールドと設定画面の専用入力欄を用意する。
- `manual/_pages/configuration.md` の表にある設定キーは、設定画面から設定できる個別入力欄または適切なリスト入力を用意する。表にない将来のキーには汎用の「キー=値」入力を残す。
- 配列設定はカンマ区切り文字列にせず、設定画面では順序付きリストとして扱い、環境変数では `__0__` から始まるインデックス付きキーへ変換する。
- `LauncherSettings` のJSONスキーマ、読み込み・保存・バリデーション、`ProcessSupervisor` の環境変数伝搬を同時に更新する。
- `specs/007-tray-icon-app/contracts/tray-settings-file-contract.md`、`specs/007-tray-icon-app/data-model.md`、仕様書、マニュアルを更新する。
- 既存のトレイ設定ファイルを壊す変更では、読み込み時の移行処理と移行テストを追加する。
- APIキー、秘密鍵などの秘密情報をトレイ設定ファイルや設定画面へ追加しない。秘密値はLocal secret storeで管理する。
- 設定項目を追加したら、設定値の保存・再読み込み、子プロセスへの伝搬、既定値、Host/Web間の一致をユニットテストで検証する。
- 変更後は `dotnet build WorkAgents.sln` と `dotnet test tests/WorkAgents.UnitTests/WorkAgents.UnitTests.csproj` を実行する。
