using System.Net;
using OpsDesk.Application.Notifications;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Quem é avisado de quê. As regras são do README, seção 18 (versão 1.2): o solicitante
/// recebe e-mail, o responsável recebe no portal, ninguém é avisado da própria ação — e a
/// invariante 2 vale no canal novo: nota interna nunca vira e-mail.
/// </summary>
public class NotificationPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 14, 0, 0, TimeSpan.Zero);
    private static readonly EmailChannel Email = new("https://opsdesk.empresa.com");

    private static readonly NotificationPerson Requester =
        new(Guid.NewGuid(), "Solicitante Silva", "solicitante@empresa.com", UserRole.Requester, true);

    private static readonly NotificationPerson Technician =
        new(Guid.NewGuid(), "Técnica Ana", "ana@empresa.com", UserRole.Technician, true);

    private static readonly NotificationPerson OtherTechnician =
        new(Guid.NewGuid(), "Técnico Bruno", "bruno@empresa.com", UserRole.Technician, true);

    private static readonly NotificationPerson Manager =
        new(Guid.NewGuid(), "Gestora Carla", "carla@empresa.com", UserRole.Manager, true);

    private static TicketChange Change(
        NotificationPerson? assignee = null,
        TicketStatus current = TicketStatus.InProgress,
        TicketStatus? previous = null,
        bool assigneeChanged = false,
        NotificationPerson? previousAssignee = null,
        params AddedComment[] comments) => new(
        Guid.NewGuid(),
        "OPS-000123",
        "VPN não conecta",
        Requester,
        assignee,
        assigneeChanged,
        previousAssignee,
        current,
        previous,
        comments);

    private static AddedComment Public(NotificationPerson author, string content = "Pode testar agora?") =>
        new(author.Id, author.Name, content, IsInternal: false);

    private static AddedComment Internal(NotificationPerson author, string content = "Suspeito do certificado.") =>
        new(author.Id, author.Name, content, IsInternal: true);

    // ----- E-mail para o solicitante -----

    [Fact]
    public void Resposta_publica_da_equipe_vira_email_para_o_solicitante()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Public(Technician, "Reiniciei o concentrador.")), Technician.Id, Now, Email);

        var email = Assert.Single(planned.Emails);
        Assert.Equal(Requester.Email, email.ToAddress);
        Assert.Contains("[OPS-000123]", email.Subject);
        Assert.Contains("Reiniciei o concentrador.", email.HtmlBody);
        Assert.Equal(OutboundEmailStatus.Pending, email.Status);
    }

    [Fact]
    public void Nota_interna_nunca_vira_email()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Internal(Technician)), Technician.Id, Now, Email);

        Assert.Empty(planned.Emails);
    }

    [Fact]
    public void Nota_interna_nao_entra_no_email_de_outro_evento_da_mesma_gravacao()
    {
        // Nota interna e mudança de status juntas: o status gera e-mail, a nota não pode
        // pegar carona nele.
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.Resolved, TicketStatus.InProgress,
                comments: Internal(Technician, "Senha do roteador: 1234")),
            Technician.Id, Now, Email);

        var email = Assert.Single(planned.Emails);
        Assert.DoesNotContain("1234", email.HtmlBody);
        Assert.DoesNotContain("roteador", email.HtmlBody);
    }

    [Fact]
    public void Nota_interna_nao_vira_email_nem_para_solicitante_da_equipe()
    {
        // A regra é do canal, não da pessoa: caixa de e-mail é encaminhada e lida em
        // lugares que o portal não controla.
        var staffRequester = Requester with { Role = UserRole.Technician };
        var change = Change(Technician, comments: Internal(Technician)) with { Requester = staffRequester };

        Assert.Empty(NotificationPlanner.Plan(change, Technician.Id, Now, Email).Emails);
    }

    [Fact]
    public void Comentario_e_mudanca_de_status_na_mesma_gravacao_viram_um_email_so()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.Closed, TicketStatus.InProgress, comments: Public(Technician, "Concluído.")),
            Technician.Id, Now, Email);

        // Acento vira entidade HTML (&#237;), que todo cliente de e-mail exibe normalmente.
        var body = WebUtility.HtmlDecode(Assert.Single(planned.Emails).HtmlBody);
        Assert.Contains("Concluído.", body);
        Assert.Contains("Fechado", body);
    }

    [Fact]
    public void Mudanca_de_status_sozinha_tambem_vira_email()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.WaitingOnRequester, TicketStatus.InProgress), Technician.Id, Now, Email);

        var email = Assert.Single(planned.Emails);
        Assert.Contains("aguardando sua resposta", email.Subject);
    }

    [Fact]
    public void Solicitante_nao_recebe_email_da_propria_acao()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.InProgress, TicketStatus.Resolved, comments: Public(Requester, "Voltou.")),
            Requester.Id, Now, Email);

        Assert.Empty(planned.Emails);
    }

    [Fact]
    public void Envio_desligado_nao_gera_email()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Public(Technician)), Technician.Id, Now, email: null);

        Assert.Empty(planned.Emails);
    }

    [Fact]
    public void Solicitante_desativado_nao_recebe_email()
    {
        var change = Change(Technician, comments: Public(Technician)) with { Requester = Requester with { IsActive = false } };

        Assert.Empty(NotificationPlanner.Plan(change, Technician.Id, Now, Email).Emails);
    }

    [Fact]
    public void Texto_do_comentario_e_codificado_no_html()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Public(Technician, "<a href=\"https://golpe\">clique</a>")),
            Technician.Id, Now, Email);

        var body = Assert.Single(planned.Emails).HtmlBody;
        Assert.DoesNotContain("<a href=\"https://golpe\">", body);
        Assert.Contains("&lt;a href=", body);
    }

    [Fact]
    public void Quebra_de_linha_no_titulo_nao_chega_ao_assunto()
    {
        var change = Change(Technician, comments: Public(Technician)) with { Title = "VPN\r\nBcc: todos@empresa.com" };

        var subject = Assert.Single(NotificationPlanner.Plan(change, Technician.Id, Now, Email).Emails).Subject;

        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
    }

    [Fact]
    public void Email_leva_o_link_do_chamado_e_o_aviso_de_nao_responder()
    {
        var change = Change(Technician, comments: Public(Technician));

        var body = Assert.Single(NotificationPlanner.Plan(change, Technician.Id, Now, Email).Emails).HtmlBody;

        Assert.Contains($"https://opsdesk.empresa.com/chamados/{change.TicketId}", body);
        Assert.Contains("não são lidas", body);
    }

    // ----- Portal para o responsável -----

    [Fact]
    public void Resposta_do_solicitante_notifica_o_responsavel()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Public(Requester)), Requester.Id, Now, Email);

        var notification = Assert.Single(planned.InApp);
        Assert.Equal(Technician.Id, notification.UserId);
        Assert.Equal(NotificationKind.RequesterReplied, notification.Kind);
    }

    [Fact]
    public void Responsavel_nao_e_notificado_da_propria_acao()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.Resolved, TicketStatus.InProgress, comments: Public(Technician)),
            Technician.Id, Now, Email);

        Assert.Empty(planned.InApp);
    }

    [Fact]
    public void Colega_comentando_notifica_o_responsavel()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Internal(OtherTechnician)), OtherTechnician.Id, Now, Email);

        var notification = Assert.Single(planned.InApp);
        Assert.Equal(NotificationKind.CommentAdded, notification.Kind);
        Assert.Equal(OtherTechnician.Id, notification.ActorId);
    }

    [Fact]
    public void Reabertura_pelo_solicitante_notifica_como_reabertura()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.InProgress, TicketStatus.Closed, comments: Public(Requester, "Voltou.")),
            Requester.Id, Now, Email);

        Assert.Equal(NotificationKind.Reopened, Assert.Single(planned.InApp).Kind);
    }

    [Fact]
    public void Mudanca_de_status_por_outra_pessoa_notifica_com_o_status_novo()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, TicketStatus.WaitingOnRequester, TicketStatus.InProgress), Manager.Id, Now, Email);

        var notification = Assert.Single(planned.InApp);
        Assert.Equal(NotificationKind.StatusChanged, notification.Kind);
        Assert.Equal(nameof(TicketStatus.WaitingOnRequester), notification.Detail);
    }

    [Fact]
    public void Chamado_sem_responsavel_nao_notifica_ninguem_no_portal()
    {
        var planned = NotificationPlanner.Plan(Change(assignee: null, comments: Public(Requester)), Requester.Id, Now, Email);

        Assert.Empty(planned.InApp);
    }

    [Fact]
    public void Gestor_so_e_notificado_quando_o_chamado_esta_no_nome_dele()
    {
        var withTechnician = NotificationPlanner.Plan(
            Change(Technician, comments: Public(Requester)), Requester.Id, Now, Email);
        Assert.DoesNotContain(withTechnician.InApp, n => n.UserId == Manager.Id);

        var withManager = NotificationPlanner.Plan(
            Change(Manager, comments: Public(Requester)), Requester.Id, Now, Email);
        Assert.Equal(Manager.Id, Assert.Single(withManager.InApp).UserId);
    }

    [Fact]
    public void Atribuir_notifica_quem_recebe_e_quem_perde_o_chamado()
    {
        var planned = NotificationPlanner.Plan(
            Change(OtherTechnician, assigneeChanged: true, previousAssignee: Technician), Manager.Id, Now, Email);

        Assert.Contains(planned.InApp, n => n.UserId == OtherTechnician.Id && n.Kind == NotificationKind.Assigned);
        Assert.Contains(planned.InApp, n => n.UserId == Technician.Id && n.Kind == NotificationKind.Unassigned);
        Assert.Equal(2, planned.InApp.Count);
    }

    [Fact]
    public void Assumir_o_proprio_chamado_nao_notifica_a_si_mesmo()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, assigneeChanged: true), Technician.Id, Now, Email);

        Assert.Empty(planned.InApp);
    }

    [Fact]
    public void Responsavel_que_nao_e_mais_da_equipe_nao_recebe_nota_interna()
    {
        // Responsável rebaixado a solicitante não enxerga o chamado de terceiro pela API;
        // a notificação segue a mesma regra de visibilidade.
        var demoted = Technician with { Role = UserRole.Requester };

        var planned = NotificationPlanner.Plan(
            Change(demoted, comments: Internal(OtherTechnician)), OtherTechnician.Id, Now, Email);

        Assert.Empty(planned.InApp);
    }

    [Fact]
    public void Responsavel_desativado_nao_e_notificado()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician with { IsActive = false }, comments: Public(Requester)), Requester.Id, Now, Email);

        Assert.Empty(planned.InApp);
    }

    [Fact]
    public void Solicitante_nao_recebe_notificacao_no_portal()
    {
        var planned = NotificationPlanner.Plan(
            Change(Technician, comments: Public(Technician)), Technician.Id, Now, Email);

        Assert.DoesNotContain(planned.InApp, n => n.UserId == Requester.Id);
    }
}
