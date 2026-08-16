using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Orchestration.Admission;

namespace WorkAgents.UnitTests.Orchestration;

/// <summary>テスト用のインメモリ MissionQueue ストア。</summary>
internal sealed class InMemoryMissionQueueStore : IMissionQueueStore
{
    private readonly List<MissionQueueEntry> _entries = new();
    private int _nextPosition = 1;
    private readonly object _gate = new();

    public Task<int> EnqueueAsync(string missionId, MissionQueuedReason reason, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var position = _nextPosition++;
            _entries.Add(new MissionQueueEntry { MissionId = missionId, Position = position, Reason = reason });
            return Task.FromResult(position);
        }
    }

    public Task<IReadOnlyList<MissionQueueEntry>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MissionQueueEntry>>(_entries.OrderBy(e => e.Position).ToList());
        }
    }

    public Task<MissionQueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var first = _entries.OrderBy(e => e.Position).FirstOrDefault();
            if (first is not null)
            {
                _entries.Remove(first);
            }
            return Task.FromResult(first);
        }
    }

    public Task RemoveAsync(string missionId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.MissionId == missionId);
            return Task.CompletedTask;
        }
    }
}

public class AdmissionControllerTests
{
    [Fact]
    public async Task RequestMission_AdmitsUpToLimit_ThenQueuesFifoWithReason()
    {
        var queueStore = new InMemoryMissionQueueStore();
        var controller = new AdmissionController(queueStore, maxConcurrentMissions: 5, maxConcurrentAgents: 12);

        for (var i = 0; i < 5; i++)
        {
            var admission = await controller.RequestMissionAsync($"m{i}");
            Assert.True(admission.Admitted);
            Assert.Null(admission.QueuePosition);
        }

        var sixth = await controller.RequestMissionAsync("m5");
        Assert.False(sixth.Admitted);
        Assert.Equal(MissionQueuedReason.ConcurrencyLimit, sixth.Reason);
        Assert.Equal(1, sixth.QueuePosition);

        var seventh = await controller.RequestMissionAsync("m6");
        Assert.False(seventh.Admitted);
        Assert.Equal(2, seventh.QueuePosition);
    }

    [Fact]
    public async Task ReleaseMission_PromotesOldestQueuedMissionFirst()
    {
        var queueStore = new InMemoryMissionQueueStore();
        var controller = new AdmissionController(queueStore, maxConcurrentMissions: 1, maxConcurrentAgents: 12);

        var first = await controller.RequestMissionAsync("m0");
        Assert.True(first.Admitted);

        await controller.RequestMissionAsync("m1");
        await controller.RequestMissionAsync("m2");

        var promoted = await controller.ReleaseMissionAsync("m0");
        Assert.Equal("m1", Assert.Single(promoted));

        var remaining = await queueStore.ListAsync();
        Assert.Single(remaining);
        Assert.Equal("m2", remaining[0].MissionId);

        var promotedAgain = await controller.ReleaseMissionAsync("m1");
        Assert.Equal("m2", Assert.Single(promotedAgain));
        Assert.Empty(await queueStore.ListAsync());
    }

    [Fact]
    public void RequestAgent_AdmitsUpToLimit_ThenQueuesFifo()
    {
        var controller = new AdmissionController(new InMemoryMissionQueueStore(), maxConcurrentMissions: 5, maxConcurrentAgents: 3);

        for (var i = 0; i < 3; i++)
        {
            var admission = controller.RequestAgent("mission-1", $"a{i}");
            Assert.True(admission.Admitted);
        }

        var fourth = controller.RequestAgent("mission-1", "a3");
        Assert.False(fourth.Admitted);
        Assert.Equal(1, fourth.QueuePosition);

        var fifth = controller.RequestAgent("mission-1", "a4");
        Assert.False(fifth.Admitted);
        Assert.Equal(2, fifth.QueuePosition);
    }

    [Fact]
    public void ReleaseAgent_PromotesInFifoOrder()
    {
        var controller = new AdmissionController(new InMemoryMissionQueueStore(), maxConcurrentMissions: 5, maxConcurrentAgents: 1);

        controller.RequestAgent("mission-1", "a0");
        controller.RequestAgent("mission-1", "a1");
        controller.RequestAgent("mission-1", "a2");

        var promoted1 = controller.ReleaseAgent("a0");
        Assert.Equal("a1", Assert.Single(promoted1));

        var promoted2 = controller.ReleaseAgent("a1");
        Assert.Equal("a2", Assert.Single(promoted2));

        Assert.Empty(controller.ReleaseAgent("a2"));
    }
}
