using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservasBultosRF34 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Paradas_EnvioActivo",
                schema: "planificacion",
                table: "EnviosParada");

            migrationBuilder.CreateTable(
                name: "OperacionesBultos",
                schema: "planificacion",
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
                    table.PrimaryKey("PK_OperacionesBultos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservasBulto",
                schema: "planificacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParadaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AbiertaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CerradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CausaCierre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservasBulto", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnviosParada_EnvioId",
                schema: "planificacion",
                table: "EnviosParada",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesBultos_OperadorId_OperacionId",
                schema: "planificacion",
                table: "OperacionesBultos",
                columns: new[] { "OperadorId", "OperacionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservasBulto_OperadorId_RutaId_EnvioId",
                schema: "planificacion",
                table: "ReservasBulto",
                columns: new[] { "OperadorId", "RutaId", "EnvioId" });

            migrationBuilder.CreateIndex(
                name: "UX_ReservasBulto_Activa",
                schema: "planificacion",
                table: "ReservasBulto",
                columns: new[] { "OperadorId", "BultoId" },
                unique: true,
                filter: "\"CerradaEn\" IS NULL");

            migrationBuilder.Sql("""
                -- Sólo el último manifiesto validado y una pertenencia inequívoca permiten crear reservas.
                CREATE TEMP TABLE rf34_candidatos ON COMMIT DROP AS
                SELECT DISTINCT bv."OperadorId", bv."BultoId", bv."EnvioId", r."Id" AS "RutaId", ep."ParadaId",
                    v."ValidadaEn" AS "AbiertaEn", v."ResponsableId"
                FROM planificacion."ValidacionesRuta" v
                JOIN planificacion."BultosValidacionRuta" bv ON bv."ValidacionRutaId" = v."Id" AND bv."OperadorId" = v."OperadorId"
                JOIN planificacion."Rutas" r ON r."Id" = v."RutaId" AND r."OperadorId" = v."OperadorId"
                JOIN planificacion."EnviosParada" ep ON ep."RutaId" = r."Id" AND ep."EnvioId" = bv."EnvioId" AND ep."OperadorId" = r."OperadorId"
                JOIN envios."Bultos" b ON b."Id" = bv."BultoId" AND b."EnvioId" = bv."EnvioId" AND b."OperadorId" = bv."OperadorId"
                WHERE v."RevisionRuta" = (SELECT MAX(u."RevisionRuta") FROM planificacion."ValidacionesRuta" u WHERE u."RutaId" = r."Id")
                    AND b."Estado" IN ('AsignadoARuta','EnTransito','Reprogramado','EnDevolucion')
                    AND r."Estado" IN ('Planificada','Despachada','EnCurso');

                DO $rf34$ BEGIN
                IF to_regclass('ejecucion.lineas_rendicion') IS NOT NULL THEN
                    INSERT INTO rf34_candidatos
                    SELECT DISTINCT b."OperadorId", b."Id", b."EnvioId", r."Id", ep."ParadaId", r."CreadaEn", NULL::uuid
                    FROM ejecucion.lineas_rendicion l
                    JOIN ejecucion.rendiciones re ON re."Id" = l."RendicionId" AND re."OperadorId" = l."OperadorId"
                    JOIN planificacion."Rutas" r ON r."Id" = re."RutaId" AND r."OperadorId" = re."OperadorId"
                    JOIN planificacion."EnviosParada" ep ON ep."RutaId" = r."Id" AND ep."EnvioId" = l."EnvioId" AND ep."OperadorId" = r."OperadorId"
                    JOIN envios."Bultos" b ON b."Id" = l."BultoId" AND b."EnvioId" = l."EnvioId" AND b."OperadorId" = l."OperadorId"
                    WHERE r."Estado" = 'Finalizada' AND b."Estado" IN ('AsignadoARuta','EnTransito','Reprogramado','EnDevolucion')
                        AND (l."Recibida" = false OR l."Situacion" = 'NoCargado');
                END IF;
                END $rf34$;

                INSERT INTO planificacion."ReservasBulto"
                    ("Id","OperadorId","BultoId","EnvioId","RutaId","ParadaId","AbiertaEn","OperacionId","ResponsableId","Revision")
                SELECT gen_random_uuid(), c."OperadorId", c."BultoId", c."EnvioId", c."RutaId", c."ParadaId", c."AbiertaEn",
                    gen_random_uuid(), c."ResponsableId", 1 FROM rf34_candidatos c
                WHERE (SELECT COUNT(*) FROM rf34_candidatos d WHERE d."OperadorId" = c."OperadorId" AND d."BultoId" = c."BultoId") = 1;

                -- Una recepción inicial sin ninguna ruta posterior sí es un hecho físico inequívoco.
                DO $rf34$ BEGIN
                IF to_regclass('deposito."Recepciones"') IS NOT NULL THEN
                    UPDATE envios."Bultos" b SET "Custodia" = 'Deposito', "CustodiaConfirmadaEn" = re."RecibidoEn",
                        "CustodiaOperacionId" = re."Id", "RevisionCustodia" = 1
                    FROM deposito."Recepciones" re
                    WHERE re."BultoId" = b."Id" AND re."OperadorId" = b."OperadorId" AND b."Estado" = 'EnDeposito'
                        AND NOT EXISTS (SELECT 1 FROM planificacion."EnviosParada" p WHERE p."EnvioId" = b."EnvioId" AND p."OperadorId" = b."OperadorId");
                END IF;
                IF to_regclass('ejecucion.escaneos_carga') IS NOT NULL THEN
                    UPDATE envios."Bultos" b SET "Custodia" = 'Repartidor', "CustodiaRutaId" = r."RutaId",
                        "CustodiaConfirmadaEn" = s."EscaneadoEn", "CustodiaOperacionId" = s."OperacionId", "RevisionCustodia" = 1
                    FROM planificacion."ReservasBulto" r JOIN ejecucion.escaneos_carga s
                        ON s."BultoId" = r."BultoId" AND s."RutaId" = r."RutaId" AND s."OperadorId" = r."OperadorId"
                    WHERE b."Id" = r."BultoId" AND b."OperadorId" = r."OperadorId" AND s."Resultado" = 'Esperado'
                        AND b."Estado" IN ('EnTransito','Reprogramado','EnDevolucion')
                        AND NOT EXISTS (SELECT 1 FROM ejecucion.escaneos_carga otro WHERE otro."BultoId" = s."BultoId"
                            AND otro."OperadorId" = s."OperadorId" AND otro."EscaneadoEn" > s."EscaneadoEn");
                END IF;
                END $rf34$;

                INSERT INTO envios."MovimientosCustodia"
                    ("Id","OperadorId","ComercioId","BultoId","EnvioId","Ubicacion","RutaId","OperacionId","ConfirmadaEn","Motivo")
                SELECT gen_random_uuid(), b."OperadorId", b."ComercioId", b."Id", b."EnvioId", b."Custodia", b."CustodiaRutaId",
                    b."CustodiaOperacionId", b."CustodiaConfirmadaEn", 'Migración RF34: reconstrucción desde confirmación física inequívoca'
                FROM envios."Bultos" b WHERE b."RevisionCustodia" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesBultos",
                schema: "planificacion");

            migrationBuilder.DropTable(
                name: "ReservasBulto",
                schema: "planificacion");

            migrationBuilder.DropIndex(
                name: "IX_EnviosParada_EnvioId",
                schema: "planificacion",
                table: "EnviosParada");

            migrationBuilder.CreateIndex(
                name: "UX_Paradas_EnvioActivo",
                schema: "planificacion",
                table: "EnviosParada",
                column: "EnvioId",
                unique: true,
                filter: "\"Estado\" = 'Pendiente'");
        }
    }
}
