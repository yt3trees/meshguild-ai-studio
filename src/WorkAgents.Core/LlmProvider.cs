namespace WorkAgents.Core;

/// <summary>Web UI で選択できるLLMプロバイダー。</summary>
public enum LlmProvider
{
    Foundry,

    /// <summary>OpenAI公式API(標準エンドポイント + APIキー)。</summary>
    OpenAI,

    /// <summary>Amazon Bedrock Converse API(AWS SDKの標準認証チェーン + リージョン)。</summary>
    AmazonBedrock,

    /// <summary>OpenRouter API(OpenAI互換エンドポイント + APIキー)。</summary>
    OpenRouter,

    /// <summary>既存SQLiteモデルの読み込み互換用。新規登録のProviderには表示しない。</summary>
    AzureOpenAI,

    /// <summary>Anthropic API(Claude モデル。Claude Code 等が使う API キー方式)。</summary>
    Anthropic,

    /// <summary>GitHub Models(GitHub Copilot と同じ基盤。OpenAI 互換エンドポイント + PAT)。</summary>
    GitHubModels,
}
