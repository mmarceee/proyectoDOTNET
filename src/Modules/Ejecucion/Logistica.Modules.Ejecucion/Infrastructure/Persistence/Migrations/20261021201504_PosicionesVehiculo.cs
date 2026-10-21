using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosicionesVehiculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "posiciones_vehiculo",
                schema: "ejecucion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitud = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    Longitud = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    CapturadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecibidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_posiciones_vehiculo", x => x.Id);
                    table.CheckConstraint("ck_posicion_latitud", "\"Latitud\" BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_posicion_longitud", "\"Longitud\" BETWEEN -180 AND 180");
                });

            migrationBuilder.CreateIndex(
                name: "IX_posiciones_vehiculo_CapturadaEn",
                schema: "ejecucion",
                table: "posiciones_vehiculo",
                column: "CapturadaEn");

            migrationBuilder.CreateIndex(
                name: "IX_posiciones_vehiculo_OperadorId_VehiculoId_CapturadaEn_Id",
                schema: "ejecucion",
                table: "posiciones_vehiculo",
                columns: new[] { "OperadorId", "VehiculoId", "CapturadaEn", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "posiciones_vehiculo",
                schema: "ejecucion");
        }
    }
}
