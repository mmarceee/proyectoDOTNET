using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicesConsultaEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId_CreadoEn",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "CreadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId_Estado",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Envios_OperadorId_CreadoEn",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_OperadorId_Estado",
                schema: "envios",
                table: "Envios");
        }
    }
}
