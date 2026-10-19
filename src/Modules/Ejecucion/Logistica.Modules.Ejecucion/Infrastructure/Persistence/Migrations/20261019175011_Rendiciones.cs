using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rendiciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rendiciones",
                schema: "ejecucion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IniciadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinalizadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rendiciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lineas_rendicion",
                schema: "ejecucion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RendicionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Situacion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Declarada = table.Column<bool>(type: "boolean", nullable: false),
                    Observacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Recibida = table.Column<bool>(type: "boolean", nullable: true),
                    EstadoFinal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lineas_rendicion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lineas_rendicion_rendiciones_RendicionId",
                        column: x => x.RendicionId,
                        principalSchema: "ejecucion",
                        principalTable: "rendiciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lineas_rendicion_OperadorId_RendicionId_BultoId",
                schema: "ejecucion",
                table: "lineas_rendicion",
                columns: new[] { "OperadorId", "RendicionId", "BultoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lineas_rendicion_RendicionId",
                schema: "ejecucion",
                table: "lineas_rendicion",
                column: "RendicionId");

            migrationBuilder.CreateIndex(
                name: "IX_rendiciones_OperadorId_OperacionId",
                schema: "ejecucion",
                table: "rendiciones",
                columns: new[] { "OperadorId", "OperacionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rendiciones_OperadorId_RutaId",
                schema: "ejecucion",
                table: "rendiciones",
                columns: new[] { "OperadorId", "RutaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lineas_rendicion",
                schema: "ejecucion");

            migrationBuilder.DropTable(
                name: "rendiciones",
                schema: "ejecucion");
        }
    }
}
