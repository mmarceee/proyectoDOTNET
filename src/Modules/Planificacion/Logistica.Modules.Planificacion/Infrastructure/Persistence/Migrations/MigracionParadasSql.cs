namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Migrations;

// Transformación de datos del modelo anterior. No elimina envíos ni reescribe evidencia histórica.
internal static class MigracionParadasSql
{
    public const string Up = """
        INSERT INTO planificacion."EnviosParada" ("Id", "OperadorId", "RutaId", "ParadaId", "EnvioId", "Estado")
        SELECT "Id", "OperadorId", "RutaId", "Id", "EnvioId", "Estado" FROM planificacion."Paradas";

        UPDATE planificacion."Paradas" SET "ClaveDireccion" = "Id"::text, "Direccion" = 'Dirección no disponible';
        UPDATE planificacion."Paradas" p SET
            "ClaveDireccion" = upper(regexp_replace(trim(e."Direccion_Calle"), '\s+', ' ', 'g')) || '|' ||
                upper(regexp_replace(trim(e."Direccion_Numero"), '\s+', ' ', 'g')) || '|' ||
                upper(regexp_replace(trim(e."Direccion_Localidad"), '\s+', ' ', 'g')) || '|' ||
                upper(regexp_replace(trim(e."Direccion_Departamento"), '\s+', ' ', 'g')) || '|' ||
                upper(regexp_replace(trim(e."Direccion_CodigoPostal"), '\s+', ' ', 'g')),
            "Direccion" = e."Direccion_Calle" || ' ' || e."Direccion_Numero" || ', ' || e."Direccion_Localidad"
        FROM envios."Envios" e WHERE e."Id" = p."EnvioId" AND e."OperadorId" = p."OperadorId";

        WITH RECURSIVE versiones AS (
            SELECT p."Id" AS parada, r."Fecha" AS fecha, f.* FROM planificacion."Paradas" p
            JOIN planificacion."Rutas" r ON r."Id" = p."RutaId" AND r."OperadorId" = p."OperadorId"
            JOIN envios."Envios" e ON e."Id" = p."EnvioId" AND e."OperadorId" = p."OperadorId"
            JOIN administracion."FranjasHorarias" f ON f."Id" = e."FranjaHorariaId" AND f."OperadorId" = p."OperadorId"
            UNION ALL
            SELECT v.parada, v.fecha, f.* FROM versiones v
            JOIN administracion."FranjasHorarias" f ON f."ReemplazaAId" = v."Id" AND f."OperadorId" = v."OperadorId"
        ), aplicables AS (
            SELECT DISTINCT ON (parada) parada, "HoraDesde", "HoraHasta" FROM versiones
            WHERE fecha >= "VigenteDesde" AND ("VigenteHasta" IS NULL OR fecha <= "VigenteHasta")
              AND extract(dow FROM fecha)::integer = ANY("Dias")
            ORDER BY parada, "VigenteDesde" DESC
        ) UPDATE planificacion."Paradas" p SET "Desde" = f."HoraDesde", "Hasta" = f."HoraHasta"
        FROM aplicables f WHERE p."Id" = f.parada;
        -- Una franja comprometida sin versión aplicable no se interpreta como un envío libre.
        UPDATE planificacion."Paradas" p SET "ClaveDireccion" = p."Id"::text
        FROM envios."Envios" e WHERE e."Id" = p."EnvioId" AND e."OperadorId" = p."OperadorId"
          AND e."FranjaHorariaId" IS NOT NULL AND p."Desde" IS NULL;

        CREATE TEMP TABLE grupos_paradas_migrados (
            id uuid PRIMARY KEY, ruta uuid, operador uuid, clave text, desde time, hasta time, orden integer
        ) ON COMMIT DROP;
        DO $$
        DECLARE v record; destino uuid;
        BEGIN
            FOR v IN SELECT p.* FROM planificacion."Paradas" p
                JOIN planificacion."Rutas" r ON r."Id" = p."RutaId" AND r."OperadorId" = p."OperadorId"
                WHERE r."Estado" = 'Planificada' AND p."Estado" = 'Pendiente'
                ORDER BY p."RutaId", p."ClaveDireccion", p."Desde" IS NULL, p."Hasta", p."Desde", p."Orden"
            LOOP
                destino := NULL;
                SELECT g.id INTO destino FROM grupos_paradas_migrados g
                WHERE g.ruta = v."RutaId" AND g.operador = v."OperadorId" AND g.clave = v."ClaveDireccion"
                  AND (v."Desde" IS NULL OR g.desde IS NULL OR greatest(g.desde, v."Desde") < least(g.hasta, v."Hasta"))
                ORDER BY g.hasta, g.desde, g.orden LIMIT 1;
                IF destino IS NULL THEN
                    destino := v."Id";
                    INSERT INTO grupos_paradas_migrados VALUES (destino, v."RutaId", v."OperadorId", v."ClaveDireccion", v."Desde", v."Hasta", v."Orden");
                ELSE
                    UPDATE grupos_paradas_migrados SET
                        desde = greatest(desde, v."Desde"), hasta = least(hasta, v."Hasta"), orden = least(orden, v."Orden")
                    WHERE id = destino;
                    UPDATE planificacion."EnviosParada" SET "ParadaId" = destino WHERE "ParadaId" = v."Id";
                END IF;
            END LOOP;
        END $$;
        UPDATE planificacion."Paradas" p SET "Desde" = g.desde, "Hasta" = g.hasta, "Orden" = g.orden
        FROM grupos_paradas_migrados g WHERE p."Id" = g.id;
        DELETE FROM planificacion."Paradas" p WHERE NOT EXISTS
            (SELECT 1 FROM planificacion."EnviosParada" e WHERE e."ParadaId" = p."Id");
        WITH ordenes AS (
            SELECT p."Id", row_number() OVER (PARTITION BY p."RutaId" ORDER BY p."Orden", p."Id") AS orden
            FROM planificacion."Paradas" p JOIN planificacion."Rutas" r ON r."Id" = p."RutaId"
            WHERE r."Estado" = 'Planificada'
        ) UPDATE planificacion."Paradas" p SET "Orden" = o.orden FROM ordenes o WHERE p."Id" = o."Id";
        UPDATE planificacion."Rutas" r SET "Revision" = "Revision" + 1 WHERE r."Estado" = 'Planificada'
            AND EXISTS (SELECT 1 FROM planificacion."Paradas" p WHERE p."RutaId" = r."Id");
        """;

