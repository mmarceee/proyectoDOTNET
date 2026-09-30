# **Plan de trabajo y responsabilidades**

Sistema de gestión logística

Documento de planificación del Equipo 1 para organizar el desarrollo, asignar responsabilidades iniciales y controlar el avance hasta la entrega final.

| Asignatura | Laboratorio .NET 2026 |
| :---- | :---- |
| **Equipo** | Equipo 1 |
| **Integrantes** | Lucas OttonelloEzequiel MarcenalCristian Reyes |
| **Responsable del documento** | Lucas Ottonello |
| **Estado** | Aceptado |
| **Versión** | 0.3 |
| **Fecha** | 28 de septiembre de 2026 |

# **1 Objetivo y alcance**

Este documento establece la organización inicial del trabajo del Equipo 1 para el desarrollo del sistema de gestión logística. Define responsabilidades, forma de coordinación, hitos y resultados verificables. La distribución es provisional y podrá ajustarse cuando cambie la carga de trabajo o se incorpore el cambio de requerimientos previsto para el 29 de octubre.

# **2 Forma de trabajo**

El equipo trabajará de forma incremental y tomará como referencia los hitos establecidos en la letra del laboratorio. Las funcionalidades se implementarán mediante vertical slices, respetando los límites de los módulos y los contratos definidos para la comunicación interna.

Las tareas indicadas como compartidas serán distribuidas entre los integrantes de acuerdo con la carga de trabajo, la disponibilidad y las necesidades de cada etapa. La distribución podrá revisarse durante el desarrollo.

# **3 Responsabilidades iniciales**

| Integrante | Responsabilidades principales |
| :---- | :---- |
| Lucas Ottonello | Arquitectura de la solución, estructura de proyectos, comunicación entre módulos, mantenimiento de las decisiones arquitectónicas, documentación y preparación de la presentación final. |
| Ezequiel Marcenal | Modelado de dominio, infraestructura, Docker, integración continua, pruebas automáticas, Terraform y despliegue en DigitalOcean. |
| Cristian Reyes | Administración, usuarios, autenticación con Identity, multitenancy, máquina de estados y reglas relacionadas con los cambios de estado de los envíos. |
| Equipo completo | Coordinación, desarrollo de los módulos funcionales, revisión de código, resolución del cambio de requerimientos y validación integral del sistema. |

Esta distribución es inicial. El equipo podrá modificarla según el avance, la complejidad real de las funcionalidades y la disponibilidad de cada integrante.

# **4 Distribución por área**

| Área o actividad | Responsabilidad |
| :---- | :---: |
| Coordinación y seguimiento | Compartida |
| Arquitectura y estructura de la solución | Lucas |
| Infraestructura y despliegue | Ezequiel |
| Administración, Identity y multitenancy | Cristian |
| Envíos y entregas | Compartida |
| Planificación de rutas | Compartida |
| Ejecución de rutas y PWA | Compartida |
| Seguimiento, SignalR y notificaciones | Compartida |
| Depósito y liquidaciones | Compartida |
| Pruebas automáticas y calidad | Ezequiel |
| Documentación y presentación | Lucas |
| Modelado de dominio | Ezequiel |
| Máquina de estados | Cristian |

# **5 Plan por hitos**

| Fecha | Objetivo | Responsabilidad principal | Resultado esperado |
| :---: | :---- | :---: | :---- |
| 4/10 | Completar análisis y diseño | Todo el equipo | Arquitectura lógica, despliegue previsto, modelado de dominio, máquina de estados, multitenancy, stack, ADR y plan de trabajo. |
| 8/10 | Obtener una solución ejecutable | Lucas y Ezequiel | Solución .NET, proyectos iniciales, Docker Compose, servicios locales y pipeline de integración continua. |
| 15/10 | Implementar bases funcionales y transversales | Cristian con apoyo del equipo | Identity, cookies, multitenancy, persistencia y reglas de cambio de estado. |
| 22/10 | Implementar la experiencia del repartidor | Responsabilidad compartida | PWA instalable, IndexedDB, funcionamiento sin conexión, sincronización y Valkey. |
| 29/10 | Incorporar comunicación y procesamiento asíncrono | Responsabilidad compartida | RabbitMQ, Worker, reintentos, webhooks, SignalR y adaptación al cambio de requerimientos. |
| 5/11 | Preparar el entorno productivo | Ezequiel | Despliegue en App Platform, PostgreSQL y Valkey administrados, CloudAMQP, Terraform y observabilidad. |
| 12/11 | Completar opcionales y estabilizar la solución | Todo el equipo | Requerimientos opcionales completos, pruebas finales, corrección de errores, documentación y ensayo de la presentación y la demostración. |
| 15/11 | Realizar la entrega final | Todo el equipo | Sistema desplegado, documentación final y presentación. |

# **6 Seguimiento y control**

Antes de cerrar cada hito, el equipo comprobará:

* Que la solución compile y pueda ejecutarse.  
* Que las funcionalidades terminadas tengan pruebas acordes a su riesgo.  
* Que no existan dependencias indebidas entre módulos.  
* Que se mantenga el aislamiento entre tenants.  
* Que la documentación coincida con la implementación.  
* Que los cambios sean revisados antes de integrarse a la rama principal.

# **7 Gestión de cambios**

La planificación podrá modificarse después del cambio de requerimientos previsto para el 29 de octubre. Cuando aparezca un cambio significativo, el equipo seguirá este procedimiento:

1. Analizar el impacto en los módulos y casos de uso.  
2. Determinar si afecta una decisión arquitectónica.  
3. Registrar y comunicar los cambios acordados.  
4. Reasignar responsabilidades y fechas si fuera necesario.  
5. Actualizar este documento cuando cambie la planificación general.

# **8 Criterios comunes de documentación**

Este documento establece el formato visual inicial para la documentación del proyecto. Los documentos nuevos mantendrán, cuando corresponda, la misma portada, identificación del equipo, títulos numerados, tablas, encabezado, pie de página e historial de versiones.

# **9 Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 28/09/2026 | Creación del plan inicial de trabajo y responsabilidades. | Lucas Ottonello |
| 0.2 | 28/09/2026 | Se simplificó la forma de coordinación de las tareas compartidas. | Lucas Ottonello |
| 0.3 | 28/09/2026 | Se incorporaron los requerimientos opcionales al hito del 12 de noviembre. | Lucas Ottonello |

