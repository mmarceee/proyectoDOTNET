using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerificacionReprogramacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "verificaciones_reprogramacion",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    FranjaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Medio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HashSecreto = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VenceEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    UsadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Anulada = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verificaciones_reprogramacion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_verificaciones_reprogramacion_OperadorId_EnvioId_CreadaEn",
                schema: "seguimiento",
                table: "verificaciones_reprogramacion",
                columns: new[] { "OperadorId", "EnvioId", "CreadaEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verificaciones_reprogramacion",
                schema: "seguimiento");
        }
    }
}
