using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Faz o trabalho em segundo plano agir em nome de uma pessoa, dentro do escopo atual.
///
/// Existe para a caixa de entrada: a resposta que chega por e-mail é da pessoa que a
/// mandou, e sem isto o histórico registraria "Sistema" como autor, e a notificação
/// trataria a ação como de ninguém — mandando à pessoa um e-mail sobre a resposta que
/// ela mesma acabou de escrever.
///
/// Vale para o escopo de injeção em que é chamado, e só para ele. Chame sempre num escopo
/// criado para o trabalho — nunca no escopo de uma requisição, onde trocaria a identidade
/// de quem está autenticado.
/// </summary>
public interface IActorScope
{
    void ActAs(Guid userId, UserRole role);
}
