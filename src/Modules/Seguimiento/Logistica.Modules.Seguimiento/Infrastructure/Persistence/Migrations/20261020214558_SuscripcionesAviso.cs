using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuscripcionesAviso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suscripciones_aviso",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelacionComercialId = table.Column<Guid>(type: "uuid", nullable: false),
                    UrlDestino = table.Column<string>(type: "text", nullable: false),
                    TiposEvento = table.Column<int[]>(type: "integer[]", nullable: false),
                    SecretoFirma = table.Column<string>(type: "text", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suscripciones_aviso", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_suscripciones_aviso_OperadorId_ComercioId_Activa",
                schema: "seguimiento",
                table: "suscripciones_aviso",
                columns: new[] { "OperadorId", "ComercioId", "Activa" });

            migrationBuilder.CreateIndex(
                name: "IX_suscripciones_aviso_RelacionComercialId",
                schema: "seguimiento",
                table: "suscripciones_aviso",
                column: "RelacionComercialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suscripciones_aviso",
                schema: "seguimiento");
        }
    }
}
