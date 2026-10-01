using OpsDesk.Infrastructure.Storage;

namespace OpsDesk.Tests.Unit;

public class AttachmentStorageOptionsTests
{
    private const string Azurite =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;";

    private static AttachmentStorageOptions Blob(string? serviceUri = null, string? connectionString = null, string container = "attachments") => new()
    {
        Provider = AttachmentStorageProvider.AzureBlob,
        AzureBlob = new AzureBlobStorageOptions
        {
            ServiceUri = serviceUri,
            ConnectionString = connectionString,
            ContainerName = container
        }
    };

    private static bool Valid(AttachmentStorageOptions options) =>
        new AttachmentStorageOptionsValidator().Validate(null, options).Succeeded;

    [Fact]
    public void Disco_nao_pede_nada_do_blob()
    {
        Assert.True(Valid(new AttachmentStorageOptions()));
    }

    [Fact]
    public void Conta_de_verdade_por_endereco_e_identidade_e_aceita()
    {
        Assert.True(Valid(Blob(serviceUri: "https://opsdesk.blob.core.windows.net")));
    }

    [Theory]
    [InlineData("UseDevelopmentStorage=true")]
    [InlineData(Azurite)]
    public void Connection_string_do_emulador_e_aceita(string connectionString)
    {
        Assert.True(Valid(Blob(connectionString: connectionString)));
    }

    [Fact]
    public void Connection_string_com_chave_de_conta_de_verdade_e_recusada()
    {
        // Chave em configuração é credencial permanente: o caminho é a Managed Identity.
        var real = "DefaultEndpointsProtocol=https;AccountName=opsdesk;AccountKey=abc123==;EndpointSuffix=core.windows.net";

        var result = new AttachmentStorageOptionsValidator().Validate(null, Blob(connectionString: real));

        Assert.True(result.Failed);
        Assert.Contains("Managed Identity", result.FailureMessage);
    }

    [Fact]
    public void Sem_endereco_nem_connection_string_ou_com_os_dois_e_recusado()
    {
        Assert.False(Valid(Blob()));
        Assert.False(Valid(Blob(serviceUri: "https://opsdesk.blob.core.windows.net", connectionString: Azurite)));
    }

    [Theory]
    [InlineData("http://opsdesk.blob.core.windows.net")]
    [InlineData("opsdesk.blob.core.windows.net")]
    public void Endereco_sem_https_e_recusado(string serviceUri)
    {
        Assert.False(Valid(Blob(serviceUri: serviceUri)));
    }

    [Theory]
    [InlineData("Anexos")]
    [InlineData("ab")]
    [InlineData("anexos--opsdesk")]
    [InlineData("anexos_opsdesk")]
    public void Nome_de_conteiner_invalido_e_recusado_na_subida(string container)
    {
        Assert.False(Valid(Blob(serviceUri: "https://opsdesk.blob.core.windows.net", container: container)));
    }
}
