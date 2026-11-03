using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AmbienteEnviosApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Envios_ReferenciaComercio",
                schema: "envios",
                table: "Envios");

            migrationBuilder.AddColumn<bool>(
                name: "EsPrueba",
                schema: "envios",
                table: "Envios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_Envios_ReferenciaComercio",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "ComercioId", "EsPrueba", "ReferenciaComercio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Envios_ReferenciaComercio",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "EsPrueba",
                schema: "envios",
                table: "Envios");

            migrationBuilder.CreateIndex(
                name: "UX_Envios_ReferenciaComercio",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "ComercioId", "ReferenciaComercio" },
                unique: true);
        }
    }
}
