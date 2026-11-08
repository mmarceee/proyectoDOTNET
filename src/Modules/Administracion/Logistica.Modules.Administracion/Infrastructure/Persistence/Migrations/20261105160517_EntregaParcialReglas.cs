using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EntregaParcialReglas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RF 33: las versiones ya publicadas quedan como antes del cambio (decisiones-entrega-parcial.md, sección 5,
            // decisión del 03/11/2026): sin entrega parcial, revisita sin recargo y recargo 0.
            migrationBuilder.AddColumn<bool>(
                name: "EntregaParcial_AdmiteEntregaParcial",
                schema: "administracion",
                table: "VersionesReglas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EntregaParcial_Pendientes",
                schema: "administracion",
                table: "VersionesReglas",
                type: "text",
                nullable: false,
                // EF propone "", que no es un valor del enum y rompería la lectura de las versiones existentes.
                defaultValue: "NuevaVisitaSinRecargo");

            migrationBuilder.AddColumn<decimal>(
                name: "EntregaParcial_RecargoNuevaVisita",
                schema: "administracion",
                table: "VersionesReglas",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntregaParcial_AdmiteEntregaParcial",
                schema: "administracion",
                table: "VersionesReglas");

            migrationBuilder.DropColumn(
                name: "EntregaParcial_Pendientes",
                schema: "administracion",
                table: "VersionesReglas");

            migrationBuilder.DropColumn(
                name: "EntregaParcial_RecargoNuevaVisita",
                schema: "administracion",
                table: "VersionesReglas");
        }
    }
}
