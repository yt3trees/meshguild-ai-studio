using System.Text;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> に登録済みの値を対象に伏せ字化する (T034)。
/// 平文に加えて、URL エンコード形・Base64 エンコード形も検出する (R-14)。
/// 永続化直前 (messages.body、artifacts.summary、evaluations.notes、
/// missions.error、approvals.args_summary 等) に通す単一経路として使う。
/// </summary>
public sealed class StoreBackedSecretRedactor : ISecretRedactor
{
    private const string Mask = "[REDACTED]";

    private readonly ISecretStore _secretStore;

    public StoreBackedSecretRedactor(ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        _secretStore = secretStore;
    }

    public async Task<string> RedactAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var names = await _secretStore.ListAsync(ct);
        if (names.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var name in names)
        {
            var value = await _secretStore.GetAsync(name, ct);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            foreach (var form in EncodedForms(value))
            {
                if (string.IsNullOrEmpty(form) || !result.Contains(form, StringComparison.Ordinal))
                {
                    continue;
                }

                result = result.Replace(form, Mask, StringComparison.Ordinal);
            }
        }

        return result;
    }

    private static IEnumerable<string> EncodedForms(string secretValue)
    {
        yield return secretValue;

        string? urlEncoded = null;
        try
        {
            urlEncoded = Uri.EscapeDataString(secretValue);
        }
        catch
        {
            // ignore malformed values that cannot be percent-encoded
        }
        if (urlEncoded is not null)
        {
            yield return urlEncoded;
        }

        string? base64Encoded = null;
        try
        {
            base64Encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(secretValue));
        }
        catch
        {
            // ignore
        }
        if (base64Encoded is not null)
        {
            yield return base64Encoded;
        }
    }
}
