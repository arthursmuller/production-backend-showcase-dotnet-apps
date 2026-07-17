using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarterEntities;
using System.Linq;

namespace Comment.Infra.EntityConfigurations
{
    public abstract class BaseConfiguration
    {
        protected string setPropriedadesDeEntidadeBase<T>(EntityTypeBuilder<T> modelBuilder, bool gerarId = true) where T : BaseEntity
        {
            string entity = modelBuilder.Metadata.Name
                .Split(".")
                .Last();

            modelBuilder.ToTable(entity);
            modelBuilder.Property(model => model.UserUpdate).IsUnicode(false).HasMaxLength(10);

            modelBuilder.Property(e => e.CreatedDate)
                .HasField("_createdDate")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            modelBuilder.HasKey(u => u.Id);

            var idProp = modelBuilder.Property(u => u.Id);
            idProp
                .HasField("_id")
                .HasColumnName("Id");

            if (!gerarId)
                idProp.ValueGeneratedNever();

            return entity;
        }
    }
    public abstract class StarterConfiguration : BaseConfiguration
    {
        protected void setRatingProps<TEntity, TLike, TDislike, TRatingLevel, TReport, TShare, TComment>(EntityTypeBuilder<TEntity> builder)
            where TEntity : CommomRating<TLike, TDislike, TRatingLevel, TReport, TShare, TComment>
            where TLike : CommomLike
            where TDislike : CommomDisLike
            where TRatingLevel : CommomRatingLevel
            where TReport : CommomReport
            where TShare : CommomShare
            where TComment : CommomComment
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.HasMany(e => e.Shares).WithOne().IsRequired().OnDelete(DeleteBehavior.ClientCascade);
            builder.HasMany(e => e.Comments).WithOne().IsRequired().OnDelete(DeleteBehavior.ClientCascade);
            builder.HasMany(e => e.Likes).WithOne().IsRequired().OnDelete(DeleteBehavior.ClientCascade);
            builder.HasMany(e => e.Dislikes).WithOne().IsRequired().OnDelete(DeleteBehavior.ClientCascade);
            builder.HasMany(e => e.Reports).WithOne().IsRequired().OnDelete(DeleteBehavior.ClientCascade);
            builder.HasMany(e => e.Ratings).WithOne().IsRequired().OnDelete(DeleteBehavior.ClientCascade);
        }
        protected void setRatingAnalyticsProps<TEntity, TAverageRating>(EntityTypeBuilder<TEntity> builder)
            where TEntity : CommomSocialRatingAnalytics
            where TAverageRating : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.TotalRatings)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TotalReports)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TotalLikes)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TotalDislikes)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TotalComments)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TotalShares)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.TotalViews)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.RatingsTrueCount)
               .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.Property(m => m.AverageRating)
                .HasValueGenerator<TAverageRating>()
                .UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);

            /*builder.Property(e => e.UpdateDate).IsConcurrencyToken();*/
        }
    }

    public abstract class SocialInteractionsConfiguration : BaseConfiguration
    {
        protected void setCommomLike<T>(EntityTypeBuilder<T> builder)
               where T : CommomLike
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.PostId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.OwnsOne(m => m.User);
        }
        protected void setCommomDislike<T>(EntityTypeBuilder<T> builder)
            where T : CommomDisLike
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.PostId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.OwnsOne(m => m.User);
        }
        protected void setCommomRatingLevel<T>(EntityTypeBuilder<T> builder)
            where T : CommomRatingLevel
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.PostId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.Comment)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            
            builder.HasOne(m => m.Level).WithMany().HasForeignKey("_levelId");
            builder.OwnsOne(m => m.User);
            builder.Navigation(e => e.Level).AutoInclude(true);
        }
        protected void setCommomReport<T>(EntityTypeBuilder<T> builder)
            where T : CommomReport
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.PostId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.Reply)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.OwnsOne(m => m.User);
        }
        protected void setCommomShare<T>(EntityTypeBuilder<T> builder)
            where T : CommomShare
        {
            setPropriedadesDeEntidadeBase(builder);

            builder.Property(m => m.PostId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.UserId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
        protected void setCommomComment<T>(EntityTypeBuilder<T> builder)
            where T : CommomComment
        {
            setPropriedadesDeEntidadeBase(builder);
            builder.Property(m => m.PostId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(m => m.UserId)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
