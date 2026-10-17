// CU-50: sólo este módulo persiste la hoja personal. Cache Storage guarda únicamente el shell.
const DB = 'logistica-repartidor-v1';
const STORES = ['espacios', 'claves', 'hojas'];
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
    if (h.schemaVersion !== 1 || !guid(h.rutaId) || !Number.isSafeInteger(h.revisionRuta) || h.revisionRuta < 0
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
            envios.add(e.id);
            for (const b of e.bultos) {
                if (!guid(b.id) || !text(b.codigo) || bultos.has(b.id)) throw new Error('La hoja contiene bultos inválidos.');
                bultos.add(b.id);
            }
        }
    }
}
function open() {
    if (!globalThis.indexedDB || !globalThis.crypto?.subtle || !globalThis.isSecureContext)
        return Promise.reject(new Error('Este navegador necesita almacenamiento local y un contexto seguro para guardar la ruta.'));
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB, 1);
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
        const tx = db.transaction(['espacios', 'hojas'], 'readwrite'), done = completed(tx);
        const r = tx.objectStore('espacios').get('activo');
        r.onsuccess = () => {
            if (!r.result || r.result.token !== token || key(r.result) !== key(hoja)) { tx.abort(); return; }
            tx.objectStore('hojas').put({ meta, iv, contenido, descargadaEn: new Date().toISOString() }, key(sesion));
            tx.objectStore('espacios').put({ ...r.result, sinRuta: false }, 'activo');
        };
        await done;
    } finally { db.close(); }
    return cargar();
}
export async function sinRuta(token) {
    const db = await open();
    try {
        const tx = db.transaction('espacios', 'readwrite'), done = completed(tx);
        const store = tx.objectStore('espacios'), r = store.get('activo');
        r.onsuccess = () => {
            if (!r.result || r.result.token !== token) { tx.abort(); return; }
            store.put({ ...r.result, sinRuta: true }, 'activo');
        };
        await done;
    } finally { db.close(); }
}
export async function cargar() {
    const sesion = await sesionLocal();
    if (!sesion) return null;
    const guardada = await read('hojas', key(sesion));
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
    return { hoja, descargadaEn: guardada.descargadaEn, sinRuta: actual.sinRuta === true };
}
