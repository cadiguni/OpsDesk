using System.Net;
using System.Net.Http.Json;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using OpsDesk.Application.Attachments;
using OpsDesk.Application.Tickets;
using OpsDesk.Domain.Enums;
using OpsDesk.Infrastructure.Storage;
using Testcontainers.Azurite;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Anexos no Azure Blob, contra o emulador (Azurite) num contêiner próprio. O que importa
/// aqui é que a troca de armazenamento não mude nada do lado de fora: mesma API, mesma
/// regra de visibilidade, o mesmo 404 para arquivo perdido.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AzureBlobStorageTests(PostgresFixture fixture) : TicketTestBase(fixture), IAsyncLifetime
{
    // `--skipApiVersionCheck`: o SDK costuma andar à frente do emulador, que recusaria a
    // versão de API mais nova com 400 sem relação nenhuma com o que se quer testar.
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithInMemoryPersistence()
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public Task InitializeAsync() => _azurite.StartAsync();

    public Task DisposeAsync() => _azurite.DisposeAsync().AsTask();

    private AzureBlobAttachmentStorage Storage(string container = "anexos")
    {
        var options = new AzureBlobStorageOptions { ConnectionString = _azurite.GetConnectionString(), ContainerName = container };

        return new AzureBlobAttachmentStorage(
            AzureBlobAttachmentStorage.CreateContainerClient(options),
            NullLogger<AzureBlobAttachmentStorage>.Instance);
    }

    [Fact]
    public async Task Grava_le_e_apaga()
    {
        var storage = Storage();
        await storage.EnsureReadyAsync();

        await storage.SaveAsync("2026/10/um.txt", new MemoryStream("conteúdo"u8.ToArray()));

        await using (var read = await storage.OpenReadAsync("2026/10/um.txt"))
        {
            Assert.NotNull(read);
            Assert.Equal("conteúdo", await new StreamReader(read).ReadToEndAsync());
        }

        await storage.DeleteAsync("2026/10/um.txt");

        Assert.Null(await storage.OpenReadAsync("2026/10/um.txt"));
    }

    [Fact]
    public async Task Arquivo_que_nao_existe_e_nulo_e_nao_excecao()
    {
        var storage = Storage();
        await storage.EnsureReadyAsync();

        Assert.Null(await storage.OpenReadAsync("2026/10/sumiu.txt"));

        // Apagar o que já não existe também não é erro: a limpeza de pendentes pode rodar
        // em duas réplicas ao mesmo tempo.
        await storage.DeleteAsync("2026/10/sumiu.txt");
    }

    [Fact]
    public async Task Chave_repetida_nao_sobrescreve()
    {
        var storage = Storage();
        await storage.EnsureReadyAsync();
        await storage.SaveAsync("2026/10/dois.txt", new MemoryStream("primeiro"u8.ToArray()));

        var ex = await Assert.ThrowsAsync<RequestFailedException>(
            () => storage.SaveAsync("2026/10/dois.txt", new MemoryStream("segundo"u8.ToArray())));

        Assert.Equal(409, ex.Status);
    }

    [Fact]
    public async Task Chave_com_caminho_relativo_e_recusada()
    {
        var storage = Storage();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.SaveAsync("2026/../../fora.txt", new MemoryStream([1])));
    }

    [Fact]
    public async Task Pela_api_o_anexo_vai_e_volta_pelo_blob_com_a_mesma_visibilidade()
    {
        await Fixture.ResetAsync();
        var world = await SetUpAsync();

        await using var api = new OpsDeskApiFactory(Fixture.ConnectionString, extraSettings: new Dictionary<string, string?>
        {
            ["AttachmentStorage:Provider"] = "AzureBlob",
            ["AttachmentStorage:AzureBlob:ConnectionString"] = _azurite.GetConnectionString(),
            ["AttachmentStorage:AzureBlob:ContainerName"] = "opsdesk-api"
        });

        var (requester, _) = await SignInAsync(api, UserRole.Requester);
        var (other, _) = await SignInAsync(api, UserRole.Requester);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("log da VPN"));
        file.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        form.Add(file, "file", "vpn.txt");

        var upload = await requester.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = (await upload.Content.ReadJsonAsync<AttachmentItem>())!;

        var open = await requester.PostAsJsonAsync(
            "/api/tickets", NewTicket(world.CategoryId) with { AttachmentIds = [attachment.Id] });
        open.EnsureSuccessStatusCode();

        // A subida criou o contêiner, e o arquivo está nele — não no disco.
        var container = new BlobContainerClient(_azurite.GetConnectionString(), "opsdesk-api");
        var blobs = new List<string>();
        await foreach (var blob in container.GetBlobsAsync())
        {
            blobs.Add(blob.Name);
        }

        Assert.Single(blobs);

        var download = await requester.GetAsync($"/api/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("log da VPN", await download.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/attachments/{attachment.Id}")).StatusCode);
    }
}
