using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Checkpoints;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class CheckpointTests
{
    [Fact]
    public async Task SaveAndRestore_CopiesWorkspaceAndDiscardsLaterMessages()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "state.db");
        var workspace = Path.Combine(root, "work");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "result.txt"), "checkpointed");
        try
        {
            var messages = new SqliteMessageStore(databasePath);
            await messages.AppendAsync(new Message
            {
                MessageId = "first",
                MissionId = "mission",
                Seq = 0,
                SenderKind = MessageSenderKind.System,
                Kind = MessageKind.SystemNote,
                Body = "first",
            });
            var manager = new CheckpointManager(
                new SqliteCheckpointStore(databasePath),
                messages,
                options: new CheckpointOptions { WorkspaceRoot = root, MaxWorkspaceBytes = 1_024 });
            var checkpoint = await manager.SaveAsync("mission", CheckpointBoundaryKind.Node, "{}", 1, workspacePath: workspace);
            File.AppendAllText(Path.Combine(workspace, "later.txt"), "later");
            await messages.AppendAsync(new Message
            {
                MessageId = "second",
                MissionId = "mission",
                Seq = 0,
                SenderKind = MessageSenderKind.System,
                Kind = MessageKind.SystemNote,
                Body = "second",
            });

            var restore = await manager.RestoreLatestAsync("mission", workspace);

            Assert.Equal(checkpoint.CheckpointId, restore.Checkpoint!.CheckpointId);
            Assert.True(restore.WorkspaceRestored);
            Assert.True(File.Exists(Path.Combine(workspace, "result.txt")));
            Assert.False(File.Exists(Path.Combine(workspace, "later.txt")));
            Assert.Single(await messages.ListAsync("mission"));
            Assert.Equal(2, (await messages.ListAsync("mission", includeDiscarded: true)).Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_MarksWorkspaceNonRestorableWhenSizeLimitIsExceeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "state.db");
        var workspace = Path.Combine(root, "work");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "large.txt"), new string('x', 100));
        try
        {
            var checkpoint = await new CheckpointManager(
                new SqliteCheckpointStore(databasePath),
                options: new CheckpointOptions { WorkspaceRoot = root, MaxWorkspaceBytes = 10 })
                .SaveAsync("mission", CheckpointBoundaryKind.Iteration, "{}", 0, workspacePath: workspace);

            Assert.False(checkpoint.WorkspaceRestorable);
            Assert.Null(checkpoint.WorkspacePath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Save_MissionWorkspaceUsesCheckpointSubdirectoryAndKeepsActiveRootSeparate()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "state.db");
        var workspace = Path.Combine(root, "missions", "mission", "work");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "result.txt"), "checkpointed");
        try
        {
            var checkpoint = await new CheckpointManager(
                new SqliteCheckpointStore(databasePath),
                options: new CheckpointOptions { WorkspaceRoot = root, MaxWorkspaceBytes = 1_024 })
                .SaveAsync("mission", CheckpointBoundaryKind.Node, "{}", 0, workspacePath: workspace);

            Assert.True(checkpoint.WorkspaceRestorable);
            Assert.NotNull(checkpoint.WorkspacePath);
            Assert.StartsWith(Path.Combine(root, "missions", "mission", "checkpoints"), checkpoint.WorkspacePath!, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Path.GetFullPath(workspace), Path.GetFullPath(checkpoint.WorkspacePath!));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
