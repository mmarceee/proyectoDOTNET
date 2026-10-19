using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EscaneosCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escaneos_carga",
                schema: "ejecucion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CodigoBulto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EscaneadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escaneos_carga", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escaneos_carga_OperadorId_OperacionId_CodigoBulto",
                schema: "ejecucion",
                table: "escaneos_carga",
                columns: new[] { "OperadorId", "OperacionId", "CodigoBulto" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escaneos_carga_OperadorId_RutaId",
                schema: "ejecucion",
                table: "escaneos_carga",
                columns: new[] { "OperadorId", "RutaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escaneos_carga",
                schema: "ejecucion");
        }
    }
}
