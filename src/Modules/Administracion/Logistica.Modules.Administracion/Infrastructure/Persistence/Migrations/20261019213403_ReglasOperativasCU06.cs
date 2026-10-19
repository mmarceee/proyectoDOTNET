using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReglasOperativasCU06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VersionesReglas",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    VigenteDesde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VigenteHasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxIntentos = table.Column<int>(type: "integer", nullable: false),
                    PlazoEntreIntentosHoras = table.Column<int>(type: "integer", nullable: false),
                    AnticipacionReprogramacionHoras = table.Column<int>(type: "integer", nullable: false),
                    MaxParadasPorRuta = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Plazos = table.Column<string>(type: "jsonb", nullable: false),
                    PoliticaDevolucion_DevolverTrasIntentosAgotados = table.Column<bool>(type: "boolean", nullable: false),
                    PoliticaDevolucion_PlazoDevolucionDias = table.Column<int>(type: "integer", nullable: false),
                    PruebaExigida_ExigeFoto = table.Column<bool>(type: "boolean", nullable: false),
                    PruebaExigida_ExigeNombreYDocumento = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionesReglas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MotivosNoEntrega",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionReglasId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Reprogramable = table.Column<bool>(type: "boolean", nullable: false),
                    ExigeEvidencia = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MotivosNoEntrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MotivosNoEntrega_VersionesReglas_VersionReglasId",
                        column: x => x.VersionReglasId,
                        principalSchema: "administracion",
                        principalTable: "VersionesReglas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MotivosNoEntrega_VersionReglasId",
                schema: "administracion",
                table: "MotivosNoEntrega",
                column: "VersionReglasId");

            migrationBuilder.CreateIndex(
                name: "IX_VersionesReglas_OperadorId_Numero",
                schema: "administracion",
                table: "VersionesReglas",
                columns: new[] { "OperadorId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_VersionesReglas_Borrador",
                schema: "administracion",
                table: "VersionesReglas",
                column: "OperadorId",
                unique: true,
                filter: "\"Estado\" = 'Borrador'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MotivosNoEntrega",
                schema: "administracion");

            migrationBuilder.DropTable(
                name: "VersionesReglas",
                schema: "administracion");
        }
    }
}
