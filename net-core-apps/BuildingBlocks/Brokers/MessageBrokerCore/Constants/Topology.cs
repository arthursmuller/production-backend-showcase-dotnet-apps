namespace MessageBrokerCore.Constants;

/// <summary>
/// RabbitMQ topology used by the retained Comment, Feed, and Following services
/// and their shared provider adapters.
/// </summary>
public static class Topology
{
    public static readonly UserDomainTopology User = new();
    public static readonly CategoriesDomainTopology Categories = new();
    public static readonly FeedDomainTopology Feed = new();
    public static readonly NotificationDomainTopology Notification = new();
    public static readonly PostsDomainTopology Posts = new();
    public static readonly FollowingDomainTopology Following = new();
    public static readonly CommentDomainTopology Comment = new();

    public const string SseFanoutExchange = "sse.fanout";
}

public sealed record RoutingTopology(string RoutingKey);

public sealed record QueueRoutingTopology<TQueues>(string RoutingKey, TQueues Queues);

public sealed record FeedQueues<TQueue>(TQueue Feed);

public sealed record CommentQueues<TQueue>(TQueue Comment);

public sealed record FollowingQueues<TQueue>(TQueue Following);

public sealed record UserConnectionsAddQueue(string UserConnectionsAdd);

public sealed record UserConnectionsRemoveQueue(string UserConnectionsRemove);

public sealed record ChangeVisibilityQueue(string ChangeVisibility);

public sealed record AddItemsQueue(string AddItems);

public sealed record RemoveItemsQueue(string RemoveItems);

public sealed record ShareCreatedQueue(string ShareCreated);

public sealed record CommentCreatedQueue(string CommentCreated);

public sealed record BookmarkCreatedQueue(string BookmarkCreated);

public sealed record FollowingSubscriptionQueue(string FollowingQueue);

public sealed class UserDomainTopology
{
    public readonly string EventsExchange = "user.events";

    public readonly RoutingTopology UpdateAnalytics = new("user.update.analytics");
}

public sealed class CategoriesDomainTopology
{
    public readonly string EventsExchange = "categories.events";

    public readonly RoutingTopology Mentions = new("categories.mentions");

    public readonly RoutingTopology UpdateFollow = new("categories.update.follow");
}

public sealed class FeedDomainTopology
{
    public readonly string EventsExchange = "feed.events";

    public readonly QueueRoutingTopology<FeedQueues<UserConnectionsAddQueue>> UserConnectionsAdd = new(
        "feed.user.connections.add",
        new(new("feed_user_connections_add_queue")));

    public readonly QueueRoutingTopology<FeedQueues<UserConnectionsRemoveQueue>> UserConnectionsRemove = new(
        "feed.user.connections.remove",
        new(new("feed_user_connections_remove_queue")));

    public readonly QueueRoutingTopology<FeedQueues<ChangeVisibilityQueue>> ChangeVisibility = new(
        "feed.change.visibility",
        new(new("feed_change_visibility_queue")));

    public readonly QueueRoutingTopology<FeedQueues<AddItemsQueue>> AddItems = new(
        "feed.add.items",
        new(new("feed_add_items_queue")));

    public readonly QueueRoutingTopology<FeedQueues<RemoveItemsQueue>> RemoveItems = new(
        "feed.remove.items",
        new(new("feed_remove_items_queue")));
}

public sealed class NotificationDomainTopology
{
    public readonly string EventsExchange = "notification.events";

    public readonly RoutingTopology Add = new("notification.add");
}

public sealed class PostsDomainTopology
{
    public readonly string EventsExchange = "subscription.events";
}

public sealed class FollowingDomainTopology
{
    public readonly QueueRoutingTopology<FollowingQueues<FollowingSubscriptionQueue>> SubscriptionFeatureNetworking = new(
        "subscription.feature.networking",
        new(new("following_subscription_feature_networking_queue")));
}

public sealed class CommentDomainTopology
{
    public readonly QueueRoutingTopology<CommentQueues<ShareCreatedQueue>> ShareCreated = new(
        "comment.share.created",
        new(new("comment_share_created_queue")));

    public readonly QueueRoutingTopology<CommentQueues<CommentCreatedQueue>> CommentCreated = new(
        "comment.comment.created",
        new(new("comment_comment_created_queue")));

    public readonly QueueRoutingTopology<CommentQueues<BookmarkCreatedQueue>> BookmarkCreated = new(
        "comment.bookmark.created",
        new(new("comment_bookmark_created_queue")));
}
