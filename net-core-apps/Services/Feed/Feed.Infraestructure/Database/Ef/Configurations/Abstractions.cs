using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarterEntities;
using System.Linq;

namespace Feed.Infra.EntityConfigurations
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
}
