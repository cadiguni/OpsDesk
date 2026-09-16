namespace OpsDesk.Domain.Common;

/// <summary>Entidade cuja criação é datada pelo interceptor, não pelos serviços.</summary>
public interface IHasCreatedAt
{
    DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Entidade cuja última alteração é datada pelo interceptor.</summary>
public interface IHasUpdatedAt : IHasCreatedAt
{
    DateTimeOffset UpdatedAt { get; set; }
}
