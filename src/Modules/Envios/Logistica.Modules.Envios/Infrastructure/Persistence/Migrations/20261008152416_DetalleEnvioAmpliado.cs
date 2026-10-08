using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DetalleEnvioAmpliado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detalle",
                schema: "envios",
                table: "EventosEnvio",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitud",
                schema: "envios",
                table: "EventosEnvio",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitud",
                schema: "envios",
                table: "EventosEnvio",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VersionTarifarioId",
                schema: "envios",
                table: "Envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionTarifarioNumero",
                schema: "envios",
                table: "Envios",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArchivosEvidencia",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoContenido = table.Column<string>(type: "text", nullable: false),
                    Contenido = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivosEvidencia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivosEvidencia_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Devoluciones",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    IniciadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecibidaEnDepositoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NombreReceptor = table.Column<string>(type: "text", nullable: true),
                    DocumentoReceptor = table.Column<string>(type: "text", nullable: true),
                    EntregadaAlComercioEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devoluciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devoluciones_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Incidencias",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResueltaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolucion = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidencias_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntentosEntrega",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroIntento = table.Column<int>(type: "integer", nullable: false),
                    FechaHora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false),
                    MotivoNoEntregaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Evidencia_FirmaArchivoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Evidencia_FotoArchivoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Evidencia_NombreReceptor = table.Column<string>(type: "text", nullable: true),
                    Evidencia_DocumentoReceptor = table.Column<string>(type: "text", nullable: true),
                    Evidencia_Latitud = table.Column<decimal>(type: "numeric", nullable: true),
                    Evidencia_Longitud = table.Column<decimal>(type: "numeric", nullable: true),
                    Evidencia_CapturadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Observaciones = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentosEntrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntentosEntrega_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchivosEvidencia_EnvioId",
                schema: "envios",
                table: "ArchivosEvidencia",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivosEvidencia_OperadorId_ComercioId_EnvioId",
                schema: "envios",
                table: "ArchivosEvidencia",
                columns: new[] { "OperadorId", "ComercioId", "EnvioId" });

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_EnvioId",
                schema: "envios",
                table: "Devoluciones",
                column: "EnvioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidencias_EnvioId",
                schema: "envios",
                table: "Incidencias",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidencias_OperadorId_ComercioId_EnvioId",
                schema: "envios",
                table: "Incidencias",
                columns: new[] { "OperadorId", "ComercioId", "EnvioId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntentosEntrega_EnvioId_NumeroIntento",
                schema: "envios",
                table: "IntentosEntrega",
                columns: new[] { "EnvioId", "NumeroIntento" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivosEvidencia",
                schema: "envios");

            migrationBuilder.DropTable(
                name: "Devoluciones",
                schema: "envios");

            migrationBuilder.DropTable(
                name: "Incidencias",
                schema: "envios");

            migrationBuilder.DropTable(
                name: "IntentosEntrega",
                schema: "envios");

            migrationBuilder.DropColumn(
                name: "Detalle",
                schema: "envios",
                table: "EventosEnvio");

            migrationBuilder.DropColumn(
                name: "Latitud",
                schema: "envios",
                table: "EventosEnvio");

            migrationBuilder.DropColumn(
                name: "Longitud",
                schema: "envios",
                table: "EventosEnvio");

            migrationBuilder.DropColumn(
                name: "VersionTarifarioId",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "VersionTarifarioNumero",
                schema: "envios",
                table: "Envios");
        }
    }
}
