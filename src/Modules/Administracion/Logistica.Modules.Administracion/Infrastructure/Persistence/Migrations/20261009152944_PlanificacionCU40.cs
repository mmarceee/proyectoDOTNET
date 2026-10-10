using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanificacionCU40 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FranjasHorarias",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    HoraDesde = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    HoraHasta = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Dias = table.Column<int[]>(type: "integer[]", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    VigenteHasta = table.Column<DateOnly>(type: "date", nullable: true),
                    ReemplazaAId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranjasHorarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repartidores",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Documento = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repartidores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehiculos",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Matricula = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    CapacidadPesoKg = table.Column<decimal>(type: "numeric", nullable: false),
                    CapacidadVolumenM3 = table.Column<decimal>(type: "numeric", nullable: false),
                    LargoCargaCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AnchoCargaCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AltoCargaCm = table.Column<decimal>(type: "numeric", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehiculos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VersionesReglasPlanificacion",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    VigenteDesde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VigenteHasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxParadasPorRuta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionesReglasPlanificacion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Zonas",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    CodigosPostales = table.Column<string[]>(type: "text[]", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zonas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FranjasHorarias_OperadorId_ZonaId_VigenteDesde",
                schema: "administracion",
                table: "FranjasHorarias",
                columns: new[] { "OperadorId", "ZonaId", "VigenteDesde" });

            migrationBuilder.CreateIndex(
                name: "IX_FranjasHorarias_ReemplazaAId",
                schema: "administracion",
                table: "FranjasHorarias",
                column: "ReemplazaAId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repartidores_OperadorId_Documento",
                schema: "administracion",
                table: "Repartidores",
                columns: new[] { "OperadorId", "Documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehiculos_OperadorId_Matricula",
                schema: "administracion",
                table: "Vehiculos",
                columns: new[] { "OperadorId", "Matricula" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VersionesReglasPlanificacion_OperadorId_Numero",
                schema: "administracion",
                table: "VersionesReglasPlanificacion",
                columns: new[] { "OperadorId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zonas_OperadorId_Codigo",
                schema: "administracion",
                table: "Zonas",
                columns: new[] { "OperadorId", "Codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FranjasHorarias",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "Repartidores",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "Vehiculos",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "VersionesReglasPlanificacion",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "Zonas",
                schema: "administracion");
        }
    }
}
