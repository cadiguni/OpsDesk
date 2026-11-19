using System.Text;
using System.Text.Json;
using OpsDesk.Application.Auth;
using OpsDesk.Infrastructure.Authentication;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// Conferência do id token do Entra ID. A assinatura não é verificada — o token vem direto
/// do endpoint de token, por TLS e com o client secret —, então são estas conferências que
/// separam o token deste fluxo de qualquer outro.
/// </summary>
public class EntraIdTokenReaderTests
{
    private const string Tenant = "0f6c5e3a-1b2c-4d5e-8f90-a1b2c3d4e5f6";
    private const string Client = "11111111-2222-3333-4444-555555555555";
    private const string Nonce = "nonce-do-fluxo";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly EntraAppCredentials App = new(Tenant, Client, "segredo");

    [Fact]
    public void Token_valido_devolve_oid_email_e_nome()
    {
        var result = Read(Claims());

        var identity = Assert.IsType<EntraRedeemResult.Succeeded>(result).Identity;
        Assert.Equal("oid-1", identity.ObjectId);
        Assert.Equal("ana@empresa.com", identity.Email);
        Assert.Equal("Ana", identity.Name);
    }

    [Fact]
    public void Sem_claim_email_usa_o_upn()
    {
        var claims = Claims();
        claims.Remove("email");

        var identity = Assert.IsType<EntraRedeemResult.Succeeded>(Read(claims)).Identity;
        Assert.Equal("ana.upn@empresa.com", identity.Email);
    }

    [Fact]
    public void Aud_em_lista_com_o_client_id_e_aceito()
    {
        var claims = Claims();
        claims["aud"] = new[] { Client };

        Assert.IsType<EntraRedeemResult.Succeeded>(Read(claims));
    }

    [Theory]
    [InlineData("aud", "outro-aplicativo")]
    [InlineData("nonce", "nonce-de-outro-fluxo")]
    [InlineData("tid", "99999999-9999-9999-9999-999999999999")]
    [InlineData("iss", "https://sts.windows.net/0f6c5e3a-1b2c-4d5e-8f90-a1b2c3d4e5f6/")]
    public void Claim_que_nao_confere_recusa(string claim, string value)
    {
        var claims = Claims();
        claims[claim] = value;

        Assert.IsType<EntraRedeemResult.Failed>(Read(claims));
    }

    [Theory]
    [InlineData("oid")]
    [InlineData("nonce")]
    [InlineData("exp")]
    [InlineData("tid")]
    public void Claim_obrigatoria_ausente_recusa(string claim)
    {
        var claims = Claims();
        claims.Remove(claim);

        Assert.IsType<EntraRedeemResult.Failed>(Read(claims));
    }

    [Fact]
    public void Sem_email_nem_upn_com_arroba_recusa()
    {
        var claims = Claims();
        claims.Remove("email");
        claims["preferred_username"] = "ana";

        Assert.IsType<EntraRedeemResult.Failed>(Read(claims));
    }

    [Fact]
    public void Token_expirado_alem_da_tolerancia_recusa()
    {
        var claims = Claims();
        claims["exp"] = Now.AddMinutes(-6).ToUnixTimeSeconds();

        Assert.IsType<EntraRedeemResult.Failed>(Read(claims));
    }

    [Fact]
    public void Relogio_um_pouco_adiantado_ainda_aceita()
    {
        var claims = Claims();
        claims["exp"] = Now.AddMinutes(-2).ToUnixTimeSeconds();

        Assert.IsType<EntraRedeemResult.Succeeded>(Read(claims));
    }

    [Fact]
    public void Tenant_configurado_pelo_dominio_confere_so_o_emissor()
    {
        // empresa.onmicrosoft.com não se compara com o tid; quem amarra o tenant é o
        // endpoint /{tenant}/ e o emissor, que precisa bater com o tid do próprio token.
        var app = App with { TenantId = "empresa.onmicrosoft.com" };

        Assert.IsType<EntraRedeemResult.Succeeded>(EntraIdTokenReader.Read(Token(Claims()), app, Nonce, Now));
    }

    [Theory]
    [InlineData("nao-e-jwt")]
    [InlineData("a.@@@.c")]
    public void Token_malformado_recusa(string token) =>
        Assert.IsType<EntraRedeemResult.Failed>(EntraIdTokenReader.Read(token, App, Nonce, Now));

    private static EntraRedeemResult Read(Dictionary<string, object> claims) =>
        EntraIdTokenReader.Read(Token(claims), App, Nonce, Now);

    private static Dictionary<string, object> Claims() => new()
    {
        ["iss"] = $"https://login.microsoftonline.com/{Tenant}/v2.0",
        ["aud"] = Client,
        ["tid"] = Tenant,
        ["oid"] = "oid-1",
        ["nonce"] = Nonce,
        ["exp"] = Now.AddMinutes(60).ToUnixTimeSeconds(),
        ["nbf"] = Now.AddMinutes(-1).ToUnixTimeSeconds(),
        ["email"] = "ana@empresa.com",
        ["preferred_username"] = "ana.upn@empresa.com",
        ["name"] = "Ana"
    };

    /// <summary>JWT sem assinatura válida: o leitor não a confere, de propósito.</summary>
    private static string Token(Dictionary<string, object> claims) =>
        $"{Segment(new { alg = "RS256", typ = "JWT" })}.{Segment(claims)}.assinatura";

    private static string Segment(object value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
