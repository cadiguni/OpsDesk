using OpsDesk.Application.Inbound;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// As regras puras da caixa de entrada (docs/integracao-email.md, seções 4.3 e 4.6) e a
/// extração do texto de uma resposta.
/// </summary>
public class InboundRulesTests
{
    private const string Mailbox = "suporte@empresa.com";

    private static readonly Dictionary<string, string> NoHeaders = [];

    // ----- Mensagens automáticas -----

    [Theory]
    [InlineData("auto-submitted", "auto-replied")]
    [InlineData("x-auto-response-suppress", "All")]
    [InlineData("precedence", "bulk")]
    [InlineData("content-type", "multipart/report; report-type=delivery-status; boundary=x")]
    public void Cabecalho_de_mensagem_automatica_retem(string header, string value)
    {
        var verdict = AutomaticMessageDetector.Check("fulano@empresa.com", Mailbox, new Dictionary<string, string> { [header] = value });

        Assert.True(verdict.Held);
        Assert.NotNull(verdict.Reason);
    }

    [Theory]
    [InlineData("noreply@banco.com")]
    [InlineData("no-reply@sistema.com")]
    [InlineData("MAILER-DAEMON@empresa.com")]
    [InlineData("SUPORTE@empresa.com")]
    public void Remetente_automatico_ou_a_propria_caixa_retem(string from)
    {
        Assert.True(AutomaticMessageDetector.Check(from, Mailbox, NoHeaders).Held);
    }

    [Fact]
    public void Auto_submitted_no_e_mensagem_de_pessoa()
    {
        var verdict = AutomaticMessageDetector.Check(
            "fulano@empresa.com", Mailbox, new Dictionary<string, string> { ["auto-submitted"] = "no" });

        Assert.False(verdict.Held);
    }

    // ----- Spam -----

    private static SpamRule Rule(SpamRuleType type, string pattern, SpamRuleAction action) =>
        new() { Type = type, Pattern = pattern, Action = action };

    [Fact]
    public void Liberacao_vence_bloqueio_de_dominio()
    {
        var rules = new[]
        {
            Rule(SpamRuleType.SenderDomain, "parceiro.com", SpamRuleAction.Block),
            Rule(SpamRuleType.SenderAddress, "contato@parceiro.com", SpamRuleAction.Allow)
        };

        Assert.False(SpamClassifier.Classify("contato@parceiro.com", "Dúvida", "Texto", NoHeaders, rules, null).Held);
        Assert.True(SpamClassifier.Classify("outro@parceiro.com", "Dúvida", "Texto", NoHeaders, rules, null).Held);
    }

    [Fact]
    public void Bloqueio_de_dominio_pega_subdominio_mas_nao_dominio_parecido()
    {
        var rules = new[] { Rule(SpamRuleType.SenderDomain, "golpe.com", SpamRuleAction.Block) };

        Assert.True(SpamClassifier.Classify("x@mail.golpe.com", "a", "b", NoHeaders, rules, null).Held);
        Assert.False(SpamClassifier.Classify("x@naogolpe.com", "a", "b", NoHeaders, rules, null).Held);
    }

    [Fact]
    public void Caixa_interna_retem_dominio_de_fora_com_o_motivo()
    {
        var verdict = SpamClassifier.Classify("x@gmail.com", "Ajuda", "Texto", NoHeaders, [], ["empresa.com"]);

        Assert.True(verdict.Held);
        Assert.Contains("gmail.com", verdict.Reason);
        Assert.False(SpamClassifier.Classify("x@empresa.com", "Ajuda", "Texto", NoHeaders, [], ["empresa.com"]).Held);
    }

    [Theory]
    [InlineData("mx.empresa.com; spf=fail smtp.mailfrom=x.com; dkim=none", "SPF")]
    [InlineData("mx.empresa.com; spf=pass; dkim=pass; dmarc=fail action=quarantine", "DMARC")]
    public void Autenticacao_reprovada_retem(string authenticationResults, string expected)
    {
        var verdict = SpamClassifier.Classify("x@empresa.com", "Ajuda", "Texto",
            new Dictionary<string, string> { ["authentication-results"] = authenticationResults }, [], null);

        Assert.True(verdict.Held);
        Assert.Contains(expected, verdict.Reason);
    }

    [Fact]
    public void Autenticacao_aprovada_passa()
    {
        var verdict = SpamClassifier.Classify("x@empresa.com", "Ajuda", "Texto",
            new Dictionary<string, string> { ["authentication-results"] = "spf=pass; dkim=pass; dmarc=pass" }, [], null);

        Assert.False(verdict.Held);
    }

    [Fact]
    public void Conteudo_suspeito_soma_pontos_ate_reter()
    {
        var links = string.Join(" ", Enumerable.Range(0, 12).Select(i => $"https://promo{i}.example"));

        Assert.True(SpamClassifier.Classify("x@empresa.com", "", links, NoHeaders, [], null).Held);
        Assert.False(SpamClassifier.Classify("x@empresa.com", "", "Minha impressora parou.", NoHeaders, [], null).Held);
    }

    // ----- Título a partir do assunto -----

    [Theory]
    [InlineData("RE: [OPS-000123] VPN", "VPN")]
    [InlineData("RES: RE: Fwd: Impressora", "Impressora")]
    [InlineData("ENC: [OPS-000042] Chamado resolvido: Teclado", "Chamado resolvido: Teclado")]
    [InlineData("Re[2]: Acesso", "Acesso")]
    [InlineData("Resumo da reunião", "Resumo da reunião")]
    [InlineData("RE:", "")]
    public void Assunto_vira_titulo_sem_codigo_nem_prefixos(string subject, string title)
    {
        Assert.Equal(title, InboundEmailProcessor.CleanSubject(subject));
    }

    // ----- Texto da resposta -----

    [Fact]
    public void Corta_na_marca_do_opsdesk()
    {
        var body = $"Testei de novo e funcionou.\n\n—— {ReplyTextExtractor.Marker} ——\nOlá, Fulano. Há novidade no seu chamado...";

        Assert.Equal("Testei de novo e funcionou.", ReplyTextExtractor.Extract(body));
    }

    [Theory]
    [InlineData("Obrigado!\n\nEm qua., 30 de set. de 2026 às 14:00, Suporte TI escreveu:\n> texto antigo")]
    [InlineData("Obrigado!\n\nOn Wed, Sep 30, 2026 at 2:00 PM Suporte TI wrote:\n> old text")]
    [InlineData("Obrigado!\r\n\r\nDe: Suporte TI <suporte@empresa.com>\r\nEnviado: quarta-feira\r\nAssunto: [OPS-000001]")]
    [InlineData("Obrigado!\n> citação\n> mais citação")]
    public void Corta_a_citacao_dos_clientes_de_email(string body)
    {
        Assert.Equal("Obrigado!", ReplyTextExtractor.Extract(body));
    }

    [Fact]
    public void Se_o_corte_nao_deixar_nada_devolve_o_corpo_inteiro()
    {
        var body = "> só citação\n> e nada mais";

        Assert.Equal(body, ReplyTextExtractor.Extract(body));
    }
}
