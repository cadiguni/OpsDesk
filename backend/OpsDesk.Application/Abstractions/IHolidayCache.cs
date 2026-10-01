namespace OpsDesk.Application.Abstractions;

/// <summary>
/// Descarta os feriados em memória. Sem isto, um feriado cadastrado pelo gestor só
/// passaria a valer quando o cache expirasse, até uma hora depois — e o chamado aberto
/// nesse intervalo ganharia prazo contando o feriado como dia útil.
/// </summary>
public interface IHolidayCache
{
    void Invalidate();
}
