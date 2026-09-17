using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using OpsDesk.Application.Abstractions;
using OpsDesk.Application.Attachments;

namespace OpsDesk.Api.Endpoints;

/// <summary>
/// Envio e download de anexos.
///
/// O envio não recebe o chamado: o anexo nasce pendente, e é a abertura do chamado ou o
/// comentário que o vincula. Isso existe porque o formulário de abertura precisa aceitar
/// arquivo antes de o chamado existir, e porque um anexo destinado a uma nota interna não
/// pode passar por um estado em que o solicitante o enxergue.
///
/// O download não tem filtro próprio: quem decide é o <c>VisibleTo</c> dentro do serviço.
/// </summary>
public static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/attachments")
            .RequireAuthorization()
            .WithTags("Anexos");

        group.MapPost("/", Upload)
            .DisableAntiforgery()
            .WithSummary("Envia um arquivo, que fica pendente ate ser vinculado.");

        group.MapGet("/{id:guid}", Download)
            .WithSummary("Baixa o conteudo do anexo, se for visivel para o usuario.");

        routes.MapGet("/api/tickets/{id:guid}/attachments", ListOfTicket)
            .RequireAuthorization()
            .WithTags("Anexos")
            .WithSummary("Anexos do chamado visiveis para o usuario.");

        return routes;
    }

    private static async Task<IResult> Upload(
        IFormFile file,
        AttachmentService attachments,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();

        var result = await attachments.UploadAsync(
            new UploadAttachmentRequest(file.FileName, file.ContentType, file.Length, content),
            currentUser.Viewer,
            cancellationToken);

        return result switch
        {
            UploadAttachmentResult.Uploaded uploaded => TypedResults.Created(
                $"/api/attachments/{uploaded.Attachment.Id}", uploaded.Attachment),

            UploadAttachmentResult.Empty empty => TypedResults.Problem(
                detail: empty.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Arquivo vazio"),

            // 413 é a resposta própria para tamanho, e a interface usa isso para dizer
            // qual arquivo passou do limite em vez de um "pedido inválido" genérico.
            UploadAttachmentResult.TooLarge tooLarge => TypedResults.Problem(
                detail: tooLarge.Message,
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Arquivo grande demais"),

            UploadAttachmentResult.TypeNotAllowed notAllowed => TypedResults.Problem(
                detail: notAllowed.Message,
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Tipo de arquivo não aceito"),

            _ => throw new InvalidOperationException(
                $"Resultado de envio não tratado: {result.GetType().Name}.")
        };
    }

    private static async Task<IResult> Download(
        Guid id,
        AttachmentService attachments,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var content = await attachments.OpenAsync(id, currentUser.Viewer, cancellationToken);

        if (content is null)
        {
            // Inexistente, invisível e arquivo perdido respondem igual: distinguir
            // confirmaria a existência de um anexo a quem não pode vê-lo.
            return TypedResults.NotFound();
        }

        // `fileDownloadName` faz o ASP.NET escrever o Content-Disposition com o nome
        // original, codificado — inclusive com acento, que é a regra e não a exceção aqui.
        return TypedResults.File(
            content.Content,
            contentType: content.ContentType,
            fileDownloadName: content.FileName);
    }

    private static async Task<IResult> ListOfTicket(
        Guid id,
        AttachmentService attachments,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await attachments.ListAsync(id, currentUser.Viewer, cancellationToken));
}
