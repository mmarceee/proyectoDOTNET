# **Casos de uso**

| Estado | Borrador — para revisar con el equipo |
| :---- | :---- |
| **Fecha** | 30 de septiembre de 2026 |
| **Autor** | Ezequiel Marcenal |
| **Equipo** | Equipo 1 \- Lucas Ottonello, Ezequiel Marcenal y Cristian Reyes |
| **Catálogo** | [catalogo-casos-de-uso.md](catalogo-casos-de-uso.md) |

Este documento detalla cada caso de uso del [catálogo](catalogo-casos-de-uso.md). Las secciones y los identificadores coinciden con los del catálogo.

# **1\. Convenciones**

Cada caso de uso se describe con los campos: caso de uso, descripción, pre-condición, post-condición, actores, flujo de evento principal, flujos alternativos y requerimientos especiales.

•  Los flujos alternativos se identifican como **A1, A2, …** e indican el paso del flujo principal en el que se originan.  
•  **BO**: Backoffice. **PC**: Portal del comercio. **SP**: Seguimiento público. **PWA**: aplicación del repartidor. **API**: API pública. **W**: Worker.  
•  **T1 … T19**: transiciones de la tabla de transiciones (T18 y T19 propuestas en el catálogo, sección 2.6).  
•  Todo cambio de estado de un envío pasa por `Envio.Transicionar`, registra un `EventoEnvio` y publica un evento por Outbox (catálogo, sección 1). Para no repetirlo, los casos de uso lo mencionan sólo cuando agrega algo.  
•  Todas las consultas y operaciones quedan restringidas al inquilino de la sesión (ADR-0002). Para no repetirlo, se menciona sólo cuando el caso tiene una particularidad.

# **2\. Casos de uso**

## **2.1 Administración y configuración**

### **CU-01 · Gestionar comercios**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-01 · Gestionar comercios |
| **Descripción** | El administrador da de alta, modifica, suspende, reactiva, da de baja y consulta los comercios que trabajan con su operador. Como un comercio puede operar con varios operadores, el comercio es una entidad global identificada por su documento fiscal, y lo que se gestiona para cada operador es su `RelacionComercial`. |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*. |
| **Post-Condición** | **Éxito:**<br>• Alta: existe el comercio (nuevo o existente) y una `RelacionComercial` **Activa** con el operador.<br>• Modificación: el correo de contacto de la relación queda actualizado.<br>• Suspensión: la relación queda **Suspendida**; el comercio no puede crear envíos con el operador, y sus envíos existentes siguen su curso.<br>• Reactivación: la relación vuelve a **Activa**.<br>• Baja: la relación queda en **Baja** (baja lógica, sin borrar datos).<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** administrador, desde el Backoffice. |
| **Flujo de evento principal** | *Alta de un comercio:*<br>1. El administrador elige **Comercios**.<br>2. El sistema lista los comercios del operador con razón social, documento fiscal, estado de la relación y fecha de alta.<br>3. El administrador elige **Nuevo comercio** e ingresa el documento fiscal.<br>4. El sistema verifica que no exista un comercio con ese documento fiscal en la plataforma.<br>5. El administrador ingresa la razón social y el correo de contacto.<br>6. El administrador confirma.<br>7. El sistema crea el comercio y su `RelacionComercial` activa con el operador.<br>8. El sistema muestra el comercio en el listado. |
| **Flujos alternativos** | **A1 · El comercio ya existe en la plataforma (paso 4).** Otro operador ya trabaja con ese comercio. El sistema muestra su razón social, sin revelar con qué otros operadores trabaja, y el administrador confirma vincularlo: se crea sólo la `RelacionComercial`.<br><br>**A2 · El comercio ya trabaja con este operador (paso 4).** El sistema lo informa y ofrece abrir el comercio.<br><br>**A3 · Datos inválidos (paso 6).** Documento fiscal o correo con formato inválido, o razón social vacía. El sistema indica el error en cada campo.<br><br>**A4 · Modificar.** El administrador abre un comercio, cambia el correo de contacto y confirma. La razón social no la modifica el operador.<br><br>**A5 · Suspender o reactivar.** El administrador abre un comercio y cambia el estado de la relación; el sistema pide confirmación.<br><br>**A6 · Dar de baja.** Si el comercio tiene envíos en estados no terminales con el operador, el sistema no permite la baja e informa cuántos son. Si no, pide confirmación y pasa la relación a *Baja*.<br><br>**A7 · Buscar.** El administrador filtra el listado por razón social, documento fiscal o estado. |
| **Requerimientos especiales** | 1. `Comercio` es global y no lleva `OperadorId`; `RelacionComercial` sí (ADR-0002, modelo de dominio).<br>2. El documento fiscal es único en toda la plataforma.<br>3. Un operador nunca ve con qué otros operadores trabaja un comercio.<br>4. Control de concurrencia optimista sobre `RelacionComercial` (sección 6.4).<br>5. **Decisión:** el comercio global guarda sólo el documento fiscal, que no cambia, y la razón social, que sólo corrige el propio comercio. El correo de contacto se guarda en la `RelacionComercial`: cada operador tiene y edita el suyo. |

### **CU-02 · Gestionar usuarios**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-02 · Gestionar usuarios |
| **Descripción** | El administrador crea, modifica, desactiva y consulta los usuarios de su operador (perfiles *Administrador*, *Despachador*, *Repartidor* y *Operario de depósito*) y los de los comercios que trabajan con él (perfil *Usuario de comercio*). El perfil determina los permisos del usuario. |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*.<br>2. Para crear un usuario de comercio, el comercio tiene una `RelacionComercial` activa con el operador. |
| **Post-Condición** | **Éxito:**<br>• Alta: existe el usuario en ASP.NET Core Identity, con su perfil y con el `OperadorId` (personal del operador) o el `ComercioId` (usuario de comercio) como claim; queda pendiente de activación hasta que defina su contraseña.<br>• Modificación: el nombre o el perfil quedan actualizados.<br>• Desactivación: el usuario no puede iniciar sesión y sus sesiones abiertas quedan invalidadas.<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** administrador, desde el Backoffice.<br>**Secundarios:** ASP.NET Core Identity; Worker, que envía el correo de invitación. |
| **Flujo de evento principal** | *Alta de un usuario:*<br>1. El administrador elige **Usuarios**.<br>2. El sistema lista los usuarios del operador y de sus comercios, con nombre, correo, perfil, comercio (si corresponde) y estado.<br>3. El administrador elige **Nuevo usuario** e ingresa nombre, correo y perfil.<br>4. Si el perfil es *Usuario de comercio*, el administrador elige el comercio. Si es *Repartidor*, puede vincularlo a un repartidor existente (CU-08).<br>5. El administrador confirma.<br>6. El sistema crea el usuario, pendiente de activación.<br>7. El sistema registra el evento `UsuarioCreado` por Outbox; el Worker envía al usuario un correo con un enlace para definir su contraseña. |
| **Flujos alternativos** | **A1 · Correo ya registrado (paso 5).** El sistema informa que ya existe un usuario con ese correo y no lo crea.<br><br>**A2 · Datos inválidos (paso 5).** El sistema indica el error en cada campo.<br><br>**A3 · Cambiar el perfil.** El administrador abre un usuario del operador y elige otro perfil; el cambio rige desde el próximo inicio de sesión.<br><br>**A4 · Desactivar.** El administrador desactiva un usuario; el sistema invalida sus sesiones.<br><br>**A5 · Desactivarse a sí mismo o dejar al operador sin administradores (A3 o A4).** El sistema no lo permite.<br><br>**A6 · El enlace de invitación venció.** El administrador reenvía la invitación desde el usuario. |
| **Requerimientos especiales** | 1. Identity con cookies HttpOnly y Secure; las contraseñas nunca se guardan ni se envían en claro (Propuesta de stack, sección 3.4; sección 6.3 de la letra).<br>2. Los perfiles son roles de Identity; la autorización se aplica en la API por perfil y por inquilino (sección 6.3).<br>3. El enlace de invitación es de un solo uso y vence a las 48 horas.<br>4. **Decisión:** el operador que da de alta un comercio le crea su usuario. Si el comercio trabaja después con otro operador, usa el mismo usuario y elige el operador al iniciar sesión (CU-03). Ese usuario también corrige la razón social del comercio desde el Portal.<br>5. Responsable: Cristian Reyes (hito del 15/10). |

### **CU-03 · Seleccionar el operador de la sesión**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-03 · Seleccionar el operador de la sesión |
| **Descripción** | Un usuario de comercio cuyo comercio trabaja con más de un operador elige con cuál trabaja en la sesión. Todo lo que hace después (altas, consultas, avisos) queda asociado a ese operador, y el portal muestra su identidad visual (RF 5). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Usuario de comercio*.<br>2. Su comercio tiene al menos una `RelacionComercial` activa. |
| **Post-Condición** | **Éxito:** la sesión tiene el operador seleccionado, y el portal muestra la identidad visual de ese operador.<br>**Fracaso:** la sesión queda sin operador y el usuario no puede operar. |
| **Actores** | **Principal:** usuario de comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario inicia sesión.<br>2. El sistema obtiene las relaciones activas de su comercio.<br>3. El sistema muestra los operadores disponibles, con su nombre y logo.<br>4. El usuario elige un operador.<br>5. El sistema verifica que la relación siga activa y registra el operador en la sesión.<br>6. El sistema muestra el portal con la identidad visual del operador. |
| **Flujos alternativos** | **A1 · Una sola relación activa (paso 3).** El sistema la selecciona automáticamente y continúa en el paso 5.<br><br>**A2 · Ninguna relación activa (paso 2).** El sistema informa que el comercio no tiene operadores activos.<br><br>**A3 · Cambiar de operador.** Durante la sesión, el usuario elige **Cambiar operador** y el flujo vuelve al paso 3.<br><br>**A4 · La relación se suspende durante la sesión.** En la siguiente operación, el sistema la rechaza, informa la situación y vuelve al paso 3. |
| **Requerimientos especiales** | 1. El operador seleccionado se guarda como claim en la cookie de sesión, que se vuelve a emitir al cambiar de operador. Completa lo que el ADR-0002 dejó para el modelo de dominio.<br>2. El servidor nunca acepta un operador enviado por el cliente sin verificar la relación (ADR-0002). |

### **CU-04 · Definir zonas de cobertura y franjas horarias**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-04 · Definir zonas de cobertura y franjas horarias |
| **Descripción** | El administrador define las zonas en las que el operador entrega, con los códigos postales que abarca cada una, y las franjas horarias de entrega que ofrece en cada zona y en qué días (RF 2). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*. |
| **Post-Condición** | **Éxito:** la zona queda activa, con sus códigos postales y franjas, y puede usarse en el alta de envíos (CU-10) y en el cuadro tarifario (CU-05).<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** administrador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El administrador elige **Zonas**.<br>2. El sistema lista las zonas con su código, nombre, cantidad de códigos postales y estado.<br>3. El administrador elige **Nueva zona** e ingresa código, nombre y los códigos postales.<br>4. El administrador agrega una o más franjas, cada una con hora de inicio, hora de fin y días de la semana.<br>5. El administrador confirma.<br>6. El sistema valida los datos y guarda la zona activa.<br>7. El sistema invalida la caché de zonas del operador. |
| **Flujos alternativos** | **A1 · Código de zona repetido (paso 6).** Ya existe una zona con ese código en el operador. El sistema lo informa.<br><br>**A2 · Código postal ya asignado (paso 6).** Un código postal ya pertenece a otra zona activa del operador. El sistema lo informa: cada código postal pertenece a una sola zona, para que el alta de envíos determine la zona sin ambigüedad.<br><br>**A3 · Franja inválida (paso 6).** La hora de inicio no es anterior a la de fin, o la franja se superpone con otra del mismo día. El sistema lo informa.<br><br>**A4 · Modificar.** El administrador cambia nombre, códigos postales o franjas de una zona. Los envíos ya admitidos conservan su zona.<br><br>**A5 · Desactivar.** La zona deja de aceptarse en nuevos envíos; los envíos existentes no se modifican. |
| **Requerimientos especiales** | 1. Unicidad del código de zona por operador (índice compuesto con `OperadorId`, ADR-0002).<br>2. **Supuesto del modelo:** la cobertura se define por códigos postales, no por polígonos geográficos (modelo de dominio).<br>3. La caché de zonas se invalida al guardar (sección 6.7). |

