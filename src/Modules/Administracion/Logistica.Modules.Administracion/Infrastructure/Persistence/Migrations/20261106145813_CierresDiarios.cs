using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CierresDiarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cierres_diarios",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    EnviosDevueltos = table.Column<int>(type: "integer", nullable: false),
                    RutasSinFinalizar = table.Column<int>(type: "integer", nullable: false),
                    LiquidacionesGeneradas = table.Column<int>(type: "integer", nullable: false),
                    EjecutadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cierres_diarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cierres_diarios_OperadorId_Fecha",
                schema: "administracion",
                table: "cierres_diarios",
                columns: new[] { "OperadorId", "Fecha" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cierres_diarios",
                schema: "administracion");
        }
    }
}
