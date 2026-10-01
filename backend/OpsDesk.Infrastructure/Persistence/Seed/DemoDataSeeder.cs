using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Sla;
using OpsDesk.Domain.Entities;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Infrastructure.Persistence.Seed;

public enum DemoSeedResult
{
    Created,

    /// <summary>O banco já tem chamado: dados de demonstração não se misturam com dados reais.</summary>
    RefusedDatabaseInUse
}

/// <summary>
/// Dados de demonstração (README, seção 18): pessoas e chamados em todos os estados que
/// valem a pena mostrar — novo, em atendimento, aguardando o solicitante, vencido, com
/// anexo, resolvido, fechado e cancelado.
///
/// Separado do seed de referência de propósito, e com duas travas: só roda por comando
/// explícito (<c>--seed-demo</c>), e recusa banco que já tenha qualquer chamado. Os
/// usuários têm senha conhecida — numa instalação real, isso seria uma porta aberta.
/// </summary>
public class DemoDataSeeder(
    OpsDeskDbContext db,
    DatabaseSeeder referenceData,
    IPasswordHasher<User> passwordHasher,
    SlaClock sla,
    IAttachmentStorage storage,
    IClock clock,
    ILogger<DemoDataSeeder> logger)
{
    public const string Password = "OpsDesk@123";

    public async Task<DemoSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Tickets.AnyAsync(cancellationToken))
        {
            return DemoSeedResult.RefusedDatabaseInUse;
        }

        await referenceData.SeedAsync(includeDevelopmentUsers: false, cancellationToken);

        var now = clock.UtcNow;

        await UserAsync("gestor@opsdesk.local", "Gestora de Suporte", UserRole.Manager, cancellationToken);
        var technician = await UserAsync("tecnico@opsdesk.local", "Técnico de Suporte", UserRole.Technician, cancellationToken);
        var otherTechnician = await UserAsync("tecnica@opsdesk.local", "Técnica de Redes", UserRole.Technician, cancellationToken);
        var requester = await UserAsync("usuario@opsdesk.local", "Usuário de Exemplo", UserRole.Requester, cancellationToken);
        var otherRequester = await UserAsync("financeiro@opsdesk.local", "Ana do Financeiro", UserRole.Requester, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var categories = await db.Categories.ToDictionaryAsync(c => c.Name, cancellationToken);
        var policies = await db.SlaPolicies.Where(p => p.IsActive).ToDictionaryAsync(p => p.Priority, cancellationToken);

        Ticket New(string title, string description, string category, TicketPriority priority, User by, TimeSpan age)
        {
            var createdAt = now - age;
            var deadlines = sla.Calculate(createdAt, policies[priority]);

            return new Ticket
            {
                Title = title,
                Description = description,
                CategoryId = categories[category].Id,
                Priority = priority,
                RequesterId = by.Id,
                Status = TicketStatus.Open,
                Source = TicketSource.Portal,
                CreatedAt = createdAt,
                SlaResponseDueAt = deadlines.ResponseDueAt,
                SlaResolutionDueAt = deadlines.ResolutionDueAt
            };
        }

        var fresh = New("Impressora do 3º andar não imprime",
            "A impressora da copa do 3º andar está com a luz laranja piscando e não aceita trabalho de ninguém.",
            "Impressora", TicketPriority.Medium, requester, TimeSpan.FromMinutes(40));

        var vpn = New("VPN cai a cada dez minutos",
            "Desde a atualização de ontem a VPN desconecta sozinha a cada dez minutos, mais ou menos. Trabalho remoto às terças e quintas.",
            "VPN", TicketPriority.High, otherRequester, TimeSpan.FromHours(3));

        var access = New("Acesso ao sistema de RH",
            "Fui transferida para o RH e preciso de acesso ao sistema de folha.",
            "Acesso", TicketPriority.Medium, otherRequester, TimeSpan.FromDays(1));

        var laptop = New("Notebook não liga",
            "O notebook não liga nem com o carregador na tomada. Tenho apresentação para a diretoria hoje.",
            "Hardware", TicketPriority.Critical, requester, TimeSpan.FromDays(2));

        var slowServer = New("Lentidão no servidor de arquivos",
            "Abrir qualquer planilha da pasta compartilhada do Financeiro está levando mais de um minuto.",
            "Servidores", TicketPriority.Medium, otherRequester, TimeSpan.FromHours(6));

        var powerBi = New("Instalar o Power BI",
            "Preciso do Power BI Desktop instalado para montar os relatórios do trimestre.",
            "Software", TicketPriority.Low, requester, TimeSpan.FromDays(3));

        var password = New("Troca de senha do e-mail",
            "Esqueci a senha do e-mail depois das férias.",
            "E-mail", TicketPriority.Medium, otherRequester, TimeSpan.FromDays(6));

        var monitor = New("Monitor extra",
            "Gostaria de um segundo monitor para a estação de trabalho.",
            "Hardware", TicketPriority.Low, requester, TimeSpan.FromDays(4));

        db.Tickets.AddRange(fresh, vpn, access, laptop, slowServer, powerBi, password, monitor);

        // Primeira gravação: os chamados nascem como "Aberto", e o código sai da sequence.
        await db.SaveChangesAsync(cancellationToken);

        TicketComment Comment(Ticket ticket, User author, string content, TimeSpan after, bool isInternal = false)
        {
            var comment = new TicketComment
            {
                TicketId = ticket.Id,
                AuthorId = author.Id,
                Content = content,
                IsInternal = isInternal,
                CreatedAt = ticket.CreatedAt + after
            };

            db.TicketComments.Add(comment);

            if (!isInternal && author.IsStaff && ticket.FirstRespondedAt is null)
            {
                ticket.FirstRespondedAt = comment.CreatedAt;
            }

            return comment;
        }

        // Em atendimento, com resposta pública e nota interna.
        vpn.AssignedTechnicianId = otherTechnician.Id;
        vpn.Status = TicketStatus.InProgress;
        Comment(vpn, otherTechnician, "Pode me dizer a versão do cliente de VPN? Fica no ícone perto do relógio.", TimeSpan.FromMinutes(25));
        Comment(vpn, otherRequester, "Versão 7.4.2.", TimeSpan.FromMinutes(50));
        Comment(vpn, otherTechnician, "Mesma versão dos outros dois casos da semana. Suspeito do perfil novo do firewall.", TimeSpan.FromHours(1), isInternal: true);

        // Aguardando o solicitante: o SLA de resolução está pausado.
        access.AssignedTechnicianId = technician.Id;
        access.Status = TicketStatus.WaitingOnRequester;
        Comment(access, technician, "Preciso da aprovação do seu novo gestor. Pode pedir para ele responder aqui?", TimeSpan.FromHours(2));
        access.SlaPausedAt = access.CreatedAt + TimeSpan.FromHours(2);

        // Vencido e sem responsável: aparece no filtro de vencidos e no dashboard.
        // (fica como está: aberto há dois dias, crítico.)

        // Em triagem, com anexo do solicitante.
        slowServer.Status = TicketStatus.Triage;
        await AttachAsync(slowServer, otherRequester, "tempo-de-abertura.txt",
            "Planilha orcamento-2026.xlsx: 74 s\nPlanilha fluxo-de-caixa.xlsx: 91 s\nPasta Financeiro (listar): 12 s\n",
            cancellationToken);

        // Resolvido, esperando o solicitante confirmar.
        powerBi.AssignedTechnicianId = technician.Id;
        powerBi.Status = TicketStatus.Resolved;
        Comment(powerBi, technician, "Instalado. Ele aparece no menu Iniciar como Power BI Desktop.", TimeSpan.FromHours(20));
        powerBi.ResolvedAt = powerBi.CreatedAt + TimeSpan.FromHours(20);

        // Fechado, com confirmação do solicitante.
        password.AssignedTechnicianId = technician.Id;
        password.Status = TicketStatus.Closed;
        Comment(password, technician, "Senha redefinida. A provisória foi enviada por SMS.", TimeSpan.FromHours(1));
        Comment(password, otherRequester, "Funcionou, obrigada!", TimeSpan.FromHours(3));
        password.ResolvedAt = password.CreatedAt + TimeSpan.FromHours(1);
        password.ClosedAt = password.CreatedAt + TimeSpan.FromHours(3);

        // Cancelado pelo próprio solicitante.
        monitor.Status = TicketStatus.Cancelled;
        Comment(monitor, requester, "Consegui um monitor emprestado com outra equipe, pode cancelar.", TimeSpan.FromHours(4));

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Dados de demonstração criados. Usuários com senha {Password}: {Users}. Nunca use isto numa instalação real.",
            Password, "gestor@, tecnico@, tecnica@, usuario@ e financeiro@opsdesk.local");

        return DemoSeedResult.Created;
    }

    private async Task<User> UserAsync(string email, string name, UserRole role, CancellationToken cancellationToken)
    {
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var user = new User { Email = email, Name = name, Role = role };
        user.PasswordHash = passwordHasher.HashPassword(user, Password);
        db.Users.Add(user);

        return user;
    }

    private async Task AttachAsync(Ticket ticket, User by, string fileName, string content, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var attachment = new TicketAttachment
        {
            TicketId = ticket.Id,
            UploadedById = by.Id,
            FileName = fileName,
            ContentType = "text/plain",
            SizeInBytes = bytes.Length,
            CreatedAt = ticket.CreatedAt
        };

        // Mesmo formato de chave do AttachmentService: ano/mês/identificador.
        attachment.StorageKey = $"{ticket.CreatedAt:yyyy}/{ticket.CreatedAt:MM}/{attachment.Id:N}.txt";

        await using var stream = new MemoryStream(bytes);
        await storage.SaveAsync(attachment.StorageKey, stream, cancellationToken);

        db.TicketAttachments.Add(attachment);
    }
}
