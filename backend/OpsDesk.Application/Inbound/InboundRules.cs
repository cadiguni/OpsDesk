using System.Text.RegularExpressions;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Application.Inbound;

/// <summary>Resultado de uma regra que retém a mensagem.</summary>
public record Verdict(bool Held, int Score, string? Reason)
{
    public static readonly Verdict Pass = new(false, 0, null);
}

/// <summary>
/// Mensagens automáticas (docs/integracao-email.md, seção 4.3): resposta de férias, aviso
/// de falha de entrega, robô. Sem esta barreira, a resposta de férias do solicitante e a
/// notificação do próprio sistema conversariam entre si para sempre.
/// </summary>
public static partial class AutomaticMessageDetector
{
    public static Verdict Check(string fromAddress, string mailboxAddress, IReadOnlyDictionary<string, string> headers)
    {
        var from = fromAddress.Trim().ToLowerInvariant();
        var local = from.Split('@')[0];

        if (from == mailboxAddress.Trim().ToLowerInvariant())
        {
            return Hold("Remetente é a própria caixa monitorada");
        }

        if (Header(headers, "auto-submitted") is { } autoSubmitted && !autoSubmitted.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return Hold($"Mensagem automática (Auto-Submitted: {autoSubmitted})");
        }

        if (Header(headers, "x-auto-response-suppress") is not null)
        {
            return Hold("Mensagem automática (X-Auto-Response-Suppress)");
        }

        if (Header(headers, "precedence") is { } precedence
            && precedence.Trim().ToLowerInvariant() is "bulk" or "junk" or "list" or "auto_reply")
        {
            return Hold($"Mensagem em massa (Precedence: {precedence})");
        }

        if (NoReplyPattern().IsMatch(local) || local is "mailer-daemon" or "postmaster")
        {
            return Hold("Remetente automático");
        }

        if (Header(headers, "content-type") is { } contentType
            && contentType.Contains("report-type=delivery-status", StringComparison.OrdinalIgnoreCase))
        {
            return Hold("Aviso de falha de entrega");
        }

        return Verdict.Pass;
    }

    internal static string? Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value : null;

    private static Verdict Hold(string reason) => new(true, 0, reason);

    [GeneratedRegex("^no[-_.]?reply", RegexOptions.IgnoreCase)]
    private static partial Regex NoReplyPattern();
}

/// <summary>
/// Classificação de spam por regras (docs/integracao-email.md, seção 4.6), nesta ordem:
/// liberação explícita, bloqueio explícito, domínio fora da organização, autenticação do
/// remetente reprovada e, por último, heurística de conteúdo por pontuação.
///
/// Sem aprendizado de máquina: as regras cobrem uma caixa de suporte interna, e o que
/// escapar a equipe corrige na caixa de SPAM. O motivo vai sempre junto, para a revisão
/// ser auditável.
/// </summary>
public static partial class SpamClassifier
{
    /// <summary>Pontuação a partir da qual a heurística retém a mensagem.</summary>
    public const int Threshold = 5;

