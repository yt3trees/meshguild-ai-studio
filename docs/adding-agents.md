# エージェントと定義ファイルの追加

エージェント本体は `src/WorkAgents.Agents/agents/<name>/` に置きます。

必要なファイルは次の二つです。

```text
agent.yaml
instructions.md
```

`agent.yaml` の `name` はフォルダー名と一致させます。
API キー、アクセストークン、秘密情報は定義ファイルへ書きません。

## 画面から作る・編集する

エージェントは Web UI からも作成できます。

- 作成: New definition (`/definitions/new?kind=agent`) で「エージェント」を選び、name を決めて作成します。白紙から作るほか、既存エージェントの複製もできます。
- 編集: Teams & Agents の一覧から「編集」を開くと、使える共有スキル、`harness` の権限、`instructions.md` をフォームで編集できます。
- 共有スキルは `skills/<name>/SKILL.md` を走査した一覧から選ぶ形式で、名前を手で入力しません。ローカルスキル (`agents/<name>/skills/`) は置くだけで読み込まれるため、画面では一覧表示のみです。

保存すると `agent.yaml` と `instructions.md` に書き戻されます。YAML のコメントは保持されません。
`name` はフォルダー名と一体のため編集画面では変更できません。改名は新しい名前で作り直します。
実行中のプロセスへの反映はアプリの再起動後です。

## MCPサーバから利用する

MCPクライアントから定義一覧やMission実行を利用する場合は、Hostの `Mcp:Enabled` を有効にします。Localではloopback endpointのみを使い、Shell、承認決定、定義書き込みはMCPへ公開されません。詳細なTool/Resource契約と検証手順は [MCPサーバ仕様](../specs/009-mcp-server-support/spec.md) を参照してください。

## チーム

チームは `src/WorkAgents.Agents/teams/<name>/team.yaml` に置きます。
`orchestrator.agent` と `members[].agent` は既存エージェントを参照します。
直接会話を許可する組み合わせは `channels.allow` に列挙します。
未知のキー、未知のエージェント、重複メンバー、上限超過は読み込み時に拒否されます。

```yaml
version: 1
name: feature-team
orchestrator:
  agent: orchestrator-agent
members:
  - agent: dev-agent
channels:
  default: via-orchestrator
```

## グラフ

グラフは `src/WorkAgents.Agents/graphs/<name>/graph.yaml` に置きます。
ノードとエッジを保存する前に `POST /graphs/<name>/validate` で検証できます。
`loopBack: true` 以外の循環、未到達ノード、未解決参照は保存できません。
`kind: code` のスクリプトはグラフフォルダー配下の `scripts/` に置きます。

旧 `workflow.yaml` を追加する場合は、実行前に次を実行します。

```powershell
dotnet run --project src/WorkAgents.Host -- migrate-workflows --dry-run
dotnet run --project src/WorkAgents.Host -- migrate-workflows
```

## チーム定義パッケージ・チーム固有ツールの分離配布

共通システム(本体)のソースコードを変更・再ビルドせずに、チーム固有のエージェント/チーム/グラフ定義や、社内APIを呼び出すようなカスタムツールを追加できます(specs/006-team-config-distribution)。

### 外部の定義ソースを追加する

1. 任意の場所(別リポジトリでも可)に `agents/`, `skills/`, `teams/`, `graphs/`, `workflows/` の必要なサブディレクトリを作成し、上記と同じスキーマで定義を置く。
2. `appsettings.json`(または環境固有の appsettings)の `Agents:DefinitionSources` に、標準ソースの後ろにそのディレクトリを追加する。

   ```jsonc
   {
     "Agents": {
       "DefinitionSources": [
         { "Label": "standard", "Path": "./agents" },
         { "Label": "team-sales", "Path": "C:/teams/sales-agents" }
       ]
     }
   }
   ```

3. 起動すると、後に列挙したソース側が同名定義を優先して上書きする(標準エージェントのカスタマイズも同じ仕組み)。上書きの発生と読み込み失敗は起動ログに記録され、起動は停止しない。

詳細な設定キーは [contracts/definition-source-config.md](../specs/006-team-config-distribution/contracts/definition-source-config.md)、検証手順は [quickstart.md](../specs/006-team-config-distribution/quickstart.md) を参照。

### チーム固有ツールプラグインを追加する

1. 共通システムが提供するツールプロバイダ契約(`IAgentToolProvider` 相当)を実装した公開クラスを、別プロジェクトとしてビルドする。ツール名・説明・引数・戻り値に機密情報を含めない。危険度の高い操作は承認必須(`Approval: "required"`)を宣言する。
2. ビルドしたDLLを `Agents:ToolPluginDirectories` に設定したディレクトリへ配置する。到達してよいホストは `Agents:ToolPlugins:AllowedHosts` で制限できる。
3. システムを再起動すると、プラグインは本体と分離されたロードコンテキストで読み込まれ、起動ログに読み込み結果(`Loaded`/`Failed`とツール名一覧)が記録される。

契約の詳細は [contracts/tool-plugin-contract.md](../specs/006-team-config-distribution/contracts/tool-plugin-contract.md) を参照。

### JavaScript/Pythonのスクリプトツールを追加する

.NETでのビルドを前提とせず、JavaScript/Pythonで書いたスクリプトもチーム固有ツールとして追加できる(User Story 4)。

1. スクリプト本体(`<name>.js` または `<name>.py`)と、それと同じフォルダに置くマニフェスト(`<name>.tool.yaml`)を用意する。マニフェストには `name`/`description`/`agentName`/`runtime`(`node`または`python`)/`entryPoint`/`approval`(`automatic`または`required`)を宣言する。危険度の高い操作は `approval: required` を宣言する。ツール名・説明・引数・戻り値・マニフェストの内容に機密情報を含めない。
2. 到達先ホストを制限したい場合は、マニフェストの `allowedHosts` と `Agents:ToolPlugins:AllowedHosts` の両方に対象ホストを追加する(`allowedHosts` がグローバルallowlistを満たさない場合、そのツールはロードされない)。
3. スクリプトとマニフェストの組を `Agents:ToolPluginDirectories` に設定したディレクトリへ配置する(DLLプラグインと同じディレクトリに共存できる)。
4. システムを再起動すると、呼び出しごとに `node`/`python` の子プロセスが起動され、標準入出力のJSON1行で引数・戻り値をやり取りする。承認必須のツールは、DLLプラグインと同じHuman-in-the-Loop承認フローを経由する。

契約の詳細は [contracts/script-tool-contract.md](../specs/006-team-config-distribution/contracts/script-tool-contract.md) を参照。
