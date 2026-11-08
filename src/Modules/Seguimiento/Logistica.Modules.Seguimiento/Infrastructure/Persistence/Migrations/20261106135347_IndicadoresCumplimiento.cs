using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicadoresCumplimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EntregaComprometidaHasta",
                schema: "seguimiento",
                table: "envios_seguidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsPrueba",
                schema: "seguimiento",
                table: "envios_seguidos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ZonaId",
                schema: "seguimiento",
                table: "envios_seguidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "registros_cumplimiento",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZonaId = table.Column<Guid>(type: "uuid", nullable: true),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ATiempo = table.Column<bool>(type: "boolean", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_cumplimiento", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_registros_cumplimiento_OperadorId_Tipo_OcurridoEn",
                schema: "seguimiento",
                table: "registros_cumplimiento",
                columns: new[] { "OperadorId", "Tipo", "OcurridoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registros_cumplimiento",
                schema: "seguimiento");

            migrationBuilder.DropColumn(
                name: "EntregaComprometidaHasta",
                schema: "seguimiento",
                table: "envios_seguidos");

            migrationBuilder.DropColumn(
                name: "EsPrueba",
                schema: "seguimiento",
                table: "envios_seguidos");

            migrationBuilder.DropColumn(
                name: "ZonaId",
                schema: "seguimiento",
                table: "envios_seguidos");
        }
    }
}
