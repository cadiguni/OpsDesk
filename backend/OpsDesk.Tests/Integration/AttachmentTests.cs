using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using OpsDesk.Application.Attachments;
using OpsDesk.Application.Tickets;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Anexos de chamado.
///
/// A ênfase é a mesma dos comentários: o que cada perfil <b>não</b> pode baixar. Anexo é
/// justamente o caminho pelo qual um print de nota interna vazaria sem que ninguém
/// percebesse, porque o arquivo não aparece no texto que alguém revisa.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AttachmentTests(PostgresFixture fixture) : TicketTestBase(fixture)
{
    // ----- Envio -----

    [Fact]
    public async Task Arquivo_enviado_fica_pendente_e_so_quem_enviou_enxerga()
    {
        var world = await SetUpAsync();

        var attachment = await UploadAsync(world.Requester, "print.png", "image/png");

        // Pendente é legível por quem enviou...
        Assert.Equal(HttpStatusCode.OK, (await Download(world.Requester, attachment.Id)).StatusCode);

        // ...e por mais ninguém, nem pelo gestor, que enxerga todo chamado. Enquanto não
        // houver vínculo, o arquivo não é de chamado nenhum.
        Assert.Equal(HttpStatusCode.NotFound, (await Download(world.Manager, attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Download(world.Technician, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task Tipo_fora_da_lista_e_recusado()
    {
        var world = await SetUpAsync();

        var response = await PostFileAsync(
            world.Requester, "instalador.exe", "application/x-msdownload", [1, 2, 3]);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Arquivo_acima_do_limite_e_recusado()
    {
        var world = await SetUpAsync();

        var bytes = new byte[AttachmentService.MaxFileSizeInBytes + 1];

        var response = await PostFileAsync(world.Requester, "log.txt", "text/plain", bytes);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Arquivo_vazio_e_recusado()
    {
        var world = await SetUpAsync();

        var response = await PostFileAsync(world.Requester, "vazio.txt", "text/plain", []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Envio_sem_autenticacao_e_recusado()
    {
        await SetUpAsync();

        using var anonymous = Fixture.Api.CreateBrowserClient();
        var response = await PostFileAsync(anonymous, "print.png", "image/png", [1, 2, 3]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ----- Abertura com anexo -----

    [Fact]
    public async Task Anexo_da_abertura_fica_visivel_para_o_solicitante_e_para_a_equipe()
    {
        var world = await SetUpAsync();
        var attachment = await UploadAsync(world.Requester, "print.png", "image/png");

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { AttachmentIds = [attachment.Id] });

        response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        var forRequester = await ListAttachmentsAsync(world.Requester, ticket!.Id);
        var item = Assert.Single(forRequester);
        Assert.Equal("print.png", item.FileName);
        Assert.False(item.IsInternal);
        Assert.True(item.IsImage);

        Assert.Single(await ListAttachmentsAsync(world.Manager, ticket.Id));

        Assert.Equal(HttpStatusCode.OK, (await Download(world.Manager, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task O_conteudo_baixado_e_o_mesmo_que_foi_enviado()
    {
        var world = await SetUpAsync();
        var bytes = Encoding.UTF8.GetBytes("linha 1\nlinha 2\nacentuação preservada");

        var attachment = await UploadAsync(world.Requester, "log.txt", "text/plain", bytes);

        var response = await Download(world.Requester, attachment.Id);
        var downloaded = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(bytes, downloaded);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("log.txt", response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task Anexo_de_outra_pessoa_nao_entra_no_meu_chamado()
    {
        // Sem esta recusa, mandar o identificador de um anexo alheio o traria para dentro
        // de um chamado que eu controlo — e com ele a visibilidade desse chamado.
        var world = await SetUpAsync();
        var alheio = await UploadAsync(world.OtherRequester, "segredo.pdf", "application/pdf");

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets",
            NewTicket(world.CategoryId) with { AttachmentIds = [alheio.Id] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // E o chamado não foi aberto: a recusa é da operação inteira.
        Assert.Equal(0, (await ListAsync(world.Requester)).TotalCount);
    }

    [Fact]
    public async Task Anexo_ja_vinculado_nao_e_reaproveitado()
    {
        var world = await SetUpAsync();
        var attachment = await UploadAsync(world.Requester, "print.png", "image/png");

        var first = await world.Requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId) with { AttachmentIds = [attachment.Id] });
        first.EnsureSuccessStatusCode();

        var second = await world.Requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId) with { AttachmentIds = [attachment.Id] });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    // ----- Anexo de comentário: o caso que não pode vazar -----

    [Fact]
    public async Task Anexo_de_nota_interna_nunca_chega_ao_solicitante()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var attachment = await UploadAsync(world.Technician, "evidencia.png", "image/png");

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest(
                "Log do servidor, não mandar para o usuário.",
                IsInternal: true,
                AttachmentIds: [attachment.Id]));

        response.EnsureSuccessStatusCode();

        // Na listagem do chamado o anexo não aparece para o solicitante...
        Assert.Empty(await ListAttachmentsAsync(world.Requester, ticket));

        // ...e o download direto pelo identificador também não passa. As duas pontas
        // importam: esconder só na listagem deixaria o arquivo acessível a quem tivesse
        // o identificador.
        Assert.Equal(HttpStatusCode.NotFound, (await Download(world.Requester, attachment.Id)).StatusCode);

        // Para a equipe, aparece marcado como interno.
        var forStaff = Assert.Single(await ListAttachmentsAsync(world.Manager, ticket));
        Assert.True(forStaff.IsInternal);
    }

    [Fact]
    public async Task Anexo_de_comentario_publico_chega_ao_solicitante()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var attachment = await UploadAsync(world.Technician, "passo-a-passo.pdf", "application/pdf");

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Segue o procedimento.", AttachmentIds: [attachment.Id]));

        response.EnsureSuccessStatusCode();

        var forRequester = Assert.Single(await ListAttachmentsAsync(world.Requester, ticket));
        Assert.False(forRequester.IsInternal);
        Assert.Equal(HttpStatusCode.OK, (await Download(world.Requester, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task Comentario_com_anexo_invalido_nao_e_gravado()
    {
        var world = await SetUpAsync();
        var ticket = await OpenTicketAsync(world);

        var response = await world.Technician.PostAsJsonAsync(
            $"/api/tickets/{ticket}/comments",
            new AddCommentRequest("Segue em anexo.", AttachmentIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nem o comentário, nem o fechamento, nem nada: o `return` acontece antes do
        // SaveChanges e a transação inteira fica para trás.
        Assert.Empty(await CommentsAsync(world.Manager, ticket));
    }

    [Fact]
    public async Task Anexo_de_chamado_de_terceiro_nao_e_visivel()
    {
        var world = await SetUpAsync();
        var attachment = await UploadAsync(world.Requester, "print.png", "image/png");

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId) with { AttachmentIds = [attachment.Id] });
        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        Assert.Empty(await ListAttachmentsAsync(world.OtherRequester, ticket!.Id));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Download(world.OtherRequester, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task Anexo_de_chamado_de_outro_tecnico_nao_e_visivel()
    {
        // Mesma regra do chamado: técnico alcança o que está sem responsável e o que é
        // dele. O anexo acompanha, sem exceção própria.
        var world = await SetUpAsync();
        var attachment = await UploadAsync(world.Requester, "print.png", "image/png");

        var response = await world.Requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId) with { AttachmentIds = [attachment.Id] });
        var ticket = await response.Content.ReadJsonAsync<TicketDetail>();

        await AssignAsync(world.Manager, ticket!.Id, world.OtherTechnicianId);

        Assert.Empty(await ListAttachmentsAsync(world.Technician, ticket.Id));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Download(world.Technician, attachment.Id)).StatusCode);
    }

    // ----- Atalhos -----

    private async Task<AttachmentItem> UploadAsync(
        HttpClient client, string fileName, string contentType, byte[]? bytes = null)
    {
        var response = await PostFileAsync(client, fileName, contentType, bytes ?? [1, 2, 3, 4]);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<AttachmentItem>())!;
    }

    private static async Task<HttpResponseMessage> PostFileAsync(
        HttpClient client, string fileName, string contentType, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);

        // Content-Type inválido no cabeçalho da parte estouraria aqui em vez de virar
        // resposta da API, então tipos exóticos entram pelo TryAddWithoutValidation.
        file.Headers.TryAddWithoutValidation("Content-Type", contentType);
        form.Add(file, "file", fileName);

        return await client.PostAsync("/api/attachments", form);
    }

    private static Task<HttpResponseMessage> Download(HttpClient client, Guid id) =>
        client.GetAsync($"/api/attachments/{id}");

    private static async Task<List<AttachmentItem>> ListAttachmentsAsync(
        HttpClient client, Guid ticketId)
    {
        var response = await client.GetAsync($"/api/tickets/{ticketId}/attachments");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadJsonAsync<List<AttachmentItem>>())!;
    }
}