### **CU-05 · Configurar el cuadro tarifario**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-05 · Configurar el cuadro tarifario |
| **Descripción** | El administrador crea una nueva versión del cuadro tarifario a partir de la vigente, edita sus reglas (precio base por zona, modalidad y rangos de peso y volumen) y sus ajustes (recargos y bonificaciones), y la publica con una fecha de entrada en vigencia (RF 3). Una versión publicada no se modifica: los envíos admitidos con una versión anterior conservan su tarifa (sección 6.6). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*.<br>2. El operador tiene al menos una zona activa. |
| **Post-Condición** | **Éxito:**<br>• La nueva versión queda **Vigente** desde la fecha indicada; la anterior pasa a **Reemplazada**, con su fecha de fin de vigencia.<br>• Los envíos existentes no cambian su tarifa.<br>• Se publica `TarifarioPublicado` en memoria, que invalida la caché del cuadro tarifario del operador.<br>**Fracaso:** la versión queda como borrador, sin publicar. |
| **Actores** | **Principal:** administrador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El administrador elige **Cuadro tarifario**.<br>2. El sistema muestra la versión vigente, las anteriores y el borrador en curso, si lo hay.<br>3. El administrador elige **Nueva versión**.<br>4. El sistema crea un borrador con una copia de las reglas y ajustes de la versión vigente.<br>5. El administrador agrega, modifica o elimina reglas (zona, modalidad, peso desde y hasta, volumen desde y hasta, precio base) y ajustes (recargo o bonificación, porcentaje o monto fijo, modalidad).<br>6. El administrador guarda el borrador.<br>7. El administrador elige **Publicar** e indica desde cuándo rige: de inmediato o en una fecha futura.<br>8. El sistema valida la coherencia del borrador.<br>9. El sistema publica la versión y la muestra como vigente o programada. |
| **Flujos alternativos** | **A1 · Rangos superpuestos (paso 8).** Dos reglas de la misma zona y modalidad tienen rangos de peso o volumen superpuestos, de modo que un bulto tendría dos precios. El sistema indica las reglas en conflicto y no publica.<br><br>**A2 · Zonas o modalidades sin tarifa (paso 8).** Alguna zona activa no tiene reglas para alguna modalidad. El sistema advierte que esos envíos serán rechazados en el alta (CU-10, A3) y pide confirmación.<br><br>**A3 · Fecha de vigencia en el pasado (paso 7).** El sistema no lo permite.<br><br>**A4 · Ya existe un borrador (paso 3).** El sistema abre el borrador existente en lugar de crear otro: hay a lo sumo un borrador por operador.<br><br>**A5 · Otro administrador publicó antes (paso 9).** El sistema detecta el conflicto de concurrencia, informa que la versión vigente cambió y pide revisar el borrador. |
| **Requerimientos especiales** | 1. **Versionado inmutable** (sección 6.6): una versión publicada no se edita; cada envío guarda la versión con que fue admitido.<br>2. **Tarifa por bulto:** cada regla se aplica a un bulto según su peso y volumen; la tarifa del envío es la suma de sus bultos (CU-10).<br>3. La configuración se hace desde la interfaz, sin tocar código ni desplegar (RF 3). Es el escenario 5 de la defensa (sección 9.3).<br>4. La caché del cuadro tarifario se invalida al publicar (sección 6.7).<br>5. Control de concurrencia optimista sobre la versión vigente (sección 6.4). |

### **CU-06 · Configurar las reglas operativas**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-06 · Configurar las reglas operativas |
| **Descripción** | El administrador crea y publica una nueva versión de las reglas operativas del operador: cantidad máxima de intentos, plazo entre intentos, prueba de entrega exigida, catálogo de motivos de no entrega, política de devolución y plazos comprometidos por modalidad (RF 4). Funciona igual que el cuadro tarifario: borrador, publicación con fecha de vigencia y versiones inmutables. |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*. |
| **Post-Condición** | **Éxito:** la nueva versión queda **Vigente** desde la fecha indicada y la anterior pasa a **Reemplazada**. Los envíos existentes conservan la versión con que fueron admitidos. Se publica `ReglasPublicadas` en memoria, que invalida la caché.<br>**Fracaso:** la versión queda como borrador. |
| **Actores** | **Principal:** administrador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El administrador elige **Reglas operativas**.<br>2. El sistema muestra la versión vigente y el borrador en curso, si lo hay.<br>3. El administrador elige **Nueva versión** y el sistema crea un borrador copiando la vigente.<br>4. El administrador edita:<br>  • la cantidad máxima de intentos y el plazo mínimo entre intentos (horas);<br>  • las pruebas de entrega adicionales a la firma, que es siempre obligatoria: foto, y nombre y documento del receptor;<br>  • el catálogo de motivos de no entrega: código, descripción, si es reprogramable y si exige evidencia;<br>  • la política de devolución: si se devuelve al agotar los intentos y el plazo de devolución;<br>  • el plazo comprometido para cada modalidad (horas);<br>  • la anticipación mínima para que el destinatario reprograme (horas);<br>  • la cantidad máxima de paradas por ruta.<br>5. El administrador guarda y elige **Publicar**, indicando desde cuándo rige.<br>6. El sistema valida y publica la versión. |
| **Flujos alternativos** | **A1 · Valores inválidos (paso 6).** La cantidad de intentos es menor que 1, algún plazo no es mayor que cero, falta el plazo de una modalidad o hay códigos de motivo repetidos. El sistema lo indica.<br><br>**A2 · Catálogo de motivos vacío (paso 6).** El sistema no publica: el repartidor necesita al menos un motivo para registrar un intento fallido (CU-18).<br><br>**A3 · Fecha de vigencia en el pasado, borrador existente o conflicto de concurrencia.** Igual que en CU-05 (A3, A4 y A5). |
| **Requerimientos especiales** | 1. Versionado inmutable (sección 6.6).<br>2. La caché de reglas se invalida al publicar (sección 6.7).<br>3. Es el otro caso del escenario 5 de la defensa: modificar una regla operativa en caliente.<br>4. Incluye la anticipación mínima para que el destinatario reprograme (CU-61) y el máximo de paradas por ruta (CU-40). |

### **CU-07 · Gestionar la identidad visual**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-07 · Gestionar la identidad visual |
| **Descripción** | El administrador configura la marca del operador: logo, colores primario y secundario, y datos de contacto. Se reflejan en el Portal del comercio, en el Seguimiento público y en las comunicaciones al destinatario (RF 5). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*. |
| **Post-Condición** | **Éxito:** la identidad visual queda actualizada y se ve en la siguiente carga del Portal y del Seguimiento público, y en las siguientes notificaciones. Se invalida la caché de la identidad del operador.<br>**Fracaso:** se mantiene la identidad anterior. |
| **Actores** | **Principal:** administrador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El administrador elige **Identidad visual**.<br>2. El sistema muestra la configuración actual y una vista previa.<br>3. El administrador sube un logo, elige los colores e ingresa el correo y el teléfono de contacto.<br>4. El sistema actualiza la vista previa.<br>5. El administrador confirma.<br>6. El sistema guarda la identidad visual e invalida su caché. |
| **Flujos alternativos** | **A1 · Logo inválido (paso 3).** El archivo no es PNG, JPG o SVG, o supera 1 MB. El sistema lo rechaza.<br><br>**A2 · Color inválido (paso 3).** El valor no es un color hexadecimal. El sistema lo indica.<br><br>**A3 · Contraste insuficiente (paso 4).** El texto sobre el color primario no es legible. El sistema lo advierte y permite continuar. |
| **Requerimientos especiales** | 1. Los archivos (logos, fotos y firmas) se guardan en PostgreSQL, con un límite de 1 MB, detrás de una interfaz de Infrastructure (sección 3).<br>2. El Seguimiento público lee la identidad visual de la caché: es la página de más tráfico (sección 4 de la letra). |

### **CU-08 · Gestionar repartidores y vehículos**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-08 · Gestionar repartidores y vehículos |
| **Descripción** | El administrador da de alta, modifica, desactiva y consulta los repartidores y los vehículos del operador, con la capacidad de carga de cada vehículo (RF 6). Esas capacidades se usan para validar las rutas (CU-40). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*. |
| **Post-Condición** | **Éxito:** el repartidor o el vehículo queda registrado o actualizado, y disponible para armar rutas si está activo.<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** administrador, desde el Backoffice. |
| **Flujo de evento principal** | *Alta de un vehículo:*<br>1. El administrador elige **Vehículos** y luego **Nuevo vehículo**.<br>2. Ingresa matrícula, tipo, capacidad de peso (kg), capacidad de volumen (m³) y largo, ancho y alto de la caja de carga (cm).<br>3. El administrador confirma.<br>4. El sistema valida y guarda el vehículo activo.<br><br>*Alta de un repartidor:*<br>5. El administrador elige **Repartidores** y luego **Nuevo repartidor**.<br>6. Ingresa nombre y documento, y opcionalmente lo vincula a un usuario con perfil *Repartidor* (CU-02).<br>7. El sistema valida y guarda el repartidor activo. |
| **Flujos alternativos** | **A1 · Matrícula o documento repetido (pasos 4 o 7).** El sistema informa que ya existe en el operador.<br><br>**A2 · Capacidades inválidas (paso 4).** Alguna capacidad o dimensión no es mayor que cero. El sistema lo indica.<br><br>**A3 · Desactivar con una ruta en curso.** El vehículo o el repartidor están asignados a una ruta no finalizada. El sistema no permite desactivarlos hasta que la ruta termine o se reasigne. |
| **Requerimientos especiales** | 1. Unicidad de la matrícula y del documento por operador (índices compuestos con `OperadorId`).<br>2. El repartidor necesita un usuario vinculado para usar la PWA. |

### **CU-09 · Consultar y reintentar los mensajes fallidos**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-09 · Consultar y reintentar los mensajes fallidos |
| **Descripción** | El administrador consulta los mensajes de la cola que agotaron sus reintentos (por ejemplo, una notificación que no pudo enviarse o una reacción entre módulos que falló) y decide reintentarlos o descartarlos. Es la *visibilidad operativa* que exige la sección 6.8. |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Administrador*. |
| **Post-Condición** | **Éxito:** el mensaje reintentado vuelve a su cola original para procesarse; el descartado se elimina de la cola de fallidos, y queda registrado quién lo descartó y cuándo.<br>**Fracaso:** el mensaje permanece en la cola de fallidos. |
| **Actores** | **Principal:** administrador, desde el Backoffice.<br>**Secundario:** RabbitMQ. |
| **Flujo de evento principal** | 1. El administrador elige **Mensajes fallidos**.<br>2. El sistema lista los mensajes fallidos del operador con tipo de evento, fecha, cola, cantidad de intentos y último error.<br>3. El administrador abre un mensaje y ve su contenido y su historial de errores.<br>4. El administrador elige **Reintentar**.<br>5. El sistema vuelve a publicar el mensaje en su cola original, con el mismo `MessageId`.<br>6. El sistema lo quita de la lista de fallidos. |
| **Flujos alternativos** | **A1 · Descartar (paso 4).** El administrador elige **Descartar** e indica el motivo. El sistema elimina el mensaje y registra la acción.<br><br>**A2 · Reintento masivo (paso 2).** El administrador selecciona varios mensajes y los reintenta juntos.<br><br>**A3 · Vuelve a fallar.** El mensaje reintentado recorre de nuevo los reintentos y, si falla, vuelve a la lista. |
| **Requerimientos especiales** | 1. Cada administrador ve sólo los mensajes de su operador, a partir del `OperadorId` del sobre del mensaje (ADR-0003, sección 2.6).<br>2. Reintentar conserva el `MessageId`, de modo que la idempotencia del consumidor evite efectos duplicados.<br>3. **Ajuste pendiente del ADR-0003:** proponía consultar los fallidos en la consola de RabbitMQ; la letra exige que lo haga el administrador desde el sistema. |

## **2.2 Envíos y entregas**

