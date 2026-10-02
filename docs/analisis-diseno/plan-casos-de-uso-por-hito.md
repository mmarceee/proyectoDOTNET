# **Plan de casos de uso por hito**

| Estado | Acordado — reparto de responsables aprobado por el responsable del plan de trabajo (Lucas Ottonello) |
| :---- | :---- |
| **Fecha** | 1 de octubre de 2026 (actualizado el 2 de octubre de 2026) |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Complementa** | [Plan de trabajo y responsabilidades](plan-de-trabajo-y-responsabilidades.md), [catálogo](catalogo-casos-de-uso.md) y [detalle](casos-de-uso.md) de casos de uso |

Este documento reparte los 49 casos de uso entre los hitos del laboratorio, les asigna un nivel de profundidad y un responsable. Los responsables siguen la afinidad por área del plan de trabajo, con una cantidad de casos equilibrada entre los tres integrantes.

# **1\. Criterios**

## **1.1 Horas disponibles**

Del 1/10 al 12/11 quedan unas 6 semanas: 3 integrantes × 12 horas × 6 semanas ≈ **215 horas**. Descontando los requerimientos no funcionales, los ADR pendientes, la documentación, el cambio de requerimientos del 29/10 y los opcionales, quedan unas **50 a 80 horas** para los casos de uso. Por eso no todos los casos se implementan con la misma profundidad.

## **1.2 Niveles de profundidad**

| Nivel | Qué incluye | Para qué casos |
| :---- | :---- | :---- |
| **C · Completo** | Flujo principal, todos los flujos alternativos y requerimientos especiales; pruebas unitarias y de integración. | Los que se demuestran en un monitoreo o en un escenario de la defensa (letra, sección 9.3). |
| **M · Mínimo** | Flujo principal y los flujos alternativos que protegen reglas de negocio importantes; pruebas de sus reglas. | La mayoría: ABM y transiciones que no se demuestran en detalle. |
| **B · Básico** | Funciona de punta a punta, con la interfaz más simple posible. | Reportes, incidencias, etiquetas y procesos secundarios. |

Todos los casos se implementan: la letra exige cubrir todos los requerimientos funcionales (sección 5). La diferencia está en la profundidad.

## **1.3 Qué significa ser responsable de un caso de uso**

**Todo el equipo trabaja en todos los casos de uso.** El responsable no es quien lo programa en solitario, sino quien:

1\.  Se asegura de que el caso quede terminado y probado en su hito.

2\.  Revisa los cambios que hacen los demás en ese caso.

3\.  Lo puede explicar y defender. La letra establece que *"un componente que su responsable no pueda explicar se considerará no entregado a los efectos de su calificación individual"* (sección 2), y que en la defensa cada integrante explica código *"de su área de responsabilidad"* (sección 9.4).

## **1.4 Responsables por bloque**

Cada integrante responde por bloques coherentes, alineados con su área del plan de trabajo:

| Integrante | Área del plan de trabajo | Bloques de casos de uso | Casos | Completos |
| :---- | :---- | :---- | :---: | :---: |
| **Cristian Reyes** | Administración, usuarios, Identity, multitenancy y máquina de estados | **Administración y configuración** (CU-01 a CU-08) · **transiciones de estado** (CU-16, CU-19, CU-20, CU-21, CU-22) · **seguimiento público y tablero** (CU-60, CU-61, CU-66) | 16 | 5 |
| **Ezequiel Marcenal** | Modelado de dominio, infraestructura, Docker, CI, pruebas, Terraform y despliegue | **Núcleo de Envíos** (CU-10, CU-11, CU-17, CU-18) · **Depósito y liquidaciones** (CU-30 a CU-33, CU-55) · **mensajería, Worker y reportes** (CU-09, CU-52, CU-62, CU-64, CU-70, CU-71, CU-72) | 16 | 6 |
| **Lucas Ottonello** | Arquitectura, comunicación entre módulos, documentación y presentación | **Planificación** (CU-40 a CU-43) · **Ejecución en la PWA** (CU-50, CU-51, CU-53, CU-54) · **consultas y avisos del Portal** (CU-13, CU-14, CU-15, CU-63, CU-65) · **API pública** (CU-80 a CU-83) | 17 | 5 |

# **2\. Plan por hito**

## **2.1 Hito del 8/10 · Esqueleto de extremo a extremo**

**Objetivo de la letra:** *docker compose up levanta el entorno completo, un envío dado de alta desde el portal del comercio se ve en el backoffice, y el pipeline de integración continua corre.*

| CU | Caso de uso | Nivel | Responsable | Nota |
| :---- | :---- | :---- | :---- | :---- |
| CU-10 | Crear un envío individual | C (versión mínima ahora) | Ezequiel | Un bulto, sin franja, tenant provisorio. Se completa para el 15/10. |
| CU-13 | Consultar envíos | M | Lucas | Listado del Backoffice sin filtros. |

