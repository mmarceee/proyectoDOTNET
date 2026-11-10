// CU-50: sólo este módulo persiste la hoja personal. Cache Storage guarda únicamente el shell.
const DB = 'logistica-repartidor-v1';
const STORES = ['espacios', 'claves', 'hojas', 'borradores', 'operaciones', 'instalacion', 'recibos'];
const listeners = new Set();
const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel('logistica-repartidor') : null;
const encoder = new TextEncoder();
function notify() {
    for (const ref of listeners) ref.invokeMethodAsync('InvalidarEspacio').catch(() => listeners.delete(ref));
}
if (channel) channel.onmessage = () => notify();
export function escuchar(ref) { listeners.add(ref); }
export function dejarDeEscuchar(ref) { listeners.delete(ref); }
function publish() { notify(); channel?.postMessage('espacio-cambiado'); }
function key(s) { return [s.usuarioId, s.operadorId, s.repartidorId].join(':'); }
function guid(value) { return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value); }
function date(value) { return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value); }
function text(value) { return typeof value === 'string' && value.trim().length > 0; }
function identity(s) {
    if (!s || ![s.usuarioId, s.operadorId, s.repartidorId].every(guid)
        || !date(s.fechaOperativa) || !text(s.zonaHorariaOperador) || !Number.isFinite(Date.parse(s.verificadaEn)))
        throw new Error('La identidad del repartidor no es válida.');
}
export function validarHoja(h) {
    identity({ ...h, verificadaEn: h?.generadaEn });
    if (![1,2,3].includes(h.schemaVersion) || !guid(h.rutaId) || !Number.isSafeInteger(h.revisionRuta) || h.revisionRuta < 0
        || !date(h.fechaRuta) || !['Despachada', 'EnCurso'].includes(h.estadoRuta)
        || !guid(h.vehiculo?.id) || !text(h.vehiculo?.matricula)
        || !Array.isArray(h.zonas) || h.zonas.length === 0 || h.zonas.some(z => !guid(z.id) || !text(z.nombre) || !text(z.codigo))
        || !Array.isArray(h.paradas) || h.paradas.length === 0)
        throw new Error('La hoja de ruta está incompleta.');
    const paradas = new Set(), ordenes = new Set(), envios = new Set(), bultos = new Set();
    for (const p of h.paradas) {
        if (!guid(p.id) || paradas.has(p.id) || !Number.isInteger(p.orden) || p.orden < 1 || ordenes.has(p.orden)
            || !text(p.direccion) || !text(p.estado) || !Array.isArray(p.envios) || p.envios.length === 0)
            throw new Error('La hoja contiene una parada inválida.');
        paradas.add(p.id); ordenes.add(p.orden);
        for (const e of p.envios) {
            const r = e.reglasEntrega;
            if (!guid(e.id) || envios.has(e.id) || !text(e.numero) || !text(e.estado)
                || !text(e.destinatarioNombre) || !text(e.destinatarioTelefono)
                || !e.direccion || ['calle','numero','localidad','departamento','codigoPostal'].some(c => !text(e.direccion[c]))
                || !Array.isArray(e.bultos) || e.bultos.length === 0
                || !r || !text(r.origen) || !(r.versionId === null || guid(r.versionId)) || r.firmaObligatoria !== true
                || typeof r.exigeFoto !== 'boolean' || typeof r.exigeNombreYDocumento !== 'boolean'
                || !Number.isInteger(r.maxIntentos) || r.maxIntentos < 1 || !text(r.plazoEntreIntentos)
                || !Array.isArray(r.motivosNoEntrega) || r.motivosNoEntrega.length === 0
                || r.motivosNoEntrega.some(m => !guid(m.id) || !text(m.codigo) || !text(m.nombreParaMostrar)
                    || typeof m.reprogramable !== 'boolean' || typeof m.exigeEvidencia !== 'boolean'))
                throw new Error('La hoja contiene un envío incompleto.');
            if (h.schemaVersion >= 3 && typeof r.admiteEntregaParcial !== 'boolean') throw new Error('Falta la regla de entrega parcial.');
            envios.add(e.id);
            for (const b of e.bultos) {
                if (!guid(b.id) || !text(b.codigo) || bultos.has(b.id)) throw new Error('La hoja contiene bultos inválidos.');
                if (h.schemaVersion >= 3 && !['Admitido','EnDeposito','AsignadoARuta','EnTransito','Entregado','NoEntregado','Reprogramado','EnDevolucion','Devuelto','Extraviado','Cancelado'].includes(b.estado)) throw new Error('Falta el estado del bulto.');
                bultos.add(b.id);
            }
        }
    }
    if (h.schemaVersion >= 2) {
        const declarables = new Set(['NoIntentado','IntentoFallido','CargaParcial']);
        const situaciones = new Set([...declarables,'NoCargado','EntregadoEnCalle','Extraviado','Resuelto','Revisar']);
        const manifiesto = new Map(h.paradas.flatMap(p => p.envios).flatMap(e => e.bultos.map(b => [b.id, { envio:e.id, codigo:b.codigo }])));
        const lista = h.rendicion?.bultos;
        if (!Array.isArray(lista) || lista.length !== bultos.size || new Set(lista.map(b => b.bultoId)).size !== bultos.size
            || lista.some(b => manifiesto.get(b.bultoId)?.envio !== b.envioId || manifiesto.get(b.bultoId)?.codigo !== b.codigo
                || !text(b.numeroEnvio) || !situaciones.has(b.situacion) || b.requiereDeclaracion !== declarables.has(b.situacion)))
            throw new Error('La base de rendición está incompleta. Se conserva la hoja anterior.');
    }
}
function open() {
    if (!globalThis.indexedDB || !globalThis.crypto?.subtle || !globalThis.isSecureContext)
        return Promise.reject(new Error('Este navegador necesita almacenamiento local y un contexto seguro para guardar la ruta.'));
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB, 3);
        request.onupgradeneeded = () => { for (const name of STORES) if (!request.result.objectStoreNames.contains(name)) request.result.createObjectStore(name); };
        request.onsuccess = () => { request.result.onversionchange = () => request.result.close(); resolve(request.result); };
        request.onerror = () => reject(request.error);
        request.onblocked = () => reject(new Error('Cerrá las otras pestañas para actualizar el almacenamiento.'));
    });
}
function requestResult(request) {
    return new Promise((resolve, reject) => { request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error); });
}
function completed(tx) {
    return new Promise((resolve, reject) => {
        tx.oncomplete = () => resolve();
        tx.onabort = tx.onerror = () => reject(tx.error || new Error('No se pudo guardar la ruta.'));
    });
}
async function read(store, id) {
    const db = await open();
    try {
        const tx = db.transaction(store, 'readonly'), done = completed(tx);
        const value = await requestResult(tx.objectStore(store).get(id));
        await done;
        return value;
    } finally { db.close(); }
}
export async function activar(sesion) {
    identity(sesion);
    const { nombreUsuario, ...identidadLocal } = sesion;
    const anterior = await read('espacios', 'activo');
    const token = crypto.randomUUID();
    const db = await open();
    try {
        const tx = db.transaction('espacios', 'readwrite'), done = completed(tx);
        tx.objectStore('espacios').put({ ...identidadLocal, token,
            rutaSeleccionada: anterior && key(anterior) === key(sesion) ? anterior.rutaSeleccionada ?? null : null,
            sinRuta: anterior && key(anterior) === key(sesion) && anterior.fechaOperativa === sesion.fechaOperativa
                ? anterior.sinRuta === true : false }, 'activo');
        await done;
    } finally { db.close(); }
    if (anterior && key(anterior) !== key(sesion)) channel?.postMessage('espacio-cambiado');
    return token;
}
export async function bloquear() {
    const db = await open();
    try {
        const tx = db.transaction('espacios', 'readwrite'), done = completed(tx);
        tx.objectStore('espacios').delete('activo');
        await done;
    } finally { db.close(); }
    publish();
}
export async function sesionLocal() { return (await read('espacios', 'activo')) || null; }
function header(h) {
    return { espacio: key(h), schemaVersion: h.schemaVersion, rutaId: h.rutaId, fechaRuta: h.fechaRuta };
}
async function encryptionKey(id) {
    const existente = await read('claves', id);
    if (existente) return existente;
    const nueva = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
    const db = await open();
    try {
        const tx = db.transaction('claves', 'readwrite'), done = completed(tx);
        const store = tx.objectStore('claves');
        // Lectura y alta en una sola transacción: dos pestañas no crean claves distintas para el mismo espacio.
        const r = store.get(id);
        let elegida = nueva;
        r.onsuccess = () => { if (r.result) elegida = r.result; else store.put(nueva, id); };
        await done;
        return elegida;
    } finally { db.close(); }
}
export async function guardar(hoja, token) {
    validarHoja(hoja);
    const sesion = await sesionLocal();
    if (!sesion || sesion.token !== token || key(sesion) !== key(hoja)) throw new Error('La sesión local cambió.');
    const clave = await encryptionKey(key(sesion));
    const meta = header(hoja), iv = crypto.getRandomValues(new Uint8Array(12));
    const contenido = await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: encoder.encode(JSON.stringify(meta)), tagLength: 128 },
        clave, encoder.encode(JSON.stringify(hoja)));
    const db = await open();
    try {
        const tx = db.transaction(['espacios', 'hojas', 'instalacion'], 'readwrite'), done = completed(tx);
        const r = tx.objectStore('espacios').get('activo');
        r.onsuccess = () => {
            if (!r.result || r.result.token !== token || key(r.result) !== key(hoja)) { tx.abort(); return; }
            const hojas = tx.objectStore('hojas'), anterior = hojas.get(key(sesion));
            anterior.onsuccess = () => {
                if (anterior.result && anterior.result.meta.rutaId !== hoja.rutaId)
                    hojas.put(anterior.result, `${key(sesion)}:ruta:${anterior.result.meta.rutaId}`);
                hojas.put({ meta, iv, contenido, descargadaEn: new Date().toISOString() }, key(sesion));
                hojas.delete(`${key(sesion)}:ruta:${hoja.rutaId}`);
                tx.objectStore('instalacion').delete(`gps-cerrada:${key(sesion)}:${hoja.rutaId}`);
                tx.objectStore('espacios').put({ ...r.result, rutaSeleccionada:hoja.rutaId, sinRuta:false }, 'activo');
            };
        };
        await done;
    } finally { db.close(); }
    return cargar();
}
export async function sinRuta(token) {
    const db = await open();
    try {
        const tx = db.transaction(['espacios','instalacion'], 'readwrite'), done = completed(tx);
        const store = tx.objectStore('espacios'), r = store.get('activo');
        r.onsuccess = () => {
            if (!r.result || r.result.token !== token) { tx.abort(); return; }
            store.put({ ...r.result, sinRuta: true }, 'activo');
            if (r.result.rutaSeleccionada) tx.objectStore('instalacion')
                .put(true,`gps-cerrada:${key(r.result)}:${r.result.rutaSeleccionada}`);
        };
        await done;
    } finally { db.close(); }
}
export async function cargar(rutaId = null) {
    const sesion = await sesionLocal();
    if (!sesion) return null;
    const ruta = rutaId ?? sesion.rutaSeleccionada;
    let guardada = await read('hojas', key(sesion));
    if (ruta && guardada?.meta.rutaId !== ruta) guardada = await read('hojas', `${key(sesion)}:ruta:${ruta}`);
    if (!guardada) return null;
    const clave = await read('claves', key(sesion));
    if (!clave || guardada.meta.espacio !== key(sesion)) throw new Error('No se puede abrir esta copia de la ruta.');
    const bytes = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: guardada.iv,
        additionalData: encoder.encode(JSON.stringify(guardada.meta)), tagLength: 128 }, clave, guardada.contenido);
    const hoja = JSON.parse(new TextDecoder().decode(bytes));
    validarHoja(hoja);
    if (key(hoja) !== key(sesion) || JSON.stringify(header(hoja)) !== JSON.stringify(guardada.meta))
        throw new Error('La copia no pertenece a esta cuenta.');
    const actual = await sesionLocal();
    if (!actual || actual.token !== sesion.token) throw new Error('La sesión local cambió.');
    return { hoja, descargadaEn: guardada.descargadaEn,
        sinRuta: actual.sinRuta === true && (!actual.rutaSeleccionada || actual.rutaSeleccionada === hoja.rutaId) };
}

