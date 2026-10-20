using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnulacionDespachoCU43 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnulacionesDespacho",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FechaRuta = table.Column<DateOnly>(type: "date", nullable: false),
                    DespachadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevisionAnterior = table.Column<long>(type: "bigint", nullable: false),
                    RevisionRuta = table.Column<long>(type: "bigint", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnulacionesDespacho", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnulacionesDespacho_Rutas_OperadorId_RutaId",
                        columns: x => new { x.OperadorId, x.RutaId },
                        principalSchema: "planificacion",
                        principalTable: "Rutas",
                        principalColumns: new[] { "OperadorId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnulacionesDespacho_OperadorId_RutaId",
                schema: "planificacion",
                table: "AnulacionesDespacho",
                columns: new[] { "OperadorId", "RutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnulacionesDespacho_RutaId_RevisionRuta",
                schema: "planificacion",
                table: "AnulacionesDespacho",
                columns: new[] { "RutaId", "RevisionRuta" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnulacionesDespacho",
                schema: "planificacion");
        }
    }
}
