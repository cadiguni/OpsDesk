using System.Security.Cryptography;

namespace OpsDesk.Application.Auth;

/// <summary>
/// Senha provisória gerada pelo sistema, para o gestor entregar quando não há e-mail.
///
/// Sem caracteres que se confundem lidos em voz alta ou numa tela pequena — 0 e O, 1, l e
/// I —, porque o caminho típico dela é alguém ditando para outra pessoa. Doze caracteres
/// desse alfabeto passam de 60 bits, e ela vale para um único login.
/// </summary>
public static class TemporaryPassword
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    public static string Generate(int length = 12) =>
        new(Enumerable.Range(0, length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
}
