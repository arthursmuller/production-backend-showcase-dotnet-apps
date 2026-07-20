using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using IdentityProvider;
using Interaction.Application.Dtos;
using Interaction.Application.Features.InteractionStatus.Queries;
using Interaction.Application.Features.Maintenance.Commands;
using Interaction.Application.Features.Maintenance.Queries;
using Interaction.Application.Features.Projections.Commands;
using Interaction.Application.Features.Privacy.Commands;
using Interaction.Application.Features.Ratings.Commands;
using Interaction.Application.Features.Reactions.Commands;
using Interaction.Application.Features.Views.Commands;
using Interaction.Application.Options;
using Interaction.Application.Providers.Cache;
using Interaction.Application.Providers.Events;
using Interaction.Application.Providers.Execution;
using Interaction.Application.Providers.Privacy;
using Interaction.Contracts;
using Interaction.Domain.AggregatesModel;
using Interaction.Domain.Exceptions;
using Interaction.Domain.Repositories;
using MessageBrokerContracts.Privacy.Restricted;
using Microsoft.Extensions.Options;
using Xunit;
using ReactionKind = Interaction.Domain.AggregatesModel.ReactionKind;
using ResourceLifecycle = Interaction.Domain.AggregatesModel.ResourceLifecycle;
using ResourceVisibility = Interaction.Domain.AggregatesModel.ResourceVisibility;

namespace Interaction.UnitTests;

public sealed class InteractionApplicationTests
{
    private static readonly ResourceRef Resource = new(ResourceType.Comment, "c-42");

