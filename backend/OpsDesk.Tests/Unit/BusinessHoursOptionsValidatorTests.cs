using OpsDesk.Infrastructure.Time;

namespace OpsDesk.Tests.Unit;

/// <summary>
/// O expediente é validado na subida.
///
/// Os três casos abaixo têm a mesma assinatura: passam em qualquer validação de formato,
/// não levantam exceção na subida, e só aparecem depois — no cálculo de SLA de um chamado
/// real, longe da configuração que os causou.
/// </summary>
public class BusinessHoursOptionsValidatorTests
{
    private readonly BusinessHoursOptionsValidator _validator = new();

    [Fact]
    public void O_padrao_e_valido()
    {
        Assert.True(_validator.Validate(null, new BusinessHoursOptions()).Succeeded);
    }

    [Fact]
    public void Fuso_inexistente_derruba_a_subida()
    {
        var result = _validator.Validate(
            null, new BusinessHoursOptions { TimeZone = "America/Sao_Paolo" });

        // O erro de digitação acima é o realista: sem esta verificação, ele viraria um
        // TimeZoneNotFoundException no meio do cálculo de prazo do primeiro chamado.
        Assert.False(result.Succeeded);
        Assert.Contains("America/Sao_Paolo", result.FailureMessage);
    }

    [Fact]
    public void Expediente_invertido_derruba_a_subida()
    {
        var result = _validator.Validate(null, new BusinessHoursOptions
        {
            Start = new TimeOnly(18, 0),
            End = new TimeOnly(8, 0)
        });

        // Sem isto todo prazo vira negativo, sem exceção nenhuma: o chamado nasce vencido
        // e o dashboard passa a mentir com a cara mais séria do mundo.
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Sem_dia_util_derruba_a_subida()
    {
        var result = _validator.Validate(null, new BusinessHoursOptions { WorkingDays = [] });

        Assert.False(result.Succeeded);
    }
}
