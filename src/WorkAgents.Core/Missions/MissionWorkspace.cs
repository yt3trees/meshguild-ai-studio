namespace WorkAgents.Core.Missions;

public enum MissionWorkspaceState
{
    NotCreated,
    Available,
    Empty,
    Deleted,
    Unreadable,
}

public enum WorkspaceEntryKind
{
    File,
    Directory,
}

public enum WorkspaceEntryStatus
{
    Available,
    Unreadable,
}

public sealed record MissionWorkspaceRecord
{
    public required string MissionId { get; init; }

    public required string WorkspaceKey { get; init; }

    public required DateTimeOffset PreparedAtUtc { get; init; }

    public DateTimeOffset? DeletedAtUtc { get; init; }
}

public sealed record MissionWorkspaceEntry
{
    public required string RelativePath { get; init; }

    public required WorkspaceEntryKind Kind { get; init; }

    public long? SizeBytes { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public required WorkspaceEntryStatus Status { get; init; }
}

public sealed record MissionWorkspaceSnapshot
{
    public required string MissionId { get; init; }

    public required MissionWorkspaceState State { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public IReadOnlyList<MissionWorkspaceEntry> Items { get; init; } = Array.Empty<MissionWorkspaceEntry>();
}
