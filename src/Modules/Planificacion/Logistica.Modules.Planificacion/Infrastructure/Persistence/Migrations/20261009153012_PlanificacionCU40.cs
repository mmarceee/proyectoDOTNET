using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanificacionCU40 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planificacion");

            migrationBuilder.CreateTable(
                name: "Rutas",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DespachadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ReservaActiva = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rutas", x => x.Id);
                    table.UniqueConstraint("AK_Rutas_OperadorId_Id", x => new { x.OperadorId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Paradas",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    LlegadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paradas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Paradas_Rutas_OperadorId_RutaId",
                        columns: x => new { x.OperadorId, x.RutaId },
                        principalSchema: "planificacion",
                        principalTable: "Rutas",
                        principalColumns: new[] { "OperadorId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValidacionesRuta",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionRuta = table.Column<long>(type: "bigint", nullable: false),
                    ValidadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operacion = table.Column<string>(type: "text", nullable: false),
                    FechaRuta = table.Column<DateOnly>(type: "date", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapacidadPesoKg = table.Column<decimal>(type: "numeric", nullable: false),
                    CapacidadVolumenM3 = table.Column<decimal>(type: "numeric", nullable: false),
                    LargoCargaCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AnchoCargaCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AltoCargaCm = table.Column<decimal>(type: "numeric", nullable: false),
                    VersionReglasId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxParadasPorRuta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidacionesRuta", x => x.Id);
                    table.UniqueConstraint("AK_ValidacionesRuta_OperadorId_Id", x => new { x.OperadorId, x.Id });
                    table.ForeignKey(
                        name: "FK_ValidacionesRuta_Rutas_OperadorId_RutaId",
                        columns: x => new { x.OperadorId, x.RutaId },
                        principalSchema: "planificacion",
                        principalTable: "Rutas",
                        principalColumns: new[] { "OperadorId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BultosValidacionRuta",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidacionRutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultoId = table.Column<Guid>(type: "uuid", nullable: false),
                    PesoKg = table.Column<decimal>(type: "numeric", nullable: false),
                    LargoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AnchoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AltoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    VolumenM3 = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BultosValidacionRuta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BultosValidacionRuta_ValidacionesRuta_OperadorId_Validacion~",
                        columns: x => new { x.OperadorId, x.ValidacionRutaId },
                        principalSchema: "planificacion",
                        principalTable: "ValidacionesRuta",
                        principalColumns: new[] { "OperadorId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BultosValidacionRuta_OperadorId_ValidacionRutaId",
                schema: "planificacion",
                table: "BultosValidacionRuta",
                columns: new[] { "OperadorId", "ValidacionRutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_BultosValidacionRuta_ValidacionRutaId_BultoId",
                schema: "planificacion",
                table: "BultosValidacionRuta",
                columns: new[] { "ValidacionRutaId", "BultoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Paradas_OperadorId_RutaId",
                schema: "planificacion",
                table: "Paradas",
                columns: new[] { "OperadorId", "RutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Paradas_RutaId_EnvioId",
                schema: "planificacion",
                table: "Paradas",
                columns: new[] { "RutaId", "EnvioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Paradas_EnvioActivo",
                schema: "planificacion",
                table: "Paradas",
                column: "EnvioId",
                unique: true,
                filter: "\"Estado\" = 'Pendiente'");

            migrationBuilder.CreateIndex(
                name: "UX_Rutas_RepartidorReserva",
                schema: "planificacion",
                table: "Rutas",
                columns: new[] { "OperadorId", "Fecha", "RepartidorId" },
                unique: true,
                filter: "\"ReservaActiva\" = true");

            migrationBuilder.CreateIndex(
                name: "UX_Rutas_VehiculoReserva",
                schema: "planificacion",
                table: "Rutas",
                columns: new[] { "OperadorId", "Fecha", "VehiculoId" },
                unique: true,
                filter: "\"ReservaActiva\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ValidacionesRuta_OperadorId_RutaId",
                schema: "planificacion",
                table: "ValidacionesRuta",
                columns: new[] { "OperadorId", "RutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidacionesRuta_RutaId_RevisionRuta",
                schema: "planificacion",
                table: "ValidacionesRuta",
                columns: new[] { "RutaId", "RevisionRuta" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BultosValidacionRuta",
                schema: "planificacion");

            migrationBuilder.DropTable(
                name: "Paradas",
                schema: "planificacion");

            migrationBuilder.DropTable(
                name: "ValidacionesRuta",
                schema: "planificacion");

            migrationBuilder.DropTable(
                name: "Rutas",
                schema: "planificacion");
        }
    }
}
