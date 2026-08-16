using WorkAgents.Core.Abstractions;

namespace WorkAgents.Core;

/// <summary>Web UI で管理するLLMモデル接続設定。APIキーとClient secretは <see cref="ISecretStore"/> に分離して保存する。</summary>
public sealed class LlmModelSettings
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public LlmProvider Provider { get; init; } = LlmProvider.Foundry;
    public string ProjectEndpoint { get; init; } = "";
    /// <summary>プロバイダーの接続先。Amazon BedrockではAWSリージョン名を保持する。</summary>
    public string Endpoint { get; init; } = "";
    public required string DeploymentName { get; init; }
    public string Api { get; init; } = "ChatCompletion";
    public bool IsDefault { get; init; }
    public bool HasApiKey { get; init; }

    /// <summary>Foundryプロバイダーでサービスプリンシパル認証を使う場合のEntra IDテナントID。</summary>
    public string TenantId { get; init; } = "";

    /// <summary>Foundryプロバイダーでサービスプリンシパル認証を使う場合のアプリケーション(クライアント)ID。</summary>
    public string ClientId { get; init; } = "";

    /// <summary>クライアントシークレットが保存済みか。値自体は一覧画面に含めない。</summary>
    public bool HasClientSecret { get; init; }

    public int MaxContextWindowTokens { get; init; } = 128_000;
    public int MaxOutputTokens { get; init; } = 4_096;
    public int CompactionTriggerTokens { get; init; } = 96_000;
    public int CompactionTargetTokens { get; init; } = 64_000;
    public int CompactionMinimumPreservedGroups { get; init; } = 8;

    /// <summary>実行時だけ設定されるAPIキー。永続化テーブルや一覧画面には含めない。</summary>
    public string? ApiKey { get; init; }

    /// <summary>実行時だけ設定されるサービスプリンシパルのクライアントシークレット。永続化テーブルや一覧画面には含めない。</summary>
    public string? ClientSecret { get; init; }
}
