using Following.Domain.AggregatesModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Following.Infra.EntityConfigurations
{
    public abstract class BaseConfiguration
    {
        protected static void ConfigureBase<T>(
            EntityTypeBuilder<T> builder,
            string tableName,
            bool generateId = true)
            where T : FollowingEntity
        {
            builder.ToTable(tableName);
            builder.HasKey(entity => entity.Id);

            var id = builder.Property(entity => entity.Id)
                .HasField("_id")
                .HasColumnName("Id")
                .IsRequired();
            if (generateId)
                id.ValueGeneratedOnAdd();
            else
                id.ValueGeneratedNever();

            builder.Property(entity => entity.CreatedDate)
                .HasField("_createdDate")
                .HasColumnName("CreatedDate")
                .HasColumnType("datetime(6)")
                .IsRequired();

            builder.Property(entity => entity.UpdateDate)
                .HasColumnName("UpdateDate")
                .HasColumnType("datetime(6)")
                .IsRequired();

            builder.Property(entity => entity.UserUpdate)
                .HasColumnName("UserUpdate")
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("SISTEMA")
                .IsRequired();

            builder.Property(entity => entity.Version)
                .HasColumnName("Version")
                .HasColumnType("bigint")
                .HasDefaultValue(1L)
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken()
                .IsRequired();
        }
    }
}