export async function seleccionarRuta(rutaId) {
    if (!guid(rutaId)) throw new Error('La ruta no es válida.');
    const s = await activo(), db = await open();
    try {
        const tx = db.transaction('espacios','readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'),s,() => tx.objectStore('espacios')
            .put({ ...s,rutaSeleccionada:rutaId,sinRuta:false,token:crypto.randomUUID() },'activo'),tx);
        await done;
    } finally { db.close(); }
}
export async function rutaSeleccionada() { return (await sesionLocal())?.rutaSeleccionada ?? null; }
export async function rutasGuardadas() {
    const s = await sesionLocal();
    if (!s) return [];
    const db = await open(); let filas;
    try {
        const tx = db.transaction('hojas','readonly'), done = completed(tx);
        filas = await requestResult(tx.objectStore('hojas').getAll()); await done;
    } finally { db.close(); }
    const ids = [...new Set(filas.filter(f => f.meta.espacio === key(s)).map(f => f.meta.rutaId))];
    return (await Promise.all(ids.map(id => cargar(id)))).filter(Boolean);
}
export async function estadosLocalesRutas() {
    const s = await sesionLocal();
    if (!s) return [];
    const db = await open(); let filas;
    try {
        const tx = db.transaction('borradores','readonly'), done = completed(tx);
        filas = await requestResult(tx.objectStore('borradores').getAll()); await done;
    } finally { db.close(); }
    const estados = [];
    for (const f of filas.filter(f => f.meta.espacio === key(s))) {
        const b = await descifrar(f,'borrador',f.meta.id,s);
        estados.push({ rutaId:b.rutaId,tipo:b.tipoBorrador ?? 'carga',resultado:b.resultado });
    }
    if ((await activo()).token !== s.token) throw new Error('La sesión cambió.');
    return estados;
}

// CU-51/52. Identificadores en claro para indexar; borrador y operación completos siempre cifrados.
function hex(bytes) { return [...bytes].map(b => b.toString(16).padStart(2, '0')).join(''); }
function randomBytes(n) {
    let bytes;
    do { bytes = crypto.getRandomValues(new Uint8Array(n)); } while (bytes.every(b => b === 0));
    return bytes;
}
export function traceparent() { return `00-${hex(randomBytes(16))}-${hex(randomBytes(8))}-01`; }
export function uuidv7() {
    const b = randomBytes(16);
    let ms = BigInt(Date.now());
    for (let i = 5; i >= 0; i--) { b[i] = Number(ms & 255n); ms >>= 8n; }
    b[6] = (b[6] & 15) | 112; b[8] = (b[8] & 63) | 128;
    const s = hex(b);
    return `${s.slice(0,8)}-${s.slice(8,12)}-${s.slice(12,16)}-${s.slice(16,20)}-${s.slice(20)}`;
}
async function activo() {
    const s = await sesionLocal();
    if (!s) throw new Error('Iniciá sesión con conexión para acceder a la carga.');
    return s;
}
function draftKey(s, ruta, tipo = 'carga') {
    // Conservar las claves históricas de carga/rendición; cada visita tiene espacio propio por envío.
    return `${key(s)}:${ruta}${tipo === 'carga' ? '' : ':' + tipo}`;
}
async function cifrar(tipo, id, valor, s) {
    const meta = { espacio: key(s), tipo, id }, iv = randomBytes(12);
    const clave = await encryptionKey(key(s));
    const contenido = await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: encoder.encode(JSON.stringify(meta)) },
        clave, encoder.encode(JSON.stringify(valor)));
    return { meta, iv, contenido };
}
async function descifrar(registro, tipo, id, s) {
    if (!registro) return null;
    if (registro.meta.espacio !== key(s) || registro.meta.tipo !== tipo || registro.meta.id !== id)
        throw new Error('La carga no pertenece a esta cuenta.');
    const clave = await read('claves', key(s));
    if (!clave) throw new Error('No se puede abrir la carga guardada.');
    const bytes = await crypto.subtle.decrypt({ name:'AES-GCM', iv:registro.iv,
        additionalData:encoder.encode(JSON.stringify(registro.meta)) }, clave, registro.contenido);
    return JSON.parse(new TextDecoder().decode(bytes));
}
function comprobarToken(store, s, fn, tx) {
    const r = store.get('activo');
    r.onsuccess = () => {
        if (!r.result || r.result.token !== s.token || key(r.result) !== key(s)) tx.abort();
        else fn();
    };
}
export async function borradorCarga(ruta) {
    return leerBorrador(ruta, 'carga');
}
async function leerBorrador(ruta, tipo) {
    const s = await activo(), id = draftKey(s, ruta, tipo);
    await recuperarColisionVisita(s, ruta);
    const valor = await descifrar(await read('borradores', id), 'borrador', id, s);
    if ((await activo()).token !== s.token) throw new Error('La sesión cambió.');
    return valor;
}

