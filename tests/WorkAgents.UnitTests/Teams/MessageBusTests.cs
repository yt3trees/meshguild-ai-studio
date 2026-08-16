using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Teams;

public sealed class MessageBusTests
{
    [Fact]
    public async Task Publish_AssignsGlobalMissionOrderAndRaisesOneEventPerMessage()
    {
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var store = new SqliteMessageStore(databasePath);
            var bus = new MessageBus(store);
            var published = new List<Message>();
            bus.Published += notification =>
            {
                published.Add(notification.Message);
                return Task.CompletedTask;
            };

            var first = await bus.SendAsync("mission-1", MessageSenderKind.Agent, MessageKind.Delegate, "one");
            var second = await bus.SendAsync("mission-1", MessageSenderKind.Agent, MessageKind.Report, "two");
            var other = await bus.SendAsync("mission-2", MessageSenderKind.System, MessageKind.SystemNote, "other");

            Assert.Equal(1, first.Seq);
            Assert.Equal(2, second.Seq);
            Assert.Equal(1, other.Seq);
            Assert.Equal(new[] { 1L, 2L, 1L }, published.Select(message => message.Seq));
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task Publish_PreservesReplyThreadAndRecipientMetadata()
    {
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var bus = new MessageBus(new SqliteMessageStore(databasePath));
            var question = await bus.SendAsync(
                "mission-1", MessageSenderKind.Agent, MessageKind.Question, "question",
                senderInstanceId: "a", recipientInstanceId: "b", threadKey: "thread-1");
            var answer = await bus.SendAsync(
                "mission-1", MessageSenderKind.Agent, MessageKind.Answer, "answer",
                senderInstanceId: "b", recipientInstanceId: "a", threadKey: "thread-1", inReplyTo: question.MessageId);

            Assert.Equal(question.MessageId, answer.InReplyTo);
            Assert.Equal("thread-1", answer.ThreadKey);
            Assert.Equal("a", answer.RecipientInstanceId);
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }
}
