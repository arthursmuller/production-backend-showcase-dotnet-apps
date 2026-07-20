using Feed.Infra.Persistence;
using MessageBrokerCore.Abstractions;
using MessageBrokerExtensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Feed.UnitTests;

public sealed class EntityFrameworkBusOutboxExtensionsTests
{
    private const string OutboxMessageEntityName =
        "MassTransit.EntityFrameworkCoreIntegration.OutboxMessage";

    [Fact]
    public async Task Publish_guard_accepts_exactly_one_added_outbox_row()
    {
        await using var context = CreateContext();
        var messageId = Guid.NewGuid();
        var publisher = new StagingPublisher(context);

        var result = await context.PublishToBusOutboxAsync(
            publisher,
            new TestMessage(),
            messageId,
            CancellationToken.None);

        Assert.Equal(messageId, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Staging_guard_rejects_missing_or_duplicate_outbox_rows(int stagedCount)
    {
        using var context = CreateContext();
        var messageId = Guid.NewGuid();
        for (var index = 0; index < stagedCount; index++)
            Stage(context, messageId);

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.EnsureBusOutboxMessageStaged(messageId));

        Assert.Contains($"found {stagedCount}", exception.Message);
    }

    [Fact]
    public void Staging_guard_rejects_an_empty_message_id()
    {
        using var context = CreateContext();

        Assert.Throws<InvalidOperationException>(
            () => context.EnsureBusOutboxMessageStaged(Guid.Empty));
    }

    private static FeedContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FeedContext>()
            .UseMySql(
                "Server=localhost;Database=feed_outbox_guard_tests;User=model;Password=model;",
                new MariaDbServerVersion(new Version(10, 6, 27)))
            .Options;
        return new FeedContext(options);
    }

    private static void Stage(FeedContext context, Guid messageId)
    {
        var entityType = context.Model.FindEntityType(OutboxMessageEntityName)!;
        var outboxMessage = Activator.CreateInstance(entityType.ClrType)!;
        context.Add(outboxMessage);
        context.Entry(outboxMessage).Property("MessageId").CurrentValue = messageId;
    }

    private sealed class StagingPublisher(FeedContext context)
        : IIntegrationMessagePublisher
    {
        public Task<Guid> PublishAsync<TMessage>(
            TMessage message,
            Guid messageId,
            CancellationToken cancellationToken)
            where TMessage : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stage(context, messageId);
            return Task.FromResult(messageId);
        }
    }

    private sealed class TestMessage;
}
