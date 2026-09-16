using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(30).IsRequired();

        // Endereço de e-mail é identificador de login e, a partir da 2.0, chave de
        // identificação do remetente. Único, sem depender de comparação na aplicação.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Ignore(u => u.IsStaff);
    }
}
