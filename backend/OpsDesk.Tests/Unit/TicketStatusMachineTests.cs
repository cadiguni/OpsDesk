using OpsDesk.Domain.Enums;
using OpsDesk.Domain.Tickets;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Máquina de estados do chamado. O grafo está no README, seção 5.8, e o teste é
/// exaustivo de propósito: toda transição permitida e toda transição negada aparecem aqui,
/// para que abrir uma aresta nova exija mexer neste arquivo.
/// </summary>
public class TicketStatusMachineTests
{
    private static readonly TicketStatus[] AllStatuses = Enum.GetValues<TicketStatus>();

    /// <summary>O grafo esperado, escrito à mão a partir do README.</summary>
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Expected = new()
    {
        [TicketStatus.Open] = [TicketStatus.Triage, TicketStatus.InProgress, TicketStatus.Cancelled],
        [TicketStatus.Triage] = [TicketStatus.InProgress, TicketStatus.WaitingOnRequester, TicketStatus.Cancelled],
        [TicketStatus.InProgress] =
        [
            TicketStatus.Triage, TicketStatus.WaitingOnRequester, TicketStatus.Resolved, TicketStatus.Cancelled
        ],
        [TicketStatus.WaitingOnRequester] =
        [
            TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Cancelled
        ],
        [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.InProgress],
        [TicketStatus.Closed] = [],
        [TicketStatus.Cancelled] = []
    };

    public static TheoryData<TicketStatus, TicketStatus, bool> AllPairs()
    {
        var data = new TheoryData<TicketStatus, TicketStatus, bool>();

        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                data.Add(from, to, Expected[from].Contains(to));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Todo_par_de_status_segue_o_grafo_documentado(
        TicketStatus from, TicketStatus to, bool allowed)
    {
        Assert.Equal(allowed, TicketStatusMachine.CanTransition(from, to));
    }

    [Fact]
    public void Transicao_para_o_mesmo_status_nao_e_transicao()
    {
        foreach (var status in AllStatuses)
        {
            Assert.False(TicketStatusMachine.CanTransition(status, status));
        }
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Status_terminal_nao_admite_saida(TicketStatus terminal)
    {
        Assert.Empty(TicketStatusMachine.AllowedFrom(terminal));
        Assert.Contains(terminal, TicketStatusMachine.Terminal);
    }

    [Fact]
    public void Chamado_aberto_nao_pula_direto_para_resolvido()
    {
        // Resolver sem passar por atendimento deixaria o chamado sem responsável e sem
        // marco de resposta, e o indicador de SLA não teria o que medir.
        Assert.False(TicketStatusMachine.CanTransition(TicketStatus.Open, TicketStatus.Resolved));
    }

    [Fact]
    public void Chamado_fechado_nao_reabre_na_versao_1()
    {
        foreach (var target in AllStatuses)
        {
            Assert.False(TicketStatusMachine.CanTransition(TicketStatus.Closed, target));
        }
    }

    // ----- Permissão por perfil -----

    [Theory]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Technician, true)]
    [InlineData(UserRole.Requester, false)]
    public void Assumir_e_resolver_e_trabalho_da_equipe(UserRole role, bool allowed)
    {
        Assert.Equal(allowed, TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Open, TicketStatus.InProgress, role));

        Assert.Equal(allowed, TicketStatusMachine.IsAllowedForRole(
            TicketStatus.InProgress, TicketStatus.Resolved, role));
    }

    [Fact]
    public void Solicitante_pode_cancelar_o_proprio_chamado()
    {
        Assert.True(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Open, TicketStatus.Cancelled, UserRole.Requester));
    }

    [Fact]
    public void Solicitante_pode_confirmar_a_resolucao_fechando_o_chamado()
    {
        Assert.True(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Resolved, TicketStatus.Closed, UserRole.Requester));
    }

    [Fact]
    public void Solicitante_nao_coloca_o_chamado_em_espera_nem_o_reabre()
    {
        Assert.False(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.InProgress, TicketStatus.WaitingOnRequester, UserRole.Requester));

        Assert.False(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Resolved, TicketStatus.InProgress, UserRole.Requester));
    }

    [Fact]
    public void Perfil_nenhum_faz_transicao_que_o_grafo_nega()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            Assert.False(TicketStatusMachine.IsAllowedForRole(
                TicketStatus.Closed, TicketStatus.Open, role));
        }
    }

    // ----- Efeito sobre o relógio de SLA -----

    [Theory]
    [InlineData(TicketStatus.WaitingOnRequester, true)]
    [InlineData(TicketStatus.Open, false)]
    [InlineData(TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.Triage, false)]
    public void So_aguardando_usuario_pausa_o_sla(TicketStatus status, bool pauses)
    {
        Assert.Equal(pauses, TicketStatusMachine.PausesResolutionSla(status));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.Closed, true)]
    [InlineData(TicketStatus.Cancelled, true)]
    [InlineData(TicketStatus.WaitingOnRequester, false)]
    [InlineData(TicketStatus.Open, false)]
    public void Resolvido_fechado_e_cancelado_encerram_a_contagem(TicketStatus status, bool ends)
    {
        Assert.Equal(ends, TicketStatusMachine.EndsSlaClock(status));
    }
}
