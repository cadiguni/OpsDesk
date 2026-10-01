using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Storage;

/// <summary>
/// Anexos no Azure Blob Storage — o que permite mais de uma réplica da API, porque todas
/// leem e gravam no mesmo lugar (README, seção 18, "Infraestrutura").
///
/// Os bytes continuam passando pela API, como no disco: o filtro de visibilidade roda
/// antes de o conteúdo ser aberto, e não existe link para o arquivo que funcione sem
/// sessão. URL SAS direta para o navegador seria mais barata em tráfego, mas é credencial
/// ao portador — quem tem o link baixa, até expirar.
/// </summary>
public class AzureBlobAttachmentStorage(BlobContainerClient container, ILogger<AzureBlobAttachmentStorage> logger)
    : IAttachmentStorage
{
    public static BlobContainerClient CreateContainerClient(AzureBlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobContainerClient(options.ConnectionString, options.ContainerName);
        }

        // Managed Identity no Azure; fora dele, service principal pelas variáveis
        // AZURE_TENANT_ID, AZURE_CLIENT_ID e AZURE_CLIENT_SECRET; em desenvolvimento, o
        // `az login` de quem está rodando.
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                ? null
                : options.ManagedIdentityClientId
        });

        return new BlobServiceClient(new Uri(options.ServiceUri!), credential).GetBlobContainerClient(options.ContainerName);
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        // O equivalente ao FileMode.CreateNew do disco: a chave é única, e sobrescrever em
        // silêncio esconderia o defeito que gerou a colisão.
        await Blob(key).UploadAsync(
            content,
            new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var download = await Blob(key).DownloadStreamingAsync(cancellationToken: cancellationToken);

            return download.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Mesmo caso do disco: registro sem arquivo é inconsistência de infraestrutura —
            // restauração parcial, soft delete expirado —, e a API responde 404 em vez de 500.
            logger.LogWarning("Anexo {Key} não encontrado no contêiner {Container}.", key, container.Name);

            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await Blob(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cria o contêiner, se faltar, e grava e apaga um blob de sonda. Chamado na subida,
    /// pelo mesmo motivo da sonda em disco: permissão faltando na identidade só apareceria
    /// no primeiro anexo, semanas depois.
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var probe = container.GetBlobClient($".opsdesk-write-probe-{Guid.NewGuid():N}");

        await probe.UploadAsync(BinaryData.Empty, cancellationToken);
        await probe.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public string Describe() => container.Uri.ToString();

    /// <summary>
    /// A chave vira o nome do blob. Ela é gerada pelo sistema; a recusa de caminho relativo
    /// fica aqui pelo mesmo motivo da do disco — o dia em que alguém montar a chave com dado
    /// de entrada, o erro aparece como exceção.
    /// </summary>
    private BlobClient Blob(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains('\\')
            || key.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Chave de anexo inválida: {key}");
        }

        return container.GetBlobClient(key);
    }
}
