using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanificacionCU40 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaEntregaProgramada",
                schema: "envios",
                table: "Envios",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FranjaHorariaId",
                schema: "envios",
                table: "Envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ZonaId",
                schema: "envios",
                table: "Envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Contenido = table.Column<string>(type: "text", nullable: false),
                    PublicadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublicadoEn_OcurridoEn",
                schema: "envios",
                table: "outbox_messages",
                columns: new[] { "PublicadoEn", "OcurridoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "envios");

            migrationBuilder.DropColumn(
                name: "FechaEntregaProgramada",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "FranjaHorariaId",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "ZonaId",
                schema: "envios",
                table: "Envios");
        }
    }
}