### **CU-10 · Crear un envío individual**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-10 · Crear un envío individual |
| **Descripción** | El usuario del comercio da de alta un envío con sus datos de destino y uno o más bultos. El sistema determina la zona de cobertura, calcula la tarifa de cada bulto con el cuadro tarifario vigente del operador, asigna un número de envío y un token de seguimiento, y deja el envío en estado **Admitido** (T1). Es el caso base que reutilizan la importación desde archivo (CU-11) y la API pública (CU-81). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Usuario de comercio*.<br>2. El usuario seleccionó el operador de la sesión (CU-03), y la `RelacionComercial` entre su comercio y ese operador está **Activa**.<br>3. El operador tiene una versión del cuadro tarifario y una versión de las reglas operativas en estado **Vigente**.<br>4. El operador tiene al menos una zona de cobertura activa. |
| **Post-Condición** | **Éxito:**<br>1. Existe un envío en estado **Admitido**, asociado a la `RelacionComercial`, con su `OperadorId` y su `ComercioId`.<br>2. El envío tiene un número único dentro del operador y un token de seguimiento firmado.<br>3. El envío guarda la zona, la modalidad, el monto de la tarifa y las versiones del cuadro tarifario y de las reglas vigentes al momento del alta (RF 9, sección 6.6).<br>4. Cada bulto tiene su código de identificación, peso, dimensiones y tarifa (RF 8).<br>5. Existe el `EventoEnvio` inicial, con estado anterior nulo, estado nuevo *Admitido*, origen *PortalComercio*, el usuario como responsable y la fecha y hora (RF 12).<br>6. El evento `EnvioAdmitido` quedó registrado en el outbox de Envíos (ADR-0003).<br>Los puntos 1 a 6 se confirman en una única transacción.<br><br>**Fracaso:** no se persiste nada. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio.<br>**Secundarios:** módulo de Administración y configuración, que resuelve la zona y calcula la tarifa mediante `IAdministracionModuleApi`; Worker, que procesa el evento `EnvioAdmitido` (aviso al comercio, RF 28, y enlace de seguimiento al destinatario, RF 27). |
| **Flujo de evento principal** | 1. El usuario elige **Nuevo envío** en el Portal.<br>2. El sistema muestra el formulario de alta, con las modalidades de servicio del operador (Estándar y Urgente).<br>3. El usuario ingresa:<br>  • la referencia propia del comercio (opcional);<br>  • los datos del destinatario: nombre, teléfono y, opcionalmente, correo electrónico y documento;<br>  • la dirección: calle, número, localidad, departamento, código postal y, opcionalmente, una referencia;<br>  • la modalidad de servicio y, opcionalmente, la franja horaria preferida;<br>  • uno o más bultos, cada uno con peso (kg) y largo, ancho y alto (cm).<br>4. El usuario elige **Calcular tarifa**.<br>5. El sistema valida el formato de los datos.<br>6. El sistema determina la zona de cobertura a partir del código postal.<br>7. El sistema calcula la tarifa de **cada bulto** con la versión vigente del cuadro tarifario, según la zona, la modalidad y el peso y el volumen del bulto, aplicando los recargos y bonificaciones de la modalidad. La tarifa del envío es la suma de las tarifas de sus bultos.<br>8. El sistema muestra un resumen del envío con la zona, la tarifa de cada bulto y el total.<br>9. El usuario elige **Confirmar**.<br>10. El sistema verifica que la versión del cuadro tarifario usada en el paso 7 siga vigente.<br>11. El sistema asigna el número de envío, genera el código de cada bulto y el token de seguimiento, crea el envío en estado *Admitido*, registra el `EventoEnvio` inicial y el evento `EnvioAdmitido` en el outbox.<br>12. El sistema muestra la confirmación con el número de envío, la tarifa y el enlace de seguimiento, y ofrece imprimir las etiquetas (CU-15). |
| **Flujos alternativos** | **A1 · Datos inválidos (paso 5).** Falta un dato obligatorio, el peso o una dimensión no es mayor que cero, o el correo no tiene formato válido. El sistema indica el error en cada campo y no continúa.<br><br>**A2 · Dirección fuera de cobertura (paso 6).** El código postal no pertenece a ninguna zona activa del operador. El sistema informa que el operador no cubre esa dirección y no permite continuar (guarda de T1).<br><br>**A3 · Bulto sin tarifa aplicable (paso 7).** No hay una regla del cuadro tarifario vigente para la zona, la modalidad, el peso o el volumen de algún bulto. El sistema indica qué bulto no tiene tarifa y no permite continuar.<br><br>**A4 · Franja no ofrecida en la zona (paso 6).** La franja horaria elegida no se ofrece en la zona del envío. El sistema muestra las franjas disponibles y vuelve al paso 3.<br><br>**A5 · Referencia del comercio repetida (paso 11).** Ya existe un envío con la misma referencia en la misma `RelacionComercial`. El sistema informa el número del envío existente y no crea otro. En la importación (CU-11) y en la API (CU-81), en cambio, devuelve el envío existente sin error: es lo que hace idempotente la carga masiva (RF 7).<br><br>**A6 · Cambió la tarifa vigente (paso 10).** Entre el cálculo y la confirmación se publicó una nueva versión del cuadro tarifario. El sistema recalcula la tarifa, muestra el nuevo monto y vuelve al paso 8.<br><br>**A7 · Relación comercial suspendida (paso 1 o paso 11).** La `RelacionComercial` con el operador no está activa. El sistema informa que el comercio no puede operar con ese operador y no crea el envío.<br><br>**A8 · El usuario cancela (pasos 3 a 9).** El usuario abandona el formulario. No se persiste nada. |
| **Requerimientos especiales** | 1. **Aislamiento por inquilino:** el `OperadorId` y el `ComercioId` se toman de `ICurrentTenant`, nunca de datos enviados por el cliente (ADR-0002).<br>2. **Atomicidad:** envío, bultos, `EventoEnvio` y fila del outbox se guardan en la misma transacción (ADR-0003).<br>3. **Versionado de la configuración:** el envío conserva las versiones del cuadro tarifario y de las reglas con que fue admitido; un cambio posterior de configuración no lo afecta (sección 6.6).<br>4. **Número de envío:** único por operador, con índice único compuesto (`OperadorId`, `Numero`) (ADR-0002).<br>5. **Token de seguimiento:** firmado, no adivinable, e incluye el `OperadorId` para resolver el inquilino en el seguimiento público sin exponer identificadores internos (RF 25, sección 6.3).<br>6. **Único punto de cambio de estado:** el alta pasa por `Envio.Transicionar` (RF 11).<br>7. **Caché:** la zona y el cuadro tarifario vigente se leen de la caché distribuida, que se invalida al publicar una nueva versión (sección 6.7).<br>8. **Validación de formato:** con la validación incorporada de .NET 10; los errores se devuelven como ProblemDetails.<br>9. **Tarifa por bulto:** cada bulto se tarifa por separado y el monto del envío es la suma (decisión del equipo).<br>10. **Alcance mínimo para el 8/10:** un solo bulto, sin franja horaria y con el comercio y el operador de un `ICurrentTenant` provisorio (catálogo, sección 3). |

### **CU-11 · Importar envíos desde un archivo**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-11 · Importar envíos desde un archivo |
| **Descripción** | El usuario del comercio carga un archivo con muchos envíos a la vez. El sistema valida cada envío con las mismas reglas del alta individual (CU-10), muestra una vista previa y crea los válidos. La importación es **idempotente**: volver a importar el mismo archivo, completo o en parte, no genera envíos duplicados (RF 7). |
| **Pre-Condición** | Las mismas de CU-10. |
| **Post-Condición** | **Éxito:** cada envío válido del archivo que no existía queda creado como en CU-10. Los que ya existían (misma referencia del comercio) no se duplican. El usuario obtiene un informe con el resultado de cada fila.<br>**Fracaso de un envío:** ese envío no se crea; los demás no se ven afectados. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio.<br>**Secundarios:** los de CU-10. |
| **Flujo de evento principal** | 1. El usuario elige **Importar envíos** y descarga, si lo necesita, la plantilla del archivo.<br>2. El usuario selecciona el archivo CSV y lo sube.<br>3. El sistema valida la estructura del archivo: columnas, codificación y cantidad de filas.<br>4. El sistema agrupa las filas por referencia del comercio: cada fila es un bulto, y las filas con la misma referencia forman un envío.<br>5. El sistema valida cada envío como en CU-10 (pasos 5 a 7) y verifica si su referencia ya existe.<br>6. El sistema muestra una vista previa: envíos a crear con su tarifa, envíos con errores con el detalle, y envíos que ya existían.<br>7. El usuario confirma la importación.<br>8. El sistema crea cada envío válido nuevo, cada uno en su propia transacción, como en CU-10 (paso 11).<br>9. El sistema muestra el resultado y permite descargar el informe por fila. |
| **Flujos alternativos** | **A1 · Estructura inválida (paso 3).** Faltan columnas, la codificación no es UTF-8 o el archivo supera las 1.000 filas. El sistema rechaza el archivo completo e indica el motivo.<br><br>**A2 · Todos los envíos con errores (paso 6).** El sistema muestra los errores y no ofrece confirmar.<br><br>**A3 · Reimportación (paso 5).** El archivo ya se importó, total o parcialmente. Los envíos existentes se muestran como *ya existentes* y no se vuelven a crear; los faltantes se crean normalmente.<br><br>**A4 · Importación simultánea del mismo archivo (paso 8).** Dos importaciones intentan crear el mismo envío. El índice único sobre la referencia hace que una de ellas lo cree y la otra lo informe como *ya existente*.<br><br>**A5 · Datos inconsistentes en un mismo envío (paso 4).** Filas con la misma referencia tienen destinatarios o direcciones distintas. El sistema marca el envío con error.<br><br>**A6 · El usuario cancela (paso 7).** No se crea ningún envío. |
| **Requerimientos especiales** | 1. **Idempotencia:** la referencia del comercio es obligatoria en la importación, y hay un índice único sobre (`RelacionComercialId`, `ReferenciaComercio`).<br>2. Reutiliza el mismo caso de uso de CU-10; no duplica reglas.<br>3. Cada envío se crea en su propia transacción, para que un error no revierta los demás.<br>4. Límite inicial de 1.000 filas por archivo, procesadas en el momento. Si se necesitaran archivos más grandes, el procesamiento pasaría al Worker. |

### **CU-12 · Crear envíos por API**

Se detalla como **CU-81** en la sección 2.7 (API pública para comercios).

### **CU-13 · Consultar envíos**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-13 · Consultar envíos |
| **Descripción** | El usuario consulta un listado paginado de envíos y lo filtra. El personal del operador ve los envíos de todos los comercios de su operador; el usuario del comercio ve sólo los envíos de su comercio con el operador seleccionado en la sesión. Desde el listado se accede al detalle de un envío (CU-14). |
| **Pre-Condición** | 1. El usuario está autenticado.<br>2. El inquilino está resuelto: el operador, para el personal del operador; el operador y el comercio, para el usuario del comercio (ADR-0002). |
| **Post-Condición** | No modifica datos. El usuario obtiene la página de envíos que cumple con los filtros aplicados, restringida a los envíos que puede ver. |
| **Actores** | **Principales:** administrador, despachador y operario de depósito, desde el Backoffice; usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario abre el listado de envíos.<br>2. El sistema muestra la primera página de envíos, ordenados del más reciente al más antiguo, con: número, referencia del comercio, comercio (sólo en el Backoffice), nombre del destinatario, localidad, zona, modalidad, estado, fecha de alta y monto.<br>3. El usuario aplica uno o más filtros: estado, rango de fechas de alta, zona, modalidad, comercio (sólo en el Backoffice) o texto libre sobre número, referencia del comercio o nombre del destinatario.<br>4. El sistema aplica los filtros y muestra la primera página de resultados, con la cantidad total de envíos encontrados.<br>5. El usuario navega entre páginas.<br>6. El usuario selecciona un envío y el sistema muestra su detalle (CU-14). |
| **Flujos alternativos** | **A1 · Sin resultados (pasos 2 o 4).** No hay envíos que cumplan con los filtros. El sistema lo informa y ofrece limpiar los filtros.<br><br>**A2 · Filtros inválidos (paso 3).** La fecha *desde* es posterior a la fecha *hasta*. El sistema indica el error y no aplica los filtros.<br><br>**A3 · Página fuera de rango (paso 5).** Se pide una página mayor que la última. El sistema muestra la última página. |
| **Requerimientos especiales** | 1. **Aislamiento por inquilino:** el filtro por operador y, para el usuario del comercio, por comercio, lo aplican los Global Query Filters (ADR-0002). Un filtro por otro comercio enviado por el usuario del comercio no amplía lo que puede ver. Cubierto por las pruebas de aislamiento de la sección 6.5.<br>2. **Paginación en el servidor**, con un máximo de 100 envíos por página.<br>3. **Consulta de sólo lectura:** sin seguimiento de cambios (`AsNoTracking`) y proyectando sólo las columnas del listado.<br>4. **Índices** sobre (`OperadorId`, `CreadoEn`) y (`OperadorId`, `Estado`), para que el listado y los filtros más usados no recorran la tabla completa.<br>5. **Alcance mínimo para el 8/10:** el listado del Backoffice sin filtros, en el que aparece el envío creado desde el Portal (CU-10). |

### **CU-14 · Consultar el detalle de un envío**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-14 · Consultar el detalle de un envío |
| **Descripción** | El usuario consulta toda la información de un envío: datos del destinatario y la dirección, bultos, tarifa, estado actual, historial completo de eventos (RF 12), intentos de entrega con su evidencia, incidencias y devolución, si la hay. |
| **Pre-Condición** | Las mismas de CU-13. |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principales:** personal del operador, desde el Backoffice; usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario selecciona un envío desde el listado (CU-13) o busca su número.<br>2. El sistema muestra los datos del envío, sus bultos y la tarifa con la versión del cuadro tarifario aplicada.<br>3. El sistema muestra el historial de eventos en orden cronológico: estado anterior y nuevo, fecha y hora, origen, responsable y ubicación, si la tiene.<br>4. El sistema muestra los intentos de entrega con su resultado, motivo y evidencia (firma, foto, nombre y documento del receptor).<br>5. El sistema muestra las incidencias y la devolución, si existen.<br>6. El sistema muestra las acciones disponibles según el perfil del usuario y el estado del envío (por ejemplo, cancelar, reprogramar o declarar extravío). |
| **Flujos alternativos** | **A1 · El envío no existe o pertenece a otro inquilino (paso 1).** El sistema responde *envío no encontrado*, sin distinguir entre los dos casos, para no revelar la existencia de envíos ajenos. |
| **Requerimientos especiales** | 1. El historial es inmutable: se muestra tal como se registró (RF 12).<br>2. Las fotos y firmas se sirven con control de acceso: sólo las ve quien puede ver el envío.<br>3. Las acciones disponibles se calculan con la misma tabla de transiciones que usa `Envio.Transicionar`, para que la interfaz nunca ofrezca una acción inválida. |

### **CU-15 · Imprimir las etiquetas de un envío**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-15 · Imprimir las etiquetas de un envío |
| **Descripción** | El usuario del comercio genera las etiquetas de un envío, una por bulto, para pegarlas en los paquetes. Cada etiqueta tiene el código de barras que se escanea en el depósito (CU-30) y al cargar el vehículo (CU-51). |
| **Pre-Condición** | 1. Las mismas de CU-13.<br>2. El envío no está *Cancelado*. |
| **Post-Condición** | El usuario obtiene un PDF con una etiqueta por bulto. No modifica datos. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario elige **Imprimir etiquetas** desde la confirmación del alta (CU-10) o desde el detalle del envío (CU-14).<br>2. El sistema genera un PDF con una etiqueta por bulto, con: logo del operador, número de envío, código del bulto como código de barras, número de bulto sobre el total (por ejemplo, 2 de 3), nombre del destinatario, dirección, zona y modalidad.<br>3. El usuario descarga o imprime el PDF. |
| **Flujos alternativos** | **A1 · Envío cancelado (paso 1).** El sistema no ofrece la opción.<br><br>**A2 · Impresión de varios envíos.** Desde el listado (CU-13), el usuario selecciona varios envíos y el sistema genera un único PDF con todas sus etiquetas. |
| **Requerimientos especiales** | 1. El código de barras debe poder leerse con la cámara del teléfono (PWA) y con un lector de depósito. Formato propuesto: Code 128.<br>2. La etiqueta no incluye el teléfono ni el documento del destinatario.<br>3. **Pendiente de decisión:** la biblioteca para generar el PDF. |

