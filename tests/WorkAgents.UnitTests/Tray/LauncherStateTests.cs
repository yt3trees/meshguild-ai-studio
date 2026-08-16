using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

public class LauncherStateTests
{
    [Fact]
    public void InitialPhase_IsStarting()
    {
        var state = new LauncherState();
        Assert.Equal(LauncherPhase.Starting, state.Phase);
    }

    [Theory]
    [InlineData(LauncherPhase.Starting, LauncherPhase.Running)]
    [InlineData(LauncherPhase.Starting, LauncherPhase.Error)]
    [InlineData(LauncherPhase.Running, LauncherPhase.Updating)]
    [InlineData(LauncherPhase.Running, LauncherPhase.Exiting)]
    [InlineData(LauncherPhase.Running, LauncherPhase.Error)]
    [InlineData(LauncherPhase.Updating, LauncherPhase.Running)]
    [InlineData(LauncherPhase.Updating, LauncherPhase.Error)]
    [InlineData(LauncherPhase.Error, LauncherPhase.Updating)]
    [InlineData(LauncherPhase.Error, LauncherPhase.Exiting)]
    public void TransitionTo_AllowedTransition_Succeeds(LauncherPhase from, LauncherPhase to)
    {
        var state = DriveTo(from);
        state.TransitionTo(to);
        Assert.Equal(to, state.Phase);
    }

    [Theory]
    [InlineData(LauncherPhase.Updating, LauncherPhase.Updating)] // FR-014: 多重「更新」の拒否
    [InlineData(LauncherPhase.Exiting, LauncherPhase.Updating)]
    [InlineData(LauncherPhase.Starting, LauncherPhase.Updating)] // 起動完了前の早すぎる「更新」の拒否
    [InlineData(LauncherPhase.Starting, LauncherPhase.Exiting)]
    [InlineData(LauncherPhase.Exiting, LauncherPhase.Running)]
    public void TransitionTo_DisallowedTransition_Throws(LauncherPhase from, LauncherPhase to)
    {
        var state = DriveTo(from);
        Assert.Throws<InvalidOperationException>(() => state.TransitionTo(to));
        Assert.Equal(from, state.Phase);
    }

    [Fact]
    public void TransitionTo_Error_SetsErrorMessage()
    {
        var state = new LauncherState();
        state.TransitionTo(LauncherPhase.Error, "boom");
        Assert.Equal("boom", state.ErrorMessage);
    }

    [Fact]
    public void TransitionTo_AwayFromError_ClearsErrorMessage()
    {
        var state = new LauncherState();
        state.TransitionTo(LauncherPhase.Error, "boom");
        state.TransitionTo(LauncherPhase.Updating);
        Assert.Null(state.ErrorMessage);
    }

    private static LauncherState DriveTo(LauncherPhase target)
    {
        var state = new LauncherState();
        if (target == LauncherPhase.Starting)
        {
            return state;
        }

        state.TransitionTo(LauncherPhase.Running);
        if (target == LauncherPhase.Running)
        {
            return state;
        }

        switch (target)
        {
            case LauncherPhase.Updating:
                state.TransitionTo(LauncherPhase.Updating);
                break;
            case LauncherPhase.Exiting:
                state.TransitionTo(LauncherPhase.Exiting);
                break;
            case LauncherPhase.Error:
                state.TransitionTo(LauncherPhase.Error, "boom");
                break;
        }

        return state;
    }
}
