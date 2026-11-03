using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReferenciaComercio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferenciaComercio",
                schema: "envios",
                table: "Envios",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Envios_ReferenciaComercio",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "ComercioId", "ReferenciaComercio" },
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
                name: "ReferenciaComercio",
                schema: "envios",
                table: "Envios");
        }
    }
}
