using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

public sealed class SettingsWindowTests
{
    [Fact]
    public void SettingsWindow_CanBeConstructedOnStaThread()
    {
        Exception? exception = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var window = new SettingsWindow(new LauncherSettings());
                window.Show();
                window.Close();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)));
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.Null(exception);
    }
}
