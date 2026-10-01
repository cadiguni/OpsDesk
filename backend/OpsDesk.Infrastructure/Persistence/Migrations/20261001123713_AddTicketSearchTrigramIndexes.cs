using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpsDesk.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Índices de trigramas para a busca da lista de chamados.
    ///
    /// A busca é <c>lower(coluna) LIKE '%termo%'</c> (TicketFilterExtensions), que índice
    /// B-tree não acelera — o curinga no começo obriga a varrer a tabela. Trigramas do
    /// pg_trgm aceleram exatamente essa forma, inclusive no meio do texto. A expressão do
    /// índice precisa ser a mesma da consulta, <c>lower(...)</c> incluído, e o teste
    /// <c>Busca_usa_os_indices_de_trigramas</c> fixa isso.
    ///
    /// SQL escrito à mão porque o EF não modela índice sobre expressão com classe de
    /// operador própria. Continua sendo migration — a invariante 7 proíbe SQL direto no
    /// banco, não SQL dentro da migration.
    ///
    /// O pg_trgm é extensão confiável desde o PostgreSQL 13: o dono do banco a cria sem
    /// precisar de superusuário.
    /// </summary>
    public partial class AddTicketSearchTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_tickets_title_trgm ON tickets USING gin (lower(title) gin_trgm_ops);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_tickets_description_trgm ON tickets USING gin (lower(description) gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_description_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tickets_title_trgm;");

            // A extensão fica: outra coisa pode ter passado a usá-la.
        }
    }
}