### **CU-16 · Cancelar un envío**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-16 · Cancelar un envío |
| **Descripción** | El usuario del comercio anula un envío que todavía no fue recibido en el depósito del operador (T17). |
| **Pre-Condición** | 1. Las mismas de CU-13, con perfil *Usuario de comercio*.<br>2. El envío está en estado **Admitido**. |
| **Post-Condición** | **Éxito:** el envío queda **Cancelado** (estado terminal) y se publica `EnvioCancelado` por Outbox.<br>**Fracaso:** el envío no cambia. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio.<br>**Secundario:** Worker (aviso al comercio y al destinatario). |
| **Flujo de evento principal** | 1. El usuario elige **Cancelar** en el detalle del envío.<br>2. El sistema pide confirmación y, opcionalmente, un motivo.<br>3. El usuario confirma.<br>4. El sistema verifica que el envío siga *Admitido*.<br>5. El sistema cancela el envío (T17).<br>6. El sistema muestra el envío como cancelado. |
| **Flujos alternativos** | **A1 · El envío ya fue recibido en depósito (paso 4).** Mientras el usuario confirmaba, el operario lo recibió. El sistema informa que ya no puede cancelarse y ofrece iniciar la devolución (CU-20). |
| **Requerimientos especiales** | 1. Control de concurrencia optimista sobre el envío: la cancelación y la recepción en depósito (CU-30) no pueden aplicarse las dos (sección 6.4). |

### **CU-17 · Registrar una entrega**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-17 · Registrar una entrega |
| **Descripción** | El repartidor registra que entregó un envío, con la firma del receptor en pantalla, que es siempre obligatoria, y las pruebas adicionales que exijan las reglas del operador: foto, y nombre y documento del receptor. Cada prueba registra la posición y la hora del dispositivo (RF 20). El registro puede hacerse sin conexión y llegar al servidor al sincronizar (CU-52). |
| **Pre-Condición** | 1. El repartidor inició sesión en la PWA y tiene descargada la hoja de ruta (CU-50).<br>2. El envío está **EnTransito** en una ruta asignada al repartidor. |
| **Post-Condición** | **Éxito:** el envío queda **Entregado** (T6), con un `IntentoEntrega` exitoso y su `PruebaEntrega`. Se publica `EnvioEntregado` por Outbox; Planificación marca la parada como completada y Depósito y liquidaciones lo considera para la liquidación.<br>**Fracaso:** no se registra la entrega. |
| **Actores** | **Principal:** repartidor, desde la PWA.<br>**Secundarios:** módulo de Ejecución, que recibe la sincronización (CU-52); Worker. |
| **Flujo de evento principal** | 1. El repartidor selecciona la parada y elige **Entregado**.<br>2. La PWA muestra los datos que exige la versión de reglas del envío.<br>3. El repartidor registra la firma del receptor en pantalla y, si las reglas lo exigen, la foto y el nombre y documento del receptor.<br>4. La PWA obtiene la posición y la hora del dispositivo.<br>5. El repartidor confirma.<br>6. La PWA guarda la operación localmente, cifrada, y la marca como pendiente de sincronizar.<br>7. Al sincronizar (CU-52), el servidor valida la prueba y registra la entrega (T6). |
| **Flujos alternativos** | **A1 · Prueba incompleta (paso 5).** Falta alguno de los datos exigidos. La PWA no permite confirmar.<br><br>**A2 · Sin posición (paso 4).** El dispositivo no puede obtener la posición. La PWA reintenta y, si no lo logra, no permite confirmar: la posición es obligatoria (RF 20).<br><br>**A3 · Con conexión (paso 6).** La operación se sincroniza de inmediato.<br><br>**A4 · Conflicto al sincronizar (paso 7).** El estado del envío en el servidor ya no es *EnTransito* (por ejemplo, fue declarado extraviado). Se aplica la política de resolución de conflictos (CU-52). |
| **Requerimientos especiales** | 1. **Prueba de entrega:** la firma del receptor es siempre obligatoria. Las pruebas adicionales son las de la versión de reglas con que se admitió el envío (sección 6.6).<br>2. La hora del evento es la del dispositivo, no la de la sincronización.<br>3. La foto se comprime en el dispositivo antes de guardarla.<br>4. Los datos personales guardados en el dispositivo se cifran (sección 6.3).<br>5. Las fotos y firmas se guardan en PostgreSQL y se sirven por un endpoint que verifica los permisos (sección 3). |

### **CU-18 · Registrar un intento fallido**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-18 · Registrar un intento fallido |
| **Descripción** | El repartidor registra que no pudo entregar un envío, eligiendo un motivo del catálogo del operador y adjuntando evidencia si el motivo lo exige (RF 21). Según las reglas del operador, el sistema reprograma el envío o inicia su devolución. |
| **Pre-Condición** | Las mismas de CU-17. |
| **Post-Condición** | **Éxito:**<br>• Queda un `IntentoEntrega` fallido con su motivo y evidencia, y el envío pasa a **NoEntregado** (T7).<br>• En la misma operación, el sistema aplica las reglas: si se agotaron los intentos o el motivo no es reprogramable, inicia la devolución (T10, CU-20); si no, reprograma el envío para después del plazo entre intentos (T9, CU-19).<br>• Se publican los eventos de cada transición por Outbox.<br>**Fracaso:** no se registra el intento. |
| **Actores** | **Principal:** repartidor, desde la PWA.<br>**Secundarios:** módulo de Ejecución (CU-52); Worker (aviso al comercio y al destinatario). |
| **Flujo de evento principal** | 1. El repartidor selecciona la parada y elige **No entregado**.<br>2. La PWA muestra el catálogo de motivos de la versión de reglas del envío.<br>3. El repartidor elige un motivo y, si el motivo lo exige, saca una foto como evidencia. Puede agregar observaciones.<br>4. La PWA obtiene la posición y la hora del dispositivo.<br>5. El repartidor confirma.<br>6. La PWA guarda la operación localmente y la marca como pendiente.<br>7. Al sincronizar (CU-52), el servidor registra el intento (T7) e incrementa el contador de intentos.<br>8. El sistema aplica las reglas del operador y reprograma el envío (T9) o inicia su devolución (T10). |
| **Flujos alternativos** | **A1 · Falta la evidencia exigida (paso 5).** La PWA no permite confirmar.<br><br>**A2 · Intentos agotados o motivo no reprogramable (paso 8).** El sistema inicia la devolución (T10).<br><br>**A3 · Conflicto al sincronizar (paso 7).** Se aplica la política de resolución de conflictos (CU-52). |
| **Requerimientos especiales** | 1. Los motivos y el máximo de intentos salen de la versión de reglas del envío (sección 6.6).<br>2. La pareja (envío, número de intento) es única, lo que evita registrar dos veces el mismo intento al reenviar una sincronización.<br>3. Las transiciones T7 y T9 o T10 se aplican en la misma transacción. |

### **CU-19 · Reprogramar un envío no entregado**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-19 · Reprogramar un envío no entregado |
| **Descripción** | Un envío que tuvo un intento fallido recibe una nueva fecha y franja de entrega, y queda a la espera de ser asignado a una ruta (T9). Lo hace el sistema automáticamente al registrar el intento (CU-18), y el despachador puede hacerlo o cambiar la fecha manualmente. |
| **Pre-Condición** | 1. El envío está **NoEntregado** (o **Reprogramado**, para cambiar la fecha).<br>2. La cantidad de intentos es menor que el máximo de la versión de reglas del envío. |
| **Post-Condición** | **Éxito:** el envío queda **Reprogramado**, con la nueva fecha y franja.<br>**Fracaso:** el envío no cambia. |
| **Actores** | **Principales:** sistema (desde CU-18); despachador, desde el Backoffice.<br>**Secundario:** Worker (aviso al destinatario). |
| **Flujo de evento principal** | *Reprogramación manual:*<br>1. El despachador elige **Reprogramar** en el detalle del envío.<br>2. El sistema muestra las fechas y franjas disponibles para la zona, a partir del plazo mínimo entre intentos.<br>3. El despachador elige fecha y franja y confirma.<br>4. El sistema reprograma el envío (T9). |
| **Flujos alternativos** | **A1 · Fecha anterior al plazo entre intentos (paso 3).** El sistema no la permite.<br><br>**A2 · Intentos agotados (paso 1).** El sistema no ofrece reprogramar y ofrece iniciar la devolución (CU-20). |
| **Requerimientos especiales** | 1. El plazo entre intentos y el máximo de intentos salen de la versión de reglas del envío (RF 4, sección 6.6). |

### **CU-20 · Iniciar la devolución de un envío**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-20 · Iniciar la devolución de un envío |
| **Descripción** | Se decide devolver un envío al comercio. Puede decidirlo el sistema (intentos agotados, T10, o reprogramación vencida, T14), el despachador (T10, T12 o T14) o el comercio, si el envío está en el depósito (T12). Se crea la `Devolucion` del envío. |
| **Pre-Condición** | 1. El envío está **NoEntregado** (T10), **EnDeposito** (T12) o **Reprogramado** (T14). |
| **Post-Condición** | **Éxito:** el envío queda **EnDevolucion**, con una `Devolucion` en estado *Pendiente* y su motivo. Se publica `DevolucionIniciada` por Outbox.<br>**Fracaso:** el envío no cambia. |
| **Actores** | **Principales:** sistema (CU-18 y CU-70); despachador, desde el Backoffice; usuario del comercio, desde el Portal.<br>**Secundario:** Worker (aviso al comercio). |
| **Flujo de evento principal** | *Devolución pedida por el comercio o el despachador:*<br>1. El usuario elige **Iniciar devolución** en el detalle del envío.<br>2. El sistema pide el motivo.<br>3. El usuario confirma.<br>4. El sistema verifica que el estado permita la devolución, crea la `Devolucion` y cambia el estado del envío. |
| **Flujos alternativos** | **A1 · Estado que no permite devolución (paso 4).** Por ejemplo, el envío ya está en tránsito. El sistema lo informa.<br><br>**A2 · El comercio pide la devolución de un envío que no está en depósito.** El sistema sólo ofrece la opción al comercio cuando el envío está *EnDeposito*. |
| **Requerimientos especiales** | 1. `Devolucion` es un agregado propio, separado de `Envio` (modelo de dominio); se crea en la misma transacción que la transición. |

### **CU-21 · Declarar un envío como extraviado**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-21 · Declarar un envío como extraviado |
| **Descripción** | El administrador o el despachador declara perdido un envío que estaba en el depósito, en tránsito o en devolución (T16). Es un estado terminal. |
| **Pre-Condición** | 1. El usuario tiene perfil *Administrador* o *Despachador*.<br>2. El envío está **EnDeposito**, **EnTransito** o **EnDevolucion**. |
| **Post-Condición** | **Éxito:** el envío queda **Extraviado**, con el motivo; se crea una incidencia de tipo *Extravío*, y se publica `EnvioExtraviado` por Outbox. Si estaba en una ruta, Planificación marca la parada como fallida.<br>**Fracaso:** el envío no cambia. |
| **Actores** | **Principal:** administrador o despachador, desde el Backoffice.<br>**Secundario:** Worker (aviso al comercio y al destinatario). |
| **Flujo de evento principal** | 1. El usuario elige **Declarar extravío** en el detalle del envío.<br>2. El sistema pide el motivo, que es obligatorio.<br>3. El usuario confirma.<br>4. El sistema declara el envío extraviado (T16) y crea la incidencia. |
| **Flujos alternativos** | **A1 · Motivo vacío (paso 3).** El sistema no permite confirmar.<br><br>**A2 · Estado que no lo permite (paso 4).** El sistema lo informa. |
| **Requerimientos especiales** | 1. Es irreversible: el sistema pide una confirmación explícita. |

### **CU-22 · Gestionar incidencias**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-22 · Gestionar incidencias |
| **Descripción** | El despachador registra y resuelve problemas asociados a un envío: reclamos, daños en un bulto, extravíos u otros. Una incidencia no cambia el estado del envío. |
| **Pre-Condición** | 1. El usuario tiene perfil *Despachador* o *Administrador*. |
| **Post-Condición** | **Éxito:** la incidencia queda registrada o actualizada, con su historial.<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** despachador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El despachador elige **Nueva incidencia** en el detalle de un envío.<br>2. Elige el tipo (reclamo, extravío, daño en bulto u otro) e ingresa la descripción.<br>3. El sistema registra la incidencia en estado *Abierta*.<br>4. El despachador la pasa a *En proceso* mientras la atiende.<br>5. El despachador la resuelve indicando la resolución; el sistema la pasa a *Resuelta* con la fecha. |
| **Flujos alternativos** | **A1 · Listado de incidencias.** El despachador consulta las incidencias del operador, filtradas por estado, tipo y fecha.<br><br>**A2 · Resolver sin descripción de la resolución (paso 5).** El sistema no lo permite. |
| **Requerimientos especiales** | 1. `Incidencia` es un agregado propio (modelo de dominio).<br>2. Los tipos de incidencia son un catálogo fijo del sistema (modelo de dominio). |

## **2.3 Depósito y liquidaciones**

