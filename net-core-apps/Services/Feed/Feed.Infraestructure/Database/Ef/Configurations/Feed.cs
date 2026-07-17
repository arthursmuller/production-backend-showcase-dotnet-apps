using Feed.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Feed.Infra.EntityConfigurations
{
    public class UserConnectionsFeedConfiguration : BaseConfiguration, IEntityTypeConfiguration<UserConnectionsFeed>
    {
        public void Configure(EntityTypeBuilder<UserConnectionsFeed> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.HasIndex(m => m.UserId);

            builder.Property(m => m.UserId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.Description)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.EventTrigger)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.DateSeen)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            
            builder.HasOne(e => e.Post);
            builder.Navigation(e => e.Post).AutoInclude(true);
        }
    }

    public class PostConfiguration : BaseConfiguration, IEntityTypeConfiguration<Post>
    {
        public void Configure(EntityTypeBuilder<Post> builder)
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.UserId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.EntityId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.Type)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.IsPublicViewable)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
   
    public class FeedVisibilityLevelsConfiguration : IEntityTypeConfiguration<FeedVisibilityLevel>
    {
        public void Configure(EntityTypeBuilder<FeedVisibilityLevel> modelBuilder)
        {
            modelBuilder.ToTable("VisbilityLevels");

            modelBuilder.HasKey(o => o.Id);

            modelBuilder.Property(o => o.Id)
                .ValueGeneratedNever()
                .IsRequired();

            modelBuilder.Property(o => o.Name)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.Property(o => o.Description)
                .HasMaxLength(200)
                .IsRequired();

            modelBuilder.HasData(FeedVisibilityLevel.List());
        }
    }

}
