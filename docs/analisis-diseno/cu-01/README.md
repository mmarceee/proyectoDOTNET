# CU-01 · Gestionar comercios

Implementación del Backoffice en `/backoffice/comercios`, sólo para Administrador.

- Listado por razón social, documento fiscal y estado de la relación.
- Alta en dos pasos: búsqueda exacta del documento y confirmación. Si el comercio ya existe, se reutiliza sin cambiar la razón social ni revelar sus otros operadores. Si ya está vinculado, se ofrece abrir el detalle.
- Documento fiscal: formato de 12 dígitos, sin separadores, coherente con los datos uruguayos de demostración; no se calcula dígito verificador.
- Correo de contacto propio de cada relación. El operador no modifica la razón social.
- Activa → Suspendida; Suspendida → Activa; Activa o Suspendida → Baja sólo sin envíos pendientes. Baja no se reactiva ni se vuelve a crear. Los cambios de estado piden confirmación.
- Son terminales para la baja: Entregado, Devuelto, Extraviado y Cancelado. NoEntregado sigue siendo transitorio.
- Versión optimista de la relación mediante xmin de PostgreSQL. Un formulario obsoleto se rechaza y requiere recargar.
- El alta de un envío y los cambios de relación comparten un bloqueo de fila dentro de la unidad de trabajo: suspender/bajar y admitir un envío no pueden confirmarse sobre comprobaciones contradictorias. La suspensión no modifica envíos existentes ni relaciones de otro operador.
- El detalle ofrece invitar un usuario mediante CU-02 cuando no tiene cuenta y la relación está activa; si ya existe, se conserva la cuenta compartida.

ADR-0001: Presentation → Application → Domain; persistencia por puerto/repositorio; otros módulos sólo por Contracts.
ADR-0002: tenant de sesión; relaciones filtradas; búsqueda exacta global acotada a CU-01 A1 (aclaración en §2.6).
ADR-0003: comercio y relación se confirman juntos; CU-01 no incorpora reacciones externas ni nuevos eventos de integración. Las invitaciones siguen usando el outbox de CU-02.

Pruebas: reglas de dominio; alta/vinculación sin duplicar; aislamiento de lecturas/escrituras; contacto por operador; formatos; roles; concurrencia y versión obsoleta; bloqueo de nuevos envíos con cookie ya abierta; baja con pendientes.