### **CU-30 · Recibir bultos en depósito**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-30 · Recibir bultos en depósito |
| **Descripción** | El operario de depósito escanea los bultos que llegan de los comercios, los verifica contra lo declarado (cantidad, peso y dimensiones) y registra las discrepancias. Cuando termina con un envío, el envío pasa a **EnDeposito** (T2) y queda disponible para planificar (RF 10). |
| **Pre-Condición** | 1. El usuario tiene perfil *Operario de depósito*.<br>2. Los bultos pertenecen a envíos del operador en estado **Admitido**. |
| **Post-Condición** | **Éxito:** hay una `RecepcionDeposito` por cada bulto recibido, *Conforme* o *Con discrepancia*. El envío queda **EnDeposito** y se publica `EnvioRecibidoEnDeposito` por Outbox.<br>**Fracaso:** no se registra la recepción del bulto. |
| **Actores** | **Principal:** operario de depósito, desde el Backoffice.<br>**Secundario:** módulo de Envíos, que aplica la transición mediante `IEnviosModuleApi`. |
| **Flujo de evento principal** | 1. El operario elige **Recepción**.<br>2. El operario escanea el código de un bulto.<br>3. El sistema muestra el envío al que pertenece, los bultos declarados y cuáles ya se recibieron.<br>4. Opcionalmente, el operario ingresa el peso y las dimensiones medidas.<br>5. El sistema compara con lo declarado y registra la recepción del bulto como *Conforme* o *Con discrepancia*, con el detalle.<br>6. El operario repite los pasos 2 a 5 con los demás bultos.<br>7. Cuando se recibieron todos los bultos del envío, el sistema lo pasa a *EnDeposito* (T2). |
| **Flujos alternativos** | **A1 · Código desconocido (paso 2).** El código no corresponde a ningún bulto del operador. El sistema informa *bulto desconocido*, sin indicar si existe en otro operador.<br><br>**A2 · Bulto ya recibido (paso 2).** El sistema avisa que ese bulto ya se escaneó.<br><br>**A3 · Envío cancelado (paso 3).** El sistema avisa que el envío fue cancelado y que el bulto debe separarse para devolverlo al comercio.<br><br>**A4 · Faltan bultos (paso 7).** El operario elige **Cerrar recepción** con bultos faltantes. El sistema registra la discrepancia y pasa el envío a *EnDeposito*: las discrepancias se registran, no bloquean (tabla de transiciones, T2).<br><br>**A5 · Diferencia de peso o dimensiones (paso 5).** Se registra como discrepancia y la recepción continúa. |
| **Requerimientos especiales** | 1. El escaneo funciona con un lector de códigos de barras (que actúa como teclado) o con la cámara.<br>2. La transición la aplica el módulo de Envíos; Depósito la solicita mediante `IEnviosModuleApi`.<br>3. La recepción (Depósito) y la transición (Envíos) se guardan en la misma transacción: los dos `DbContext` comparten la conexión a la base (sección 3). |

### **CU-31 · Confirmar la devolución al comercio**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-31 · Confirmar la devolución al comercio |
| **Descripción** | El operario de depósito registra que el comercio recibió un envío devuelto (T15), ya sea porque lo retiró del depósito o porque se lo entregaron. |
| **Pre-Condición** | 1. El usuario tiene perfil *Operario de depósito*.<br>2. El envío está **EnDevolucion**. |
| **Post-Condición** | **Éxito:** el envío queda **Devuelto** (estado terminal); su `Devolucion` queda *Cerrada*. Se publica `EnvioDevuelto` por Outbox.<br>**Fracaso:** el envío no cambia. |
| **Actores** | **Principal:** operario de depósito, desde el Backoffice.<br>**Secundario:** módulo de Envíos. |
| **Flujo de evento principal** | 1. El operario busca el envío por su número o escanea uno de sus bultos.<br>2. El sistema muestra la devolución con sus bultos.<br>3. El operario ingresa el nombre y el documento de quien recibe por el comercio.<br>4. El operario confirma.<br>5. El sistema registra la entrega, cierra la devolución y pasa el envío a *Devuelto* (T15). |
| **Flujos alternativos** | **A1 · El envío no está en devolución (paso 2).** El sistema lo informa.<br><br>**A2 · Faltan bultos (paso 4).** El sistema pide confirmar la entrega parcial y registra la discrepancia. |
| **Requerimientos especiales** | 1. Queda registrado quién recibió por el comercio, para dirimir reclamos (sección 3.1 de la letra). |

### **CU-32 · Generar la liquidación de un comercio**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-32 · Generar la liquidación de un comercio |
| **Descripción** | El sistema calcula lo que un comercio debe al operador por los envíos de un período: una línea por envío, con su tarifa, más los ajustes, y los totales (RF 30). La genera el cierre diario al terminar el período (CU-70) o el administrador a pedido. |
| **Pre-Condición** | 1. Existe un período cerrado, o el administrador indica uno. |
| **Post-Condición** | **Éxito:** existe una `Liquidacion` del comercio y el período, con sus `LineaLiquidacion` y sus totales, en estado *Borrador*; al emitirla pasa a *Emitida* y se publica `LiquidacionEmitida` por Outbox.<br>**Fracaso:** no se genera la liquidación. |
| **Actores** | **Principales:** sistema (CU-70); administrador, desde el Backoffice.<br>**Secundario:** Worker (aviso al comercio). |
| **Flujo de evento principal** | 1. El sistema (o el administrador) indica el comercio y el período.<br>2. El sistema obtiene los envíos del comercio que llegaron a un estado liquidable en el período, a partir de su propio registro de eventos.<br>3. El sistema crea una línea por envío con su tarifa.<br>4. El sistema calcula el total bruto, los ajustes y el total neto.<br>5. El sistema guarda la liquidación en *Borrador*.<br>6. El administrador revisa y la emite. |
| **Flujos alternativos** | **A1 · Sin envíos en el período (paso 2).** El sistema no genera la liquidación.<br><br>**A2 · Envío ya liquidado (paso 3).** Un envío aparece en una sola liquidación: si ya fue liquidado, se omite.<br><br>**A3 · Marcar como pagada.** El administrador registra el pago de una liquidación emitida, que pasa a *Pagada*. |
| **Requerimientos especiales** | 1. Depósito y liquidaciones no consulta las tablas de Envíos: mantiene su propio registro de los envíos liquidables, alimentado por los eventos `EnvioEntregado` y `EnvioDevuelto` por Outbox (addendum 3).<br>2. **Envíos liquidables:** se cobran los que llegaron a *Entregado* o *Devuelto* en el período, por la tarifa calculada al admitirlos, sin recargos por reintentos ni por devolución. Los cancelados y los extraviados no se cobran.<br>3. Generarla dos veces para el mismo comercio y período no duplica líneas. |

### **CU-33 · Consultar la cuenta corriente y las liquidaciones**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-33 · Consultar la cuenta corriente y las liquidaciones |
| **Descripción** | El usuario del comercio consulta sus liquidaciones con el operador: saldo, liquidaciones emitidas y pagadas, y el detalle por envío de cada una. |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Usuario de comercio* y seleccionó el operador (CU-03). |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario elige **Cuenta corriente**.<br>2. El sistema muestra el saldo pendiente y la lista de liquidaciones con período, estado y totales.<br>3. El usuario abre una liquidación.<br>4. El sistema muestra sus líneas: número de envío, fecha, concepto y monto.<br>5. El usuario puede descargar la liquidación. |
| **Flujos alternativos** | **A1 · Sin liquidaciones (paso 2).** El sistema lo informa. |
| **Requerimientos especiales** | 1. El comercio ve sólo sus liquidaciones con el operador seleccionado; las liquidaciones en *Borrador* no se muestran. |

## **2.4 Planificación de rutas**

### **CU-40 · Armar una hoja de ruta**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-40 · Armar una hoja de ruta |
| **Descripción** | El despachador crea una ruta para una fecha, le asigna un repartidor y un vehículo, y le agrega envíos como paradas. El sistema valida las restricciones: cantidad máxima de paradas, capacidad de peso y volumen del vehículo y compatibilidad con las franjas comprometidas (RF 13 y 14). Garantiza además que un envío no quede en dos rutas a la vez, aunque dos despachadores lo intenten al mismo tiempo (RF 15). |
| **Pre-Condición** | 1. El usuario tiene perfil *Despachador*.<br>2. Hay repartidores y vehículos activos (CU-08).<br>3. Hay envíos **EnDeposito** o **Reprogramado** para la fecha. |
| **Post-Condición** | **Éxito:** existe una ruta *Planificada* con sus paradas. Cada envío asignado queda **AsignadoARuta** (T3 o T13) y se publica `EnvioAsignadoARuta` por Outbox.<br>**Fracaso:** la ruta y los envíos no cambian. |
| **Actores** | **Principal:** despachador, desde el Backoffice.<br>**Secundario:** módulo de Envíos (transición mediante `IEnviosModuleApi`). |
| **Flujo de evento principal** | 1. El despachador elige **Nueva ruta** e indica fecha, repartidor y vehículo.<br>2. El sistema muestra los envíos disponibles para la fecha, filtrables por zona y franja, con su peso y volumen.<br>3. El despachador selecciona envíos y los agrega a la ruta.<br>4. El sistema valida las restricciones y muestra la carga acumulada contra la capacidad del vehículo y la cantidad de paradas contra el máximo.<br>5. El despachador confirma.<br>6. El sistema crea las paradas y asigna los envíos a la ruta. |
| **Flujos alternativos** | **A1 · Se excede la capacidad o el máximo de paradas (paso 4).** El sistema indica qué restricción se excede y no permite agregar el envío.<br><br>**A2 · Franja incompatible (paso 4).** La franja comprometida del envío no es compatible con la fecha de la ruta. El sistema lo indica.<br><br>**A3 · El envío ya fue asignado por otro despachador (paso 6).** El sistema informa qué envíos ya no están disponibles, los quita de la selección y pide confirmar el resto (RF 15).<br><br>**A4 · Repartidor o vehículo ocupado (paso 1).** Ya tiene una ruta no finalizada para esa fecha. El sistema lo informa.<br><br>**A5 · Agregar envíos a una ruta existente.** El despachador abre una ruta *Planificada* y repite los pasos 2 a 6. |
| **Requerimientos especiales** | 1. **RF 15:** índice único filtrado que impide que un envío tenga dos paradas activas, más control de concurrencia optimista sobre la ruta (modelo de dominio, notas de Planificación).<br>2. Las validaciones las orquesta un servicio de dominio que usa la ruta, el vehículo (`Vehiculo.admiteCarga`) y los envíos candidatos (modelo de dominio).<br>3. El máximo de paradas por ruta sale de la versión de reglas vigente (`maxParadasPorRuta`, CU-06).<br>4. La ruta (Planificación) y las transiciones (Envíos) se guardan en la misma transacción, como en CU-30. |

### **CU-41 · Quitar un envío de una ruta**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-41 · Quitar un envío de una ruta |
| **Descripción** | El despachador saca un envío de una ruta que todavía no fue despachada. El envío vuelve a quedar disponible en el depósito (T4). |
| **Pre-Condición** | 1. El usuario tiene perfil *Despachador*.<br>2. La ruta está *Planificada* (no despachada). |
| **Post-Condición** | **Éxito:** la parada se elimina, el envío queda **EnDeposito** y se publica `EnvioDesasignadoDeRuta` por Outbox. Las paradas restantes se renumeran.<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** despachador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El despachador abre la ruta y elige **Quitar** sobre una parada.<br>2. El sistema pide confirmación.<br>3. El sistema elimina la parada, desasigna el envío (T4) y renumera las paradas. |
| **Flujos alternativos** | **A1 · La ruta ya fue despachada (paso 1).** El sistema no ofrece la opción. |
| **Requerimientos especiales** | 1. Control de concurrencia optimista sobre la ruta: quitar un envío y despachar la ruta al mismo tiempo no pueden aplicarse los dos. |

### **CU-42 · Ordenar las paradas de una ruta**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-42 · Ordenar las paradas de una ruta |
| **Descripción** | El sistema ordena las paradas de una ruta con un criterio razonable y documentado, y el despachador puede ajustar el orden manualmente (RF 16). No se busca resolver el problema de optimización de rutas (aclaración de alcance de la letra). |
| **Pre-Condición** | 1. El usuario tiene perfil *Despachador*.<br>2. La ruta está *Planificada* y tiene paradas. |
| **Post-Condición** | **Éxito:** las paradas quedan numeradas en el nuevo orden.<br>**Fracaso:** se mantiene el orden anterior. |
| **Actores** | **Principal:** despachador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El despachador elige **Ordenar paradas**.<br>2. El sistema ordena las paradas por hora de inicio de la franja comprometida y, dentro de la misma franja, por zona y código postal.<br>3. El sistema muestra el orden propuesto.<br>4. El despachador lo acepta o mueve paradas manualmente.<br>5. El despachador confirma y el sistema guarda el orden. |
| **Flujos alternativos** | **A1 · Orden que incumple franjas (paso 4).** El orden manual deja una parada después del fin de su franja estimada. El sistema lo advierte y permite confirmar. |
| **Requerimientos especiales** | 1. El criterio es preliminar: queda pendiente del ADR de despacho (modelo de dominio, `CriterioOrden`).<br>2. Si el equipo aborda el opcional de optimización (sección 7.3), se compara contra este criterio. |

