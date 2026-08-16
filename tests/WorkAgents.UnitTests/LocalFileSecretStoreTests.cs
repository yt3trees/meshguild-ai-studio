using WorkAgents.Infrastructure.Secrets;

namespace WorkAgents.UnitTests;

public sealed class LocalFileSecretStoreTests
{
    [Fact]
    public async Task SetAndGet_RoundTripsValue()
    {
        var root = CreateRootPath();
        try
        {
            var store = new LocalFileSecretStore(root);

            await store.SetAsync("api-key", "super-secret-value");
            var value = await store.GetAsync("api-key");

            Assert.Equal("super-secret-value", value);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenSecretDoesNotExist()
    {
        var root = CreateRootPath();
        try
        {
            var store = new LocalFileSecretStore(root);

            Assert.Null(await store.GetAsync("missing"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsStoredNamesWithoutValues()
    {
        var root = CreateRootPath();
        try
        {
            var store = new LocalFileSecretStore(root);
            await store.SetAsync("b-secret", "value-b");
            await store.SetAsync("a-secret", "value-a");

            var names = await store.ListAsync();

            Assert.Equal(["a-secret", "b-secret"], names);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenRootDoesNotExist()
    {
        var root = CreateRootPath();

        var store = new LocalFileSecretStore(root);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task DeleteAsync_RemovesSecret_AndReturnsFalseWhenAlreadyGone()
    {
        var root = CreateRootPath();
        try
        {
            var store = new LocalFileSecretStore(root);
            await store.SetAsync("to-delete", "value");

            Assert.True(await store.DeleteAsync("to-delete"));
            Assert.Null(await store.GetAsync("to-delete"));
            Assert.False(await store.DeleteAsync("to-delete"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRootPath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"secrets-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
