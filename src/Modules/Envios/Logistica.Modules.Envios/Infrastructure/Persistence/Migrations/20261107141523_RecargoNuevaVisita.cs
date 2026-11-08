using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecargoNuevaVisita : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecargosNuevaVisita",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric", nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecargosNuevaVisita", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecargosNuevaVisita_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecargosNuevaVisita_EnvioId",
                schema: "envios",
                table: "RecargosNuevaVisita",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_RecargosNuevaVisita_OperadorId_ComercioId_OcurridoEn",
                schema: "envios",
                table: "RecargosNuevaVisita",
                columns: new[] { "OperadorId", "ComercioId", "OcurridoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecargosNuevaVisita",
                schema: "envios");
        }
    }
}
