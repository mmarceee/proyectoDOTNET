using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EntregaParcialBultos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BultosEntregados",
                schema: "envios",
                table: "Envios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Resumen",
                schema: "envios",
                table: "Envios",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Terminado",
                schema: "envios",
                table: "Envios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                schema: "envios",
                table: "Bultos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "IntentosFallidos",
                schema: "envios",
                table: "Bultos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Datos existentes (pendiente 5 de decisiones-entrega-parcial.md): hasta ahora todos los bultos acompañaban
            // a su envío, así que cada bulto toma el estado del envío y los intentos fallidos registrados. No se
            // inventa historial por bulto.
            migrationBuilder.Sql("""
                UPDATE envios."Bultos" AS b
                SET "Estado" = e."Estado",
                    "IntentosFallidos" = (SELECT count(*) FROM envios."IntentosEntrega" AS i
                                          WHERE i."EnvioId" = e."Id" AND i."Resultado" = 'Fallido')
                FROM envios."Envios" AS e
                WHERE b."EnvioId" = e."Id";
                """);

            // Excepción acotada: un envío Admitido con parte de sus bultos escaneados ya tiene esas recepciones en
            // Depósito (CU-30 sólo hacía T2 con el último bulto). Se preservan para que el resumen quede En operación
            // (ADR-0007, 2.1). Es una lectura única de migración, nunca de un request; une también por OperadorId para
            // no cruzar inquilinos (ADR-0002, sección 4). En una base nueva la tabla puede no existir todavía.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('deposito."Recepciones"') IS NOT NULL THEN
                        UPDATE envios."Bultos" AS b
                        SET "Estado" = 'EnDeposito'
                        FROM deposito."Recepciones" AS r
                        WHERE r."BultoId" = b."Id" AND r."OperadorId" = b."OperadorId" AND b."Estado" = 'Admitido';
                    END IF;
                END $$;
                """);

            // El resumen sale de los bultos, con las mismas reglas que Envio.CalcularResumen.
            migrationBuilder.Sql("""
                UPDATE envios."Envios" AS e
                SET "BultosEntregados" = c.entregados,
                    "Terminado" = c.terminados = c.total,
                    "Resumen" = CASE
                        WHEN c.entregados = c.total THEN 'Entregado'
                        WHEN c.entregados > 0 THEN 'EntregadoParcialmente'
                        WHEN c.recibidos > 0 THEN 'EnOperacion'
                        ELSE 'Creado' END
                FROM (SELECT "EnvioId",
                             count(*) AS total,
                             count(*) FILTER (WHERE "Estado" = 'Entregado') AS entregados,
                             count(*) FILTER (WHERE "Estado" IN ('Entregado', 'Devuelto', 'Extraviado', 'Cancelado')) AS terminados,
                             count(*) FILTER (WHERE "Estado" NOT IN ('Admitido', 'Cancelado')) AS recibidos
                      FROM envios."Bultos" GROUP BY "EnvioId") AS c
                WHERE c."EnvioId" = e."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId_Resumen",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "Resumen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Envios_OperadorId_Resumen",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "BultosEntregados",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "Resumen",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "Terminado",
                schema: "envios",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "Estado",
                schema: "envios",
                table: "Bultos");

            migrationBuilder.DropColumn(
                name: "IntentosFallidos",
                schema: "envios",
                table: "Bultos");
        }
    }
}
