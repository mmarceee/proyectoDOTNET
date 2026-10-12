using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistorialRetirosCU41 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetirosEnvioRuta",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroEnvio = table.Column<string>(type: "text", nullable: false),
                    OrdenAnterior = table.Column<int>(type: "integer", nullable: false),
                    ParadasRestantes = table.Column<int>(type: "integer", nullable: false),
                    RevisionRuta = table.Column<long>(type: "bigint", nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetirosEnvioRuta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetirosEnvioRuta_Rutas_OperadorId_RutaId",
                        columns: x => new { x.OperadorId, x.RutaId },
                        principalSchema: "planificacion",
                        principalTable: "Rutas",
                        principalColumns: new[] { "OperadorId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosEnvioRuta_OperadorId_RutaId",
                schema: "planificacion",
                table: "RetirosEnvioRuta",
                columns: new[] { "OperadorId", "RutaId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetirosEnvioRuta_RutaId_RevisionRuta",
                schema: "planificacion",
                table: "RetirosEnvioRuta",
                columns: new[] { "RutaId", "RevisionRuta" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetirosEnvioRuta",
                schema: "planificacion");
        }
    }
}