    public static Verdict Classify(
        string fromAddress,
        string subject,
        string body,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyCollection<SpamRule> rules,
        IReadOnlyCollection<string>? internalDomains)
    {
        var from = fromAddress.Trim().ToLowerInvariant();
        var domain = from.Contains('@') ? from[(from.LastIndexOf('@') + 1)..] : "";
        var subjectLower = subject.ToLowerInvariant();
        var bodyLower = body.ToLowerInvariant();

        bool Matches(SpamRule rule) => rule.IsActive && rule.Type switch
        {
            SpamRuleType.SenderAddress => from == rule.Pattern,
            SpamRuleType.SenderDomain => domain == rule.Pattern || domain.EndsWith("." + rule.Pattern),
            SpamRuleType.SubjectContains => subjectLower.Contains(rule.Pattern),
            SpamRuleType.BodyContains => bodyLower.Contains(rule.Pattern),
            _ => false
        };

        // 1. Liberado pela equipe vence tudo — inclusive bloqueio de domínio que pegaria
        // um colega de um parceiro que a equipe quis deixar passar.
        if (rules.FirstOrDefault(r => r.Action == SpamRuleAction.Allow && Matches(r)) is not null)
        {
            return Verdict.Pass;
        }

        // 2. Bloqueado pela equipe.
        if (rules.FirstOrDefault(r => r.Action == SpamRuleAction.Block && Matches(r)) is { } block)
        {
            return new Verdict(true, 0, $"Regra de bloqueio: {Describe(block)}");
        }

        // 3. Caixa só para dentro da organização.
        if (internalDomains is { Count: > 0 } && !internalDomains.Contains(domain))
        {
            return new Verdict(true, 0, $"Domínio fora da organização ({domain})");
        }

        // 4. Autenticação do remetente reprovada: é o sinal mais forte de falsificação.
        if (AutomaticMessageDetector.Header(headers, "authentication-results") is { } auth
            && AuthFailure().Match(auth) is { Success: true } failure)
        {
            return new Verdict(true, 0, $"Autenticação do remetente reprovada ({failure.Groups[1].Value.ToUpperInvariant()})");
        }

        // 5. Heurística de conteúdo.
        var score = 0;
        var signals = new List<string>();

        if (string.IsNullOrWhiteSpace(subject))
        {
            score += 2;
            signals.Add("assunto vazio");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            score += 3;
            signals.Add("corpo vazio");
        }

        var links = LinkPattern().Matches(body).Count;
        if (links > 10)
        {
            score += 4;
            signals.Add($"{links} links");
        }
        else if (links > 5)
        {
            score += 2;
            signals.Add($"{links} links");
        }

        return score >= Threshold
            ? new Verdict(true, score, $"Conteúdo suspeito: {string.Join(", ", signals)}")
            : new Verdict(false, score, null);
    }

    private static string Describe(SpamRule rule) => rule.Type switch
    {
        SpamRuleType.SenderAddress => $"remetente {rule.Pattern}",
        SpamRuleType.SenderDomain => $"domínio {rule.Pattern}",
        SpamRuleType.SubjectContains => $"assunto contém \"{rule.Pattern}\"",
        SpamRuleType.BodyContains => $"corpo contém \"{rule.Pattern}\"",
        _ => rule.Pattern
    };

    [GeneratedRegex(@"\b(spf|dkim|dmarc)=(?:fail|softfail|permerror)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AuthFailure();

    [GeneratedRegex(@"https?://", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}

/// <summary>
/// O que a pessoa escreveu numa resposta, sem o texto citado da mensagem anterior.
///
/// A primeira linha de corte é a marca que os e-mails do OpsDesk levam no topo. Sem ela —
/// cliente que reescreveu tudo, resposta a uma mensagem de outra origem —, cortam os
/// separadores que os clientes de e-mail usam: "Em ... escreveu:", "On ... wrote:", o
/// bloco "De:/Enviado:" do Outlook e as linhas citadas com "&gt;".
/// </summary>
public static partial class ReplyTextExtractor
{
    public const string Marker = "Responda acima desta linha";

    public static string Extract(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Contains(Marker, StringComparison.OrdinalIgnoreCase)
                || QuoteHeader().IsMatch(trimmed)
                || OutlookSeparator().IsMatch(trimmed)
                || trimmed.StartsWith("-----Original Message-----", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("-----Mensagem original-----", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (trimmed.StartsWith('>'))
            {
                continue;
            }

            kept.Add(line);
        }

        var text = string.Join('\n', kept).Trim();

        // Corte que deixou nada — a pessoa respondeu embaixo da citação, por exemplo — é
        // pior que texto com citação: devolve o corpo inteiro.
        return text.Length > 0 ? text : body.Trim();
    }

    [GeneratedRegex(@"^(Em|On)\s.+(escreveu|wrote):?$", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteHeader();

    [GeneratedRegex(@"^(De|From):\s.+", RegexOptions.IgnoreCase)]
    private static partial Regex OutlookSeparator();
}
