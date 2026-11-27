namespace OpsDesk.Domain.Enums;

/// <summary>Evento da trilha de auditoria de usuário. Persistido como string.</summary>
public enum UserAuditAction
{
    /// <summary>Conta criada — pelo próprio portal ou pelo administrador, conforme o autor.</summary>
    Created,

    RoleChanged,

    Deactivated,

    Reactivated,

    /// <summary>A própria pessoa trocou a senha.</summary>
    PasswordChanged,

    /// <summary>O administrador gerou senha provisória.</summary>
    PasswordReset,

    /// <summary>A pessoa definiu a senha pelo link do e-mail.</summary>
    PasswordSetByLink,

    /// <summary>A conta passou a entrar com a conta Microsoft; o valor novo é o <c>oid</c> do Entra ID.</summary>
    MicrosoftAccountLinked,

    /// <summary>
    /// Cargo trocado. Os valores são os <b>nomes</b> do cargo no momento da troca, e não
    /// os identificadores: a trilha conta o que se via naquele dia, mesmo que o cargo seja
    /// renomeado depois.
    /// </summary>
    JobTitleChanged,

    /// <summary>Nome exibido trocado pelo administrador. Os valores são o nome antigo e o novo.</summary>
    NameChanged
}
