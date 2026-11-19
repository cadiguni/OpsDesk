using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Recado para a trilha de auditoria, não dado.
        builder.Ignore(u => u.PasswordChangeReason);

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(30).IsRequired();

        // Endereço de e-mail é identificador de login e, a partir da 2.0, chave de
        // identificação do remetente. Único, sem depender de comparação na aplicação.
        builder.HasIndex(u => u.Email).IsUnique();

        // Uma identidade do Entra ID, uma conta. O índice é que garante isso quando dois
        // primeiros logins da mesma pessoa chegam ao mesmo tempo.
        builder.Property(u => u.EntraObjectId).HasMaxLength(64);
        builder.HasIndex(u => u.EntraObjectId).IsUnique().HasFilter("entra_object_id IS NOT NULL");

        builder.Ignore(u => u.IsStaff);
    }
}
