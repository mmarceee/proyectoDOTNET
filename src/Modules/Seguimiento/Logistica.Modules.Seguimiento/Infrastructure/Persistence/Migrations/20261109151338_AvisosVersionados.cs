using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // RF 36: las suscripciones y entregas existentes quedan en v1, la versión que ya recibían sus receptores.
    public partial class AvisosVersionados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "seguimiento",
                table: "suscripciones_aviso",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CodigoBulto",
                schema: "seguimiento",
                table: "entregas_aviso",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstadoBulto",
                schema: "seguimiento",
                table: "entregas_aviso",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoBulto",
                schema: "seguimiento",
                table: "entregas_aviso",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "seguimiento",
                table: "entregas_aviso",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                schema: "seguimiento",
                table: "suscripciones_aviso");

            migrationBuilder.DropColumn(
                name: "CodigoBulto",
                schema: "seguimiento",
                table: "entregas_aviso");

            migrationBuilder.DropColumn(
                name: "EstadoBulto",
                schema: "seguimiento",
                table: "entregas_aviso");

            migrationBuilder.DropColumn(
                name: "MotivoBulto",
                schema: "seguimiento",
                table: "entregas_aviso");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "seguimiento",
                table: "entregas_aviso");
        }
    }
}
