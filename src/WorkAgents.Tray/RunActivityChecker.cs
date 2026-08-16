using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using System.Net.Http;

namespace WorkAgents.Tray;

/// <summary>
/// Hostの<c>GET /runs</c>を呼び出し、進行中Run(Queued/Running/AwaitingApproval)の有無を判定する
/// (research.md「3.」)。API疎通不可時は安全側(進行中とみなす)にフォールバックする。
/// </summary>
public sealed class RunActivityChecker
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Queued", "Running", "AwaitingApproval",
    };

    private readonly Func<int, CancellationToken, Task<IReadOnlyList<string>?>> _fetchStatuses;

    public RunActivityChecker(HttpClient? httpClient = null)
    {
        var client = httpClient ?? new HttpClient();
        _fetchStatuses = async (hostPort, ct) =>
        {
            using var response = await client.GetAsync(new Uri($"http://localhost:{hostPort}/runs"), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var runs = await response.Content
                .ReadFromJsonAsync<List<RunSummaryDto>>(cancellationToken: ct)
                .ConfigureAwait(false);
            return runs?.Select(run => run.Status).ToList();
        };
    }

    /// <summary>テストからHTTP呼び出しを差し替えるための内部コンストラクタ。</summary>
    internal RunActivityChecker(Func<int, CancellationToken, Task<IReadOnlyList<string>?>> fetchStatuses)
    {
        _fetchStatuses = fetchStatuses;
    }

    /// <summary>API呼び出し自体が失敗した場合はtrue(進行中とみなす)を返すフェイルセーフ実装。</summary>
    public async Task<bool> HasActiveRunsAsync(int hostPort, CancellationToken ct = default)
    {
        try
        {
            var statuses = await _fetchStatuses(hostPort, ct).ConfigureAwait(false);
            if (statuses is null)
            {
                return true;
            }

            return statuses.Any(status => ActiveStatuses.Contains(status));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return true;
        }
    }

    private sealed class RunSummaryDto
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
    }
}
