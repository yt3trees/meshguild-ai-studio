using WorkAgents.Infrastructure.Secrets;
using WorkAgents.UnitTests.Fakes;

namespace WorkAgents.UnitTests.Redaction;

public sealed class RedactionCoverageTests
{
    [Fact]
    public async Task PersistedTextSurfacesAreRedactedBeforeTheyCanBeStored()
    {
        var secrets = new InMemorySecretStore();
        await secrets.SetAsync("token", "secret-value");
        var redactor = new StoreBackedSecretRedactor(secrets);
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret-value"));
        var surfaces = new[]
        {
            "message secret-value",
            "artifact " + Uri.EscapeDataString("secret-value"),
            "evaluation " + encoded,
            "mission error secret-value",
            "approval args secret-value",
            "workspace metadata secret-value",
        };

        foreach (var surface in surfaces)
        {
            var result = await redactor.RedactAsync(surface);
            Assert.DoesNotContain("secret-value", result, StringComparison.Ordinal);
        }
    }
}
