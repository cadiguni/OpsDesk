using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace OpsDesk.Infrastructure.Storage;

public enum AttachmentStorageProvider
{
    /// <summary>Disco local ou volume. Uma réplica só.</summary>
    FileSystem,

    /// <summary>Azure Blob Storage. O que permite mais de uma réplica da API.</summary>
    AzureBlob
}

public class AttachmentStorageOptions
{
    public const string SectionName = "AttachmentStorage";

    public AttachmentStorageProvider Provider { get; set; } = AttachmentStorageProvider.FileSystem;

    /// <summary>
    /// Raiz dos arquivos, quando o provedor é o disco. No compose é um volume montado; em
    /// produção, um caminho fora da árvore da aplicação, para que um deploy não leve os
    /// anexos embora.
    /// </summary>
    [Required]
    public string RootPath { get; set; } = "attachments";

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    /// <summary>
    /// Quanto tempo um anexo pendente sobrevive sem ser vinculado.
    ///
    /// Vinte e quatro horas é folgado para qualquer formulário que uma pessoa deixe aberto
    /// e retome — e curto o suficiente para que arquivo abandonado não acumule.
    /// </summary>
    [Range(1, 24 * 30)]
    public int PendingRetentionHours { get; set; } = 24;

    /// <summary>
    /// Intervalo entre varreduras. Zero desliga a varredura, o que é útil em ambiente de
    /// teste e em execução com muitas réplicas, onde a limpeza deve rodar em um lugar só.
    /// </summary>
    [Range(0, 24 * 7)]
    public int CleanUpIntervalHours { get; set; } = 6;
}

public class AzureBlobStorageOptions
{
    /// <summary>
    /// Endereço do serviço de blob da conta, como <c>https://conta.blob.core.windows.net</c>.
    /// A autenticação é por identidade — Managed Identity no Azure, service principal por
    /// variável de ambiente fora dele —, nunca por chave da conta.
    /// </summary>
    public string? ServiceUri { get; set; }

    /// <summary>Contêiner dos anexos. Criado na subida, se não existir.</summary>
    public string ContainerName { get; set; } = "attachments";

    /// <summary>
    /// Client id da Managed Identity atribuída pelo usuário. Vazio usa a identidade do
    /// sistema, ou a credencial que o ambiente oferecer.
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Só para o emulador (Azurite), em desenvolvimento e teste. Connection string com a
    /// chave de uma conta de verdade é recusada na subida: chave em configuração é
    /// credencial permanente, que vaza em log de deploy e em dump de variável de ambiente.
    /// </summary>
    public string? ConnectionString { get; set; }
}

/// <summary>
/// O que a anotação de dados não alcança: as regras que dependem do provedor escolhido.
/// </summary>
public partial class AttachmentStorageOptionsValidator : IValidateOptions<AttachmentStorageOptions>
{
    /// <summary>Nome da conta fixo do emulador. Conta de verdade não pode ter esse nome.</summary>
    public const string EmulatorAccountName = "devstoreaccount1";

    public ValidateOptionsResult Validate(string? name, AttachmentStorageOptions options)
    {
        if (options.Provider != AttachmentStorageProvider.AzureBlob)
        {
            return ValidateOptionsResult.Success;
        }

        var blob = options.AzureBlob;
        var hasUri = !string.IsNullOrWhiteSpace(blob.ServiceUri);
        var hasConnectionString = !string.IsNullOrWhiteSpace(blob.ConnectionString);

        if (!ContainerNamePattern().IsMatch(blob.ContainerName))
        {
            return ValidateOptionsResult.Fail(
                $"AttachmentStorage:AzureBlob:ContainerName '{blob.ContainerName}' não é um nome de contêiner válido: " +
                "de 3 a 63 caracteres, letras minúsculas, números e hífen.");
        }

        if (hasUri == hasConnectionString)
        {
            return ValidateOptionsResult.Fail(
                "Com AttachmentStorage:Provider = AzureBlob, informe AttachmentStorage:AzureBlob:ServiceUri " +
                "(https://conta.blob.core.windows.net, com Managed Identity). ConnectionString é só para o emulador, " +
                "e as duas juntas são ambíguas.");
        }

        if (hasUri)
        {
            return Uri.TryCreate(blob.ServiceUri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"AttachmentStorage:AzureBlob:ServiceUri '{blob.ServiceUri}' precisa ser um endereço https, " +
                    "como https://conta.blob.core.windows.net.");
        }

        return IsEmulator(blob.ConnectionString!)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "AttachmentStorage:AzureBlob:ConnectionString só é aceita para o emulador (Azurite). Para uma conta " +
                "de verdade use ServiceUri com Managed Identity: chave da conta em configuração é credencial " +
                "permanente que vaza em log e em variável de ambiente.");
    }

    public static bool IsEmulator(string connectionString)
    {
        var builder = new DbConnectionStringBuilder();

        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (builder.TryGetValue("UseDevelopmentStorage", out var development)
            && string.Equals(development?.ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return builder.TryGetValue("AccountName", out var account)
               && string.Equals(account?.ToString(), EmulatorAccountName, StringComparison.Ordinal);
    }

    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ContainerNamePattern();
}