### **CU-43 · Despachar una ruta**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-43 · Despachar una ruta |
| **Descripción** | El despachador cierra la planificación de una ruta y la pone a disposición del repartidor, que puede descargarla en la PWA (RF 17). Una ruta despachada ya no se modifica. |
| **Pre-Condición** | 1. El usuario tiene perfil *Despachador*.<br>2. La ruta está *Planificada* y tiene al menos una parada ordenada. |
| **Post-Condición** | **Éxito:** la ruta queda *Despachada*, con la fecha y hora. Se publica `RutaDespachada` por Outbox para avisar al repartidor.<br>**Fracaso:** la ruta sigue *Planificada*. |
| **Actores** | **Principal:** despachador, desde el Backoffice.<br>**Secundario:** Worker (aviso al repartidor). |
| **Flujo de evento principal** | 1. El despachador elige **Despachar** en la ruta.<br>2. El sistema vuelve a validar las restricciones (CU-40).<br>3. El sistema pide confirmación.<br>4. El sistema marca la ruta como despachada. |
| **Flujos alternativos** | **A1 · Restricciones incumplidas (paso 2).** Por ejemplo, se desactivó el vehículo. El sistema indica el problema y no despacha.<br><br>**A2 · Ruta sin paradas (paso 1).** El sistema no ofrece despachar. |
| **Requerimientos especiales** | 1. Una ruta despachada no admite agregar ni quitar paradas; las reasignaciones urgentes quedan fuera del alcance mínimo. |

## **2.5 Ejecución de rutas**

### **CU-50 · Descargar la hoja de ruta**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-50 · Descargar la hoja de ruta |
| **Descripción** | El repartidor descarga en la PWA la ruta despachada del día, con todo lo necesario para trabajar sin conexión: paradas en orden, datos de cada envío y las reglas de prueba de entrega y motivos de no entrega que le corresponden (RF 18). |
| **Pre-Condición** | 1. El repartidor inició sesión en la PWA con conexión.<br>2. Tiene una ruta *Despachada* para el día. |
| **Post-Condición** | **Éxito:** la ruta queda guardada en el dispositivo, cifrada, y la PWA puede operar sin conexión.<br>**Fracaso:** la PWA conserva la última ruta descargada, si la hay. |
| **Actores** | **Principal:** repartidor, desde la PWA. |
| **Flujo de evento principal** | 1. El repartidor abre la PWA.<br>2. La PWA solicita la ruta del día.<br>3. El sistema devuelve la ruta con sus paradas en orden y, por cada envío: número, nombre y teléfono del destinatario, dirección, franja, códigos de sus bultos, prueba de entrega exigida y catálogo de motivos de su versión de reglas.<br>4. La PWA guarda la ruta localmente, cifrada.<br>5. La PWA muestra la ruta lista para la carga (CU-51). |
| **Flujos alternativos** | **A1 · Sin ruta despachada (paso 3).** El sistema informa que no hay ruta para el día.<br><br>**A2 · Sin conexión (paso 2).** La PWA muestra la última ruta descargada e indica cuándo se descargó.<br><br>**A3 · La ruta cambió después de descargarla.** En la siguiente sincronización (CU-52), la PWA recibe la versión actualizada. |
| **Requerimientos especiales** | 1. **Cifrado en reposo** de los datos personales guardados en el dispositivo (sección 6.3).<br>2. Se descarga sólo lo necesario para trabajar: minimización de datos personales.<br>3. La aplicación misma queda en caché por el service worker (addendum 5). |

### **CU-51 · Escanear la carga del vehículo**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-51 · Escanear la carga del vehículo |
| **Descripción** | Al cargar el vehículo, el repartidor escanea cada bulto. La PWA lo verifica contra la hoja de ruta y avisa ante faltantes o sobrantes (RF 19). Al confirmar la carga, los envíos cargados pasan a **EnTransito** (T5). |
| **Pre-Condición** | 1. El repartidor descargó la ruta (CU-50).<br>2. Los envíos de la ruta están **AsignadoARuta**. |
| **Post-Condición** | **Éxito:** queda un `EscaneoCarga` por bulto escaneado (*Esperado* o *Sobrante*). Los envíos con todos sus bultos cargados quedan **EnTransito** y se publica `EnvioEnTransito` por Outbox. Los faltantes y sobrantes se informan al despachador.<br>**Fracaso:** los envíos siguen *AsignadoARuta*. |
| **Actores** | **Principal:** repartidor, desde la PWA.<br>**Secundarios:** módulo de Ejecución (CU-52); despachador, que recibe el aviso en el tablero (CU-66). |
| **Flujo de evento principal** | 1. El repartidor elige **Cargar vehículo**.<br>2. El repartidor escanea un bulto con la cámara.<br>3. La PWA verifica que el bulto pertenezca a la ruta y lo marca como cargado.<br>4. El repartidor repite los pasos 2 y 3.<br>5. La PWA muestra los bultos que faltan.<br>6. El repartidor confirma la carga.<br>7. La PWA guarda la operación localmente y la sincroniza (CU-52); el servidor pasa a *EnTransito* los envíos cargados completos (T5). |
| **Flujos alternativos** | **A1 · Bulto sobrante (paso 3).** El bulto no pertenece a la ruta. La PWA avisa, lo registra como *Sobrante* y pide separarlo.<br><br>**A2 · Bulto repetido (paso 3).** La PWA avisa que ya se escaneó.<br><br>**A3 · Confirmar con faltantes (paso 6).** La PWA pide confirmación y lista los envíos incompletos: esos envíos no pasan a *EnTransito* y el despachador recibe el aviso para quitarlos de la ruta (CU-41) o resolverlos.<br><br>**A4 · Código ilegible (paso 2).** El repartidor puede ingresar el código a mano. |
| **Requerimientos especiales** | 1. Funciona sin conexión; la verificación se hace en el dispositivo contra la ruta descargada.<br>2. Los faltantes no se guardan: se calculan como diferencia entre los bultos esperados y los escaneados (modelo de dominio). |

### **CU-52 · Sincronizar las operaciones sin conexión**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-52 · Sincronizar las operaciones sin conexión |
| **Descripción** | Cuando hay conexión, la PWA envía al servidor las operaciones que el repartidor registró sin conexión (carga, entregas, intentos fallidos, posiciones e inicio de rendición). El módulo de Ejecución las procesa en orden, aplica la política de resolución de conflictos y, para las que cambian el estado de un envío, llama al módulo de Envíos (RF 24). |
| **Pre-Condición** | 1. La PWA tiene operaciones pendientes y recupera la conexión.<br>2. La sesión del repartidor es válida. |
| **Post-Condición** | **Éxito:** cada operación queda **aplicada** o **rechazada por conflicto**, con su motivo. La PWA actualiza su estado local e informa los conflictos al repartidor.<br>**Fracaso de la conexión:** las operaciones no confirmadas siguen pendientes y se reenvían. |
| **Actores** | **Principal:** repartidor (automático), desde la PWA.<br>**Secundarios:** módulo de Envíos (`IEnviosModuleApi`); despachador, que ve los conflictos en el tablero. |
| **Flujo de evento principal** | 1. La PWA detecta que hay conexión y operaciones pendientes.<br>2. La PWA envía un lote con las operaciones en el orden en que ocurrieron, cada una con su identificador único, su fecha y hora del dispositivo y un identificador de correlación.<br>3. El sistema procesa cada operación: verifica que no se haya aplicado antes, evalúa si es compatible con el estado actual en el servidor y la aplica.<br>4. El sistema responde con el resultado de cada operación.<br>5. La PWA marca como sincronizadas las aplicadas y muestra al repartidor las rechazadas, con el motivo.<br>6. La PWA descarga los cambios de su ruta, si los hay (CU-50, A3). |
| **Flujos alternativos** | **A1 · Se corta la conexión (pasos 2 a 4).** La PWA reenvía el lote más tarde. Las operaciones ya aplicadas no se repiten, porque el sistema reconoce su identificador.<br><br>**A2 · Conflicto (paso 3).** El estado en el servidor es incompatible con la operación: por ejemplo, el envío fue reasignado a otra ruta, cancelado o declarado extraviado mientras el repartidor estaba sin conexión. Se aplica la política de resolución de conflictos definida en su ADR.<br><br>**A3 · Sesión vencida (paso 2).** La PWA pide iniciar sesión de nuevo, conservando las operaciones pendientes. |
| **Requerimientos especiales** | 1. **ADR obligatorio pendiente:** política de resolución de conflictos de sincronización (sección 8.2).<br>2. **Idempotencia:** cada operación tiene un identificador único generado en el dispositivo.<br>3. **Correlación de extremo a extremo** desde la PWA hasta el Worker (sección 6.12).<br>4. La métrica de sincronizaciones pendientes se muestra en el tablero técnico (sección 6.12).<br>5. Es el escenario 2 y 3 de la defensa (sección 9.3). |

### **CU-53 · Reportar la posición del vehículo**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-53 · Reportar la posición del vehículo |
| **Descripción** | Durante la jornada, la PWA envía periódicamente la posición del vehículo, que se ve en el tablero en vivo (RF 23). |
| **Pre-Condición** | 1. El repartidor tiene una ruta en curso.<br>2. El repartidor autorizó el acceso a la ubicación. |
| **Post-Condición** | Se guarda una `PosicionVehiculo` por cada reporte, y el tablero se actualiza. |
| **Actores** | **Principal:** repartidor (automático), desde la PWA.<br>**Secundario:** despachador, que ve la posición en el tablero (CU-66). |
| **Flujo de evento principal** | 1. Con la ruta en curso, la PWA obtiene la posición cada 30 segundos.<br>2. La PWA la envía al servidor.<br>3. El sistema guarda la posición y publica `PosicionReportada` en memoria.<br>4. El tablero muestra la nueva posición en tiempo real. |
| **Flujos alternativos** | **A1 · Sin conexión (paso 2).** La PWA guarda las posiciones y las envía al sincronizar (CU-52).<br><br>**A2 · Sin permiso de ubicación (paso 1).** La PWA avisa al repartidor que el seguimiento está desactivado. |
| **Requerimientos especiales** | 1. **Limitación de la PWA:** a diferencia de una aplicación nativa, una PWA no puede reportar la posición con la pantalla apagada. El requisito de seguimiento en segundo plano es de la aplicación MAUI (sección 6.11), que el equipo reemplaza por una PWA según la exención para equipos de 3.<br>2. Política de retención de posiciones, por su volumen (modelo de dominio). |

### **CU-54 · Iniciar la rendición**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-54 · Iniciar la rendición |
| **Descripción** | Al terminar la jornada, el repartidor declara en la PWA qué envíos y bultos vuelve a entregar al depósito: los no entregados y los que no llegó a intentar (RF 22). La rendición queda pendiente hasta que el operario la confirme (CU-55). |
| **Pre-Condición** | 1. El repartidor tiene una ruta en curso. |
| **Post-Condición** | **Éxito:** existe una `Rendicion` de la ruta con sus líneas declaradas, pendiente de confirmación.<br>**Fracaso:** no se registra la rendición. |
| **Actores** | **Principal:** repartidor, desde la PWA. |
| **Flujo de evento principal** | 1. El repartidor elige **Rendir ruta**.<br>2. La PWA muestra las paradas no entregadas, con su estado: intento fallido o no intentada.<br>3. El repartidor confirma qué bultos devuelve.<br>4. La PWA registra la rendición y la sincroniza (CU-52). |
| **Flujos alternativos** | **A1 · Sin conexión (paso 4).** La rendición se sincroniza cuando haya conexión.<br><br>**A2 · Un bulto no vuelve (paso 3).** El repartidor lo indica con una observación; el operario lo verá como faltante (CU-55). |
| **Requerimientos especiales** | 1. Lo declarado por el repartidor se compara con lo recibido por el operario: control cruzado entre los dos (catálogo, sección 4). |

### **CU-55 · Confirmar la rendición**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-55 · Confirmar la rendición |
| **Descripción** | El operario de depósito escanea los bultos que efectivamente devolvió el repartidor y los compara con lo declarado (CU-54). Los envíos que no llegaron a intentarse vuelven a **EnDeposito** (T8), y la ruta se finaliza. |
| **Pre-Condición** | 1. El usuario tiene perfil *Operario de depósito*.<br>2. La ruta tiene una rendición iniciada. |
| **Post-Condición** | **Éxito:** cada línea de la rendición queda con su estado final (*Devuelto a depósito*, *Entregado en calle* o *Extraviado*). Los envíos no intentados quedan **EnDeposito** y se publica `EnvioReintegradoADeposito` por Outbox. La rendición queda cerrada y la ruta, *Finalizada*.<br>**Fracaso:** la rendición sigue abierta. |
| **Actores** | **Principal:** operario de depósito, desde el Backoffice.<br>**Secundario:** módulo de Envíos. |
| **Flujo de evento principal** | 1. El operario elige la rendición pendiente de la ruta.<br>2. El sistema muestra lo declarado por el repartidor.<br>3. El operario escanea los bultos recibidos.<br>4. El sistema compara lo recibido con lo declarado.<br>5. El operario confirma.<br>6. El sistema cierra la rendición, reintegra al depósito los envíos no intentados (T8) y finaliza la ruta. |
| **Flujos alternativos** | **A1 · Faltan bultos declarados (paso 4).** El sistema los marca como faltantes y crea una incidencia (CU-22); el despachador decide si declara el extravío (CU-21).<br><br>**A2 · Llegan bultos no declarados (paso 4).** El sistema los registra como sobrantes y pide revisar. |
| **Requerimientos especiales** | 1. Los envíos con intento fallido ya fueron reprogramados o enviados a devolución por el sistema (CU-18); la rendición sólo registra su regreso físico al depósito. |

## **2.6 Seguimiento y notificaciones**

