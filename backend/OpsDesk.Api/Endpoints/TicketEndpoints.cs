using OpsDesk.Api.Authorization;
using OpsDesk.Api.Validation;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Common;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Enums;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Endpoints de chamado.
///
/// Nenhum deles filtra por usuário: quem faz isso é o <c>VisibleTo</c> dentro do
/// <c>TicketService</c>, no <c>IQueryable</c>. O que estes endpoints fazem é exigir
/// autenticação e traduzir a consulta em parâmetros.
/// </summary>
public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/tickets")
            .RequireAuthorization()
            .WithTags("Chamados");

        group.MapPost("/", Create)
            .ValidatingBody<CreateTicketRequest>()
            .WithSummary("Abre um chamado.");

        group.MapGet("/", List)
            .WithSummary("Lista os chamados visíveis para o usuário, com filtros e paginação.");

        group.MapGet("/{id:guid}", Get)
            .WithSummary("Detalhe do chamado.");

        group.MapGet("/{id:guid}/comments", ListComments)
            .WithSummary("Comentarios visiveis para o usuario.");

        group.MapPost("/{id:guid}/comments", AddComment)
            .ValidatingBody<AddCommentRequest>()
            .WithSummary("Registra um comentario publico ou interno.");

        group.MapGet("/{id:guid}/history", ListHistory)
            .WithSummary("Historico de alteracoes do chamado.");

        group.MapPost("/{id:guid}/status", ChangeStatus)
            .ValidatingBody<ChangeStatusRequest>()
            .WithSummary("Move o chamado na maquina de estados.");

        group.MapPost("/{id:guid}/assignment", Assign)
            .RequireAuthorization(AuthorizationPolicies.Staff)
            .WithSummary("Define ou remove o tecnico responsavel.");

        routes.MapGet("/api/categories", ListCategories)
            .RequireAuthorization()
            .WithTags("Categorias")
            .WithSummary("Categorias ativas, para os seletores da interface.");

        routes.MapGet("/api/staff", ListStaff)
            .RequireAuthorization(AuthorizationPolicies.Staff)
            .WithTags("Usuarios")
            .WithSummary("Tecnicos e gestores ativos, para o seletor de responsavel.");

        return routes;
    }

    private static async Task<IResult> Create(
        CreateTicketRequest request,
        TicketService tickets,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var result = await tickets.CreateAsync(request, currentUser.Viewer, cancellationToken);

        return result switch
        {
            CreateTicketResult.Created created =>
                TypedResults.Created($"/api/tickets/{created.Ticket.Id}", created.Ticket),

            CreateTicketResult.CategoryNotFound notFound => TypedResults.Problem(
                detail: notFound.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Categoria inválida"),

            // Falta de política de SLA é erro de configuração do sistema, não do pedido do
            // usuário — daí 500 e não 400. O log do Serilog registra a prioridade.
            CreateTicketResult.SlaPolicyMissing missing => TypedResults.Problem(
                detail: missing.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Política de SLA ausente"),

            _ => throw new InvalidOperationException(
                $"Resultado de criação não tratado: {result.GetType().Name}.")
        };
    }

    private static async Task<IResult> List(
        TicketService tickets,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        // Os arrays vêm repetidos na query string: ?status=Open&status=Triage
        TicketStatus[]? status = null,
        TicketPriority[]? priority = null,
        Guid? categoryId = null,
        Guid? assignedTechnicianId = null,
        bool? unassigned = null,
        bool? overdue = null,
        string? search = null,
        TicketSort sort = TicketSort.CreatedAtDescending,
        int? page = null,
        int? pageSize = null)
    {
        var filter = new TicketFilter(
            status, priority, categoryId, assignedTechnicianId, unassigned, overdue, search, sort)
        {
            // PageRequest limita o tamanho; pedido maior é reduzido em silêncio, nunca
            // honrado. Ver PageRequest.MaxPageSize.
            Page = new PageRequest(page, pageSize)
        };

        var result = await tickets.ListAsync(filter, currentUser.Viewer, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> Get(
        Guid id,
        TicketService tickets,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetAsync(id, currentUser.Viewer, cancellationToken);

        // 404 tanto para inexistente quanto para chamado de terceiro. Um 403 aqui
        // confirmaria que o chamado existe para quem estivesse testando identificadores.
        return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
    }

    private static async Task<IResult> ListComments(
        Guid id,
        TicketCommentService comments,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await comments.ListAsync(id, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> AddComment(
        Guid id,
        AddCommentRequest request,
        TicketCommentService comments,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var result = await comments.AddAsync(id, request, currentUser.Viewer, cancellationToken);

        return result switch
        {
            AddCommentResult.Added added => TypedResults.Created(
                $"/api/tickets/{id}/comments/{added.Comment.Id}", added.Comment),

            AddCommentResult.TicketNotFound => TypedResults.NotFound(),

            AddCommentResult.InternalNotAllowed forbidden => TypedResults.Problem(
                detail: forbidden.Message,
                statusCode: StatusCodes.Status403Forbidden,
                title: "Comentário interno não permitido"),

            AddCommentResult.TicketClosed closed => TypedResults.Problem(
                detail: closed.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Chamado encerrado"),

            _ => throw new InvalidOperationException(
                $"Resultado de comentário não tratado: {result.GetType().Name}.")
        };
    }

    private static async Task<IResult> ListHistory(
        Guid id,
        TicketService tickets,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await tickets.ListHistoryAsync(id, currentUser.Viewer, cancellationToken));

    private static async Task<IResult> ChangeStatus(
        Guid id,
        ChangeStatusRequest request,
        TicketWorkflowService workflow,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var result = await workflow.ChangeStatusAsync(id, request, currentUser.Viewer, cancellationToken);

        return result switch
        {
            ChangeStatusResult.Changed changed => TypedResults.Ok(changed.Ticket),

            ChangeStatusResult.TicketNotFound => TypedResults.NotFound(),

            // 409 e não 400: o pedido está bem formado, mas conflita com o estado atual do
            // chamado — que pode ter mudado entre a tela carregar e o clique.
            ChangeStatusResult.TransitionRejected rejected => TypedResults.Problem(
                detail: rejected.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Transição não permitida"),

            _ => throw new InvalidOperationException(
                $"Resultado de mudança de status não tratado: {result.GetType().Name}.")
        };
    }

    private static async Task<IResult> Assign(
        Guid id,
        AssignRequest request,
        TicketWorkflowService workflow,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var result = await workflow.AssignAsync(id, request, currentUser.Viewer, cancellationToken);

        return result switch
        {
            AssignResult.Assigned assigned => TypedResults.Ok(assigned.Ticket),

            AssignResult.TicketNotFound => TypedResults.NotFound(),

            AssignResult.NotAllowed notAllowed => TypedResults.Problem(
                detail: notAllowed.Message,
                statusCode: StatusCodes.Status403Forbidden,
                title: "Atribuição não permitida"),

            AssignResult.TechnicianInvalid invalid => TypedResults.Problem(
                detail: invalid.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Responsável inválido"),

            _ => throw new InvalidOperationException(
                $"Resultado de atribuição não tratado: {result.GetType().Name}.")
        };
    }

    private static async Task<IResult> ListStaff(
        TicketWorkflowService workflow, CancellationToken cancellationToken) =>
        TypedResults.Ok(await workflow.ListStaffAsync(cancellationToken));

    private static async Task<IResult> ListCategories(
        TicketService tickets, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tickets.ListCategoriesAsync(cancellationToken));
}