// La primera versión RF32 escribía las visitas sobre la clave de carga. Separar ambos documentos
// atómicamente, volviendo a cifrar con sus IDs correctos (los IDs son datos autenticados de AES-GCM).
async function recuperarColisionVisita(s, ruta) {
    const idCarga = draftKey(s, ruta), registro = await read('borradores', idCarga);
    const visita = await descifrar(registro, 'borrador', idCarga, s);
    if (!visita?.tipoBorrador?.startsWith('visita:')) return;
    if (!guid(visita.envioId) || visita.tipoBorrador !== 'visita:' + visita.envioId || !Array.isArray(visita.bultos))
        throw new Error('El borrador guardado necesita revisión. Los datos se conservaron.');
    const idVisita = draftKey(s, ruta, visita.tipoBorrador);
    const copiaVisita = await cifrar('borrador', idVisita, visita, s);
    const operaciones = await operacionesDeRuta(s, ruta);
    const ultima = operaciones.filter(r => r.op.tipo === 'ConfirmarCarga').at(-1);
    const hoja = ultima ? null : await cargar(ruta);
    const carga = { rutaId:ruta, revision:(registro.revision ?? 0)+1,
        revisionRuta:ultima?.op.carga.revisionRuta ?? hoja?.hoja.revisionRuta ?? 0, lecturas:ultima?.op.carga.lecturas ?? [],
        faltantes:ultima?.op.carga.faltantes ?? [], operacionId:ultima?.op.operacionId ?? null,
        resultado:ultima ? (ultima.estado === 'Enviando' ? 'Pendiente' : ultima.estado) : null,
        detalle:ultima ? null : 'La carga sin confirmar debe revisarse: se conservó el borrador de la visita.' };
    const copiaCarga = await cifrar('borrador', idCarga, carga, s);
    const db = await open();
    try {
        const tx = db.transaction(['espacios','borradores'], 'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'), s, () => {
            const store = tx.objectStore('borradores'), actual = store.get(idCarga);
            actual.onsuccess = () => {
                if (actual.result?.revision !== registro.revision) { tx.abort(); return; }
                const destino = store.get(idVisita);
                destino.onsuccess = () => {
                    if (!destino.result) store.put({ ...copiaVisita, revision:visita.revision, bloqueaRuta:false }, idVisita);
                    store.put({ ...copiaCarga, revision:carga.revision, bloqueaRuta:false }, idCarga);
                };
            };
        }, tx);
        try { await done; }
        catch { throw new Error('La sesión o el borrador cambió durante la recuperación. Volvé a abrir la ruta; los datos se conservaron.'); }
    } finally { db.close(); }
}
async function escribirBorrador(valor, esperado, s, operacion = null) {
    const id = draftKey(s, valor.rutaId, valor.tipoBorrador), registro = await cifrar('borrador', id, valor, s);
    const op = operacion ? await cifrar('operacion', operacion.operacionId, operacion, s) : null;
    const db = await open();
    try {
        const tx = db.transaction(['espacios','borradores','operaciones'], 'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'), s, () => {
            const store = tx.objectStore('borradores'), r = store.get(id);
            r.onsuccess = () => {
                if ((r.result?.revision ?? 0) !== esperado) { tx.abort(); return; }
                const escribir = () => {
                    store.put({ ...registro, revision:valor.revision, bloqueaRuta:valor.tipoBorrador === 'rendicion' && !!valor.operacionId && valor.resultado !== 'Rechazada',
                        bloqueaCarga:valor.tipoBorrador === 'inicio' && !!valor.operacionId && valor.resultado !== 'Rechazada' }, id);
                    if (op) tx.objectStore('operaciones').add({ ...op, estado:'Pendiente', secuencia:operacion.secuencia }, operacion.operacionId);
                };
                const escribirConCarga = () => {
                    if ((valor.tipoBorrador ?? 'carga') === 'carga') {
                        const inicio = store.get(draftKey(s, valor.rutaId, 'inicio'));
                        inicio.onsuccess = () => { if (inicio.result?.bloqueaCarga) tx.abort(); else escribir(); };
                    } else if (valor.tipoBorrador === 'inicio') {
                        const carga = store.get(draftKey(s, valor.rutaId));
                        carga.onsuccess = () => { if ((carga.result?.revision ?? 0) !== valor.revisionCarga) tx.abort(); else escribir(); };
                    } else escribir();
                };
                if (['ConfirmarCarga','RegistrarVisita','IniciarRuta'].includes(operacion?.tipo)) {
                    const rendicion = store.get(draftKey(s, valor.rutaId, 'rendicion'));
                    rendicion.onsuccess = () => { if (rendicion.result?.bloqueaRuta) tx.abort(); else escribirConCarga(); };
                } else escribirConCarga();
            };
        }, tx);
        try { await done; } catch { throw new Error('No se pudo guardar: la carga cambió en otra pestaña, cambió la sesión o no hay espacio. Actualizá para revisar.'); }
    } finally { db.close(); }
    return valor;
}
export function fechaOperativaActual(s) {
    const partes = new Intl.DateTimeFormat('en', { timeZone:s.zonaHorariaOperador,year:'numeric',month:'2-digit',day:'2-digit' })
        .formatToParts(new Date());
    const parte = tipo => partes.find(p => p.type === tipo).value;
    const local = `${parte('year')}-${parte('month')}-${parte('day')}`;
    return local > s.fechaOperativa ? local : s.fechaOperativa;
}
async function exigirFechaDeCarga(ruta, copia = null) {
    const s = await activo(); copia ??= await cargar();
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta || copia.hoja.fechaRuta !== fechaOperativaActual(s))
        throw new Error('Sólo podés registrar cargas para la fecha de hoy. Esta ruta queda para consulta o resolución por despacho.');
}
export async function abrirCarga(ruta) {
    const s = await activo(), copia = await cargar();
    const anterior = await borradorCarga(ruta);
    // Consultar una confirmación anterior no crea otra carga ni modifica la operación pendiente.
    if (copia?.hoja.fechaRuta !== fechaOperativaActual(s) && anterior?.operacionId && anterior.resultado) return anterior;
    await exigirFechaDeCarga(ruta, copia);
    await exigirJornadaAbierta(ruta, copia);
    if (anterior?.resultado === 'Pendiente') return anterior;
    if ((await estadoInicioRuta(ruta, copia)).iniciada && anterior) return anterior;
    if (anterior?.resultado === 'Rechazada') return anterior;
    if (anterior && anterior.revisionRuta === copia.hoja.revisionRuta) return anterior;
    const pendientes = new Set(copia.hoja.paradas.flatMap(p => p.envios).flatMap(e => e.bultos.filter(b => (b.estado || e.estado) === 'AsignadoARuta')).map(b => b.codigo));
    const valor = { rutaId:ruta, revisionRuta:copia.hoja.revisionRuta, revision:(anterior?.revision ?? 0)+1,
        lecturas:!anterior?.resultado ? anterior?.lecturas ?? [] : anterior.resultado === 'Aplicada' ? anterior.lecturas.filter(l => pendientes.has(l.codigoBulto)) : [],
        faltantes:anterior?.faltantes?.filter(f => pendientes.has(f.codigoBulto)) ?? [],
        operacionId:null, resultado:null, detalle:null };
    return escribirBorrador(valor, anterior?.revision ?? 0, s);
}
export async function escanearCarga(ruta, revision, codigo) {
    await exigirFechaDeCarga(ruta);
    await exigirCargaAbierta(ruta);
    await exigirJornadaAbierta(ruta);
    codigo = codigo.trim();
    if (!codigo || codigo.length > 200) throw new Error('Ingresá un código de bulto válido.');
    const s = await activo(), actual = await borradorCarga(ruta);
    if (!actual || actual.revision !== revision) throw new Error('La carga cambió. Actualizá antes de continuar.');
    if (actual.operacionId) throw new Error('Esta carga ya fue confirmada.');
    if (actual.lecturas.some(l => l.codigoBulto === codigo)) throw new Error('Ese bulto ya fue escaneado.');
    if (actual.lecturas.length >= 2000) throw new Error('La carga alcanzó el máximo de lecturas.');
    return escribirBorrador({ ...actual, revision:revision+1,
        lecturas:[...actual.lecturas, { codigoBulto:codigo, escaneadoEn:new Date().toISOString() }] }, revision, s);
}
export async function quitarLecturaCarga(ruta, revision, codigo) {
    await exigirFechaDeCarga(ruta);
    await exigirCargaAbierta(ruta);
    await exigirJornadaAbierta(ruta);
    const s = await activo(), actual = await borradorCarga(ruta);
    if (!actual || actual.revision !== revision) throw new Error('La carga cambió. Actualizá antes de continuar.');
    if (actual.operacionId) throw new Error('Esta carga ya fue confirmada.');
    if (!actual.lecturas.some(l => l.codigoBulto === codigo)) throw new Error('Esa lectura ya no está en el borrador.');
    return escribirBorrador({ ...actual, revision:revision+1,
        lecturas:actual.lecturas.filter(l => l.codigoBulto !== codigo) }, revision, s);
}
async function instalacion() {
    const db = await open();
    try {
        const tx = db.transaction('instalacion','readwrite'), done = completed(tx), store = tx.objectStore('instalacion');
        const r = store.get('actual'); let valor;
        r.onsuccess = () => {
            valor = { dispositivoId:r.result?.dispositivoId ?? crypto.randomUUID(), secuencia:(r.result?.secuencia ?? 0)+1 };
            store.put(valor, 'actual');
        };
        await done; return valor;
    } finally { db.close(); }
}
export async function confirmarCarga(ruta, revision, aceptaFaltantes) {
    await exigirFechaDeCarga(ruta);
    await exigirCargaAbierta(ruta);
    await exigirJornadaAbierta(ruta);
    const s = await activo(), actual = await borradorCarga(ruta), copia = await cargar();
    if (!actual || actual.operacionId || actual.revision !== revision || !actual.lecturas.length)
        throw new Error('Revisá el avance de carga antes de confirmar.');
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta || copia.hoja.revisionRuta !== actual.revisionRuta)
        throw new Error('La hoja cambió. Usá «Actualizar hoja y revisar carga» antes de confirmar; se conservan tus escaneos.');
    const candidatos = copia.hoja.paradas.flatMap(p => p.envios).map(e => ({ ...e, bultos:e.bultos.filter(b => (b.estado || e.estado) === 'AsignadoARuta') })).filter(e => e.bultos.length);
    const codigos = new Set(actual.lecturas.map(l => l.codigoBulto));
    const porBulto = copia.hoja.schemaVersion >= 3;
    const bloqueados = candidatos.filter(e => e.reglasEntrega.admiteEntregaParcial !== true && e.bultos.some(b => !codigos.has(b.codigo)));
    if (porBulto && !candidatos.some(e => e.bultos.every(b => codigos.has(b.codigo))
        || e.reglasEntrega.admiteEntregaParcial === true && e.bultos.some(b => codigos.has(b.codigo))))
        throw new Error('Completá los envíos: esta carga no tiene bultos habilitados para salir.');
    if (!aceptaFaltantes && candidatos.some(e => (!porBulto || e.reglasEntrega.admiteEntregaParcial === true) && e.bultos.some(b => !codigos.has(b.codigo))))
        throw new Error('Confirmá expresamente que vas a continuar con faltantes.');
    const faltantes = candidatos.filter(e => !porBulto || e.reglasEntrega.admiteEntregaParcial === true).flatMap(e => e.bultos).filter(b => !codigos.has(b.codigo));
    if (copia.hoja.schemaVersion >= 3 && faltantes.some(b => !actual.faltantes?.some(f => f.codigoBulto === b.codigo))) throw new Error('Indicá el motivo de cada faltante de carga.');
    const dispositivo = await instalacion();
    const op = { ...dispositivo, operacionId:uuidv7(), tipo:'ConfirmarCarga', rutaId:ruta, envioId:null,
        ocurridoEn:new Date().toISOString(), latitud:null, longitud:null, correlacionId:traceparent(),
        carga:{ revisionRuta:actual.revisionRuta, lecturas:actual.lecturas, aceptaFaltantes:aceptaFaltantes || porBulto && bloqueados.length > 0, ...(porBulto ? { porBulto:true, faltantes:faltantes.map(b => actual.faltantes.find(f => f.codigoBulto === b.codigo)) } : {}) } };
    return escribirBorrador({ ...actual, revision:revision+1, operacionId:op.operacionId, resultado:'Pendiente' }, revision, s, op);
}
export async function motivoFaltanteCarga(ruta, revision, codigoBulto, motivo) {
    await exigirCargaAbierta(ruta);
    const s = await activo(), actual = await borradorCarga(ruta), copia = await cargar();
    await exigirFechaDeCarga(ruta, copia);
    await exigirJornadaAbierta(ruta, copia);
    if (!actual || actual.operacionId || actual.revision !== revision || !copia || copia.sinRuta
        || !copia.hoja.paradas.flatMap(p => p.envios).some(e => e.bultos.some(b => b.codigo === codigoBulto && (b.estado || e.estado) === 'AsignadoARuta'))
        || actual.lecturas.some(l => l.codigoBulto === codigoBulto)
        || !['NoDisponible','NoLocalizado','SinEspacio','Otro'].includes(motivo)) throw new Error('Revisá el faltante y su motivo.');
    return escribirBorrador({ ...actual, revision:revision+1, faltantes:[...(actual.faltantes ?? []).filter(f => f.codigoBulto !== codigoBulto), { codigoBulto, motivo }] }, revision, s);
}
async function listaOperaciones(s, todas = false) {
    const db = await open();
    try {
        const tx = db.transaction(todas ? ['operaciones','recibos'] : ['operaciones'],'readonly'), done = completed(tx);
        const filas = await requestResult(tx.objectStore('operaciones').getAll());
        if (todas) filas.push(...await requestResult(tx.objectStore('recibos').getAll()));
        await done;
        const propias = filas.filter(r => r.meta.espacio === key(s) && (todas || ['Pendiente','Enviando'].includes(r.estado)))
            .sort((a,b) => a.secuencia-b.secuencia);
        return todas ? propias : propias.slice(0,20);
    } finally { db.close(); }
}
export async function pendientesCarga() {
    const s = await sesionLocal();
    if (!s) return [];
    return Promise.all((await listaOperaciones(s)).map(r => descifrar(r,'operacion',r.meta.id,s)));
}
async function aceptarResultado(op, respuesta, s) {
    const tipo = op.tipo === 'IniciarRendicion' ? 'rendicion' : op.tipo === 'IniciarRuta' ? 'inicio' : 'carga';
    const id = draftKey(s, op.rutaId, tipo), anterior = await leerBorrador(op.rutaId, tipo);
    if (!['ConfirmarCarga','IniciarRendicion','IniciarRuta'].includes(op.tipo)) return aceptarSinBorrador(op, respuesta, s);
    if (!anterior || anterior.operacionId !== op.operacionId) throw new Error('La confirmación local cambió.');
    const aplicada = ['Aplicada','YaAplicada'].includes(respuesta.resultado);
    const valor = { ...anterior, revision:anterior.revision+1, resultado:aplicada ? 'Aplicada' : 'Rechazada',
        detalle:respuesta.motivo ?? respuesta.detalle ?? null };
    const cifrada = await cifrar('borrador',id,valor,s), recibo = aplicada ? await cifrar('operacion',op.operacionId,op,s) : null, db = await open();
    try {
        const tx = db.transaction(['espacios','borradores','operaciones','recibos'],'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'),s,() => {
            const borradores = tx.objectStore('borradores'), actual = borradores.get(id);
            actual.onsuccess = () => {
                if (actual.result?.revision !== anterior.revision) { tx.abort(); return; }
                borradores.put({ ...cifrada, revision:valor.revision, bloqueaRuta:tipo === 'rendicion' && aplicada,
                    bloqueaCarga:tipo === 'inicio' && aplicada },id);
                const store = tx.objectStore('operaciones');
                if (aplicada) {
                    tx.objectStore('recibos').put({ ...recibo, estado:'Aplicada', secuencia:op.secuencia },op.operacionId);
                    store.delete(op.operacionId);
                }
                else {
                    const r = store.get(op.operacionId);
                    r.onsuccess = () => { if (r.result) store.put({ ...r.result, estado:'Rechazada', motivo:respuesta.motivo },op.operacionId); };
                }
            };
        },tx);
        await done;
    } finally { db.close(); }
}
async function enviarPendientes() {
    const s = await sesionLocal();
    if (!s) return { mensaje:null, aplicadas:0 };
    const filas = await listaOperaciones(s);
    if (!filas.length) return { mensaje:null, aplicadas:0 };
    const operaciones = await Promise.all(filas.map(r => descifrar(r,'operacion',r.meta.id,s)));
    const db = await open();
    try {
        const tx = db.transaction(['espacios','operaciones'],'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'),s,() => {
            for (const fila of filas) tx.objectStore('operaciones').put({ ...fila, estado:'Enviando' },fila.meta.id);
        },tx);
        await done;
    } finally { db.close(); }
    if ((await activo()).token !== s.token) throw new Error('La sesión cambió. La carga sigue pendiente.');
    const pendientes = (await listaOperaciones(s,true)).filter(r => r.estado !== 'Aplicada' && r.estado !== 'Rechazada').length;
    const response = await fetch('/api/repartidor/sincronizacion', { method:'POST', credentials:'same-origin',
        headers:{'Content-Type':'application/json'}, body:JSON.stringify({ operaciones, pendientes }), signal:AbortSignal.timeout(15000) });
    if (response.status === 401 || response.status === 403) {
        await bloquear(); throw new Error('Iniciá sesión de nuevo. Las cargas pendientes se conservan cifradas.');
    }
    if (!response.ok) throw new Error('No pudimos sincronizar. La carga sigue pendiente.');
    const datos = await response.json();
    if (!Array.isArray(datos.resultados)) throw new Error('Respuesta inválida: la carga sigue pendiente.');
    let aplicadas = 0;
    for (const r of datos.resultados) {
        const op = operaciones.find(o => o.operacionId === r.operacionId);
        if (!op || !['Aplicada','YaAplicada','Conflicto','Invalida'].includes(r.resultado)) continue;
        await aceptarResultado(op,r,s);
        if (['Aplicada','YaAplicada'].includes(r.resultado)) aplicadas++;
    }
    return { mensaje:aplicadas ? 'Operaciones confirmadas por el servidor.' : 'Revisá las operaciones rechazadas.', aplicadas };
}
let sincronizacionEnCurso = null;
export async function sincronizarCargas() {
    // ADR-0004 §2.8: compartir la misma promesa entre temporizador GPS, pantallas y evento online.
    if (sincronizacionEnCurso) return sincronizacionEnCurso;
    const tarea = ejecutarSincronizacion();
    sincronizacionEnCurso = tarea;
    try { return await tarea; }
    finally { if (sincronizacionEnCurso === tarea) sincronizacionEnCurso = null; }
}
async function ejecutarSincronizacion() {
    try {
        const enviar = async () => {
            let aplicadas = 0, mensaje = null;
            for (let lote = 0; lote < 100; lote++) {
                const s = await sesionLocal();
                if (!s) break;
                const antes = (await listaOperaciones(s)).map(r => r.meta.id).join(':');
                if (!antes) break;
                const r = await enviarPendientes(); aplicadas += r.aplicadas; mensaje = r.mensaje;
                if ((await listaOperaciones(s)).map(r => r.meta.id).join(':') === antes) break;
            }
            return { mensaje, aplicadas };
        };
        return globalThis.navigator?.locks
            ? await navigator.locks.request('logistica-sincronizar-cargas', enviar) : await enviar();
    } catch (e) {
        if (e instanceof TypeError || e.name === 'TimeoutError' || e.name === 'AbortError')
            throw new Error('Sin conexión con el servidor. La operación quedó guardada y pendiente de sincronizar.');
        throw e;
    }
}
export async function revisarRechazo(ruta, revision) {
    await exigirCargaAbierta(ruta);
    await exigirFechaDeCarga(ruta);
    const s = await activo(), actual = await borradorCarga(ruta), copia = await cargar();
    if (!actual || actual.revision !== revision || actual.resultado !== 'Rechazada' || !copia || copia.sinRuta || copia.hoja.rutaId !== ruta)
        throw new Error('Actualizá la hoja para revisar el rechazo.');
    return escribirBorrador({ rutaId:ruta, revisionRuta:copia.hoja.revisionRuta, revision:revision+1,
        lecturas:[], operacionId:null, resultado:null, detalle:null },revision,s);
}
let cargaRef;
function conexionRecuperada() { cargaRef?.invokeMethodAsync('Reconectar').catch(() => {}); }
export function escucharConexion(ref) { cargaRef=ref; globalThis.addEventListener?.('online',conexionRecuperada); }
export function dejarConexion() { globalThis.removeEventListener?.('online',conexionRecuperada); cargaRef=null; }

