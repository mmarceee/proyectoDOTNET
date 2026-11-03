using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AmbienteEnviosApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsPrueba",
                schema: "seguimiento",
                table: "entregas_aviso",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EsPrueba",
                schema: "seguimiento",
                table: "entregas_aviso");
        }
    }
}
