using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Following.Infra.EntityConfigurations
{
    // User and category identities are owned by other services. These scalar IDs
    // deliberately have no relational FK: cross-service foreign keys would couple
    // deployment, deletion, and availability boundaries.
    public sealed class FriendRequestConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<FriendRequest>
    {
        public void Configure(EntityTypeBuilder<FriendRequest> builder)
        {
            ConfigureBase(builder, "FriendRequest");
            builder.Property(entity => entity.SenderUserId)
                .HasColumnName("User_Id")
                .IsRequired();
            builder.Property(entity => entity.SenderDisplayName)
                .HasColumnName("User_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);
            builder.Property(entity => entity.RecipientUserId)
                .HasColumnName("Friend_Id")
                .IsRequired();
            builder.Property(entity => entity.RecipientDisplayName)
                .HasColumnName("Friend_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);
            builder.Property(entity => entity.Status)
                .HasColumnName("Status")
                .HasConversion<byte>()
                .HasColumnType("tinyint unsigned")
                .HasDefaultValue(FriendRequestStatus.Pending)
                .IsRequired();

            builder.Property<int>("PairMinUserId")
                .HasComputedColumnSql("LEAST(`User_Id`, `Friend_Id`)", stored: true);
            builder.Property<int>("PairMaxUserId")
                .HasComputedColumnSql("GREATEST(`User_Id`, `Friend_Id`)", stored: true);
            builder.Property<int?>("ActivePairMinUserId")
                .HasComputedColumnSql(
                    "CASE WHEN `Status` = 0 THEN LEAST(`User_Id`, `Friend_Id`) ELSE NULL END",
                    stored: true);
            builder.Property<int?>("ActivePairMaxUserId")
                .HasComputedColumnSql(
                    "CASE WHEN `Status` = 0 THEN GREATEST(`User_Id`, `Friend_Id`) ELSE NULL END",
                    stored: true);

            builder.HasIndex("ActivePairMinUserId", "ActivePairMaxUserId")
                .IsUnique()
                .HasDatabaseName("UX_FriendRequest_ActivePair");
            builder.HasIndex("PairMinUserId", "PairMaxUserId", nameof(FriendRequest.Status))
                .HasDatabaseName("IX_FriendRequest_Pair_Status");
            builder.HasIndex(entity => new { entity.SenderUserId, entity.Id })
                .HasDatabaseName("IX_FriendRequest_Erase_Sender");
            builder.HasIndex(entity => new { entity.RecipientUserId, entity.Id })
                .HasDatabaseName("IX_FriendRequest_Erase_Recipient");
            builder.HasIndex(entity => new
            {
                entity.SenderUserId,
                entity.Status,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("IX_FriendRequest_Sender_Status_Cursor");
            builder.HasIndex(entity => new
            {
                entity.RecipientUserId,
                entity.Status,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("IX_FriendRequest_Recipient_Status_Cursor");

            builder.ToTable("FriendRequest", table =>
            {
                table.HasCheckConstraint(
                    "CK_FriendRequest_PositiveUsers",
                    "`User_Id` > 0 AND `Friend_Id` > 0");
                table.HasCheckConstraint(
                    "CK_FriendRequest_NoSelf",
                    "`User_Id` <> `Friend_Id`");
                table.HasCheckConstraint(
                    "CK_FriendRequest_Status",
                    "`Status` BETWEEN 0 AND 3");
            });
        }
    }

    public sealed class FriendshipConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<Friendship>
    {
        public void Configure(EntityTypeBuilder<Friendship> builder)
        {
            ConfigureBase(builder, "Friendship");
            builder.Property(entity => entity.UserId)
                .HasColumnName("User_Id")
                .IsRequired();
            builder.Property(entity => entity.UserDisplayName)
                .HasColumnName("User_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);
            builder.Property(entity => entity.FriendUserId)
                .HasColumnName("Friend_Id")
                .IsRequired();
            builder.Property(entity => entity.FriendDisplayName)
                .HasColumnName("Friend_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);

            builder.Property<int>("PairMinUserId")
                .HasComputedColumnSql("LEAST(`User_Id`, `Friend_Id`)", stored: true);
            builder.Property<int>("PairMaxUserId")
                .HasComputedColumnSql("GREATEST(`User_Id`, `Friend_Id`)", stored: true);

            builder.HasIndex(entity => new { entity.UserId, entity.FriendUserId })
                .IsUnique()
                .HasDatabaseName("UX_Friendship_Direction");
            builder.HasIndex("PairMinUserId", "PairMaxUserId")
                .HasDatabaseName("IX_Friendship_NormalizedPair");
            builder.HasIndex(entity => new { entity.UserId, entity.Id })
                .HasDatabaseName("IX_Friendship_Erase_User");
            builder.HasIndex(entity => new { entity.FriendUserId, entity.Id })
                .HasDatabaseName("IX_Friendship_Erase_Friend");
            builder.HasIndex(entity => new
            {
                entity.UserId,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_Friendship_User_Cursor");
            builder.HasIndex(entity => new
            {
                entity.FriendUserId,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_Friendship_Reverse_Cursor");

            builder.ToTable("Friendship", table =>
            {
                table.HasCheckConstraint(
                    "CK_Friendship_PositiveUsers",
                    "`User_Id` > 0 AND `Friend_Id` > 0");
                table.HasCheckConstraint(
                    "CK_Friendship_NoSelf",
                    "`User_Id` <> `Friend_Id`");
            });
        }
    }

    public sealed class FollowConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<Follow>
    {
        public void Configure(EntityTypeBuilder<Follow> builder)
        {
            ConfigureBase(builder, "Follow");
            builder.Property(entity => entity.FollowerUserId)
                .HasColumnName("User_Id")
                .IsRequired();
            builder.Property(entity => entity.FollowerDisplayName)
                .HasColumnName("User_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);
            builder.Property(entity => entity.FollowedUserId)
                .HasColumnName("Followed_Id")
                .IsRequired();
            builder.Property(entity => entity.FollowedDisplayName)
                .HasColumnName("Followed_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);
            builder.Property(entity => entity.IsFollowedBack)
                .HasColumnName("IsFollowedBack")
                .IsRequired();

            builder.HasIndex(entity => new { entity.FollowerUserId, entity.FollowedUserId })
                .IsUnique()
                .HasDatabaseName("UX_Follow_Direction");
            builder.HasIndex(entity => new { entity.FollowerUserId, entity.Id })
                .HasDatabaseName("IX_Follow_Erase_Follower");
            builder.HasIndex(entity => new { entity.FollowedUserId, entity.Id })
                .HasDatabaseName("IX_Follow_Erase_Followed");
            builder.HasIndex(entity => new
            {
                entity.FollowerUserId,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_Follow_Follower_Cursor");
            builder.HasIndex(entity => new
            {
                entity.FollowedUserId,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_Follow_Followed_Cursor");

            builder.ToTable("Follow", table =>
            {
                table.HasCheckConstraint(
                    "CK_Follow_PositiveUsers",
                    "`User_Id` > 0 AND `Followed_Id` > 0");
                table.HasCheckConstraint(
                    "CK_Follow_NoSelf",
                    "`User_Id` <> `Followed_Id`");
            });
        }
    }

    public sealed class CategoryFollowConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<CategoryFollow>
    {
        public void Configure(EntityTypeBuilder<CategoryFollow> builder)
        {
            ConfigureBase(builder, "CategoryFollow");
            builder.Property(entity => entity.UserId)
                .HasColumnName("User_Id")
                .IsRequired();
            builder.Property(entity => entity.UserDisplayName)
                .HasColumnName("User_FullName")
                .HasMaxLength(FollowingFieldLimits.DisplayName);
            builder.Property(entity => entity.CategoryId)
                .HasColumnName("Followed_Id")
                .IsRequired();
            builder.Property(entity => entity.CategoryName)
                .HasColumnName("Followed_Name")
                .HasMaxLength(FollowingFieldLimits.CategoryName);

            builder.HasIndex(entity => new { entity.UserId, entity.CategoryId })
                .IsUnique()
                .HasDatabaseName("UX_CategoryFollow_User_Category");
            builder.HasIndex(entity => new { entity.UserId, entity.Id })
                .HasDatabaseName("IX_CategoryFollow_Erase_User");
            builder.HasIndex(entity => new
            {
                entity.UserId,
                entity.CreatedDate,
                entity.Id
            })
                .IsDescending(false, true, true)
                .HasDatabaseName("IX_CategoryFollow_User_Cursor");
            builder.HasIndex(entity => new { entity.CategoryId, entity.UserId })
                .HasDatabaseName("IX_CategoryFollow_Category_User");

            builder.ToTable("CategoryFollow", table =>
            {
                table.HasCheckConstraint(
                    "CK_CategoryFollow_PositiveIds",
                    "`User_Id` > 0 AND `Followed_Id` > 0");
            });
        }
    }

    public sealed class UserRelationAnalyticsConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<UserRelationAnalytics>
    {
        public void Configure(EntityTypeBuilder<UserRelationAnalytics> builder)
        {
            ConfigureBase(builder, "UserRelationAnalytics", generateId: false);

            builder.HasKey(entity => new { entity.Id, entity.ShardId });
            builder.Property(entity => entity.ShardId)
                .HasColumnType("tinyint unsigned")
                .IsRequired();

            builder.ToTable("UserRelationAnalytics", table =>
            {
                table.HasCheckConstraint("CK_UserRelationAnalytics_PositiveId", "`Id` > 0");
                table.HasCheckConstraint(
                    "CK_UserRelationAnalytics_Shard",
                    $"`ShardId` BETWEEN 0 AND {UserRelationAnalytics.ShardCount - 1}");
                table.HasCheckConstraint(
                    "CK_UserRelationAnalytics_NonNegative",
                    "`TotalFriends` >= 0 AND `TotalFollowers` >= 0 " +
                    "AND `TotalFollowing` >= 0 AND `TotalCategoriesFollowed` >= 0 " +
                    "AND `TotalFriendsRequests` >= 0");
            });
        }
    }

    public sealed class FriendshipSubscriptionFeatureConfiguration
        : BaseConfiguration, IEntityTypeConfiguration<FriendshipSubscriptionFeature>
    {
        public void Configure(EntityTypeBuilder<FriendshipSubscriptionFeature> builder)
        {
            ConfigureBase(builder, "FriendshipSubscriptionFeature");

            builder.Property(entity => entity.OwnerId).IsRequired();
            builder.Property(entity => entity.RequesterId).IsRequired();
            builder.Property(entity => entity.SubscriptionId)
                .HasMaxLength(FollowingFieldLimits.SubscriptionId)
                .IsRequired();
            builder.Property(entity => entity.EndDate)
                .HasColumnType("datetime(6)")
                .IsRequired();

            builder.HasIndex(entity => new
            {
                entity.OwnerId,
                entity.RequesterId,
                entity.SubscriptionId
            })
                .IsUnique()
                .HasDatabaseName("UX_SubscriptionFeature_Owner_Requester_Subscription");
            builder.HasIndex(entity => new { entity.EndDate, entity.Id })
                .HasDatabaseName("IX_SubscriptionFeature_Expiry");
            builder.HasIndex(entity => new { entity.OwnerId, entity.Id })
                .HasDatabaseName("IX_Subscription_Erase_Owner");
            builder.HasIndex(entity => new { entity.RequesterId, entity.Id })
                .HasDatabaseName("IX_Subscription_Erase_Requester");

            builder.ToTable("FriendshipSubscriptionFeature", table =>
            {
                table.HasCheckConstraint(
                    "CK_SubscriptionFeature_PositiveUsers",
                    "`OwnerId` > 0 AND `RequesterId` > 0");
                table.HasCheckConstraint(
                    "CK_SubscriptionFeature_NoSelf",
                    "`OwnerId` <> `RequesterId`");
            });
        }
    }
}