export async function estadoInicioRuta(ruta, copia = null) {
    copia ??= await cargar(ruta);
    const inicio = await leerBorrador(ruta, 'inicio');
    if (copia?.hoja.estadoRuta === 'EnCurso') return { iniciada:true, resultado:inicio?.resultado ?? 'Aplicada', detalle:inicio?.detalle ?? null };
    if (inicio?.operacionId) return { iniciada:inicio.resultado !== 'Rechazada', resultado:inicio.resultado, detalle:inicio.detalle ?? null };
    // Compatibilidad con cargas pendientes del cliente anterior al manifiesto por bulto.
    if (copia?.hoja.schemaVersion < 3) {
        const s = await activo();
        const antiguas = (await operacionesDeRuta(s, ruta)).filter(r => r.op.tipo === 'ConfirmarCarga' && r.estado !== 'Rechazada');
        if (antiguas.some(r => copia.hoja.paradas.flatMap(p => p.envios).some(e => e.bultos.every(b => r.op.carga.lecturas.some(l => l.codigoBulto === b.codigo)))))
            return { iniciada:true, resultado:'Pendiente', detalle:null };
    }
    return { iniciada:false, resultado:null, detalle:null };
}
async function exigirCargaAbierta(ruta) {
    if ((await estadoInicioRuta(ruta)).iniciada) throw new Error('La ruta ya comenzó. La carga está cerrada.');
}
async function exigirRutaIniciada(ruta, copia = null) {
    if (!(await estadoInicioRuta(ruta, copia)).iniciada) throw new Error('Primero debés pulsar «Comenzar ruta».');
}
export async function comenzarRuta(ruta) {
    const s = await activo(), copia = await cargar();
    await exigirFechaDeCarga(ruta, copia);
    await exigirJornadaAbierta(ruta, copia);
    if ((await estadoInicioRuta(ruta, copia)).iniciada) return estadoInicioRuta(ruta, copia);
    const carga = await borradorCarga(ruta), anterior = await leerBorrador(ruta, 'inicio');
    if (anterior?.resultado === 'Rechazada') throw new Error('El inicio fue rechazado: revisá el conflicto con despacho antes de continuar.');
    if (carga && !carga.operacionId && carga.lecturas.length) throw new Error('Confirmá primero la carga que estás preparando.');
    const operaciones = await operacionesDeRuta(s, ruta);
    const confirmadas = operaciones.filter(r => r.op.tipo === 'ConfirmarCarga' && r.estado !== 'Rechazada');
    const cargados = copia.hoja.paradas.flatMap(p => p.envios).some(e => e.bultos.some(b => (b.estado || e.estado) === 'EnTransito'))
        || confirmadas.some(r => copia.hoja.paradas.flatMap(p => p.envios).some(e => e.bultos.every(b => r.op.carga.lecturas.some(l => l.codigoBulto === b.codigo))
            || e.reglasEntrega.admiteEntregaParcial && e.bultos.some(b => r.op.carga.lecturas.some(l => l.codigoBulto === b.codigo))));
    if (!cargados) throw new Error('Confirmá una carga con bultos habilitados antes de comenzar la ruta.');
    const dispositivo = await instalacion();
    const op = { ...dispositivo, operacionId:uuidv7(), tipo:'IniciarRuta', rutaId:ruta, envioId:null,
        ocurridoEn:new Date().toISOString(), latitud:null, longitud:null, correlacionId:traceparent() };
    await escribirBorrador({ tipoBorrador:'inicio', rutaId:ruta, revision:(anterior?.revision ?? 0)+1,
        revisionCarga:carga?.revision ?? 0, operacionId:op.operacionId, resultado:'Pendiente', detalle:null }, anterior?.revision ?? 0, s, op);
    return estadoInicioRuta(ruta, copia);
}

