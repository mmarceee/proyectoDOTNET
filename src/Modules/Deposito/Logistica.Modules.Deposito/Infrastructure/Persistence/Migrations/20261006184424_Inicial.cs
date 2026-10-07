using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deposito");

            migrationBuilder.CreateTable(
                name: "Recepciones",
                schema: "deposito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultoId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecibidoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false),
                    Discrepancia = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recepciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_BultoId",
                schema: "deposito",
                table: "Recepciones",
                column: "BultoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_OperadorId_EnvioId",
                schema: "deposito",
                table: "Recepciones",
                columns: new[] { "OperadorId", "EnvioId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Recepciones",
                schema: "deposito");
        }
    }
}
