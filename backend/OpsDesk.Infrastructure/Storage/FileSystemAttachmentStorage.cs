using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Storage;

public class AttachmentStorageOptions
{
    public const string SectionName = "AttachmentStorage";

    /// <summary>
    /// Raiz dos arquivos. No compose é um volume montado; em produção, um caminho fora da
    /// árvore da aplicação, para que um deploy não leve os anexos embora.
    /// </summary>
    [Required]
    public string RootPath { get; set; } = "attachments";
}

/// <summary>
/// Armazenamento em disco.
///
/// Escolhido por não exigir serviço novo no ambiente local e por manter o banco enxuto.
/// A troca por S3 ou MinIO é uma implementação nova de <see cref="IAttachmentStorage"/>,
/// sem mexer em serviço nem em domínio.
/// </summary>
public class FileSystemAttachmentStorage : IAttachmentStorage
{
    private readonly string _root;
    private readonly ILogger<FileSystemAttachmentStorage> _logger;

    public FileSystemAttachmentStorage(
        IOptions<AttachmentStorageOptions> options, ILogger<FileSystemAttachmentStorage> logger)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        _logger = logger;

        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(
        string key, Stream content, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);

        if (!File.Exists(path))
        {
            // Registro no banco sem arquivo no disco é inconsistência de infraestrutura,
            // não pedido inválido — vale log, e a API responde 404 em vez de estourar.
            _logger.LogWarning("Anexo {Key} não encontrado em {Path}.", key, path);

            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920,
            useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolve a chave dentro da raiz e recusa qualquer caminho que escape dela.
    ///
    /// As chaves são geradas pelo sistema e nunca vêm do nome enviado pelo usuário, então
    /// esta verificação não deveria disparar nunca. É exatamente por isso que ela fica
    /// aqui: o dia em que alguém passar a montar a chave com dado de entrada, o erro
    /// aparece como exceção em vez de virar leitura de arquivo arbitrário.
    /// </summary>
    private string ResolvePath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));

        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Chave de anexo fora da raiz: {key}");
        }

        return path;
    }
}