// CU-54: mismo almacén cifrado y mismo transporte, con un borrador separado del de carga.
export async function borradorRendicion(ruta) { return leerBorrador(ruta, 'rendicion'); }
async function exigirJornadaAbierta(ruta, copia = null) {
    const r = await borradorRendicion(ruta), h = copia ?? await cargar();
    if (h?.hoja.rendicion?.iniciada || r?.operacionId && r.resultado !== 'Rechazada')
        throw new Error('La ruta está en rendición. Ya no admite nuevas cargas.');
}
async function aceptarSinBorrador(op, respuesta, s) {
    const aplicada = ['Aplicada','YaAplicada'].includes(respuesta.resultado);
    const recibo = aplicada && op.tipo !== 'ReportarPosicion' ? await cifrar('operacion',op.operacionId,op,s) : null;
    const rechazo = !aplicada ? await cifrar('operacion', op.operacionId, { ...op,
        diagnostico:{ detalle:respuesta.detalle ?? null, bultos:respuesta.bultos ?? [] } }, s) : null;
    const db = await open();
    try {
        const tx = db.transaction(['espacios','operaciones','recibos'],'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'),s,() => {
            const store = tx.objectStore('operaciones'), r = store.get(op.operacionId);
            r.onsuccess = () => {
                if (!r.result) { tx.abort(); return; }
                if (aplicada) {
                    if (recibo) tx.objectStore('recibos').put({ ...recibo, estado:'Aplicada', secuencia:op.secuencia },op.operacionId);
                    store.delete(op.operacionId);
                } else store.put({ ...r.result, ...rechazo, estado:'Rechazada', motivo:respuesta.motivo },op.operacionId);
            };
        },tx);
        await done;
    } finally { db.close(); }
}
async function baseRendicion(ruta) {
    const s = await activo(), copia = await cargar();
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta || !copia.hoja.rendicion)
        throw new Error('Actualizá la hoja con conexión antes de preparar la rendición. La copia anterior sigue disponible.');
    const h = copia.hoja, bultos = structuredClone(h.rendicion.bultos), envios = h.paradas.flatMap(p => p.envios);
    if (h.rendicion.iniciada) return { bultos, dependencias:[], iniciada:h.rendicion.iniciada };
    const enCurso = (await estadoInicioRuta(ruta, copia)).iniciada;
    const dependencias = [], filas = await listaOperaciones(s, true);
    for (const fila of filas) {
        const op = await descifrar(fila, 'operacion', fila.meta.id, s);
        if (op.rutaId !== ruta || !['ConfirmarCarga','IniciarRuta',...TIPOS_EN_CALLE].includes(op.tipo)) continue;
        if (fila.estado === 'Rechazada') throw new Error('Revisá las operaciones rechazadas de la ruta antes de rendir.');
        dependencias.push(op.operacionId);
        if (op.tipo === 'IniciarRuta') continue;
        if (op.tipo === 'ConfirmarCarga') {
            const leidos = new Set(op.carga.lecturas.map(l => l.codigoBulto));
            for (const envio of envios) {
                const completos = envio.bultos.every(b => leidos.has(b.codigo));
                for (const b of bultos.filter(b => b.envioId === envio.id)) {
                    if (b.situacion !== 'NoCargado' && b.situacion !== 'CargaParcial') continue;
                    if (completos || op.carga.porBulto && envio.reglasEntrega.admiteEntregaParcial && leidos.has(b.codigo)) { b.situacion = 'NoIntentado'; b.requiereDeclaracion = true; }
                    else if (!op.carga.porBulto && leidos.has(b.codigo)) { b.situacion = 'CargaParcial'; b.requiereDeclaracion = true; }
                }
            }
        } else if (op.tipo === 'RegistrarVisita') {
            for (const resultado of op.visita.bultos) {
                const b = bultos.find(b => b.envioId === op.envioId && b.codigo === resultado.codigoBulto);
                if (!b) throw new Error('La visita contiene un bulto fuera de la rendición.');
                b.situacion = resultado.motivoId ? 'IntentoFallido' : 'EntregadoEnCalle';
                b.requiereDeclaracion = !!resultado.motivoId;
            }
        } else if (['RegistrarEntrega','RegistrarIntentoFallido'].includes(op.tipo)) {
            for (const b of bultos.filter(b => b.envioId === op.envioId)) {
                b.situacion = op.tipo === 'RegistrarEntrega' ? 'EntregadoEnCalle' : 'IntentoFallido';
                b.requiereDeclaracion = op.tipo !== 'RegistrarEntrega';
            }
        }
    }
    if (!enCurso) throw new Error('La ruta debe estar en curso antes de iniciar la rendición.');
    if (bultos.some(b => b.situacion === 'Revisar')) throw new Error('Hay envíos que requieren revisión antes de rendir. Actualizá la hoja.');
    return { bultos, dependencias, iniciada:h.rendicion.iniciada };
}
export async function abrirRendicion(ruta, revisar = false) {
    const s = await activo(), anterior = await borradorRendicion(ruta);
    const iniciada = (await cargar())?.hoja.rendicion?.iniciada;
    if (anterior?.operacionId && !revisar && (!iniciada || anterior.resultado === 'Aplicada')) return anterior;
    if (revisar && anterior?.operacionId && anterior.resultado !== 'Rechazada') throw new Error('Esta rendición no admite cambios.');
    const base = await baseRendicion(ruta);
    const recibidas = new Map(base.iniciada?.lineas.map(l => [l.bultoId, l]) ?? []);
    // Un borrador sin confirmar se recalcula al abrirlo: las entregas e intentos registrados después de abrir la rendición
    // cambian la situación de los bultos. Lo declarado se conserva sólo en los bultos cuya situación no cambió.
    const borrador = anterior && !anterior.operacionId ? anterior : null;
    const previas = new Map(borrador?.bultos.map(b => [b.bultoId, b]) ?? []);
    const previa = b => previas.get(b.bultoId)?.situacion === b.situacion ? previas.get(b.bultoId) : null;
    const valor = { tipoBorrador:'rendicion', rutaId:ruta, revision:(anterior?.revision ?? 0)+1,
        bultos:base.bultos.map(b => ({ ...b,
            devuelve:recibidas.get(b.bultoId)?.declarada ?? previa(b)?.devuelve ?? (b.requiereDeclaracion ? null : false),
            observacion:recibidas.get(b.bultoId)?.observacion ?? previa(b)?.observacion ?? null })), dependencias:base.dependencias,
        observaciones:base.iniciada?.observaciones ?? borrador?.observaciones ?? null, operacionId:base.iniciada?.id ?? null,
        resultado:base.iniciada ? 'Aplicada' : null, detalle:null };
    return escribirBorrador(valor, anterior?.revision ?? 0, s);
}
export async function declararBulto(ruta, revision, id, devuelve, observacion) {
    const s = await activo(), actual = await borradorRendicion(ruta);
    if (!actual || actual.revision !== revision || actual.operacionId || !actual.bultos.some(b => b.bultoId === id && b.requiereDeclaracion)
        || typeof devuelve !== 'boolean' || (observacion?.length ?? 0) > 1000)
        throw new Error('La rendición cambió o los datos no son válidos. Actualizá para revisar.');
    return escribirBorrador({ ...actual, revision:revision+1, bultos:actual.bultos.map(b => b.bultoId === id
        ? { ...b, devuelve, observacion:observacion?.trim() || null } : b) }, revision, s);
}
export async function observarRendicion(ruta, revision, observaciones) {
    const s = await activo(), actual = await borradorRendicion(ruta);
    if (!actual || actual.revision !== revision || actual.operacionId || (observaciones?.length ?? 0) > 2000)
        throw new Error('No se puede modificar esta rendición.');
    return escribirBorrador({ ...actual, revision:revision+1, observaciones:observaciones?.trim() || null }, revision, s);
}
export async function confirmarRendicion(ruta, revision) {
    const s = await activo(), actual = await borradorRendicion(ruta), base = await baseRendicion(ruta);
    if (!actual || actual.revision !== revision || actual.operacionId || base.iniciada)
        throw new Error('Esta rendición ya fue confirmada o cambió en otra pestaña.');
    const vigentes = new Map(base.bultos.map(b => [b.bultoId, b.situacion]));
    if (actual.bultos.length !== vigentes.size || actual.bultos.some(b => vigentes.get(b.bultoId) !== b.situacion))
        throw new Error('Cambió la ejecución de la ruta. Volvé a abrir la rendición para revisar las declaraciones.');
    if (actual.bultos.some(b => b.requiereDeclaracion && (typeof b.devuelve !== 'boolean' || !b.devuelve && !b.observacion?.trim())))
        throw new Error('Indicá qué bultos devolvés y explicá los que no vuelven.');
    const dispositivo = await instalacion();
    const op = { ...dispositivo, operacionId:uuidv7(), tipo:'IniciarRendicion', rutaId:ruta, envioId:null,
        ocurridoEn:new Date().toISOString(), latitud:null, longitud:null, correlacionId:traceparent(),
        rendicion:{ bultos:actual.bultos.map(b => ({ bultoId:b.bultoId, situacion:b.situacion, devuelve:b.devuelve, observacion:b.observacion })),
            dependencias:base.dependencias, observaciones:actual.observaciones } };
    return escribirBorrador({ ...actual, revision:revision+1, operacionId:op.operacionId, resultado:'Pendiente' }, revision, s, op);
}

