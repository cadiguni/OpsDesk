namespace OpsDesk.Domain.Enums;

/// <summary>Evento da trilha de auditoria de usuário. Persistido como string.</summary>
public enum UserAuditAction
{
    /// <summary>Conta criada — pelo próprio portal ou pelo gestor, conforme o autor.</summary>
    Created,

    RoleChanged,

    Deactivated,

    Reactivated,

    /// <summary>A própria pessoa trocou a senha.</summary>
    PasswordChanged,

    /// <summary>O gestor gerou senha provisória.</summary>
    PasswordReset,

    /// <summary>A pessoa definiu a senha pelo link do e-mail.</summary>
    PasswordSetByLink,

    /// <summary>A conta passou a entrar com a conta Microsoft; o valor novo é o <c>oid</c> do Entra ID.</summary>
    MicrosoftAccountLinked
}