    [Fact]
    public async Task One_hundred_identical_reactions_create_one_effect_and_switch_atomically()
    {
        var fixture = CreateFixture();
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(Resource, ResourceLifecycle.Active, ResourceVisibility.Public, 1, fixture.Clock.GetUtcNow()),
            TestContext.Current.CancellationToken);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            fixture.Application.SetReactionAsync(Resource, ReactionKind.Like, TestContext.Current.CancellationToken)));

        var summary = Assert.Single(fixture.Store.Summaries.Values);
        Assert.Single(fixture.Store.Reactions);
        Assert.Equal(1, summary.LikeCount);
        Assert.Equal(0, summary.DislikeCount);
        Assert.Single(fixture.Publisher.Reactions);

        await fixture.Application.SetReactionAsync(Resource, ReactionKind.Dislike, TestContext.Current.CancellationToken);
        Assert.Single(fixture.Store.Reactions);
        Assert.Equal(0, summary.LikeCount);
        Assert.Equal(1, summary.DislikeCount);
    }

    [Fact]
    public async Task Rating_and_view_observation_are_unique_and_summary_converges()
    {
        var fixture = CreateFixture();
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(Resource, ResourceLifecycle.Active, ResourceVisibility.Public, 1, fixture.Clock.GetUtcNow()),
            TestContext.Current.CancellationToken);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            fixture.Application.SetRatingAsync(Resource, 5, "bounded", TestContext.Current.CancellationToken)));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            fixture.Application.ObserveViewAsync(Resource, TestContext.Current.CancellationToken)));

        Assert.Single(fixture.Store.Ratings);
        Assert.Single(fixture.Store.Observations);
        Assert.Equal(1, await fixture.Application.AggregateViewsAsync(500, TestContext.Current.CancellationToken));
        var summary = Assert.Single(fixture.Store.Summaries.Values);
        Assert.Equal(1, summary.RatingCount);
        Assert.Equal(5, summary.RatingTotal);
        Assert.Equal(1, summary.ViewCount);
        Assert.True(fixture.Cache.RemoveCount > 0);
    }

    [Fact]
    public async Task Duplicate_gap_events_are_idempotent_and_contiguous_replay_recovers()
    {
        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        var versionThree = new ResourceStateUpdate(Resource, ResourceLifecycle.Active, ResourceVisibility.Public, 3, fixture.Clock.GetUtcNow());
        await fixture.Application.ApplyResourceStateAsync(versionThree, token);
        await fixture.Application.ApplyResourceStateAsync(versionThree, token);
        var state = Assert.Single(fixture.Store.States.Values);
        Assert.True(state.HasGap);
        Assert.Single(fixture.Store.Gaps);
        Assert.False(state.IsPubliclyInteractable);

        await fixture.Application.ApplyResourceStateAsync(versionThree with { SourceVersion = 2 }, token);
        Assert.Equal(2, fixture.Store.Gaps.Count);
        await fixture.Application.ApplyResourceStateAsync(versionThree with { SourceVersion = 1 }, token);
        Assert.True(state.IsPubliclyInteractable);
        Assert.Equal(3, state.AppliedVersion);
        Assert.Empty(fixture.Store.Gaps);

        await fixture.Application.ApplyResourceStateAsync(versionThree, token);
        Assert.True(state.IsPubliclyInteractable);
        Assert.Empty(fixture.Store.Gaps);
    }

    [Fact]
    public async Task Ingestion_outcomes_and_reconciliation_drift_emit_only_bounded_dimensions()
    {
        var ingestion = new ConcurrentQueue<(string Kind, string Outcome, string[] Keys)>();
        var drift = new ConcurrentQueue<(long Value, string Counter, string[] Keys)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "Inspacy.Platform"
                && instrument.Name is "inspacy.interaction.ingestion.outcomes"
                    or "inspacy.interaction.reconciliation.drift")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var snapshot = tags.ToArray();
            if (instrument.Name == "inspacy.interaction.ingestion.outcomes")
            {
                ingestion.Enqueue((
                    Value(snapshot, "ingestion_kind"),
                    Value(snapshot, "ingestion_outcome"),
                    snapshot.Select(tag => tag.Key).Order(StringComparer.Ordinal).ToArray()));
            }
            else if (instrument.Name == "inspacy.interaction.reconciliation.drift")
            {
                drift.Enqueue((
                    measurement,
                    Value(snapshot, "counter"),
                    snapshot.Select(tag => tag.Key).Order(StringComparer.Ordinal).ToArray()));
            }
        });
        listener.Start();

        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        var now = fixture.Clock.GetUtcNow();
        var versionOne = new ResourceStateUpdate(
            Resource,
            ResourceLifecycle.Active,
            ResourceVisibility.Public,
            1,
            now);
        await fixture.Application.ApplyResourceStateAsync(versionOne, token);
        await fixture.Application.ApplyResourceStateAsync(versionOne, token);
        await fixture.Application.ApplyResourceStateAsync(versionOne with { SourceVersion = 3 }, token);

        var summary = new EngagementSummary(Resource, now);
        summary.ApplyReaction(null, ReactionKind.Like, now);
        fixture.Store.AddSummary(summary);
        await fixture.Application.ReconcileCountersAsync(10, token);

        Assert.Contains(ingestion, item => item is ("resource_state", "applied", _));
        Assert.Contains(ingestion, item => item is ("resource_state", "deduplicated", _));
        Assert.Contains(ingestion, item => item is ("resource_state", "gap_buffered", _));
        Assert.All(ingestion, item => Assert.Equal(
            ["ingestion_kind", "ingestion_outcome"],
            item.Keys));
        Assert.Contains(drift, item => item is (1, "likes", _));
        Assert.All(drift, item => Assert.Equal(["counter"], item.Keys));
    }

    [Fact]
    public async Task Projection_gap_backlog_reports_bounded_count_and_oldest_age()
    {
        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                Resource,
                ResourceLifecycle.Active,
                ResourceVisibility.Public,
                3,
                fixture.Clock.GetUtcNow()),
            token);

        fixture.Clock.Advance(TimeSpan.FromSeconds(75));
        var backlog = await fixture.Application.GetProjectionGapBacklogAsync(token);

        Assert.Equal(1, backlog.PendingCount);
        Assert.Equal(75, backlog.OldestAgeSeconds);

        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                Resource,
                ResourceLifecycle.Active,
                ResourceVisibility.Public,
                1,
                fixture.Clock.GetUtcNow()),
            token);
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                Resource,
                ResourceLifecycle.Active,
                ResourceVisibility.Public,
                2,
                fixture.Clock.GetUtcNow()),
            token);

        backlog = await fixture.Application.GetProjectionGapBacklogAsync(token);
        Assert.Equal(0, backlog.PendingCount);
        Assert.Equal(0, backlog.OldestAgeSeconds);
    }

    [Fact]
    public async Task Viewer_status_batch_maximum_is_enforced_before_storage_work()
    {
        var fixture = CreateFixture(maximum: 2);
        var inputs = new[]
        {
            new ResourceRef(ResourceType.Comment, "1"),
            new ResourceRef(ResourceType.Comment, "2"),
            new ResourceRef(ResourceType.Post, "3"),
        };
        await Assert.ThrowsAsync<InteractionValidationException>(() =>
            fixture.Application.GetViewerStatusesAsync(inputs, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Authoritative_resource_deletion_erases_actor_data_raw_views_and_publishes_zero_summary()
    {
        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        var now = fixture.Clock.GetUtcNow();
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                Resource, ResourceLifecycle.Active, ResourceVisibility.Public, 1, now),
            token);
        await fixture.Application.SetReactionAsync(Resource, ReactionKind.Like, token);
        await fixture.Application.SetRatingAsync(Resource, 5, "erase-me", token);
        await fixture.Application.ObserveViewAsync(Resource, token);

        Assert.True(await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                Resource, ResourceLifecycle.Deleted, ResourceVisibility.Private, 2, now.AddMinutes(1)),
            token));

        Assert.Empty(fixture.Store.Reactions);
        Assert.Empty(fixture.Store.Ratings);
        Assert.Empty(fixture.Store.Observations);
        var summary = Assert.Single(fixture.Store.Summaries.Values);
        Assert.True(summary.IsErased);
        Assert.Equal(0, summary.LikeCount);
        Assert.Equal(0, summary.RatingCount);
        Assert.Equal(0, summary.ViewCount);
        var tombstone = fixture.Publisher.Summaries[^1];
        Assert.Equal(0, tombstone.LikeCount);
        Assert.Equal(0, tombstone.RatingCount);

    }

    [Fact]
    public async Task Expired_pending_views_are_not_counted()
    {
        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        var now = fixture.Clock.GetUtcNow();
        var secondResource = new ResourceRef(ResourceType.Comment, "expired-view");
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                secondResource, ResourceLifecycle.Active, ResourceVisibility.Public, 1, now),
            token);
        await fixture.Store.InsertViewObservationAsync(
            new ViewObservation(
                secondResource,
                new string('d', 64),
                now.AddDays(-8),
                now.AddDays(-8),
                now.AddMinutes(-1)),
            token);

        Assert.Equal(0, await fixture.Application.AggregateViewsAsync(100, token));
        Assert.DoesNotContain(fixture.Store.Observations, item => item.ResourceId == secondResource.Id);
    }

    [Fact]
    public async Task User_erasure_tombstone_blocks_resurrection_and_completes_after_raw_view_expiry()
    {
        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        var now = fixture.Clock.GetUtcNow();
        await fixture.Application.ApplyResourceStateAsync(
            new ResourceStateUpdate(
                Resource, ResourceLifecycle.Active, ResourceVisibility.Public, 1, now),
            token);
        await fixture.Application.SetReactionAsync(Resource, ReactionKind.Like, token);
        await fixture.Application.SetRatingAsync(Resource, 5, "personal-note", token);
        await fixture.Application.ObserveViewAsync(Resource, token);

        var request = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = 7,
            ErasureVersion = 1,
            RequestedAtUtc = now,
        };
        Assert.True(await fixture.Application.ScheduleErasureAsync(request, token));
        Assert.False(await fixture.Application.ScheduleErasureAsync(request, token));
        await Assert.ThrowsAsync<InteractionUnauthorizedException>(() =>
            fixture.Application.SetReactionAsync(Resource, ReactionKind.Dislike, token));
        Assert.True(await fixture.Application.ProcessErasureAsync("unit-erasure-worker", token) > 0);
        Assert.Empty(fixture.Store.Reactions);
        Assert.Empty(fixture.Store.Ratings);
        Assert.Single(fixture.Store.Observations);
        var summary = Assert.Single(fixture.Store.Summaries.Values);
        Assert.Equal(0, summary.LikeCount);
        Assert.Equal(0, summary.RatingCount);
        Assert.Equal(InteractionUserErasureStage.AwaitingRawViewExpiry,
            fixture.Store.Erasures[7].Stage);
        Assert.Empty(fixture.Publisher.Erasures);

        fixture.Clock.Advance(TimeSpan.FromDays(8));
        Assert.True(await fixture.Application.ProcessErasureAsync("unit-erasure-worker", token) > 0);
        Assert.Empty(fixture.Store.Observations);
        Assert.Equal(InteractionUserErasureStage.Completed, fixture.Store.Erasures[7].Stage);
        var completed = Assert.Single(fixture.Publisher.Erasures);
        Assert.Equal(request.RequestId, completed.RequestId);
        Assert.Equal(UserErasureOutcomes.Completed, completed.Outcome);
    }

    [Fact]
    public async Task Erasure_waiting_for_raw_view_expiry_does_not_block_later_subjects()
    {
        var fixture = CreateFixture();
        var token = TestContext.Current.CancellationToken;
        var now = fixture.Clock.GetUtcNow();
        var first = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = 7,
            ErasureVersion = 1,
            RequestedAtUtc = now,
        };
        var second = new UserErasureRequested
        {
            EventId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            UserId = 8,
            ErasureVersion = 1,
            RequestedAtUtc = now.AddSeconds(1),
        };

        Assert.True(await fixture.Application.ScheduleErasureAsync(first, token));
        Assert.True(await fixture.Application.ScheduleErasureAsync(second, token));
        Assert.True(await fixture.Application.ProcessErasureAsync("unit-erasure-worker", token) > 0);
        Assert.Equal(
            InteractionUserErasureStage.AwaitingRawViewExpiry,
            fixture.Store.Erasures[first.UserId].Stage);

        Assert.True(await fixture.Application.ProcessErasureAsync("unit-erasure-worker", token) > 0);
        Assert.Equal(
            InteractionUserErasureStage.AwaitingRawViewExpiry,
            fixture.Store.Erasures[second.UserId].Stage);
        Assert.Empty(fixture.Publisher.Erasures);
    }

    private static string Value(
        IReadOnlyList<KeyValuePair<string, object?>> tags,
        string key) =>
        tags.Single(tag => tag.Key == key).Value?.ToString()
        ?? throw new InvalidOperationException($"Metric tag '{key}' was null.");

    private static Fixture CreateFixture(int maximum = 100)
    {
        var store = new InMemoryStore();
        var publisher = new CapturingPublisher();
        var cache = new PassthroughCache();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        var identity = new TestIdentity();
        var observerKeyFactory = new TestObserverKeyFactory();
        var context = new InteractionExecutionContext(
            store, publisher, cache,
            clock,
            Options.Create(new InteractionOptions { StatusBatchMaximum = maximum }));
        var application = new TestInteractionApplication(context, store, identity, observerKeyFactory);
        return new Fixture(application, store, publisher, cache, clock);
    }

    private sealed record Fixture(
        TestInteractionApplication Application,
        InMemoryStore Store,
        CapturingPublisher Publisher,
        PassthroughCache Cache,
        FixedTimeProvider Clock);

    private sealed class TestInteractionApplication(
        InteractionExecutionContext context,
        IInteractionErasureRepository erasures,
        IIdentityService identity,
        IObserverKeyFactory observerKeyFactory)
    {
        private readonly SetReactionCommand _setReaction = new(context, identity);
        private readonly SetRatingCommand _setRating = new(context, identity);
        private readonly ObserveViewCommand _observeView = new(context, identity, observerKeyFactory);
        private readonly GetViewerStatusesQuery _getViewerStatuses = new(context, identity);
        private readonly ApplyResourceStateCommand _applyResourceState = new(context);
        private readonly AggregateViewsCommand _aggregateViews = new(context);
        private readonly ReconcileCountersCommand _reconcileCounters = new(context);
        private readonly GetProjectionGapBacklogQuery _getProjectionGapBacklog = new(context);
        private readonly ScheduleUserErasureCommand _scheduleErasure = new(context, erasures);
        private readonly ProcessUserErasureCommand _processErasure = new(context, erasures);

        public Task<bool> SetReactionAsync(
            ResourceRef resource,
            ReactionKind kind,
            CancellationToken cancellationToken) =>
            _setReaction.Handle(new SetReactionDto(resource, kind), cancellationToken);

        public Task<bool> SetRatingAsync(
            ResourceRef resource,
            byte score,
            string? comment,
            CancellationToken cancellationToken) =>
            _setRating.Handle(new SetRatingDto(resource, score, comment), cancellationToken);

        public Task<bool> ObserveViewAsync(ResourceRef resource, CancellationToken cancellationToken) =>
            _observeView.Handle(resource, cancellationToken);

        public Task<IReadOnlyList<ViewerInteractionStatusDto>> GetViewerStatusesAsync(
            IReadOnlyList<ResourceRef> resources,
            CancellationToken cancellationToken) =>
            _getViewerStatuses.Handle(resources, cancellationToken);

        public Task<bool> ApplyResourceStateAsync(
            ResourceStateUpdate update,
            CancellationToken cancellationToken) =>
            _applyResourceState.Handle(update, cancellationToken);

        public Task<int> AggregateViewsAsync(int maximum, CancellationToken cancellationToken) =>
            _aggregateViews.Handle(maximum, cancellationToken);

        public Task<int> ReconcileCountersAsync(int maximum, CancellationToken cancellationToken) =>
            _reconcileCounters.Handle(maximum, cancellationToken);

        public Task<ProjectionGapBacklogDto> GetProjectionGapBacklogAsync(
            CancellationToken cancellationToken) =>
            _getProjectionGapBacklog.Handle(cancellationToken);

        public Task<bool> ScheduleErasureAsync(
            UserErasureRequested request,
            CancellationToken cancellationToken) =>
            _scheduleErasure.Handle(request, cancellationToken);

        public Task<int> ProcessErasureAsync(
            string leaseOwner,
            CancellationToken cancellationToken) =>
            _processErasure.Handle(leaseOwner, cancellationToken);
    }

    private sealed class TestIdentity : IIdentityService
    {
        public AuthUser User { get; } = new(7, "test");
    }

    private sealed class TestObserverKeyFactory : IObserverKeyFactory
    {
        public string Create(long actorId, DateTimeOffset bucketStartUtc) =>
            $"{actorId:x16}{bucketStartUtc.ToUnixTimeSeconds():x16}".PadRight(64, '0');
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class PassthroughCache : IEngagementSummaryCache
    {
        public int RemoveCount { get; private set; }

        public ValueTask<EngagementSummaryDto> GetOrCreateAsync(
            ResourceRef resource,
            long version,
            Func<CancellationToken, ValueTask<EngagementSummaryDto>> authoritativeSource,
            CancellationToken cancellationToken = default) =>
            authoritativeSource(cancellationToken);

        public ValueTask InvalidateAsync(
            ResourceRef resource,
            long version,
            CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingPublisher : IInteractionEventPublisher
    {
        private readonly object _gate = new();
        public List<ReactionChanged> Reactions { get; } = [];
        public List<RatingChanged> Ratings { get; } = [];
        public List<EngagementSummaryChanged> Summaries { get; } = [];
        public List<UserErasureCompleted> Erasures { get; } = [];
        public Task PublishReactionAsync(ReactionChanged message, CancellationToken cancellationToken) { lock (_gate) Reactions.Add(message); return Task.CompletedTask; }
        public Task PublishRatingAsync(RatingChanged message, CancellationToken cancellationToken) { lock (_gate) Ratings.Add(message); return Task.CompletedTask; }
        public Task PublishSummaryAsync(EngagementSummaryChanged message, CancellationToken cancellationToken) { lock (_gate) Summaries.Add(message); return Task.CompletedTask; }
        public Task PublishErasureCompletedAsync(UserErasureCompleted message, CancellationToken cancellationToken) { lock (_gate) Erasures.Add(message); return Task.CompletedTask; }
    }

    private sealed class InMemoryStore : IInteractionRepository, IInteractionErasureRepository
    {
        private readonly SemaphoreSlim _transaction = new(1, 1);
        private readonly HashSet<string> _viewKeys = new(StringComparer.Ordinal);
        public Dictionary<ResourceRef, ResourceState> States { get; } = [];
        public Dictionary<(ResourceRef, long), ResourceStateGap> Gaps { get; } = [];
        public Dictionary<(ResourceRef, long), Reaction> Reactions { get; } = [];
        public Dictionary<(ResourceRef, long), Rating> Ratings { get; } = [];
        public Dictionary<ResourceRef, EngagementSummary> Summaries { get; } = [];
        public List<ViewObservation> Observations { get; } = [];
        public Dictionary<long, InteractionUserErasure> Erasures { get; } = [];

        public async ValueTask<IInteractionTransaction> BeginSerializableAsync(CancellationToken cancellationToken)
        {
            await _transaction.WaitAsync(cancellationToken);
            return new Transaction(_transaction);
        }
        public ValueTask<IInteractionTransaction> BeginReadCommittedAsync(CancellationToken cancellationToken) => BeginSerializableAsync(cancellationToken);
        public Task<ResourceState?> GetResourceStateAsync(ResourceRef resource, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult(States.GetValueOrDefault(resource));
        public Task<bool> IsUserErasedAsync(long userId, CancellationToken cancellationToken) => Task.FromResult(Erasures.ContainsKey(userId));
        public Task<ResourceStateGap?> GetResourceGapAsync(ResourceRef resource, long version, CancellationToken cancellationToken) => Task.FromResult(Gaps.GetValueOrDefault((resource, version)));
        public Task<ProjectionGapBacklogState> GetProjectionGapBacklogAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectionGapBacklogState(
                Gaps.Count,
                Gaps.Count == 0 ? null : Gaps.Values.Min(item => item.ReceivedAtUtc)));
        public void AddResourceState(ResourceState state) => States.Add(state.Resource, state);
        public void AddResourceGap(ResourceStateGap gap) => Gaps.TryAdd((new ResourceRef(gap.ResourceType, gap.ResourceId), gap.SourceVersion), gap);
        public void RemoveResourceGap(ResourceStateGap gap) => Gaps.Remove((new ResourceRef(gap.ResourceType, gap.ResourceId), gap.SourceVersion));
        public Task<Reaction?> GetReactionAsync(ResourceRef resource, long actorId, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult(Reactions.GetValueOrDefault((resource, actorId)));
        public void AddReaction(Reaction reaction) => Reactions[(new ResourceRef(reaction.ResourceType, reaction.ResourceId), reaction.ActorId)] = reaction;
        public void RemoveReaction(Reaction reaction) => Reactions.Remove((new ResourceRef(reaction.ResourceType, reaction.ResourceId), reaction.ActorId));
        public Task<Rating?> GetRatingAsync(ResourceRef resource, long actorId, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult(Ratings.GetValueOrDefault((resource, actorId)));
        public void AddRating(Rating rating) => Ratings[(new ResourceRef(rating.ResourceType, rating.ResourceId), rating.ActorId)] = rating;
        public void RemoveRating(Rating rating) => Ratings.Remove((new ResourceRef(rating.ResourceType, rating.ResourceId), rating.ActorId));
        public Task<EngagementSummary?> GetSummaryAsync(ResourceRef resource, bool forUpdate, CancellationToken cancellationToken) => Task.FromResult(Summaries.GetValueOrDefault(resource));
        public void AddSummary(EngagementSummary summary) => Summaries[new ResourceRef(summary.ResourceType, summary.ResourceId)] = summary;
        public Task<PublicSummaryState> GetPublicSummaryStateAsync(ResourceRef resource, CancellationToken cancellationToken) => Task.FromResult(new PublicSummaryState(States.GetValueOrDefault(resource), Summaries.GetValueOrDefault(resource)));
        public Task<bool> InsertViewObservationAsync(ViewObservation observation, CancellationToken cancellationToken)
        {
            var key = $"{observation.ResourceType}:{observation.ResourceId}:{observation.ObserverHash}:{observation.BucketStartUtc:O}";
            var added = _viewKeys.Add(key);
            if (added)
                Observations.Add(observation);
            return Task.FromResult(added);
        }
        public Task<IReadOnlyList<ViewObservation>> LockPendingViewsAsync(DateTimeOffset notExpiredAtUtc, int maximum, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ViewObservation>>(Observations.Where(x => x.ProcessedAtUtc is null && x.ExpiresAtUtc >= notExpiredAtUtc).Take(maximum).ToArray());
        public Task<IReadOnlyList<EngagementSummary>> LockSummariesForReconciliationAsync(int maximum, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EngagementSummary>>(Summaries.Values.Take(maximum).ToArray());
        public Task<(long Likes, long Dislikes)> CountReactionsAsync(ResourceRef resource, CancellationToken cancellationToken) => Task.FromResult((Reactions.Values.LongCount(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id && x.Kind == ReactionKind.Like), Reactions.Values.LongCount(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id && x.Kind == ReactionKind.Dislike)));
        public Task<(long Count, long Total)> AggregateRatingsAsync(ResourceRef resource, CancellationToken cancellationToken) => Task.FromResult((Ratings.Values.LongCount(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id), Ratings.Values.Where(x => x.ResourceType == resource.Type && x.ResourceId == resource.Id).Sum(x => (long)x.Score)));
        public Task<IReadOnlyList<ViewerInteractionState>> GetViewerStatusesAsync(long actorId, IReadOnlyList<ResourceRef> resources, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ViewerInteractionState>>(resources.Select(resource => new ViewerInteractionState(resource, States.GetValueOrDefault(resource)?.IsPubliclyInteractable == true, Reactions.GetValueOrDefault((resource, actorId))?.Kind, Ratings.GetValueOrDefault((resource, actorId))?.Score, Reactions.GetValueOrDefault((resource, actorId))?.Version, Ratings.GetValueOrDefault((resource, actorId))?.Version)).ToArray());
        public Task<int> DeleteExpiredViewObservationsAsync(DateTimeOffset now, int maximum, CancellationToken cancellationToken)
        {
            var expired = Observations.Where(item => item.ExpiresAtUtc < now).OrderBy(item => item.ExpiresAtUtc).Take(maximum).ToArray();
            foreach (var item in expired)
            {
                Observations.Remove(item);
                _viewKeys.Remove(ViewKey(item));
            }
            return Task.FromResult(expired.Length);
        }
        public Task<int> PurgeResourcePersonalDataAsync(ResourceRef resource, CancellationToken cancellationToken)
        {
            var removed = 0;
            removed += RemoveKeys(Reactions, key => key.Item1 == resource);
            removed += RemoveKeys(Ratings, key => key.Item1 == resource);
            var observations = Observations.Where(item =>
                item.ResourceType == resource.Type && item.ResourceId == resource.Id).ToArray();
            foreach (var item in observations)
            {
                Observations.Remove(item);
                _viewKeys.Remove(ViewKey(item));
            }
            return Task.FromResult(removed + observations.Length);
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<InteractionUserErasure?> GetForUpdateAsync(long userId, CancellationToken cancellationToken) => Task.FromResult(Erasures.GetValueOrDefault(userId));
        public void Add(InteractionUserErasure erasure) => Erasures.Add(erasure.UserId, erasure);
        public Task<InteractionUserErasure?> LockNextPendingAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Erasures.Values.Where(item => (item.Stage == InteractionUserErasureStage.ActorData || (item.Stage == InteractionUserErasureStage.AwaitingRawViewExpiry && item.RawViewsExpireAtUtc <= now)) && (item.LeaseUntilUtc is null || item.LeaseUntilUtc <= now)).OrderBy(item => item.LastProcessedAtUtc ?? item.RequestedAtUtc).ThenBy(item => item.RequestedAtUtc).ThenBy(item => item.UserId).FirstOrDefault());
        public Task<IReadOnlyList<Reaction>> LockReactionsAsync(long userId, int maximum, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Reaction>>(Reactions.Values.Where(item => item.ActorId == userId).OrderBy(item => item.ResourceId, StringComparer.Ordinal).Take(maximum).ToArray());
        public Task<IReadOnlyList<Rating>> LockRatingsAsync(long userId, int maximum, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Rating>>(Ratings.Values.Where(item => item.ActorId == userId).OrderBy(item => item.ResourceId, StringComparer.Ordinal).Take(maximum).ToArray());
        public Task<bool> HasActorDataAsync(long userId, CancellationToken cancellationToken) => Task.FromResult(Reactions.Keys.Any(key => key.Item2 == userId) || Ratings.Keys.Any(key => key.Item2 == userId));

        private static int RemoveKeys<TKey, TValue>(
            Dictionary<TKey, TValue> values,
            Func<TKey, bool> predicate) where TKey : notnull
        {
            var keys = values.Keys.Where(predicate).ToArray();
            foreach (var key in keys)
                values.Remove(key);
            return keys.Length;
        }

        private static string ViewKey(ViewObservation observation) =>
            $"{observation.ResourceType}:{observation.ResourceId}:{observation.ObserverHash}:{observation.BucketStartUtc:O}";

        private sealed class Transaction(SemaphoreSlim semaphore) : IInteractionTransaction
        {
            private int _disposed;
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
