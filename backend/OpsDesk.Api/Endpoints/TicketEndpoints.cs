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

        routes.MapGet("/api/categories", ListCategories)
            .RequireAuthorization()
            .WithTags("Categorias")
            .WithSummary("Categorias ativas, para os seletores da interface.");

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

    private static async Task<IResult> ListCategories(
        TicketService tickets, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tickets.ListCategoriesAsync(cancellationToken));
}
