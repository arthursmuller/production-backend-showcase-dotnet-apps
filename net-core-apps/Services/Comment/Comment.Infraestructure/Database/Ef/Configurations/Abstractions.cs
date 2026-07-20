using Comment.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarterEntities;

namespace Comment.Infra.EntityConfigurations
{
    public abstract class BaseConfiguration
    {
        protected static void ConfigureBaseEntity<T>(
            EntityTypeBuilder<T> builder,
            string tableName,
            bool generateId = true)
            where T : BaseEntity
        {
            builder.ToTable(tableName);
            builder.HasKey(entity => entity.Id).HasName($"PK_{tableName}");

            var id = builder.Property(entity => entity.Id)
                .HasField("_id")
                .HasColumnName("Id");
            if (!generateId)
                id.ValueGeneratedNever();

            builder.Property(entity => entity.CreatedDate)
                .HasField("_createdDate")
                .HasColumnName("CreatedDate")
                .IsRequired()
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.Property(entity => entity.UpdateDate)
                .HasColumnName("UpdateDate")
                .IsRequired();
            builder.Property(entity => entity.UserUpdate)
                .HasColumnName("UserUpdate")
                .IsUnicode(false)
                .HasMaxLength(10);
        }

        protected static void ConfigureOwnedUser<TOwner>(
            EntityTypeBuilder<TOwner> builder,
            System.Linq.Expressions.Expression<System.Func<TOwner, User?>> navigation)
            where TOwner : class
        {
            builder.OwnsOne(navigation, user =>
            {
                user.Property(value => value.Id)
                    .HasColumnName("User_Id")
                    .IsRequired();
                user.Property(value => value.FullName)
                    .HasColumnName("User_FullName")
                    .HasMaxLength(CommentFieldLimits.UserName)
                    .IsRequired();
            });
            builder.Navigation(navigation).IsRequired();
        }
    }

}
