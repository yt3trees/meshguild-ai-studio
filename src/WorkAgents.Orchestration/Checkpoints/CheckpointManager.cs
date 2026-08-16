using System.Text.Json;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Orchestration.Checkpoints;

public sealed record CheckpointOptions
{
    public string WorkspaceRoot { get; init; } = Path.Combine(Path.GetTempPath(), "work-agents");

    public long MaxWorkspaceBytes { get; init; } = 512L * 1024 * 1024;
}

public sealed record CheckpointRestoreResult(
    Checkpoint? Checkpoint,
    bool Restored,
    bool WorkspaceRestored,
    string? Note);

/// <summary>Persists logical boundaries and bounded workspace copies for safe restart.</summary>
public sealed class CheckpointManager
{
    private readonly ICheckpointStore _checkpoints;
    private readonly IMessageStore? _messages;
    private readonly ISecretRedactor? _redactor;
    private readonly CheckpointOptions _options;

    public CheckpointManager(
        ICheckpointStore checkpoints,
        IMessageStore? messages = null,
        ISecretRedactor? redactor = null,
        CheckpointOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        _checkpoints = checkpoints;
        _messages = messages;
        _redactor = redactor;
        _options = options ?? new CheckpointOptions();
    }

    public async Task<Checkpoint> SaveAsync(
        string missionId,
        CheckpointBoundaryKind boundaryKind,
        string stateJson,
        long lastMessageSeq,
        string? workspacePath = null,
        string? nodeRunId = null,
        string? iterationId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentNullException.ThrowIfNull(stateJson);
        var checkpointId = Guid.NewGuid().ToString("N");
        var safeState = _redactor is null ? stateJson : await _redactor.RedactAsync(stateJson, ct);
        string? copyPath = null;
        var restorable = false;
        if (!string.IsNullOrWhiteSpace(workspacePath) && Directory.Exists(workspacePath))
        {
            var bytes = CalculateSize(workspacePath);
            if (bytes <= _options.MaxWorkspaceBytes)
            {
                copyPath = Path.Combine(_options.WorkspaceRoot, "missions", missionId, "checkpoints", checkpointId, "work");
                DirectoryCopy(workspacePath, copyPath);
                restorable = true;
            }
        }
        var checkpoint = new Checkpoint
        {
            CheckpointId = checkpointId,
            MissionId = missionId,
            BoundaryKind = boundaryKind,
            NodeRunId = nodeRunId,
            IterationId = iterationId,
            LastMessageSeq = lastMessageSeq,
            StateJson = safeState,
            WorkspacePath = copyPath,
            WorkspaceRestorable = restorable,
        };
        await _checkpoints.CreateAsync(checkpoint, ct);
        return checkpoint;
    }

    public async Task<CheckpointRestoreResult> RestoreLatestAsync(
        string missionId,
        string? workspacePath = null,
        CancellationToken ct = default)
    {
        var checkpoint = await _checkpoints.GetLatestAsync(missionId, ct);
        if (checkpoint is null)
        {
            return new CheckpointRestoreResult(null, false, false, "No checkpoint exists for this mission.");
        }

        if (_messages is not null)
        {
            await _messages.DiscardAsync(missionId, checkpoint.LastMessageSeq, checkpoint.CheckpointId, ct);
        }
        var restored = false;
        if (checkpoint.WorkspaceRestorable && !string.IsNullOrWhiteSpace(checkpoint.WorkspacePath) && !string.IsNullOrWhiteSpace(workspacePath))
        {
            if (Directory.Exists(workspacePath)) Directory.Delete(workspacePath, true);
            DirectoryCopy(checkpoint.WorkspacePath!, workspacePath!);
            restored = true;
        }
        return new CheckpointRestoreResult(
            checkpoint,
            true,
            restored,
            restored ? null : "Workspace was not restorable for this checkpoint.");
    }

    private static long CalculateSize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file).Length)
                .Aggregate(0L, (total, size) => checked(total + size));
        }
        catch (IOException)
        {
            return long.MaxValue;
        }
        catch (UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    private static void DirectoryCopy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            DirectoryCopy(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
