using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Following.Infra.EntityConfigurations
{
    public class FriendRequestConfiguration : BaseConfiguration, IEntityTypeConfiguration<FriendRequest>
    {
        public void Configure(EntityTypeBuilder<FriendRequest> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.Approved)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            
            builder.OwnsOne(m => m.User); 
            builder.OwnsOne(m => m.Friend);
        }
    }
 
    public class FriendshipConfiguration : BaseConfiguration, IEntityTypeConfiguration<Friendship>
    {
        public void Configure(EntityTypeBuilder<Friendship> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.OwnsOne(m => m.User);
            builder.OwnsOne(m => m.Friend);
        }
    }
    public class FollowConfiguration : BaseConfiguration, IEntityTypeConfiguration<Follow>
    {
        public void Configure(EntityTypeBuilder<Follow> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.IsFollowedBack)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.OwnsOne(m => m.User);
            builder.OwnsOne(m => m.Followed);
        }
    }

    public class CategoryFollowConfiguration : BaseConfiguration, IEntityTypeConfiguration<CategoryFollow>
    {
        public void Configure(EntityTypeBuilder<CategoryFollow> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.OwnsOne(m => m.User);
            builder.OwnsOne(m => m.Followed);
        }
    }
    public class UserRelationAnalyticsConfiguration : BaseConfiguration, IEntityTypeConfiguration<UserRelationAnalytics>
    {

        public void Configure(EntityTypeBuilder<UserRelationAnalytics> builder)
        {
            setPropriedadesDeEntidadeBase(builder);
        }
    }

    public class FriendshipSubscriptionFeatureConfiguration : BaseConfiguration, IEntityTypeConfiguration<FriendshipSubscriptionFeature>
    {
        public void Configure(EntityTypeBuilder<FriendshipSubscriptionFeature> builder)
        {
            setPropriedadesDeEntidadeBase(builder);
            builder.Property(m => m.OwnerId)
              .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.RequesterId)
              .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.SubscriptionId)
              .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.EndDate)
              .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