// CU-17 y CU-18: la entrega y el intento fallido van a la misma cola cifrada, sin borrador; la hora y la posición son
// las del dispositivo (ADR-0004). Un registro por envío: si ya hay uno pendiente, aplicado o rechazado, no se agrega
// otro (un rechazo del servidor es definitivo y lo revisa despacho). Las reglas salen de la hoja: las de la versión del
// envío. El servidor vuelve a validar todo al sincronizar.
const TIPOS_EN_CALLE = ['RegistrarVisita','RegistrarEntrega','RegistrarIntentoFallido'];
const MAX_ARCHIVO = 1024 * 1024;

// RF 32: el conjunto de la visita se obtiene del manifiesto y de las cargas confirmadas localmente.
// Nunca convertir los no cargados en intentos fallidos ni incluir los ya resueltos en una nueva firma.
async function contextoVisita(ruta, envioId) {
    const s = await activo(), copia = await cargar();
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta) throw new Error('Abrí la hoja de esta ruta antes de registrar.');
    await exigirJornadaAbierta(ruta, copia);
    await exigirRutaIniciada(ruta, copia);
    const envio = copia.hoja.paradas.flatMap(p => p.envios).find(e => e.id === envioId);
    if (!envio) throw new Error('El envío no está en esta ruta.');
    const operaciones = await operacionesDeRuta(s, ruta);
    if (operaciones.some(r => TIPOS_EN_CALLE.includes(r.op.tipo) && r.op.envioId === envioId))
        throw new Error('Este envío ya está registrado en este dispositivo.');
    const cargados = new Set(operaciones.filter(r => r.op.tipo === 'ConfirmarCarga' && r.estado !== 'Rechazada')
        .flatMap(r => r.op.carga.lecturas.map(l => l.codigoBulto)));
    const asignados = envio.bultos.filter(b => (b.estado || envio.estado) === 'AsignadoARuta');
    const cargaCompleta = asignados.length > 0 && asignados.every(b => cargados.has(b.codigo));
    const alcance = envio.bultos.filter(b => (b.estado || envio.estado) === 'EnTransito'
        || (b.estado || envio.estado) === 'AsignadoARuta' && cargados.has(b.codigo)
            && (envio.reglasEntrega.admiteEntregaParcial === true || cargaCompleta));
    if (!alcance.length) throw new Error('No hay bultos cargados para registrar esta visita.');
    return { s, envio, alcance };
}
export async function abrirVisita(ruta, envioId) {
    const { s, envio, alcance } = await contextoVisita(ruta, envioId);
    const tipoBorrador = 'visita:' + envioId;
    const anterior = await leerBorrador(ruta, tipoBorrador);
    const codigos = alcance.map(b => b.codigo);
    if (anterior && !anterior.operacionId && Array.isArray(anterior.bultos) && anterior.bultos.length === codigos.length
        && anterior.bultos.every(b => codigos.includes(b.codigoBulto))) return anterior;
    const valor = { tipoBorrador, rutaId:ruta, envioId, revision:(anterior?.revision ?? 0)+1, operacionId:null,
        bultos:alcance.map(b => ({ codigoBulto:b.codigo, entregado:false, motivoId:null })),
        excluidos:envio.bultos.filter(b => !codigos.includes(b.codigo)).map(b => ({ codigoBulto:b.codigo, estado:b.estado || envio.estado })) };
    return escribirBorrador(valor, anterior?.revision ?? 0, s);
}
export async function resultadoVisita(ruta, envioId, revision, codigo, entregado, motivoId) {
    const { s, envio } = await contextoVisita(ruta, envioId);
    const actual = await leerBorrador(ruta, 'visita:' + envioId);
    codigo = codigo.trim();
    if (!actual || actual.operacionId || actual.revision !== revision || !actual.bultos.some(b => b.codigoBulto === codigo))
        throw new Error('El bulto no pertenece a esta visita o el borrador cambió.');
    if (entregado && actual.bultos.find(b => b.codigoBulto === codigo).entregado) throw new Error('Ese bulto ya fue escaneado.');
    if (motivoId && !envio.reglasEntrega.motivosNoEntrega.some(m => m.id === motivoId)) throw new Error('Elegí un motivo del catálogo.');
    return escribirBorrador({ ...actual, revision:revision+1,
        bultos:actual.bultos.map(b => b.codigoBulto === codigo ? { codigoBulto:codigo, entregado:!!entregado, motivoId:entregado ? null : motivoId || null } : b)
    }, revision, s);
}
export async function registrarVisita(ruta, envioId, revision, datos) {
    const { s, envio, alcance } = await contextoVisita(ruta, envioId);
    const actual = await leerBorrador(ruta, 'visita:' + envioId);
    if (!actual || actual.operacionId || actual.revision !== revision || actual.bultos.length !== alcance.length
        || actual.bultos.some(b => !alcance.some(a => a.codigo === b.codigoBulto))) throw new Error('La visita cambió. Revisá los bultos.');
    if (actual.bultos.some(b => !b.entregado && !b.motivoId)) throw new Error('Escaneá cada entregado o elegí su motivo de no entrega.');
    const entregados = actual.bultos.filter(b => b.entregado).length, reglas = envio.reglasEntrega;
    if (entregados && reglas.admiteEntregaParcial !== true && (entregados !== actual.bultos.length
        || envio.bultos.some(b => !['Entregado','Devuelto','Extraviado','Cancelado'].includes(b.estado || envio.estado)
            && !alcance.some(a => a.codigo === b.codigo)))) throw new Error('Este operador no admite entrega parcial: se entregan todos los pendientes o ninguno.');
    const firma = entregados ? archivo(datos?.firma, 'La firma') : null;
    if (entregados && !firma) throw new Error('La firma de los bultos entregados es obligatoria.');
    const foto = archivo(datos?.foto, 'La foto');
    if (!foto && (entregados && reglas.exigeFoto || actual.bultos.some(b => b.motivoId
        && reglas.motivosNoEntrega.find(m => m.id === b.motivoId)?.exigeEvidencia))) throw new Error('Esta visita exige una foto como evidencia.');
    const nombre = entregados ? datos?.nombreReceptor?.trim() || null : null;
    const documento = entregados ? datos?.documentoReceptor?.trim() || null : null;
    if (entregados && reglas.exigeNombreYDocumento && (!nombre || !documento)) throw new Error('Completá el nombre y documento de quien recibe.');
    const observaciones = datos?.observaciones?.trim() || null;
    if ((nombre?.length ?? 0) > 200 || (documento?.length ?? 0) > 50 || (observaciones?.length ?? 0) > 1000) throw new Error('Los datos superan el largo permitido.');
    if (!coordenada(datos?.latitud,90) || !coordenada(datos?.longitud,180)) throw new Error('Activá la ubicación para registrar la visita.');
    const dispositivo = await instalacion();
    const op = { ...dispositivo, operacionId:uuidv7(), tipo:'RegistrarVisita', rutaId:ruta, envioId,
        ocurridoEn:new Date().toISOString(), latitud:Math.round(datos.latitud*1e6)/1e6, longitud:Math.round(datos.longitud*1e6)/1e6,
        correlacionId:traceparent(), visita:{ bultos:actual.bultos.map(b => ({ codigoBulto:b.codigoBulto, motivoId:b.entregado ? null : b.motivoId })),
            firma, foto, nombreReceptor:nombre, documentoReceptor:documento, observaciones } };
    await escribirBorrador({ ...actual, revision:revision+1, operacionId:op.operacionId }, revision, s, op);
    return { envioId, tipo:op.tipo, estado:'Pendiente', motivo:null };
}
async function operacionesDeRuta(s, ruta) {
    const lista = [];
    for (const fila of await listaOperaciones(s, true)) {
        const op = await descifrar(fila, 'operacion', fila.meta.id, s);
        if (op.rutaId === ruta) lista.push({ op, estado:fila.estado, motivo:fila.motivo ?? null });
    }
    return lista;
}
export async function estadosEnvios(ruta) {
    const s = await sesionLocal();
    if (!s) return [];
    return (await operacionesDeRuta(s, ruta)).filter(r => TIPOS_EN_CALLE.includes(r.op.tipo))
        .map(r => ({ envioId:r.op.envioId, tipo:r.op.tipo, estado:r.estado, motivo:r.motivo, ...(r.op.visita ? { resultados:r.op.visita.bultos } : {}), ...(r.op.diagnostico ? { detalle:r.op.diagnostico.detalle, bultos:r.op.diagnostico.bultos } : {}) }));
}
async function envioParaRegistrar(ruta, envioId) {
    const s = await activo(), copia = await cargar();
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta) throw new Error('Abrí la hoja de esta ruta antes de registrar.');
    await exigirJornadaAbierta(ruta, copia);
    await exigirRutaIniciada(ruta, copia);
    const envio = copia.hoja.paradas.flatMap(p => p.envios).find(e => e.id === envioId);
    if (!envio) throw new Error('El envío no está en esta ruta.');
    const operaciones = await operacionesDeRuta(s, ruta);
    if (operaciones.some(r => TIPOS_EN_CALLE.includes(r.op.tipo) && r.op.envioId === envioId))
        throw new Error('Este envío ya está registrado en este dispositivo.');
    // Sin conexión la hoja no se actualiza: un envío AsignadoARuta salió si una carga de este dispositivo leyó todos
    // sus bultos.
    const cargado = operaciones.some(r => r.op.tipo === 'ConfirmarCarga' && r.estado !== 'Rechazada'
        && envio.bultos.every(b => r.op.carga.lecturas.some(l => l.codigoBulto === b.codigo)));
    if (envio.estado !== 'EnTransito' && !(envio.estado === 'AsignadoARuta' && cargado))
        throw new Error('Este envío no salió en el vehículo: sólo se registran envíos en tránsito.');
    return { s, envio };
}
function archivo(a, nombre) {
    if (!a) return null;
    if (!['image/png','image/jpeg'].includes(a.tipoContenido) || !text(a.contenidoBase64)
        || Math.floor(a.contenidoBase64.length * 3 / 4) > MAX_ARCHIVO)
        throw new Error(`${nombre} debe ser una imagen PNG o JPEG de hasta 1 MB.`);
    return { tipoContenido:a.tipoContenido, contenidoBase64:a.contenidoBase64 };
}
function coordenada(valor, maximo) { return typeof valor === 'number' && Number.isFinite(valor) && Math.abs(valor) <= maximo; }
async function encolar(s, ruta, envioId, posicion, tipo, datos) {
    if (!coordenada(posicion?.latitud, 90) || !coordenada(posicion?.longitud, 180))
        throw new Error('No pudimos obtener tu ubicación. Activá la ubicación del dispositivo e intentá de nuevo.');
    const dispositivo = await instalacion(), redondear = v => Math.round(v * 1e6) / 1e6;
    const op = { ...dispositivo, operacionId:uuidv7(), tipo, rutaId:ruta, envioId, ocurridoEn:new Date().toISOString(),
        latitud:redondear(posicion.latitud), longitud:redondear(posicion.longitud), correlacionId:traceparent(), ...datos };
    const registro = await cifrar('operacion', op.operacionId, op, s), db = await open();
    try {
        const tx = db.transaction(['espacios','operaciones'],'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'), s, () => tx.objectStore('operaciones')
            .add({ ...registro, estado:'Pendiente', secuencia:op.secuencia }, op.operacionId), tx);
        try { await done; } catch { throw new Error('No se pudo guardar: cambió la sesión o no hay espacio en el dispositivo.'); }
    } finally { db.close(); }
    return { envioId, tipo, estado:'Pendiente', motivo:null };
}
export async function registrarEntrega(ruta, envioId, datos) {
    const { s, envio } = await envioParaRegistrar(ruta, envioId), reglas = envio.reglasEntrega;
    const firma = archivo(datos?.firma, 'La firma');
    if (!firma) throw new Error('La firma de quien recibe es obligatoria.');
    const foto = archivo(datos?.foto, 'La foto');
    if (reglas.exigeFoto && !foto) throw new Error('Este envío exige una foto de la entrega.');
    const nombre = datos?.nombreReceptor?.trim() || null, documento = datos?.documentoReceptor?.trim() || null;
    if (reglas.exigeNombreYDocumento && (!nombre || !documento))
        throw new Error('Este envío exige el nombre y el documento de quien recibe.');
    if ((nombre?.length ?? 0) > 200 || (documento?.length ?? 0) > 50) throw new Error('El nombre o el documento son demasiado largos.');
    return encolar(s, ruta, envioId, datos, 'RegistrarEntrega',
        { entrega:{ firma, foto, nombreReceptor:nombre, documentoReceptor:documento } });
}
export async function registrarIntentoFallido(ruta, envioId, datos) {
    const { s, envio } = await envioParaRegistrar(ruta, envioId);
    const motivo = envio.reglasEntrega.motivosNoEntrega.find(m => m.id === datos?.motivoId);
    if (!motivo) throw new Error('Elegí el motivo de la lista.');
    const foto = archivo(datos?.foto, 'La foto');
    if (motivo.exigeEvidencia && !foto) throw new Error(`El motivo «${motivo.nombreParaMostrar}» exige una foto como evidencia.`);
    const observaciones = datos?.observaciones?.trim() || null;
    if ((observaciones?.length ?? 0) > 1000) throw new Error('Las observaciones no pueden superar los 1000 caracteres.');
    return encolar(s, ruta, envioId, datos, 'RegistrarIntentoFallido',
        { intentoFallido:{ motivoId:motivo.id, foto, observaciones } });
}

