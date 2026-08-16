using WorkAgents.Core.Authoring;

namespace WorkAgents.Orchestration.Graph;

/// <summary>
/// <see cref="GraphValidationResult"/> を書き手向けの日本語診断へ変換する橋渡し (案D)。
/// 実行エンジンは <see cref="GraphValidationError"/> のコードだけを見ればよく、
/// 文言の面倒は <see cref="ValidationMessageCatalog"/> 側に閉じ込める。
/// </summary>
public static class GraphValidationDiagnostics
{
    public static IReadOnlyList<AuthoringDiagnostic> ToDiagnostics(this GraphValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors
            .Select(error => ValidationMessageCatalog.ForGraph(error.Code, error.Message, error.NodeIds, error.EdgeIds))
            .ToArray();
    }
}
