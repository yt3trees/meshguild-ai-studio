using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;

namespace WorkAgents.Infrastructure.Stores;

public sealed class MissionWorkspaceReader : IMissionWorkspaceReader
{
    private readonly IMissionStore _missions;
    private readonly IMissionWorkspaceStore _workspaces;
    private readonly MissionWorkspacePathResolver _paths;
    private readonly ILogger<MissionWorkspaceReader>? _logger;

    public MissionWorkspaceReader(
        IMissionStore missions,
        IMissionWorkspaceStore workspaces,
        MissionWorkspacePathResolver paths,
        ILogger<MissionWorkspaceReader>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(paths);
        _missions = missions;
        _workspaces = workspaces;
        _paths = paths;
        _logger = logger;
    }

    public async Task<MissionWorkspaceSnapshot> ReadAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        var path = _paths.ResolvePath(missionId);
        var mission = await _missions.GetAsync(missionId, ct)
            ?? throw new KeyNotFoundException($"Mission not found: '{missionId}'.");
        _ = mission;

        var observedAt = DateTimeOffset.UtcNow;
        var record = await _workspaces.GetAsync(missionId, ct);
        if (record is null)
        {
            return Snapshot(missionId, MissionWorkspaceState.NotCreated, observedAt);
        }

        if (record.DeletedAtUtc is not null || !Directory.Exists(path))
        {
            return Snapshot(missionId, MissionWorkspaceState.Deleted, observedAt);
        }

        var items = new List<MissionWorkspaceEntry>();
        try
        {
            Enumerate(root: new DirectoryInfo(path), items, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _logger?.LogWarning(ex, "mission workspace could not be read mission={MissionId}", missionId);
            return Snapshot(missionId, MissionWorkspaceState.Unreadable, observedAt);
        }

        items.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return new MissionWorkspaceSnapshot
        {
            MissionId = missionId,
            State = items.Count == 0 ? MissionWorkspaceState.Empty : MissionWorkspaceState.Available,
            ObservedAtUtc = observedAt,
            Items = items,
        };
    }

    private static void Enumerate(
        DirectoryInfo root,
        ICollection<MissionWorkspaceEntry> items,
        CancellationToken ct)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos().ToArray();
            }
            catch (IOException) when (!directory.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase))
            {
                MarkUnreadable(items, root, directory);
                continue;
            }
            catch (UnauthorizedAccessException) when (!directory.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase))
            {
                MarkUnreadable(items, root, directory);
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = RelativePath(root, entry);
                if (relativePath is null)
                {
                    continue;
                }

                var isDirectory = entry is DirectoryInfo;
                var isReparsePoint = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
                var item = CreateEntry(entry, relativePath, isDirectory, isReparsePoint);
                items.Add(item);
                if (isDirectory && !isReparsePoint && item.Status == WorkspaceEntryStatus.Available)
                {
                    pending.Push((DirectoryInfo)entry);
                }
            }
        }
    }

    private static MissionWorkspaceEntry CreateEntry(
        FileSystemInfo entry,
        string relativePath,
        bool isDirectory,
        bool unreadable)
    {
        if (unreadable)
        {
            return new MissionWorkspaceEntry
            {
                RelativePath = relativePath,
                Kind = isDirectory ? WorkspaceEntryKind.Directory : WorkspaceEntryKind.File,
                Status = WorkspaceEntryStatus.Unreadable,
            };
        }

        try
        {
            return new MissionWorkspaceEntry
            {
                RelativePath = relativePath,
                Kind = isDirectory ? WorkspaceEntryKind.Directory : WorkspaceEntryKind.File,
                SizeBytes = isDirectory ? null : ((FileInfo)entry).Length,
                LastWriteTimeUtc = new DateTimeOffset(entry.LastWriteTimeUtc, TimeSpan.Zero),
                Status = WorkspaceEntryStatus.Available,
            };
        }
        catch (IOException)
        {
            return UnreadableEntry(relativePath, isDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            return UnreadableEntry(relativePath, isDirectory);
        }
    }

    private static void MarkUnreadable(
        ICollection<MissionWorkspaceEntry> items,
        DirectoryInfo root,
        DirectoryInfo directory)
    {
        var relativePath = RelativePath(root, directory);
        if (relativePath is null)
        {
            return;
        }

        items.RemoveWhere(item => string.Equals(item.RelativePath, relativePath, StringComparison.Ordinal));
        items.Add(UnreadableEntry(relativePath, isDirectory: true));
    }

    private static string? RelativePath(DirectoryInfo root, FileSystemInfo entry)
    {
        var relative = Path.GetRelativePath(root.FullName, entry.FullName);
        if (Path.IsPathRooted(relative) || relative is "." or ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static MissionWorkspaceEntry UnreadableEntry(string relativePath, bool isDirectory)
        => new()
        {
            RelativePath = relativePath,
            Kind = isDirectory ? WorkspaceEntryKind.Directory : WorkspaceEntryKind.File,
            Status = WorkspaceEntryStatus.Unreadable,
        };

    private static MissionWorkspaceSnapshot Snapshot(
        string missionId,
        MissionWorkspaceState state,
        DateTimeOffset observedAt)
        => new()
        {
            MissionId = missionId,
            State = state,
            ObservedAtUtc = observedAt,
        };
}

internal static class MissionWorkspaceEntryCollectionExtensions
{
    public static void RemoveWhere(this ICollection<MissionWorkspaceEntry> items, Func<MissionWorkspaceEntry, bool> predicate)
    {
        foreach (var item in items.Where(predicate).ToArray())
        {
            items.Remove(item);
        }
    }
}
