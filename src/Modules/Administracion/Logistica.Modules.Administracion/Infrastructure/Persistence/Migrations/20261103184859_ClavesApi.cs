using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClavesApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClavesApi",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelacionComercialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Prefijo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimoUsoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClavesApi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClavesApi_RelacionesComerciales_RelacionComercialId",
                        column: x => x.RelacionComercialId,
                        principalSchema: "administracion",
                        principalTable: "RelacionesComerciales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClavesApi_Hash",
                schema: "administracion",
                table: "ClavesApi",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClavesApi_OperadorId_ComercioId_CreadaEn",
                schema: "administracion",
                table: "ClavesApi",
                columns: new[] { "OperadorId", "ComercioId", "CreadaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_ClavesApi_RelacionComercialId",
                schema: "administracion",
                table: "ClavesApi",
                column: "RelacionComercialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClavesApi",
                schema: "administracion");
        }
    }
}
