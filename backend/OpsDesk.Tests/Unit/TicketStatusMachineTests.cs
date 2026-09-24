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
        [TicketStatus.Open] = [TicketStatus.Triage, TicketStatus.InProgress, TicketStatus.Closed, TicketStatus.Cancelled],
        [TicketStatus.Triage] = [TicketStatus.InProgress, TicketStatus.WaitingOnRequester, TicketStatus.Closed, TicketStatus.Cancelled],
        [TicketStatus.InProgress] =
        [
            TicketStatus.Triage, TicketStatus.WaitingOnRequester, TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled
        ],
        [TicketStatus.WaitingOnRequester] =
        [
            TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled
        ],
        [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.InProgress],
        [TicketStatus.Closed] = [TicketStatus.InProgress],
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

    [Fact]
    public void Cancelado_nao_admite_saida()
    {
        Assert.Empty(TicketStatusMachine.AllowedFrom(TicketStatus.Cancelled));
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Fechado_e_cancelado_estao_encerrados(TicketStatus status)
    {
        Assert.Contains(status, TicketStatusMachine.Finished);
    }

    [Fact]
    public void Chamado_aberto_nao_pula_direto_para_resolvido()
    {
        // Resolver sem passar por atendimento deixaria o chamado sem responsável e sem
        // marco de resposta, e o indicador de SLA não teria o que medir.
        Assert.False(TicketStatusMachine.CanTransition(TicketStatus.Open, TicketStatus.Resolved));
    }

    [Fact]
    public void Chamado_fechado_so_sai_para_atendimento()
    {
        // Reabrir é voltar a trabalhar no chamado. Voltar para "Aberto" ou "Em triagem"
        // devolveria à fila de entrada um chamado que já tem responsável e histórico.
        Assert.Equal([TicketStatus.InProgress], TicketStatusMachine.AllowedFrom(TicketStatus.Closed));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.WaitingOnRequester, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed, false)]
    public void Reabertura_e_voltar_de_resolvido_ou_fechado_para_atendimento(
        TicketStatus from, TicketStatus to, bool reopening)
    {
        Assert.Equal(reopening, TicketStatusMachine.IsReopening(from, to));
    }

    // ----- Permissão por perfil -----

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Triage)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingOnRequester)]
    public void Apenas_equipe_fecha_diretamente_chamado_ativo(TicketStatus from)
    {
        Assert.True(TicketStatusMachine.IsAllowedForRole(from, TicketStatus.Closed, UserRole.Technician));
        Assert.True(TicketStatusMachine.IsAllowedForRole(from, TicketStatus.Closed, UserRole.Manager));
        Assert.False(TicketStatusMachine.IsAllowedForRole(from, TicketStatus.Closed, UserRole.Requester));
    }

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

        // O solicitante reabre respondendo o chamado, e não pela mudança de status: a
        // resposta é o motivo da reabertura, e reabrir sem dizer por quê é ruído na fila.
        Assert.False(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Resolved, TicketStatus.InProgress, UserRole.Requester));

        Assert.False(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Closed, TicketStatus.InProgress, UserRole.Requester));
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Technician)]
    public void Equipe_reabre_chamado_fechado(UserRole role)
    {
        Assert.True(TicketStatusMachine.IsAllowedForRole(
            TicketStatus.Closed, TicketStatus.InProgress, role));
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
