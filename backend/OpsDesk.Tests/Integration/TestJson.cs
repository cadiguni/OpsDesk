using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpsDesk.Tests.Integration;

/// <summary>
/// Desserialização com as mesmas regras que a API usa para serializar.
///
/// Existe porque o padrão do <c>ReadFromJsonAsync</c> não tem o conversor de enum como
/// texto: sem isto, o teste falharia ao ler <c>"role": "Requester"</c> e a mensagem
/// apontaria para o teste, não para a diferença de contrato.
/// </summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static Task<T?> ReadJsonAsync<T>(this HttpContent content) =>
        content.ReadFromJsonAsync<T>(Options);
}
