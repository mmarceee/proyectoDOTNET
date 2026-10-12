using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParadasConVariosEnvios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Paradas_OperadorId_RutaId",
                schema: "planificacion",
                table: "Paradas");

            migrationBuilder.DropIndex(
                name: "IX_Paradas_RutaId_EnvioId",
                schema: "planificacion",
                table: "Paradas");

            migrationBuilder.DropIndex(
                name: "UX_Paradas_EnvioActivo",
                schema: "planificacion",
                table: "Paradas");

migrationBuilder.AddColumn<string>(
                name: "ClaveDireccion",
                schema: "planificacion",
                table: "Paradas",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Desde",
                schema: "planificacion",
                table: "Paradas",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direccion",
                schema: "planificacion",
                table: "Paradas",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Hasta",
                schema: "planificacion",
                table: "Paradas",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Paradas_OperadorId_RutaId_Id",
                schema: "planificacion",
                table: "Paradas",
                columns: new[] { "OperadorId", "RutaId", "Id" });

            migrationBuilder.CreateTable(
                name: "EnviosParada",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParadaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosParada", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnviosParada_Paradas_OperadorId_RutaId_ParadaId",
                        columns: x => new { x.OperadorId, x.RutaId, x.ParadaId },
                        principalSchema: "planificacion",
                        principalTable: "Paradas",
                        principalColumns: new[] { "OperadorId", "RutaId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(MigracionParadasSql.Up);
            migrationBuilder.DropColumn(name: "EnvioId", schema: "planificacion", table: "Paradas");
            migrationBuilder.CreateIndex(
                name: "IX_EnviosParada_OperadorId_RutaId_ParadaId",
                schema: "planificacion",
                table: "EnviosParada",
                columns: new[] { "OperadorId", "RutaId", "ParadaId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnviosParada_RutaId_EnvioId",
                schema: "planificacion",
                table: "EnviosParada",
                columns: new[] { "RutaId", "EnvioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Paradas_EnvioActivo",
                schema: "planificacion",
                table: "EnviosParada",
                column: "EnvioId",
                unique: true,
                filter: "\"Estado\" = 'Pendiente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigracionParadasSql.Down);
        }
    }
}
