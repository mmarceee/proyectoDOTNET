using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Liquidaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Liquidaciones",
                schema: "deposito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodoDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodoHasta = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    TotalBruto = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAjustes = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalNeto = table.Column<decimal>(type: "numeric", nullable: false),
                    GeneradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EmitidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PagadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Liquidaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LineasLiquidacion",
                schema: "deposito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LiquidacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroEnvio = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Concepto = table.Column<string>(type: "text", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineasLiquidacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineasLiquidacion_Liquidaciones_LiquidacionId",
                        column: x => x.LiquidacionId,
                        principalSchema: "deposito",
                        principalTable: "Liquidaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineasLiquidacion_LiquidacionId",
                schema: "deposito",
                table: "LineasLiquidacion",
                column: "LiquidacionId");

            migrationBuilder.CreateIndex(
                name: "UX_LineasLiquidacion_EnvioId",
                schema: "deposito",
                table: "LineasLiquidacion",
                column: "EnvioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Liquidaciones_OperadorId_ComercioId_GeneradaEn",
                schema: "deposito",
                table: "Liquidaciones",
                columns: new[] { "OperadorId", "ComercioId", "GeneradaEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineasLiquidacion",
                schema: "deposito");

            migrationBuilder.DropTable(
                name: "Liquidaciones",
                schema: "deposito");
        }
    }
}
