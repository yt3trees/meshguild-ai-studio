using System.Text;
using WorkAgents.Infrastructure.Secrets;
using WorkAgents.UnitTests.Fakes;

namespace WorkAgents.UnitTests.Redaction;

public class SecretRedactorTests
{
    [Fact]
    public async Task RedactAsync_RedactsPlaintextSecretValue()
    {
        var store = new InMemorySecretStore();
        await store.SetAsync("github-token", "sk-super-secret-value-123");
        var redactor = new StoreBackedSecretRedactor(store);

        var result = await redactor.RedactAsync("token is sk-super-secret-value-123 in the log");

        Assert.DoesNotContain("sk-super-secret-value-123", result);
    }

    [Fact]
    public async Task RedactAsync_RedactsUrlEncodedForm()
    {
        var store = new InMemorySecretStore();
        var secret = "p@ss word/with+chars";
        await store.SetAsync("db-password", secret);
        var redactor = new StoreBackedSecretRedactor(store);

        var encoded = Uri.EscapeDataString(secret);
        var result = await redactor.RedactAsync($"connection string uses {encoded}");

        Assert.DoesNotContain(encoded, result);
    }

    [Fact]
    public async Task RedactAsync_RedactsBase64EncodedForm()
    {
        var store = new InMemorySecretStore();
        var secret = "my-api-key-value";
        await store.SetAsync("api-key", secret);
        var redactor = new StoreBackedSecretRedactor(store);

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        var result = await redactor.RedactAsync($"Authorization: Basic {base64}");

        Assert.DoesNotContain(base64, result);
    }

    [Fact]
    public async Task RedactAsync_LeavesNonSecretTextUnchanged()
    {
        var store = new InMemorySecretStore();
        await store.SetAsync("github-token", "sk-super-secret-value-123");
        var redactor = new StoreBackedSecretRedactor(store);

        var result = await redactor.RedactAsync("no secrets here, just a normal message");

        Assert.Equal("no secrets here, just a normal message", result);
    }

    [Fact]
    public async Task RedactAsync_NoPlaintextLeaksAcrossAllEncodedForms()
    {
        var store = new InMemorySecretStore();
        var secret = "correct-horse-battery-staple";
        await store.SetAsync("shared-secret", secret);
        var redactor = new StoreBackedSecretRedactor(store);

        var encoded = Uri.EscapeDataString(secret);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        var text = $"plain={secret} url={encoded} b64={base64}";

        var result = await redactor.RedactAsync(text);

        Assert.DoesNotContain(secret, result);
        Assert.DoesNotContain(encoded, result);
        Assert.DoesNotContain(base64, result);
    }
}