    public const string Down = """
        CREATE TEMP TABLE paradas_anteriores ON COMMIT DROP AS
        SELECT e."Id", e."OperadorId", e."RutaId", e."EnvioId", e."Estado", p."LlegadaEn",
            row_number() OVER (PARTITION BY e."RutaId" ORDER BY p."Orden", e."Id")::integer AS "Orden"
        FROM planificacion."EnviosParada" e JOIN planificacion."Paradas" p ON p."Id" = e."ParadaId";
        DROP TABLE planificacion."EnviosParada";
        DROP TABLE planificacion."Paradas";
        CREATE TABLE planificacion."Paradas" (
            "Id" uuid NOT NULL PRIMARY KEY, "OperadorId" uuid NOT NULL, "RutaId" uuid NOT NULL,
            "EnvioId" uuid NOT NULL, "Orden" integer NOT NULL, "Estado" text NOT NULL,
            "LlegadaEn" timestamp with time zone,
            CONSTRAINT "FK_Paradas_Rutas_OperadorId_RutaId" FOREIGN KEY ("OperadorId", "RutaId")
                REFERENCES planificacion."Rutas" ("OperadorId", "Id") ON DELETE CASCADE
        );
        INSERT INTO planificacion."Paradas" SELECT "Id", "OperadorId", "RutaId", "EnvioId", "Orden", "Estado", "LlegadaEn" FROM paradas_anteriores;
        CREATE INDEX "IX_Paradas_OperadorId_RutaId" ON planificacion."Paradas" ("OperadorId", "RutaId");
        CREATE UNIQUE INDEX "IX_Paradas_RutaId_EnvioId" ON planificacion."Paradas" ("RutaId", "EnvioId");
        CREATE UNIQUE INDEX "UX_Paradas_EnvioActivo" ON planificacion."Paradas" ("EnvioId") WHERE "Estado" = 'Pendiente';
        """;
}
