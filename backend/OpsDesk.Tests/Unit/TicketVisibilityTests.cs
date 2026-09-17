using OpsDesk.Application.Authorization;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Semântica do filtro de visibilidade, incluindo os casos negativos: o que cada perfil
/// <b>não</b> pode ver.
///
/// Aqui os predicados rodam sobre objetos em memória — isto verifica a regra, não a
/// tradução para SQL. A tradução é coberta pelos testes de integração contra PostgreSQL
/// de verdade, porque regra certa com SQL errado vaza dado igual.
/// </summary>
public class TicketVisibilityTests
{
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid OtherRequester = Guid.CreateVersion7();
    private static readonly Guid Technician = Guid.CreateVersion7();
    private static readonly Guid OtherTechnician = Guid.CreateVersion7();
    private static readonly Guid Manager = Guid.CreateVersion7();

    private static readonly Ticket Own = new()
    {
        Title = "Chamado do solicitante",
        RequesterId = Requester
    };

    private static readonly Ticket OfSomeoneElse = new()
    {
        Title = "Chamado de terceiro",
        RequesterId = OtherRequester
    };

    private static readonly Ticket Unassigned = new()
    {
        Title = "Sem responsável",
        RequesterId = OtherRequester
    };

    private static readonly Ticket AssignedToTechnician = new()
    {
        Title = "Atribuído ao técnico",
        RequesterId = OtherRequester,
        AssignedTechnicianId = Technician
    };

    private static readonly Ticket AssignedToAnotherTechnician = new()
    {
        Title = "Atribuído a outro técnico",
        RequesterId = OtherRequester,
        AssignedTechnicianId = OtherTechnician
    };

    private static IQueryable<Ticket> AllTickets =>
        new[] { Own, OfSomeoneElse, Unassigned, AssignedToTechnician, AssignedToAnotherTechnician }
            .AsQueryable();

    // ----- Chamados -----

    [Fact]
    public void Solicitante_ve_apenas_os_proprios_chamados()
    {
        var visible = AllTickets.VisibleTo(new TicketViewer(Requester, UserRole.Requester)).ToList();

        Assert.Single(visible);
        Assert.Contains(Own, visible);
    }

    [Fact]
    public void Solicitante_nao_ve_chamado_de_terceiro()
    {
        var visible = AllTickets.VisibleTo(new TicketViewer(Requester, UserRole.Requester)).ToList();

        Assert.DoesNotContain(OfSomeoneElse, visible);
        Assert.DoesNotContain(Unassigned, visible);
        Assert.DoesNotContain(AssignedToTechnician, visible);
    }

    [Fact]
    public void Tecnico_ve_a_fila_inteira()
    {
        var visible = AllTickets.VisibleTo(new TicketViewer(Technician, UserRole.Technician)).ToList();

        Assert.Equal(5, visible.Count);
    }

    [Fact]
    public void Tecnico_ve_chamado_atribuido_a_outro_tecnico()
    {
        // Um técnico não é terceiro para outro técnico: é a mesma equipe, com o mesmo
        // acesso a comentário interno. Esconder o chamado do colega custava atendimento
        // — encaminhar tirava o chamado da própria vista, e cobrir ausência exigia o
        // gestor — sem proteger dado de ninguém.
        var visible = AllTickets.VisibleTo(new TicketViewer(Technician, UserRole.Technician)).ToList();

        Assert.Contains(AssignedToAnotherTechnician, visible);
    }

    [Fact]
    public void Gestor_ve_todos_os_chamados()
    {
        var visible = AllTickets.VisibleTo(new TicketViewer(Manager, UserRole.Manager)).ToList();

        Assert.Equal(5, visible.Count);
    }

    [Fact]
    public void Perfil_desconhecido_cai_no_caso_mais_restrito()
    {
        // Fail closed: o filtro decide por `IsStaff`, que é falso para qualquer valor
        // fora de técnico e gestor. Perfil novo que ninguém lembrou de tratar cai no caso
        // mais restrito em vez de virar acesso amplo. Contrato do filtro, não cenário
        // esperado.
        var visible = AllTickets.VisibleTo(new TicketViewer(Requester, (UserRole)99)).ToList();

        Assert.Single(visible);
        Assert.Contains(Own, visible);
    }

    // ----- Comentários -----

    private static IQueryable<TicketComment> Comments()
    {
        var own = new Ticket { RequesterId = Requester, AssignedTechnicianId = Technician };
        var other = new Ticket { RequesterId = OtherRequester, AssignedTechnicianId = OtherTechnician };

        return new[]
        {
            new TicketComment { Content = "público no meu chamado", Ticket = own, IsInternal = false },
            new TicketComment { Content = "interno no meu chamado", Ticket = own, IsInternal = true },
            new TicketComment { Content = "público em chamado de terceiro", Ticket = other, IsInternal = false },
            new TicketComment { Content = "interno em chamado de terceiro", Ticket = other, IsInternal = true }
        }.AsQueryable();
    }

    [Fact]
    public void Solicitante_nunca_recebe_comentario_interno()
    {
        // Invariante 2 do CLAUDE.md.
        var visible = Comments()
            .VisibleTo(new TicketViewer(Requester, UserRole.Requester))
            .ToList();

        Assert.All(visible, c => Assert.False(c.IsInternal));
    }

    [Fact]
    public void Solicitante_ve_apenas_o_publico_do_proprio_chamado()
    {
        var visible = Comments()
            .VisibleTo(new TicketViewer(Requester, UserRole.Requester))
            .ToList();

        Assert.Single(visible);
        Assert.Equal("público no meu chamado", visible[0].Content);
    }

    [Fact]
    public void Tecnico_ve_comentario_interno_de_qualquer_chamado()
    {
        var visible = Comments()
            .VisibleTo(new TicketViewer(Technician, UserRole.Technician))
            .ToList();

        Assert.Equal(4, visible.Count);
        Assert.Contains(visible, c => c.IsInternal);
    }

    [Fact]
    public void Gestor_ve_todos_os_comentarios()
    {
        var visible = Comments()
            .VisibleTo(new TicketViewer(Manager, UserRole.Manager))
            .ToList();

        Assert.Equal(4, visible.Count);
    }

    // ----- Histórico -----

    [Fact]
    public void Historico_segue_a_visibilidade_do_chamado()
    {
        var own = new Ticket { RequesterId = Requester };
        var other = new Ticket { RequesterId = OtherRequester };

        var history = new[]
        {
            new TicketHistory { Ticket = own, Action = TicketHistoryAction.Created },
            new TicketHistory { Ticket = other, Action = TicketHistoryAction.Created }
        }.AsQueryable();

        var visible = history.VisibleTo(new TicketViewer(Requester, UserRole.Requester)).ToList();

        Assert.Single(visible);
        Assert.Same(own, visible[0].Ticket);
    }
}