// CU-53: la ruta operativa es independiente de la pantalla abierta. Una rendición confirmada detiene el GPS.
export async function rutaParaSeguimiento() {
    const s = await sesionLocal();
    if (!s) return null;
    const candidatas = [];
    for (const copia of await rutasGuardadas()) {
        const h = copia.hoja, rendicion = await borradorRendicion(h.rutaId);
        if (copia.sinRuta || h.rendicion?.iniciada || rendicion?.operacionId
            || await read('instalacion',`gps-cerrada:${key(s)}:${h.rutaId}`)) continue;
        if ((await estadoInicioRuta(h.rutaId, copia)).iniciada) candidatas.push(h.rutaId);
    }
    if ((await sesionLocal())?.token !== s.token) return null;
    if (candidatas.length > 1) throw new Error('Hay más de una ruta en curso. Actualizá tus rutas para habilitar el seguimiento.');
    return candidatas.length ? { rutaId:candidatas[0], token:s.token } : null;
}

export async function guardarPosicion(rutaId, token, posicion) {
    const s = await activo(), actual = await rutaParaSeguimiento();
    if (s.token !== token || actual?.rutaId !== rutaId || actual.token !== token) return false;
    if (!coordenada(posicion?.latitud,90) || !coordenada(posicion?.longitud,180)
        || !Number.isFinite(posicion.capturadaEn) || Math.abs(Date.now()-posicion.capturadaEn) > 15000)
        throw new Error('La ubicación no es reciente. Se reintentará automáticamente.');
    const dispositivo = await instalacion(), redondear = v => Math.round(v*1e6)/1e6;
    const op = { ...dispositivo, operacionId:uuidv7(), tipo:'ReportarPosicion', rutaId, envioId:null,
        ocurridoEn:new Date(posicion.capturadaEn).toISOString(), latitud:redondear(posicion.latitud),
        longitud:redondear(posicion.longitud), correlacionId:traceparent() };
    const registro = await cifrar('operacion',op.operacionId,op,s), db = await open();
    let guardada = false;
    try {
        // Serializa pestañas y rendición, incluso sin Web Locks. La ubicación completa queda cifrada.
        const tx = db.transaction(['espacios','operaciones','borradores','instalacion'],'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'),s,() => {
            const r = tx.objectStore('borradores').get(draftKey(s,rutaId,'rendicion'));
            r.onsuccess = () => {
                if (r.result?.bloqueaRuta) return;
                const id = `gps:${key(s)}:${rutaId}`, ultima = tx.objectStore('instalacion').get(id);
                ultima.onsuccess = () => {
                    if (ultima.result && Date.now()-ultima.result < 30000) return;
                    tx.objectStore('instalacion').put(Date.now(),id);
                    tx.objectStore('operaciones').add({ ...registro, estado:'Pendiente', secuencia:op.secuencia },op.operacionId);
                    guardada = true;
                };
            };
        },tx);
        await done;
    } finally { db.close(); }
    return guardada;
}
