using OpsDesk.Domain.Entities;

namespace OpsDesk.Infrastructure.Persistence.Seed;

/// <summary>
/// Feriados nacionais brasileiros, calculados por ano em vez de digitados.
///
/// Carnaval e Corpus Christi são ponto facultativo federal, não feriado por lei. Entram na
/// lista porque a equipe de suporte não está de plantão nesses dias, e SLA contando hora
/// útil em dia sem ninguém atendendo é indicador mentiroso. Para tratá-los como dia normal,
/// basta remover as duas linhas correspondentes.
/// </summary>
public static class BrazilianHolidays
{
    public static IEnumerable<Holiday> ForYear(int year)
    {
        var easter = Easter(year);

        return
        [
            new Holiday { Date = new DateOnly(year, 1, 1), Name = "Confraternização Universal" },
            new Holiday { Date = easter.AddDays(-48), Name = "Carnaval (segunda)" },
            new Holiday { Date = easter.AddDays(-47), Name = "Carnaval (terça)" },
            new Holiday { Date = easter.AddDays(-2), Name = "Sexta-feira Santa" },
            new Holiday { Date = new DateOnly(year, 4, 21), Name = "Tiradentes" },
            new Holiday { Date = new DateOnly(year, 5, 1), Name = "Dia do Trabalho" },
            new Holiday { Date = easter.AddDays(60), Name = "Corpus Christi" },
            new Holiday { Date = new DateOnly(year, 9, 7), Name = "Independência do Brasil" },
            new Holiday { Date = new DateOnly(year, 10, 12), Name = "Nossa Senhora Aparecida" },
            new Holiday { Date = new DateOnly(year, 11, 2), Name = "Finados" },
            new Holiday { Date = new DateOnly(year, 11, 15), Name = "Proclamação da República" },
            new Holiday { Date = new DateOnly(year, 11, 20), Name = "Consciência Negra" },
            new Holiday { Date = new DateOnly(year, 12, 25), Name = "Natal" }
        ];
    }

    /// <summary>
    /// Domingo de Páscoa pelo algoritmo gregoriano anônimo (Meeus/Jones/Butcher).
    /// Carnaval, Sexta-feira Santa e Corpus Christi são deslocamentos fixos a partir dele.
    /// </summary>
    private static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }
}
