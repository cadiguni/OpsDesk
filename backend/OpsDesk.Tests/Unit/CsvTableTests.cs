using OpsDesk.Application.Import;

namespace OpsDesk.Tests.Unit;

public class CsvTableTests
{
    [Fact]
    public void Le_virgula_com_campo_entre_aspas_e_aspas_escapadas()
    {
        var rows = CsvTable.Parse("titulo,descricao\nVPN,\"Diz \"\"senha inválida\"\", mesmo certa\"\n");

        var row = Assert.Single(rows);
        Assert.Equal("VPN", row.Fields["titulo"]);
        Assert.Equal("Diz \"senha inválida\", mesmo certa", row.Fields["descricao"]);
    }

    [Fact]
    public void Descobre_ponto_e_virgula_pelo_cabecalho_como_o_excel_em_portugues_salva()
    {
        var rows = CsvTable.Parse("titulo;solicitante_email;descricao\r\nImpressora;ana@empresa.com;Não imprime, nem em PDF\r\n");

        var row = Assert.Single(rows);
        Assert.Equal("ana@empresa.com", row.Fields["solicitante_email"]);
        Assert.Equal("Não imprime, nem em PDF", row.Fields["descricao"]);
    }

    [Fact]
    public void Quebra_de_linha_dentro_de_aspas_fica_no_campo_e_o_numero_da_linha_segue_o_arquivo()
    {
        var rows = CsvTable.Parse("titulo,descricao\nA,\"linha 1\nlinha 2\"\nB,simples\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("linha 1\nlinha 2", rows[0].Fields["descricao"]);
        Assert.Equal(2, rows[0].Line);

        // A segunda linha de dados começa na linha 4 do arquivo: é esse o número que a
        // pessoa procura no editor quando a prévia acusa erro.
        Assert.Equal(4, rows[1].Line);
    }

    [Fact]
    public void Bom_do_excel_nao_vira_parte_do_nome_da_primeira_coluna()
    {
        var rows = CsvTable.Parse("﻿titulo,solicitante_email\nVPN,ana@empresa.com");

        Assert.Equal("VPN", Assert.Single(rows).Fields["titulo"]);
    }

    [Fact]
    public void Nome_de_coluna_ignora_maiusculas_e_linha_em_branco_e_pulada()
    {
        var rows = CsvTable.Parse("Ticket ID,Subject\n1,VPN\n,\n\n2,Impressora\n");

        Assert.Equal(["1", "2"], rows.Select(r => r.Fields["ticket id"]));
    }
}
