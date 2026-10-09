using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanificacionCU40 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AltoCm",
                schema: "deposito",
                table: "Recepciones",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnchoCm",
                schema: "deposito",
                table: "Recepciones",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LargoCm",
                schema: "deposito",
                table: "Recepciones",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PesoKg",
                schema: "deposito",
                table: "Recepciones",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AltoCm",
                schema: "deposito",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "AnchoCm",
                schema: "deposito",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "LargoCm",
                schema: "deposito",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "PesoKg",
                schema: "deposito",
                table: "Recepciones");
        }
    }
}