**Trabajo transversal:** `SharedKernel` (Ezequiel); DbContext y migraciones de Envíos y Administración (Ezequiel); datos iniciales y `ICurrentTenant` provisorio (Cristian); pantalla del Portal y página del Backoffice (Lucas).

## **2.2 Hito del 15/10 · Máquina de estados, multitenancy y autenticación**

**Objetivo de la letra:** *máquina de estados operativa; multitenancy funcionando con dos operadores de configuración distinta; autenticación y autorización por perfil.*

| CU | Caso de uso | Nivel | Responsable | Nota |
| :---- | :---- | :---- | :---- | :---- |
| CU-10 | Crear un envío individual (completar) | C | Ezequiel | Tarifa por bulto, franjas y flujos alternativos. |
| CU-02 | Gestionar usuarios | C | Cristian | Identity, perfiles y autorización por perfil. |
| CU-03 | Seleccionar el operador de la sesión | M | Cristian | |
| CU-07 | Gestionar la identidad visual | M | Cristian | Muestra los dos operadores con marca distinta. |
| CU-14 | Consultar el detalle de un envío | M | Lucas | Historial de eventos visible en la demo. |
| CU-16 | Cancelar un envío | M | Cristian | |
| CU-19 | Reprogramar un envío no entregado | M | Cristian | |
| CU-30 | Recibir bultos en depósito | M | Ezequiel | Transición T2 con transacción compartida. |
| CU-40 | Armar una hoja de ruta | C | Lucas | RF 15: un envío en una sola ruta, con concurrencia. |
| CU-41 | Quitar un envío de una ruta | M | Lucas | |

**Trabajo transversal:** máquina de estados con su tabla declarativa y pruebas (Cristian); interceptor, filtros por inquilino y pruebas de aislamiento (Cristian y Ezequiel); datos iniciales de dos operadores con configuración distinta (Cristian); `ModuleDbContext` con schema por módulo y tabla outbox (Ezequiel).

## **2.3 Hito del 22/10 · PWA sin conexión y caché**

**Objetivo de la letra:** *aplicación móvil descargando la hoja de ruta, registrando entregas sin conectividad y sincronizando; caché en funcionamiento con su métrica de aciertos.*

| CU | Caso de uso | Nivel | Responsable | Nota |
| :---- | :---- | :---- | :---- | :---- |
| CU-43 | Despachar una ruta | M | Lucas | Requisito para que la PWA descargue la ruta. |
| CU-50 | Descargar la hoja de ruta | C | Lucas | Datos cifrados en el dispositivo. |
| CU-51 | Escanear la carga del vehículo | C | Lucas | |
| CU-17 | Registrar una entrega | C | Ezequiel | Firma obligatoria; escenario 2 de la defensa. |
| CU-18 | Registrar un intento fallido | C | Ezequiel | Reprogramación o devolución automática. |
| CU-52 | Sincronizar las operaciones sin conexión | C | Ezequiel | Escenarios 2 y 3 de la defensa. |
| CU-54 | Iniciar la rendición | M | Lucas | |
| CU-55 | Confirmar la rendición | M | Ezequiel | |
| CU-60 | Consultar el seguimiento público | C | Cristian | Caché con métrica de aciertos. |
| CU-04 | Definir zonas y franjas | M | Cristian | |
| CU-05 | Configurar el cuadro tarifario | C | Cristian | Caché con invalidación; escenario 5 de la defensa. |
| CU-06 | Configurar las reglas operativas | C | Cristian | Escenario 5 de la defensa. |

**Trabajo transversal:** ADR de resolución de conflictos de sincronización (Ezequiel, obligatorio); ADR de caché (Cristian, obligatorio); IndexedDB cifrado en la PWA (Lucas).

## **2.4 Hito del 29/10 · Mensajería, avisos y tablero**

**Objetivo de la letra:** *mensajería asíncrona con worker independiente; avisos a comercios con reintentos y cola de fallidos; tablero en tiempo real.* Además, ese día llega el **cambio de requerimientos**: conviene no cargar este hito al máximo.

| CU | Caso de uso | Nivel | Responsable | Nota |
| :---- | :---- | :---- | :---- | :---- |
| CU-62 | Notificar al destinatario | C | Ezequiel | Mailpit en desarrollo. |
| CU-63 | Configurar las suscripciones de avisos | C | Lucas | |
| CU-64 | Entregar los avisos a los comercios | C | Ezequiel | Receptor de prueba; escenario 6 de la defensa. |
| CU-65 | Consultar el historial de avisos y reenviar | C | Lucas | |
| CU-66 | Ver el tablero de operación en vivo | C | Cristian | SignalR con aislamiento por inquilino. |
| CU-09 | Consultar y reintentar los mensajes fallidos | M | Ezequiel | Exigido por la sección 6.8. |
| CU-53 | Reportar la posición del vehículo | M | Lucas | Alimenta el tablero. |
| CU-61 | Solicitar la reprogramación | M | Cristian | T18 y T19, a confirmar. |
| CU-20 | Iniciar la devolución | M | Cristian | |
| CU-21 | Declarar un envío como extraviado | M | Cristian | |
| CU-31 | Confirmar la devolución al comercio | M | Ezequiel | |

