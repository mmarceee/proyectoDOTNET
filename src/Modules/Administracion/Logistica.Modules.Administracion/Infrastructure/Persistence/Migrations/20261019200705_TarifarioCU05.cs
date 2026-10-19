using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TarifarioCU05 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VersionesTarifario",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    VigenteDesde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VigenteHasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionesTarifario", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AjustesTarifa",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionTarifarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    EsPorcentaje = table.Column<bool>(type: "boolean", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric", nullable: false),
                    Modalidad = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjustesTarifa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AjustesTarifa_VersionesTarifario_VersionTarifarioId",
                        column: x => x.VersionTarifarioId,
                        principalSchema: "administracion",
                        principalTable: "VersionesTarifario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReglasTarifa",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionTarifarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modalidad = table.Column<string>(type: "text", nullable: false),
                    PesoDesdeKg = table.Column<decimal>(type: "numeric", nullable: false),
                    PesoHastaKg = table.Column<decimal>(type: "numeric", nullable: false),
                    VolumenDesdeM3 = table.Column<decimal>(type: "numeric", nullable: false),
                    VolumenHastaM3 = table.Column<decimal>(type: "numeric", nullable: false),
                    PrecioBase = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasTarifa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReglasTarifa_VersionesTarifario_VersionTarifarioId",
                        column: x => x.VersionTarifarioId,
                        principalSchema: "administracion",
                        principalTable: "VersionesTarifario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AjustesTarifa_VersionTarifarioId",
                schema: "administracion",
                table: "AjustesTarifa",
                column: "VersionTarifarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ReglasTarifa_VersionTarifarioId",
                schema: "administracion",
                table: "ReglasTarifa",
                column: "VersionTarifarioId");

            migrationBuilder.CreateIndex(
                name: "IX_VersionesTarifario_OperadorId_Numero",
                schema: "administracion",
                table: "VersionesTarifario",
                columns: new[] { "OperadorId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_VersionesTarifario_Borrador",
                schema: "administracion",
                table: "VersionesTarifario",
                column: "OperadorId",
                unique: true,
                filter: "\"Estado\" = 'Borrador'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AjustesTarifa",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "ReglasTarifa",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "VersionesTarifario",
                schema: "administracion");
        }
    }
}
