using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BultosDeCadaIntento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "BultoIds",
                schema: "envios",
                table: "IntentosEntrega",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            // Intentos existentes: hasta ahora cada intento era del envío entero, así que corresponde a todos sus bultos.
            migrationBuilder.Sql("""
                UPDATE envios."IntentosEntrega" AS i
                SET "BultoIds" = ARRAY(SELECT b."Id" FROM envios."Bultos" AS b WHERE b."EnvioId" = i."EnvioId" ORDER BY b."Codigo")
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BultoIds",
                schema: "envios",
                table: "IntentosEntrega");
        }
    }
}
