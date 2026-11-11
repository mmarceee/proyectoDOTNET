using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotenciaCustodiaRF34 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacionesCustodia",
                schema: "deposito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesCustodia", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesCustodia_OperadorId_OperacionId",
                schema: "deposito",
                table: "OperacionesCustodia",
                columns: new[] { "OperadorId", "OperacionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesCustodia",
                schema: "deposito");
        }
    }
}
