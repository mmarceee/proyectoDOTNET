using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiagnosticoVisita : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bultos",
                schema: "ejecucion",
                table: "operaciones_dispositivo",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bultos",
                schema: "ejecucion",
                table: "operaciones_dispositivo");
        }
    }
}