### **CU-60 · Consultar el seguimiento público de un envío**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-60 · Consultar el seguimiento público de un envío |
| **Descripción** | El destinatario abre el enlace que recibió y ve el estado actual de su envío, la ventana horaria estimada y el historial de eventos, con la marca del operador, sin autenticarse y sin que se expongan datos personales de terceros ni identificadores internos (RF 25). |
| **Pre-Condición** | 1. El destinatario tiene el enlace de seguimiento del envío. |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principal:** destinatario, desde el Seguimiento público. |
| **Flujo de evento principal** | 1. El destinatario abre el enlace `/seguimiento/{token}`.<br>2. El sistema valida la firma del token y obtiene de él el operador (ADR-0002).<br>3. El sistema muestra, con la identidad visual del operador:<br>  • el estado actual del envío;<br>  • la ventana horaria estimada de entrega;<br>  • el historial de eventos, con estado, fecha y hora, sin datos del personal del operador;<br>  • el nombre de pila del destinatario y la localidad, sin dirección completa ni teléfono.<br>4. Si el estado lo permite, el sistema ofrece **Reprogramar** (CU-61). |
| **Flujos alternativos** | **A1 · Token inválido o alterado (paso 2).** El sistema muestra *enlace no válido*, sin dar más detalles.<br><br>**A2 · El envío no existe (paso 2).** El sistema muestra el mismo mensaje que en A1, para no permitir enumerar envíos.<br><br>**A3 · Demasiadas consultas (paso 1).** Se superó el límite de consultas por dirección IP. El sistema responde con un error de límite de tasa. |
| **Requerimientos especiales** | 1. **Sin autenticación:** el token firmado es la única credencial y sólo da acceso a su envío.<br>2. **Protección contra la enumeración** de envíos y **limitación de tasa** (sección 6.3).<br>3. **Caché** de corta duración, invalidada ante cada cambio de estado (sección 6.7). Es el componente de más tráfico.<br>4. Los datos salen de la tabla de lectura del módulo de Seguimiento, alimentada por los eventos de Envíos (catálogo, sección 4).<br>5. **Ventana estimada**, sin estimar tiempos de viaje: con fecha y franja asignadas, esa fecha y franja; sin fecha, *antes de* la fecha que surge del plazo comprometido de su modalidad; en tránsito, la franja del día y cuántas entregas quedan antes de la suya. |

### **CU-61 · Solicitar la reprogramación de un envío**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-61 · Solicitar la reprogramación de un envío |
| **Descripción** | Desde el seguimiento público, el destinatario elige una nueva fecha y franja de entrega, dentro de las reglas del operador (RF 26). Puede hacerlo también antes del primer intento. |
| **Pre-Condición** | 1. El destinatario abrió el seguimiento del envío (CU-60).<br>2. El envío está **Admitido**, **EnDeposito**, **AsignadoARuta** con la ruta no despachada, o **NoEntregado** (catálogo, sección 2.6). |
| **Post-Condición** | **Éxito:**<br>• Si el envío está *Admitido*, se registra la nueva fecha y franja, sin cambio de estado.<br>• En los demás casos, el envío queda **Reprogramado** (T18, T19 o T11); si estaba en una ruta, sale de ella.<br>• El `EventoEnvio` registra como origen el seguimiento público. Se publica `EnvioReprogramado` por Outbox.<br>**Fracaso:** el envío no cambia. |
| **Actores** | **Principal:** destinatario, desde el Seguimiento público.<br>**Secundarios:** módulo de Envíos, que aplica la transición; Worker (aviso al comercio). |
| **Flujo de evento principal** | 1. El destinatario elige **Reprogramar**.<br>2. El sistema muestra las fechas y franjas disponibles para la zona del envío, según las reglas del operador.<br>3. El destinatario elige fecha y franja y confirma.<br>4. El sistema verifica que el estado y las reglas lo permitan.<br>5. El sistema reprograma el envío.<br>6. El sistema muestra la nueva ventana de entrega. |
| **Flujos alternativos** | **A1 · El envío ya salió a reparto (paso 4).** El envío pasó a *EnTransito* o su ruta fue despachada. El sistema informa que ya no puede reprogramarse.<br><br>**A2 · Fuera de plazo (paso 4).** No se respeta la anticipación mínima o se superó el máximo de intentos. El sistema lo informa.<br><br>**A3 · Franja sin disponibilidad (paso 3).** El sistema muestra otras opciones. |
| **Requerimientos especiales** | 1. T18 y T19 están propuestas y pendientes de confirmación del responsable de la máquina de estados.<br>2. La anticipación mínima sale de la versión de reglas del envío (`anticipacionReprogramacionHoras`, CU-06).<br>3. Limitación de tasa también sobre esta operación (sección 6.3). |

### **CU-62 · Notificar al destinatario**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-62 · Notificar al destinatario |
| **Descripción** | El Worker envía al destinatario una notificación ante los cambios de estado relevantes de su envío, con la identidad visual del operador y el enlace de seguimiento (RF 27). |
| **Pre-Condición** | 1. Se publicó un evento de cambio de estado relevante.<br>2. El envío tiene un medio de contacto del destinatario. |
| **Post-Condición** | **Éxito:** existe una `Notificacion` en estado *Enviada*.<br>**Fracaso:** la notificación queda *Fallida*, después de los reintentos, y el mensaje pasa a la cola de fallidos (CU-09). |
| **Actores** | **Principal:** sistema (Worker).<br>**Secundario:** proveedor de correo. |
| **Flujo de evento principal** | 1. El Worker recibe un evento de cambio de estado.<br>2. Verifica que no lo haya procesado antes.<br>3. Determina si el cambio es relevante para el destinatario: admitido (con el enlace de seguimiento), en tránsito, no entregado, reprogramado, entregado y en devolución.<br>4. Arma el mensaje con la identidad visual del operador y el enlace.<br>5. Envía el mensaje y registra la notificación como *Enviada*. |
| **Flujos alternativos** | **A1 · Cambio no relevante (paso 3).** No se envía nada.<br><br>**A2 · Sin correo del destinatario (paso 4).** Se registra la notificación como no enviable, sin reintentos.<br><br>**A3 · Falla el proveedor (paso 5).** Se reintenta con espera creciente; agotados los reintentos, pasa a la cola de fallidos. |
| **Requerimientos especiales** | 1. **Idempotencia:** el mismo evento procesado dos veces no envía dos correos (ADR-0003).<br>2. Envío por SMTP: Mailpit en desarrollo y un proveedor SMTP con plan gratuito en producción (sección 3).<br>3. El Worker ejecuta el código del módulo Seguimiento y guarda las notificaciones en sus tablas (sección 3). |

### **CU-63 · Configurar las suscripciones de avisos**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-63 · Configurar las suscripciones de avisos |
| **Descripción** | El usuario del comercio configura a qué dirección de su sistema se envían los avisos automáticos y para qué tipos de evento (RF 28, sección 6.9). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Usuario de comercio* y seleccionó el operador (CU-03). |
| **Post-Condición** | **Éxito:** existe una `SuscripcionAviso` activa con su dirección, sus tipos de evento y su secreto de firma.<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario elige **Avisos** y luego **Nueva suscripción**.<br>2. Ingresa la dirección HTTPS de su sistema y elige los tipos de evento: cambio de estado, entrega fallida o devolución.<br>3. El usuario confirma.<br>4. El sistema crea la suscripción y genera el secreto de firma.<br>5. El sistema muestra el secreto una única vez, con las instrucciones para verificar la firma.<br>6. El usuario puede elegir **Enviar prueba**; el sistema envía un aviso de prueba y muestra la respuesta. |
| **Flujos alternativos** | **A1 · Dirección inválida (paso 3).** No es una URL HTTPS válida. El sistema lo indica.<br><br>**A2 · La prueba falla (paso 6).** El sistema muestra el código de respuesta o el error.<br><br>**A3 · Desactivar o regenerar el secreto.** El usuario desactiva una suscripción o genera un nuevo secreto, que invalida el anterior. |
| **Requerimientos especiales** | 1. El secreto se guarda cifrado y no vuelve a mostrarse.<br>2. Firma HMAC-SHA256 del cuerpo del aviso y de su fecha, para que el receptor verifique el origen y descarte reenvíos antiguos (sección 6.9). |

### **CU-64 · Entregar los avisos a los comercios**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-64 · Entregar los avisos a los comercios |
| **Descripción** | Ante cada cambio de estado, el Worker envía un aviso firmado por HTTP a cada suscripción activa del comercio que corresponda, con reintentos con espera creciente y cola de fallidos (RF 28, sección 6.9). |
| **Pre-Condición** | 1. Se publicó un evento de cambio de estado.<br>2. El comercio del envío tiene suscripciones activas para ese tipo de evento. |
| **Post-Condición** | **Éxito:** existe una `EntregaAviso` *Exitosa* por suscripción.<br>**Fracaso:** después de los reintentos, la entrega queda *En cola de fallidos* y el comercio puede reenviarla (CU-65). |
| **Actores** | **Principal:** sistema (Worker).<br>**Secundario:** sistema del comercio (receptor del aviso). |
| **Flujo de evento principal** | 1. El Worker recibe un evento de cambio de estado.<br>2. Obtiene las suscripciones activas del comercio para ese tipo de evento.<br>3. Por cada una, arma el aviso con el número de envío, el estado, la fecha y el identificador del mensaje, y lo firma.<br>4. Envía el aviso por HTTP.<br>5. El receptor responde con un código 2xx.<br>6. El Worker registra la entrega como *Exitosa*. |
| **Flujos alternativos** | **A1 · El receptor falla o no responde (paso 5).** Respuesta distinta de 2xx o sin respuesta en 10 segundos. El Worker reintenta con espera creciente.<br><br>**A2 · Se agotan los reintentos (paso 5).** La entrega pasa a *En cola de fallidos*.<br><br>**A3 · El receptor vuelve a estar disponible.** Los avisos pendientes de reintento se entregan; los que ya estaban en la cola de fallidos se recuperan reenviándolos (CU-65). |
| **Requerimientos especiales** | 1. Cada aviso lleva un identificador único, para que el receptor pueda descartar duplicados.<br>2. El orden de llegada no está garantizado; cada aviso incluye la fecha del evento.<br>3. **Receptor de prueba** provisto por el equipo, con escenarios de falla (sección 6.9). Es el escenario 6 de la defensa. |

### **CU-65 · Consultar el historial de avisos y reenviar los fallidos**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-65 · Consultar el historial de avisos y reenviar los fallidos |
| **Descripción** | El usuario del comercio consulta el historial de avisos enviados a su sistema, con sus intentos y respuestas, y reenvía los que fallaron (sección 6.9). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Usuario de comercio* y seleccionó el operador (CU-03). |
| **Post-Condición** | **Éxito:** el aviso reenviado se vuelve a entregar (CU-64) con el mismo identificador.<br>**Fracaso:** el aviso sigue fallido. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario elige **Historial de avisos**.<br>2. El sistema lista las entregas con fecha, tipo de evento, número de envío, estado y cantidad de intentos, filtrables por estado y fecha.<br>3. El usuario abre una entrega y ve cada intento con su código de respuesta.<br>4. El usuario elige **Reenviar** sobre una entrega fallida.<br>5. El sistema la vuelve a encolar. |
| **Flujos alternativos** | **A1 · Reenvío masivo (paso 4).** El usuario selecciona varias entregas fallidas y las reenvía juntas.<br><br>**A2 · Suscripción desactivada (paso 4).** El sistema informa que la suscripción está inactiva. |
| **Requerimientos especiales** | 1. El comercio ve sólo sus avisos con el operador seleccionado. |

### **CU-66 · Ver el tablero de operación en vivo**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-66 · Ver el tablero de operación en vivo |
| **Descripción** | El despachador ve en tiempo real la posición de la flota, el avance de las rutas y los envíos en riesgo de no llegar dentro de su ventana comprometida, además de alertas operativas (RF 29). |
| **Pre-Condición** | 1. El usuario tiene perfil *Despachador* o *Administrador*. |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principal:** despachador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El despachador abre el **Tablero**.<br>2. El sistema muestra el mapa con la última posición de cada vehículo en ruta, el avance de cada ruta y la lista de envíos en riesgo.<br>3. El tablero se actualiza en tiempo real con las nuevas posiciones, entregas, intentos fallidos y alertas (faltantes o sobrantes en la carga, conflictos de sincronización).<br>4. El despachador selecciona un envío en riesgo y abre su detalle (CU-14). |
| **Flujos alternativos** | **A1 · Se pierde la conexión en tiempo real (paso 3).** El tablero se reconecta automáticamente y recarga el estado completo. |
| **Requerimientos especiales** | 1. **SignalR** con la misma autenticación y autorización que el resto, y aislamiento por inquilino: un grupo por operador (sección 6.10).<br>2. Los datos salen de la tabla de lectura del módulo de Seguimiento (catálogo, sección 4).<br>3. **Envío en riesgo:** no entregado y con menos de un umbral de tiempo hasta el fin de su ventana. Umbral fijo de 60 minutos, no configurable. |

## **2.7 API pública para comercios**

### **CU-80 · Gestionar las claves de API**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-80 · Gestionar las claves de API |
| **Descripción** | El usuario del comercio genera, lista y revoca las claves con las que su sistema accede a la API pública. Hay claves de producción y claves de prueba; las de prueba operan en el ambiente de pruebas (sección 7.4). |
| **Pre-Condición** | 1. El usuario está autenticado con el perfil *Usuario de comercio* y seleccionó el operador (CU-03). |
| **Post-Condición** | **Éxito:**<br>• Alta: existe una clave activa asociada a la `RelacionComercial`, de producción o de prueba.<br>• Revocación: la clave deja de aceptarse de inmediato.<br>**Fracaso:** no hay cambios. |
| **Actores** | **Principal:** usuario del comercio, desde el Portal del comercio. |
| **Flujo de evento principal** | 1. El usuario elige **Claves de API**.<br>2. El sistema lista las claves con nombre, tipo, primeros caracteres, fecha de creación y último uso.<br>3. El usuario elige **Nueva clave**, le da un nombre y elige el tipo: producción o prueba.<br>4. El sistema genera la clave y la muestra una única vez.<br>5. El usuario la copia. |
| **Flujos alternativos** | **A1 · Revocar.** El usuario revoca una clave; el sistema pide confirmación.<br><br>**A2 · Clave perdida.** No se puede recuperar: el usuario genera una nueva y revoca la anterior. |
| **Requerimientos especiales** | 1. La clave se guarda como hash; nunca en claro.<br>2. La clave identifica a la `RelacionComercial`, de modo que la API resuelve el operador y el comercio sin datos del cliente (ADR-0002).<br>3. **Ambiente de pruebas:** los envíos creados con una clave de prueba quedan marcados con `EsPrueba` (sección 3). |

