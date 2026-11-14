using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminadoEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TerminadoEn",
                schema: "envios",
                table: "Envios",
                type: "timestamp with time zone",
                nullable: true);

            // Envíos que ya habían terminado antes de guardar el cierre (cierre técnico, D6 y D8): se usa su último evento
            // del historial, que es la transición que los dejó terminados. No se inventa historial nuevo.
            migrationBuilder.Sql("""
                UPDATE envios."Envios" e
                SET "TerminadoEn" = COALESCE(
                    (SELECT max(ev."OcurridoEn") FROM envios."EventosEnvio" ev WHERE ev."EnvioId" = e."Id"),
                    e."CreadoEn")
                WHERE e."Terminado";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerminadoEn",
                schema: "envios",
                table: "Envios");
        }
    }
}
