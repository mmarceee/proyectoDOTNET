using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "administracion");

            migrationBuilder.CreateTable(
                name: "Comercios",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RazonSocial = table.Column<string>(type: "text", nullable: false),
                    DocumentoFiscal = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comercios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operadores",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    ZonaHoraria = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operadores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RelacionesComerciales",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailContacto = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    FechaAlta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelacionesComerciales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RelacionesComerciales_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalSchema: "administracion",
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelacionesComerciales_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalSchema: "administracion",
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comercios_DocumentoFiscal",
                schema: "administracion",
                table: "Comercios",
                column: "DocumentoFiscal",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operadores_Slug",
                schema: "administracion",
                table: "Operadores",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelacionesComerciales_ComercioId",
                schema: "administracion",
                table: "RelacionesComerciales",
                column: "ComercioId");

            migrationBuilder.CreateIndex(
                name: "IX_RelacionesComerciales_OperadorId_ComercioId",
                schema: "administracion",
                table: "RelacionesComerciales",
                columns: new[] { "OperadorId", "ComercioId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelacionesComerciales",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "Comercios",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "Operadores",
                schema: "administracion");
        }
    }
}
