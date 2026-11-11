using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DevolucionesPorBulto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devoluciones_EnvioId",
                schema: "envios",
                table: "Devoluciones");

            migrationBuilder.CreateIndex(
                name: "UX_Devoluciones_EnvioAbierta",
                schema: "envios",
                table: "Devoluciones",
                column: "EnvioId",
                unique: true,
                filter: "\"Estado\" <> 'Cerrada'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Devoluciones_EnvioAbierta",
                schema: "envios",
                table: "Devoluciones");

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_EnvioId",
                schema: "envios",
                table: "Devoluciones",
                column: "EnvioId",
                unique: true);
        }
    }
}