### **CU-81 · Crear envíos por API**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-81 · Crear envíos por API |
| **Descripción** | El sistema del comercio crea envíos mediante la API pública, con las mismas reglas del alta individual (CU-10). Implementa CU-12 (RF 7). |
| **Pre-Condición** | 1. El sistema del comercio tiene una clave de API activa (CU-80).<br>2. Las pre-condiciones 3 y 4 de CU-10. |
| **Post-Condición** | Las de CU-10, con origen *API* en el `EventoEnvio`. |
| **Actores** | **Principal:** sistema del comercio.<br>**Secundarios:** los de CU-10. |
| **Flujo de evento principal** | 1. El sistema del comercio envía `POST /api/v1/envios` con la clave en la cabecera y los datos del envío, incluida su referencia.<br>2. El sistema valida la clave y resuelve el operador y el comercio.<br>3. El sistema aplica los pasos 5 a 7 y 11 de CU-10.<br>4. El sistema responde **201** con el número de envío, la tarifa, el estado y el enlace de seguimiento. |
| **Flujos alternativos** | **A1 · Clave inválida o revocada (paso 2).** Responde **401**.<br><br>**A2 · Límite de tasa superado (paso 2).** Responde **429**, con el tiempo de espera en la cabecera `Retry-After`.<br><br>**A3 · Datos inválidos (paso 3).** Responde **400** con el detalle en formato ProblemDetails.<br><br>**A4 · Fuera de cobertura o sin tarifa (paso 3).** Responde **422** con el motivo.<br><br>**A5 · Referencia ya existente (paso 3).** Responde **200** con el envío existente, sin crear otro: reintentar la misma solicitud es seguro (RF 7). |
| **Requerimientos especiales** | 1. La referencia del comercio es obligatoria en la API: sostiene la idempotencia.<br>2. La API está versionada en la ruta (`/api/v1/`).<br>3. Limitación de tasa diferenciada por tipo de clave (sección 7.4).<br>4. Reutiliza el caso de uso de CU-10; no duplica reglas. |

### **CU-82 · Consultar envíos por API**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-82 · Consultar envíos por API |
| **Descripción** | El sistema del comercio consulta sus envíos y el historial de cada uno mediante la API pública. |
| **Pre-Condición** | 1. El sistema del comercio tiene una clave de API activa (CU-80). |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principal:** sistema del comercio. |
| **Flujo de evento principal** | 1. El sistema del comercio envía `GET /api/v1/envios` con filtros (estado, fechas, referencia) y paginación, o `GET /api/v1/envios/{numero}` para un envío.<br>2. El sistema valida la clave.<br>3. El sistema responde con la página de envíos, o con el envío y su historial de eventos. |
| **Flujos alternativos** | **A1 · Envío inexistente o de otro comercio (paso 3).** Responde **404**.<br><br>**A2 · Clave inválida o límite superado (paso 2).** Igual que en CU-81 (A1 y A2). |
| **Requerimientos especiales** | 1. Mismas reglas de aislamiento que CU-13: la clave determina qué envíos se ven.<br>2. No expone identificadores internos: los envíos se identifican por su número. |

### **CU-83 · Consultar la documentación de la API**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-83 · Consultar la documentación de la API |
| **Descripción** | El desarrollador del comercio consulta la documentación navegable de la API pública: endpoints, esquemas, autenticación, códigos de error, límites de tasa y ejemplos, y puede probar las operaciones con una clave de prueba (sección 7.4). |
| **Pre-Condición** | Ninguna. |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principal:** desarrollador del sistema del comercio. |
| **Flujo de evento principal** | 1. El desarrollador abre la documentación de la API.<br>2. El sistema muestra la documentación de la versión vigente, generada a partir de la especificación OpenAPI.<br>3. El desarrollador ingresa una clave de prueba y ejecuta operaciones desde la documentación. |
| **Flujos alternativos** | **A1 · Clave de producción en la documentación (paso 3).** El sistema la rechaza: desde la documentación sólo se aceptan claves de prueba. |
| **Requerimientos especiales** | 1. La especificación OpenAPI se genera desde el código con el soporte incorporado de .NET 10, de modo que no se desactualiza.<br>2. Sólo incluye los endpoints públicos (`/api/v1/`), no los internos de las aplicaciones propias. |

## **2.8 Procesos del Worker y reportes**

### **CU-70 · Ejecutar el cierre diario**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-70 · Ejecutar el cierre diario |
| **Descripción** | Al final de cada día, el sistema cierra la operación: inicia la devolución de los envíos reprogramados cuyo plazo venció sin reasignarse (T14), alerta sobre las rutas del día que no se finalizaron, genera las liquidaciones de los períodos que terminan (CU-32) y recalcula los indicadores (CU-71). |
| **Pre-Condición** | 1. Llegó la hora configurada para el cierre. |
| **Post-Condición** | **Éxito:** los envíos vencidos quedan **EnDevolucion**, las liquidaciones del período quedan generadas y los indicadores actualizados. Queda registrado el resultado del cierre.<br>**Fracaso:** el cierre se reintenta, y lo que ya se había procesado no se repite. |
| **Actores** | **Principal:** sistema (Worker).<br>**Secundarios:** módulos de Envíos y de Depósito y liquidaciones. |
| **Flujo de evento principal** | 1. El Worker dispara el cierre del día para cada operador.<br>2. Se obtienen los envíos *Reprogramado* cuyo plazo de devolución venció y se inicia su devolución (T14).<br>3. Se detectan las rutas del día no finalizadas y se genera una alerta para el despachador.<br>4. Si termina un período de liquidación, se generan las liquidaciones de cada comercio (CU-32).<br>5. Se recalculan los indicadores del día (CU-71).<br>6. Se registra el resultado del cierre. |
| **Flujos alternativos** | **A1 · El cierre se ejecuta dos veces (paso 1).** El segundo no repite efectos: cada paso es idempotente.<br><br>**A2 · Falla un paso (pasos 2 a 5).** Se reintenta; si se agotan los reintentos, el mensaje pasa a la cola de fallidos (CU-09). |
| **Requerimientos especiales** | 1. El Worker se comunica con los demás módulos sólo mediante la cola (sección 6.1): publica el mensaje de cierre, y las transiciones de Envíos y las liquidaciones las ejecutan esos módulos en la API al recibirlo.<br>2. La hora del cierre usa la zona horaria del operador. |

### **CU-71 · Recalcular los indicadores de cumplimiento**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-71 · Recalcular los indicadores de cumplimiento |
| **Descripción** | El sistema calcula los indicadores que usan el tablero y los reportes: porcentaje de envíos entregados dentro de la ventana comprometida, por zona, repartidor y comercio, y la distribución de motivos de no entrega. |
| **Pre-Condición** | 1. Ocurrió un cambio de estado o se ejecuta el cierre diario (CU-70). |
| **Post-Condición** | Los indicadores del período quedan actualizados en la tabla de lectura de Seguimiento. |
| **Actores** | **Principal:** sistema (Worker). |
| **Flujo de evento principal** | 1. El sistema recibe un evento de entrega o de intento fallido, o el cierre diario.<br>2. Determina si la entrega fue dentro de la ventana comprometida, según el plazo de la modalidad en la versión de reglas del envío.<br>3. Actualiza los contadores de la zona, el repartidor y el comercio, y los de motivos de no entrega. |
| **Flujos alternativos** | **A1 · Evento repetido (paso 1).** No se cuenta dos veces (idempotencia). |
| **Requerimientos especiales** | 1. Los indicadores se mantienen en la tabla de lectura de Seguimiento; no se calculan sobre las tablas transaccionales en cada consulta. |

### **CU-72 · Consultar reportes de gestión**

| Campo | Contenido |
| :---- | :---- |
| **Caso de uso** | CU-72 · Consultar reportes de gestión |
| **Descripción** | El administrador o el despachador consulta los reportes de gestión: cumplimiento por zona, repartidor y comercio; motivos de no entrega; volumen de envíos por período; y liquidación por comercio (RF 30). |
| **Pre-Condición** | 1. El usuario tiene perfil *Administrador* o *Despachador*. |
| **Post-Condición** | No modifica datos. |
| **Actores** | **Principal:** administrador o despachador, desde el Backoffice. |
| **Flujo de evento principal** | 1. El usuario elige **Reportes** y el tipo de reporte.<br>2. Indica el período y, según el reporte, la zona, el repartidor o el comercio.<br>3. El sistema muestra el reporte en una tabla y un gráfico.<br>4. El usuario puede exportarlo a CSV. |
| **Flujos alternativos** | **A1 · Período sin datos (paso 3).** El sistema lo informa.<br><br>**A2 · Período inválido (paso 2).** La fecha *desde* es posterior a la fecha *hasta*. El sistema lo indica. |
| **Requerimientos especiales** | 1. Los reportes de cumplimiento, motivos y volumen salen de la tabla de lectura de Seguimiento; el de liquidación, de Depósito y liquidaciones (catálogo, sección 4).<br>2. Las consultas no afectan la operación transaccional. |

# **3. Decisiones sobre las preguntas surgidas del desglose**

| Tema | Casos | Por qué importa |
| :---- | :---- | :---- |
| Datos globales de un comercio que trabaja con varios operadores | CU-01 | **Resuelta.** El documento fiscal no cambia; la razón social sólo la corrige el propio comercio; el correo de contacto está en la `RelacionComercial` y cada operador edita el suyo. |
| Gestión de los usuarios de un comercio con varios operadores | CU-02 | **Resuelta.** Los crea el operador que da de alta al comercio; con otro operador se usa el mismo usuario, eligiendo el operador al iniciar sesión (CU-03). |
| Faltantes en el modelo de dominio | CU-06, CU-40, CU-61, CU-66, CU-81 | **Resuelta.** Se agregan `anticipacionReprogramacionHoras` y `maxParadasPorRuta` a `VersionReglas`, y el valor `Api` a `OrigenEvento`. El umbral de envío en riesgo es una constante fija de 60 minutos. |
| Prueba de entrega exigida | CU-06, CU-17 | **Resuelta.** La firma del receptor es siempre obligatoria. Las pruebas adicionales (foto, nombre y documento) son las de la versión de reglas del envío (sección 6.6). |
| Consistencia de las transiciones pedidas por otros módulos | CU-30, CU-40, CU-55 | **Resuelta.** Cuando un caso de uso llama sincrónicamente a otro módulo mediante `I<Modulo>ModuleApi`, todo se guarda en una sola transacción: los `DbContext` de los módulos comparten la conexión a la misma base PostgreSQL. Se implementa una vez en BuildingBlocks. |
| Persistencia del Worker | CU-62, CU-64, CU-70 | **Resuelta.** El Worker ejecuta el código del módulo Seguimiento, que es dueño de las suscripciones, notificaciones y entregas de avisos: la API lo usa para las pantallas y el Worker para procesar los mensajes. Con los demás módulos, el Worker se comunica sólo por la cola. |
| Envíos liquidables | CU-32 | **Resuelta.** Se cobran los *Entregado* y *Devuelto*, por la tarifa del alta, sin recargos por reintentos ni devolución. No se cobran los *Cancelado* ni los *Extraviado*. |
| Almacenamiento de archivos | CU-07, CU-17, CU-18 | **Resuelta.** En PostgreSQL, en una tabla de archivos: sin infraestructura nueva, con backup y con el mismo aislamiento por inquilino. Límite de 1 MB por archivo; se sirven por un endpoint que verifica los permisos. El acceso pasa por una interfaz de Infrastructure, de modo que podría cambiarse a DigitalOcean Spaces sin tocar los casos de uso. |
| Proveedor de correo | CU-02, CU-62 | **Resuelta.** La aplicación envía por SMTP. En desarrollo, Mailpit en Docker Compose captura los correos y los muestra en una página web. En producción, un proveedor SMTP con plan gratuito (por ejemplo, Brevo); cambiarlo es sólo configuración. |
| Cálculo de la ventana horaria estimada | CU-60 | **Resuelta.** Con fecha y franja asignadas, se muestran esas; sin fecha, *antes de* la fecha del plazo comprometido de su modalidad; en tránsito, la franja del día y cuántas entregas quedan antes. No se estiman tiempos de viaje. |
| Ambiente de pruebas de la API pública | CU-80, CU-81, CU-83 | **Resuelta.** Sin ambiente aparte: los envíos creados con una clave de prueba se marcan con `EsPrueba`. Se ven por la API sólo con claves de prueba y generan avisos al sistema del comercio, pero no aparecen en el Backoffice ni en las rutas, no se liquidan y no notifican al destinatario. |

# **4\. Historial de versiones**

| Versión | Fecha | Descripción | Responsable |
| :---: | :---: | :---- | :---- |
| 0.1 | 30/09/2026 | Desglose inicial de todos los casos de uso del catálogo v0.3. | Ezequiel Marcenal |
| 0.2 | 30/09/2026 | Se cierran las 11 preguntas abiertas de la sección 3. | Ezequiel Marcenal |
