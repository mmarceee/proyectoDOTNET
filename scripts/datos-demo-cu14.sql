-- Ejecutar en PostgreSQL después de aplicar DetalleEnvioAmpliado.
-- Datos ficticios para desarrollo. No modifica envíos existentes.
-- Si cambiás TenantProvisorio, ajustá los dos identificadores siguientes.
-- Las imágenes son PNG de 1 píxel: sirven para probar los enlaces,
-- no representan una firma ni una fotografía reales.
BEGIN;

DO $$
DECLARE
    operador uuid := '11111111-1111-1111-1111-111111111111';
    comercio uuid := '22222222-2222-2222-2222-222222222222';
    envio uuid;
    foto uuid;
    firma uuid;
    numero text;
    caso integer;
    creado timestamptz := '2026-10-01 09:00:00-03';
    imagen bytea := decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64');
BEGIN
    FOR caso IN 1..2 LOOP
        numero := 'ENV-DEMO-CU14-00' || caso;
        IF EXISTS (SELECT 1 FROM envios."Envios" WHERE "OperadorId" = operador AND "Numero" = numero) THEN
            RAISE NOTICE '% ya existe; se omite.', numero;
            CONTINUE;
        END IF;

        envio := gen_random_uuid();
        foto := gen_random_uuid();
        firma := gen_random_uuid();

        INSERT INTO envios."Envios" (
            "Id", "OperadorId", "ComercioId", "Numero", "Estado", "MontoTarifa", "CreadoEn",
            "Destinatario_Nombre", "Destinatario_Telefono", "Destinatario_Email", "Destinatario_Documento",
            "Direccion_Calle", "Direccion_Numero", "Direccion_Localidad", "Direccion_Departamento",
            "Direccion_CodigoPostal", "Direccion_Referencia", "VersionTarifarioId", "VersionTarifarioNumero"
        ) VALUES (
            envio, operador, comercio, numero, CASE WHEN caso = 1 THEN 'Entregado' ELSE 'Devuelto' END,
            450, creado, CASE WHEN caso = 1 THEN 'Ana Demo' ELSE 'Bruno Demo' END,
            '099000000', 'destinatario@example.com', 'DEMO-1234',
            'Av. 18 de Julio', '1234', 'Montevideo', 'Montevideo', '11200',
            'Datos ficticios CU-14. Portería en planta baja.',
            'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 3
        );

        INSERT INTO envios."Bultos" (
            "Id", "EnvioId", "OperadorId", "ComercioId", "Codigo", "PesoKg", "LargoCm", "AnchoCm", "AltoCm", "MontoTarifa"
        ) VALUES
            (gen_random_uuid(), envio, operador, comercio, numero || '-1', 2.5, 30, 20, 15, 250),
            (gen_random_uuid(), envio, operador, comercio, numero || '-2', 1.2, 20, 15, 10, 200);

        INSERT INTO envios."EventosEnvio" (
            "Id", "EnvioId", "OperadorId", "ComercioId", "EstadoAnterior", "EstadoNuevo",
            "OcurridoEn", "Origen", "ResponsableId", "Latitud", "Longitud", "Detalle"
        )
        SELECT gen_random_uuid(), envio, operador, comercio, h.anterior, h.nuevo,
            creado + h.demora, h.origen, CASE WHEN h.origen = 'Sistema' THEN NULL::uuid
                ELSE 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'::uuid END,
            -34.905, -56.191, h.detalle
        FROM (VALUES
            (NULL::text, 'Admitido', interval '0 hours', 'PortalComercio', 'Alta ficticia con dos bultos.'),
            ('Admitido', 'EnDeposito', interval '3 hours', 'Backoffice', 'Se recibieron los dos bultos.'),
            ('EnDeposito', 'AsignadoARuta', interval '23 hours', 'Backoffice', 'Asignado a ruta de demostración.'),
            ('AsignadoARuta', 'EnTransito', interval '24 hours', 'AppRepartidor', 'Salida a reparto.')
        ) AS h(anterior, nuevo, demora, origen, detalle);

        INSERT INTO envios."ArchivosEvidencia" ("Id", "EnvioId", "OperadorId", "ComercioId", "TipoContenido", "Contenido")
        VALUES (foto, envio, operador, comercio, 'image/png', imagen);

        IF caso = 1 THEN
            INSERT INTO envios."ArchivosEvidencia" ("Id", "EnvioId", "OperadorId", "ComercioId", "TipoContenido", "Contenido")
            VALUES (firma, envio, operador, comercio, 'image/png', imagen);

            INSERT INTO envios."IntentosEntrega" (
                "Id", "EnvioId", "OperadorId", "ComercioId", "NumeroIntento", "FechaHora", "Resultado",
                "MotivoNoEntregaId", "Evidencia_FirmaArchivoId", "Evidencia_FotoArchivoId",
                "Evidencia_NombreReceptor", "Evidencia_DocumentoReceptor", "Evidencia_Latitud",
                "Evidencia_Longitud", "Evidencia_CapturadaEn", "Observaciones"
            ) VALUES (
                gen_random_uuid(), envio, operador, comercio, 1, creado + interval '27 hours', 'Exitoso',
                NULL, firma, foto, 'Ana Demo', 'DEMO-1234', -34.905, -56.191,
                creado + interval '27 hours', 'Recibió la destinataria. Imágenes de prueba de 1 píxel.'
            );

            INSERT INTO envios."EventosEnvio" (
                "Id", "EnvioId", "OperadorId", "ComercioId", "EstadoAnterior", "EstadoNuevo",
                "OcurridoEn", "Origen", "ResponsableId", "Latitud", "Longitud", "Detalle"
            ) VALUES (
                gen_random_uuid(), envio, operador, comercio, 'EnTransito', 'Entregado',
                creado + interval '27 hours', 'AppRepartidor', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                -34.905, -56.191, 'Entrega confirmada con firma y foto ficticias.'
            );
        ELSE
            INSERT INTO envios."IntentosEntrega" (
                "Id", "EnvioId", "OperadorId", "ComercioId", "NumeroIntento", "FechaHora", "Resultado",
                "MotivoNoEntregaId", "Evidencia_FotoArchivoId", "Evidencia_Latitud",
                "Evidencia_Longitud", "Evidencia_CapturadaEn", "Observaciones"
            ) VALUES
                (gen_random_uuid(), envio, operador, comercio, 1, creado + interval '27 hours', 'Fallido',
                 'cccccccc-cccc-cccc-cccc-cccccccccccc', foto, -34.905, -56.191,
                 creado + interval '27 hours', 'Destinatario ausente. Motivo ficticio; catálogo pendiente.'),
                (gen_random_uuid(), envio, operador, comercio, 2, creado + interval '75 hours', 'Fallido',
                 'cccccccc-cccc-cccc-cccc-cccccccccccc', NULL, NULL, NULL, NULL,
                 'Segundo intento: destinatario ausente. Sin evidencia adicional.');

            INSERT INTO envios."EventosEnvio" (
                "Id", "EnvioId", "OperadorId", "ComercioId", "EstadoAnterior", "EstadoNuevo",
                "OcurridoEn", "Origen", "ResponsableId", "Latitud", "Longitud", "Detalle"
            )
            SELECT gen_random_uuid(), envio, operador, comercio, h.anterior, h.nuevo,
                creado + h.demora, h.origen, NULL, NULL, NULL, h.detalle
            FROM (VALUES
                ('EnTransito', 'NoEntregado', interval '27 hours', 'AppRepartidor', 'Primer intento fallido.'),
                ('NoEntregado', 'Reprogramado', interval '28 hours', 'Sistema', 'Nueva visita programada.'),
                ('Reprogramado', 'AsignadoARuta', interval '71 hours', 'Backoffice', 'Asignado a nueva ruta.'),
                ('AsignadoARuta', 'EnTransito', interval '72 hours', 'AppRepartidor', 'Segundo reparto.'),
                ('EnTransito', 'NoEntregado', interval '75 hours', 'AppRepartidor', 'Segundo intento fallido.'),
                ('NoEntregado', 'EnDevolucion', interval '76 hours', 'Sistema', 'Devolución iniciada.'),
                ('EnDevolucion', 'Devuelto', interval '100 hours', 'Backoffice', 'El comercio recibió ambos bultos.')
            ) AS h(anterior, nuevo, demora, origen, detalle);

            INSERT INTO envios."Devoluciones" (
                "Id", "EnvioId", "OperadorId", "ComercioId", "Motivo", "Estado", "IniciadaEn",
                "RecibidaEnDepositoEn", "NombreReceptor", "DocumentoReceptor", "EntregadaAlComercioEn"
            ) VALUES (
                gen_random_uuid(), envio, operador, comercio, 'Dos intentos sin encontrar al destinatario.',
                'Cerrada', creado + interval '76 hours', creado + interval '80 hours',
                'Carla Demo (comercio)', 'DEMO-5678', creado + interval '100 hours'
            );
        END IF;

        INSERT INTO envios."Incidencias" (
            "Id", "EnvioId", "OperadorId", "ComercioId", "Tipo", "Descripcion", "Estado",
            "CreadaEn", "ResueltaEn", "Resolucion"
        ) VALUES (
            gen_random_uuid(), envio, operador, comercio, 'Reclamo',
            'Consulta ficticia del comercio sobre el seguimiento.', 'Resuelta',
            creado + interval '25 hours', creado + interval '26 hours',
            'Se informó el estado y la fecha estimada de visita.'
        );
        RAISE NOTICE 'Creado %', numero;
    END LOOP;
END $$;

COMMIT;

-- Abrir /backoffice/envios/ENV-DEMO-CU14-001 (entregado)
-- o /backoffice/envios/ENV-DEMO-CU14-002 (devuelto).
SELECT "Numero", "Estado", "MontoTarifa"
FROM envios."Envios"
WHERE "Numero" IN ('ENV-DEMO-CU14-001', 'ENV-DEMO-CU14-002')
  AND "OperadorId" = '11111111-1111-1111-1111-111111111111';