**Trabajo transversal:** Outbox, publicador, inbox y topología de RabbitMQ según el ADR-0003 (Ezequiel y Lucas); el Worker ejecutando el módulo Seguimiento (Lucas); incorporación del cambio de requerimientos (todo el equipo).

## **2.5 Hito del 5/11 · Despliegue y observabilidad**

**Objetivo de la letra:** *solución desplegada en la nube mediante infraestructura como código y el tablero de observabilidad operativo.* La exención de equipos de 3 quita el requisito de dos instancias balanceadas.

| CU | Caso de uso | Nivel | Responsable | Nota |
| :---- | :---- | :---- | :---- | :---- |
| CU-01 | Gestionar comercios | M | Cristian | Hasta acá se usaban datos iniciales. |
| CU-08 | Gestionar repartidores y vehículos | M | Cristian | Ídem. |
| CU-11 | Importar envíos desde un archivo | M | Ezequiel | Idempotencia (RF 7). |
| CU-42 | Ordenar las paradas de una ruta | B | Lucas | Por franja y zona. |
| CU-32 | Generar la liquidación de un comercio | B | Ezequiel | |
| CU-33 | Consultar la cuenta corriente | B | Ezequiel | |

**Trabajo transversal:** Terraform, App Platform, PostgreSQL, Valkey y RabbitMQ administrados (Ezequiel); Serilog, OpenTelemetry y tablero técnico (Ezequiel y Lucas); ADR de modo de renderizado de Blazor (Lucas, obligatorio).

## **2.6 Hito del 12/11 · Opcionales y estabilización**

**Objetivo de la letra:** *requerimientos opcionales completos; ensayo de la presentación y de la demo; revisión final.*

| CU | Caso de uso | Nivel | Responsable | Nota |
| :---- | :---- | :---- | :---- | :---- |
| CU-80 a CU-83 | API pública para comercios | M | Lucas | Opcional 7.4 (3 puntos). Ver sección 4. |
| CU-15 | Imprimir las etiquetas | B | Lucas | |
| CU-22 | Gestionar incidencias | B | Cristian | |
| CU-70 | Ejecutar el cierre diario | B | Ezequiel | |
| CU-71 | Recalcular los indicadores de cumplimiento | B | Ezequiel | |
| CU-72 | Consultar reportes de gestión | B | Ezequiel | Tablas, sin gráficos. |

**Trabajo transversal:** opcionales restantes hasta llegar a 8 puntos; corrección de errores; documentación final, bitácora de IA y registro de horas; ensayo de la presentación (todo el equipo).

# **3\. Resumen**

| Hito | Casos | Completos | Mínimos | Básicos |
| :---- | :---: | :---: | :---: | :---: |
| 8/10 | 2 | 1 | 1 | 0 |
| 15/10 | 9 | 2 | 7 | 0 |
| 22/10 | 12 | 8 | 4 | 0 |
| 29/10 | 11 | 5 | 6 | 0 |
| 5/11 | 6 | 0 | 3 | 3 |
| 12/11 | 9 | 0 | 4 | 5 |
| **Total** | **49** | **16** | **25** | **8** |

| Responsable | Casos | Completos |
| :---- | :---: | :---: |
| Lucas Ottonello | 17 | 5 |
| Ezequiel Marcenal | 16 | 6 |
| Cristian Reyes | 16 | 5 |

La cantidad de casos está equilibrada. Ezequiel concentra los de mayor complejidad (sincronización y avisos) además de la infraestructura y el despliegue, por lo que su carga en horas es mayor en los hitos del 22/10 y el 5/11. Si hiciera falta compensarla, la opción natural es que Lucas tome CU-52, ya que es responsable del resto de la PWA.

# **4\. Decisiones abiertas del plan**

| Tema | Por qué importa |
| :---- | :---- |
| API pública (opcional 7.4) | Vale 3 puntos, pero es de los opcionales más costosos (claves, ambiente de pruebas, limitación de tasa, documentación). Hay opcionales de 2 puntos que salen casi solos con lo que ya está armado: **cobertura de pruebas superior al 70 %** y **pruebas de resiliencia**. Conviene definir qué combinación llega a los 8 puntos antes del 29/10. |
| Hito del 22/10 | Es el más cargado (12 casos, 8 completos). Si el 15/10 se atrasa, conviene pasar CU-04 y CU-54 al 29/10. |
| Responsables | Acordados con el responsable del plan de trabajo. Se pueden ajustar según el avance, como prevé el plan de trabajo. |

# **5\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 01/10/2026 | Propuesta inicial. | Ezequiel Marcenal |
| 0.2 | 02/10/2026 | Responsable = dueño que responde por el caso, no único que lo programa. Reparto equilibrado por bloques (16, 16 y 17 casos), acordado con Lucas Ottonello. | Ezequiel Marcenal |
