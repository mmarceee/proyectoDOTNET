# Paradas y envíos · acuerdo del 12/10/2026

Una parada es una visita a una dirección. Puede contener varios envíos, incluso a apartamentos distintos del mismo edificio. Cada envío conserva destinatario, referencia/apartamento, compromiso y estado propios.

La dirección se compara por calle, número, localidad, departamento y código postal, normalizando mayúsculas y espacios. La referencia no separa visitas. No se infieren equivalencias de abreviaturas, nombres de calles ni direcciones incompletas.

Los envíos con franja se agrupan sólo si todos tienen un intervalo común de duración positiva para la fecha de la ruta. Mañana y tarde implican dos visitas. Se procesan primero las franjas por hora de fin y luego los envíos sin franja: cada uno de estos se incluye una sola vez en la primera visita compatible de esa dirección. Si ninguno tiene franja, hay una sola visita sin horario restringido. Un intervalo que termina cuando empieza otro no permite agruparlos.

## Modelo y comportamiento

- Ruta contiene Paradas; Parada contiene EnviosParada. Los tres tipos pertenecen al operador y los hijos tienen referencias compuestas que impiden cruzar rutas o empresas.
- Parada guarda dirección, orden, intervalo común, estado y llegada. EnvioParada guarda el identificador y el estado individual del envío dentro de la visita.
- El máximo de paradas cuenta visitas. La capacidad suma todos los bultos, sin reducir peso o volumen por agrupar.
- RF 15 sigue siendo por envío: el índice único parcial está en EnviosParada, con el nombre UX_Paradas_EnvioActivo conservado para el manejo de conflictos.
- Crear, agregar y cambiar fecha/recursos usan el mismo agrupador para evaluar y persistir. Se conservan los miembros existentes al cambiar de visita; CU-42 sigue siendo responsable del ordenamiento posterior.
- CU-41 quita únicamente el miembro. La visita se elimina sólo cuando queda sin envíos. Se renumeran las visitas restantes y la ruta libera recursos únicamente cuando queda vacía.
- La evidencia de validaciones y retiros conserva su significado histórico y no se reescribe para reflejar el modelo nuevo.

## Persistencia y pantalla

La migración ParadasConVariosEnvios copia las asignaciones anteriores a EnviosParada antes de eliminar EnvioId de Paradas. Agrupa las rutas Planificadas por dirección y franjas aplicables, siguiendo sus reemplazos. Conserva las visitas de rutas ya despachadas o ejecutadas. Incrementa la revisión de rutas Planificadas con envíos para invalidar formularios antiguos. Su reversión vuelve a una parada por envío sin perder asignaciones.

El detalle muestra una sección por visita, con dirección, horario si corresponde y los envíos que contiene. El botón Quitar pertenece a cada envío. Los contratos HTTP de paradas ahora devuelven una lista Envios, en lugar de un único EnvioId.

Las migraciones se prueban en PostgreSQL descartable; la base local se actualiza al reiniciar la API con Database:MigrateOnStartup habilitado. Identity y publicación por RabbitMQ mantienen sus dependencias separadas.

Verificación: compilación Release sin errores ni advertencias y **203 pruebas aprobadas** (46 unitarias, 54 de arquitectura y 103 de integración). Incluye edificios con apartamentos distintos, normalización, franjas incompatibles y solapadas, envío sin franja sin duplicación, agregar/quitar miembros, RF 15, conservación de evidencia, migración de ida/vuelta y unión de visitas tras un reemplazo de franja aplicable a la nueva fecha.
