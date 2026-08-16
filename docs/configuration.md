# 設定リファレンス

ミッション実行の設定は `Orchestration` セクションに置きます。

```json
{
  "Orchestration": {
    "Engine": { "Enabled": true },
    "Limits": {
      "MaxConcurrentMissions": 5,
      "MaxConcurrentAgents": 12,
      "AskTimeoutSeconds": 300
    },
    "Checkpoint": {
      "MaxWorkspaceBytes": 536870912
    },
    "Triggers": {
      "Webhook": { "Loopback": true }
    }
  }
}
```

`Engine:Enabled` は Host で `true`、Web で `false` にします。
Host だけが Mission、Trigger、Recovery の BackgroundService を起動します。

`Limits:MaxConcurrentMissions` は同時実行ミッション数です。
`Limits:MaxConcurrentAgents` は全ミッションをまたぐエージェントインスタンス数です。
上限に達したミッションは SQLite の FIFO 待機列へ入ります。

`Checkpoint:MaxWorkspaceBytes` を超える作業領域はコピーせず、チェックポイントを復元不可として保存します。
`Triggers:Webhook:Loopback` は外部イベントをループバックに限定する運用前提を示します。
Webhook の共有秘密は `ISecretStore` に登録し、設定ファイルには書きません。

## Microsoft Foundry

LLMモデルはWebの `/models` 画面で登録します。Microsoft FoundryではProject endpointとDeployment / model nameを入力します。

- API keyを入力した場合は、Project endpoint配下のOpenAI互換Responses API (`/openai/v1`) をAPIキーで呼び出します。
- API keyを空欄にした場合は、Microsoft Entra ID認証を使います。開発環境では `az login`、サービスプリンシパルを使う場合はModels画面のTenant ID・Client ID・Client secretを入力します。
- API keyとClient secretは秘密ストアへ保存し、設定ファイルやSQLiteには値を保存しません。
- Azure OpenAIは新規モデル登録のProviderとして提供しません。

## OpenAI

LLMモデルはWebの `/models` 画面で `OpenAI` を選び、Deployment / model nameにOpenAIのモデル名を入力します。

- OpenAI公式エンドポイント(`https://api.openai.com/v1`)へ接続します。エンドポイントの入力は不要です。
- API keyは必須です。`Chat Completions` と `Responses` を選択できます。
- API keyは秘密ストアへ保存し、設定ファイルやSQLiteには値を保存しません。

## Amazon Bedrock

LLMモデルはWebの `/models` 画面で `Amazon Bedrock` を選び、AWS regionとBedrockのモデルIDを入力します。

- AWS SDKの標準認証チェーン(env、AWS CLI profile、IAM role等)で認証します。AWS access keyやsecret keyをモデル設定へ入力・保存しません。
- Bedrock RuntimeのConverse / ConverseStream APIを使用します。
- `Endpoint`欄にはAWSリージョン名(例: `us-east-1`)を入力します。

## OpenRouter

LLMモデルはWebの `/models` 画面で `OpenRouter` を選び、Deployment / model nameにOpenRouterのモデルID(例: `openai/gpt-4.1`)を入力します。

- OpenRouterのOpenAI互換Chat Completionsエンドポイント(`https://openrouter.ai/api/v1`)へ接続します。エンドポイントの入力は不要です。
- API keyは必須で、秘密ストアへ保存します。

## Anthropic (Claude)

LLMモデルはWebの `/models` 画面で `Anthropic (Claude)` を選び、Deployment / model nameにClaudeのモデル名(例: `claude-opus-5`)を入力します。

- 既定エンドポイント(`https://api.anthropic.com`)へ接続します。エンドポイントの入力欄は表示されません。
- API keyは必須で、秘密ストアへ保存します。
- カスタムベースURL(プロキシ経由等)は未対応です。詳細は [ADR-0002](decisions/0002-additional-llm-providers.md) を参照してください。

## GitHub Models (Copilot)

LLMモデルはWebの `/models` 画面で `GitHub Models (Copilot)` を選び、Resource endpointに `https://models.github.ai/inference`、Deployment / model nameにモデルID(例: `openai/gpt-4.1`)を入力します。

- OpenAI互換のChat Completions APIへ接続します。Responses相当のAPIは未対応です。
- API keyには `models: read` 権限を持つGitHub Personal Access Tokenを指定し、秘密ストアへ保存します。
