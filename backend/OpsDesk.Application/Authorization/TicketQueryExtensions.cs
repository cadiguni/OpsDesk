using OpsDesk.Domain.Entities;

namespace OpsDesk.Application.Authorization;

/// <summary>
/// Filtro de visibilidade aplicado no <see cref="IQueryable{T}"/>, antes de qualquer projeção.
/// Invariante 1 do CLAUDE.md: toda leitura de chamado passa por aqui. Não existe caminho
/// em que esconder chamado de terceiro dependa de atributo de rota ou de condicional na interface.
///
/// A divisão é entre <b>equipe e solicitante</b>, e não entre os três perfis. Técnico e
/// gestor enxergam a fila inteira; solicitante enxerga apenas os próprios chamados, e
/// dentro deles nunca o que é interno.
///
/// Técnico já viu só os chamados sem responsável e os atribuídos a ele. A restrição
/// custava mais do que protegia: quem encaminhava um chamado para um colega o perdia de
/// vista no mesmo instante, ninguém conseguia reclassificar o chamado que o colega tinha
/// assumido, e cobrir uma ausência exigia passar pelo gestor. Nada disso protegia dado de
/// ninguém — é a mesma equipe, com o mesmo acesso a comentário interno. O que separa
/// visibilidade de verdade é a fronteira entre equipe e solicitante, e essa continua
/// inteira.
///
/// Todos os predicados decidem por <see cref="TicketViewer.IsStaff"/>, que é falso para
/// qualquer valor de perfil fora de técnico e gestor. É um fail closed de propósito:
/// perfil novo que ninguém lembrou de tratar cai no caso mais restrito em vez de virar
/// acesso amplo.
/// </summary>
public static class TicketQueryExtensions
{
    /// <summary>
    /// Chamados que o usuário pode enxergar: equipe vê todos, solicitante vê os próprios.
    /// </summary>
    public static IQueryable<Ticket> VisibleTo(this IQueryable<Ticket> tickets, TicketViewer viewer) =>
        viewer.IsStaff
            ? tickets
            : tickets.Where(t => t.RequesterId == viewer.UserId);

    /// <summary>
    /// Comentários que o usuário pode enxergar. Invariante 2 do CLAUDE.md: comentário com
    /// <c>IsInternal = true</c> não sai daqui para quem não é da equipe.
    /// </summary>
    public static IQueryable<TicketComment> VisibleTo(
        this IQueryable<TicketComment> comments, TicketViewer viewer) =>
        viewer.IsStaff
            ? comments
            : comments.Where(c => !c.IsInternal && c.Ticket.RequesterId == viewer.UserId);

    /// <summary>
    /// Anexos que o usuário pode enxergar.
    ///
    /// Duas regras empilhadas. A primeira é o estado pendente: anexo ainda sem chamado é
    /// arquivo solto no formulário de quem o enviou, e não existe para mais ninguém —
    /// nem para o gestor. É o que fecha a janela em que um print destinado a uma nota
    /// interna ficaria legível para o solicitante entre o envio do arquivo e o envio do
    /// comentário.
    ///
    /// A segunda é a do chamado, com o mesmo corte por <c>IsInternal</c> dos comentários:
    /// invariante 2 do CLAUDE.md vale para o anexo da nota interna tanto quanto para o
    /// texto dela.
    /// </summary>
    public static IQueryable<TicketAttachment> VisibleTo(
        this IQueryable<TicketAttachment> attachments, TicketViewer viewer) =>
        viewer.IsStaff
            ? attachments.Where(a => a.TicketId != null || a.UploadedById == viewer.UserId)
            : attachments.Where(a =>
                a.TicketId == null
                    ? a.UploadedById == viewer.UserId
                    : !a.IsInternal && a.Ticket!.RequesterId == viewer.UserId);

    /// <summary>Histórico segue a visibilidade do chamado a que pertence.</summary>
    public static IQueryable<TicketHistory> VisibleTo(
        this IQueryable<TicketHistory> history, TicketViewer viewer) =>
        viewer.IsStaff
            ? history
            : history.Where(h => h.Ticket.RequesterId == viewer.UserId);

    /// <summary>
    /// Comentários de um chamado já filtrados por visibilidade. Atalho para o caso mais
    /// comum, que é a tela de detalhe.
    /// </summary>
    public static IQueryable<TicketComment> OfTicketVisibleTo(
        this IQueryable<TicketComment> comments, Guid ticketId, TicketViewer viewer) =>
        comments.Where(c => c.TicketId == ticketId).VisibleTo(viewer);
}
