using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustodiaBultosRF34 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Custodia",
                schema: "envios",
                table: "Bultos",
                type: "text",
                nullable: false,
                defaultValue: "Desconocida");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustodiaConfirmadaEn",
                schema: "envios",
                table: "Bultos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodiaOperacionId",
                schema: "envios",
                table: "Bultos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodiaResponsableId",
                schema: "envios",
                table: "Bultos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustodiaRutaId",
                schema: "envios",
                table: "Bultos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReintentoDesde",
                schema: "envios",
                table: "Bultos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevisionCustodia",
                schema: "envios",
                table: "Bultos",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "MovimientosCustodia",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ubicacion = table.Column<string>(type: "text", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosCustodia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimientosCustodia_Bultos_BultoId",
                        column: x => x.BultoId,
                        principalSchema: "envios",
                        principalTable: "Bultos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCustodia_BultoId",
                schema: "envios",
                table: "MovimientosCustodia",
                column: "BultoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCustodia_OperadorId_BultoId_OperacionId",
                schema: "envios",
                table: "MovimientosCustodia",
                columns: new[] { "OperadorId", "BultoId", "OperacionId" },
                unique: true);

            // No se deduce presencia física de un estado. Las ubicaciones inciertas quedan bloqueadas
            // y con un registro auditable, sin modificar estados, intentos, evidencia ni colas.
            migrationBuilder.Sql("""
                UPDATE envios."Bultos" b SET "ReintentoDesde" = e."ReintentoDesde"
                FROM envios."Envios" e WHERE e."Id" = b."EnvioId" AND e."OperadorId" = b."OperadorId"
                    AND b."Estado" = 'Reprogramado';
                INSERT INTO envios."MovimientosCustodia"
                    ("Id", "OperadorId", "ComercioId", "BultoId", "EnvioId", "Ubicacion", "OperacionId", "ConfirmadaEn", "Motivo")
                SELECT gen_random_uuid(), b."OperadorId", b."ComercioId", b."Id", b."EnvioId", 'Desconocida',
                    gen_random_uuid(), CURRENT_TIMESTAMP, 'Migración RF34: ubicación pendiente de verificación física'
                FROM envios."Bultos" b WHERE b."Estado" NOT IN ('Entregado', 'Devuelto', 'Extraviado', 'Cancelado');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovimientosCustodia",
                schema: "envios");

            migrationBuilder.DropColumn(
                name: "Custodia",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "CustodiaConfirmadaEn",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "CustodiaOperacionId",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "CustodiaResponsableId",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "CustodiaRutaId",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "ReintentoDesde",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "RevisionCustodia",
                schema: "envios",
                table: "Bultos");
        }
    }
}
