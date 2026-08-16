using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

public class SingleInstanceGuardTests
{
    [Fact]
    public void FirstGuard_AcquiresAsPrimaryInstance()
    {
        var mutexName = "WorkAgentsTrayTests_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(mutexName);

        Assert.True(first.IsPrimaryInstance);
    }

    [Fact]
    public void SecondGuard_WithSameName_IsNotPrimaryInstance()
    {
        var mutexName = "WorkAgentsTrayTests_" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(mutexName);
        using var second = new SingleInstanceGuard(mutexName);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
    }

    [Fact]
    public void AfterPrimaryDisposed_NewGuardWithSameName_CanBecomePrimary()
    {
        var mutexName = "WorkAgentsTrayTests_" + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstanceGuard(mutexName))
        {
            Assert.True(first.IsPrimaryInstance);
        }

        using var second = new SingleInstanceGuard(mutexName);
        Assert.True(second.IsPrimaryInstance);
    }
}
