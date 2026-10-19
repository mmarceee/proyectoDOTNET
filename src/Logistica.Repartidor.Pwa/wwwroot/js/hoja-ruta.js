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
    if (![1,2].includes(h.schemaVersion) || !guid(h.rutaId) || !Number.isSafeInteger(h.revisionRuta) || h.revisionRuta < 0
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
    if (h.schemaVersion === 2) {
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
        const tx = db.transaction(['espacios', 'hojas'], 'readwrite'), done = completed(tx);
        const r = tx.objectStore('espacios').get('activo');
        r.onsuccess = () => {
            if (!r.result || r.result.token !== token || key(r.result) !== key(hoja)) { tx.abort(); return; }
            const hojas = tx.objectStore('hojas'), anterior = hojas.get(key(sesion));
            anterior.onsuccess = () => {
                if (anterior.result && anterior.result.meta.rutaId !== hoja.rutaId)
                    hojas.put(anterior.result, `${key(sesion)}:ruta:${anterior.result.meta.rutaId}`);
                hojas.put({ meta, iv, contenido, descargadaEn: new Date().toISOString() }, key(sesion));
                hojas.delete(`${key(sesion)}:ruta:${hoja.rutaId}`);
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
        const tx = db.transaction('espacios', 'readwrite'), done = completed(tx);
        const store = tx.objectStore('espacios'), r = store.get('activo');
        r.onsuccess = () => {
            if (!r.result || r.result.token !== token) { tx.abort(); return; }
            store.put({ ...r.result, sinRuta: true }, 'activo');
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
function draftKey(s, ruta, tipo = 'carga') { return `${key(s)}:${ruta}${tipo === 'rendicion' ? ':rendicion' : ''}`; }
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
    const valor = await descifrar(await read('borradores', id), 'borrador', id, s);
    if ((await activo()).token !== s.token) throw new Error('La sesión cambió.');
    return valor;
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
                    store.put({ ...registro, revision:valor.revision, bloqueaRuta:valor.tipoBorrador === 'rendicion' && !!valor.operacionId && valor.resultado !== 'Rechazada' }, id);
                    if (op) tx.objectStore('operaciones').add({ ...op, estado:'Pendiente', secuencia:operacion.secuencia }, operacion.operacionId);
                };
                if (operacion?.tipo === 'ConfirmarCarga') {
                    const rendicion = store.get(draftKey(s, valor.rutaId, 'rendicion'));
                    rendicion.onsuccess = () => { if (rendicion.result?.bloqueaRuta) tx.abort(); else escribir(); };
                } else escribir();
            };
        }, tx);
        try { await done; } catch { throw new Error('No se pudo guardar: la carga cambió en otra pestaña, cambió la sesión o no hay espacio. Actualizá para revisar.'); }
    } finally { db.close(); }
    return valor;
}
export async function abrirCarga(ruta) {
    const s = await activo(), copia = await cargar();
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta || copia.hoja.fechaRuta > s.fechaOperativa)
        throw new Error('Descargá una ruta asignada cuya fecha ya haya llegado antes de iniciar la carga.');
    await exigirJornadaAbierta(ruta, copia);
    const anterior = await borradorCarga(ruta);
    if (anterior && (!anterior.resultado || anterior.resultado === 'Rechazada')) return anterior;
    if (anterior && anterior.revisionRuta === copia.hoja.revisionRuta) return anterior;
    const pendientes = new Set(copia.hoja.paradas.flatMap(p => p.envios).filter(e => e.estado === 'AsignadoARuta')
        .flatMap(e => e.bultos).map(b => b.codigo));
    const valor = { rutaId:ruta, revisionRuta:copia.hoja.revisionRuta, revision:(anterior?.revision ?? 0)+1,
        lecturas:anterior?.resultado === 'Aplicada' ? anterior.lecturas.filter(l => pendientes.has(l.codigoBulto)) : [],
        operacionId:null, resultado:null, detalle:null };
    return escribirBorrador(valor, anterior?.revision ?? 0, s);
}
export async function escanearCarga(ruta, revision, codigo) {
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
    await exigirJornadaAbierta(ruta);
    const s = await activo(), actual = await borradorCarga(ruta), copia = await cargar();
    if (!actual || actual.operacionId || actual.revision !== revision || !actual.lecturas.length)
        throw new Error('Revisá el avance de carga antes de confirmar.');
    if (!copia || copia.sinRuta || copia.hoja.rutaId !== ruta || copia.hoja.revisionRuta !== actual.revisionRuta)
        throw new Error('La hoja cambió. Revisá la carga antes de confirmar.');
    const candidatos = copia.hoja.paradas.flatMap(p => p.envios).filter(e => e.estado === 'AsignadoARuta');
    const codigos = new Set(actual.lecturas.map(l => l.codigoBulto));
    if (!aceptaFaltantes && candidatos.some(e => e.bultos.some(b => !codigos.has(b.codigo))))
        throw new Error('Confirmá expresamente que vas a continuar con faltantes.');
    const dispositivo = await instalacion();
    const op = { ...dispositivo, operacionId:uuidv7(), tipo:'ConfirmarCarga', rutaId:ruta, envioId:null,
        ocurridoEn:new Date().toISOString(), latitud:null, longitud:null, correlacionId:traceparent(),
        carga:{ revisionRuta:actual.revisionRuta, lecturas:actual.lecturas, aceptaFaltantes } };
    return escribirBorrador({ ...actual, revision:revision+1, operacionId:op.operacionId, resultado:'Pendiente' }, revision, s, op);
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
    const tipo = op.tipo === 'IniciarRendicion' ? 'rendicion' : 'carga';
    const id = draftKey(s, op.rutaId, tipo), anterior = await leerBorrador(op.rutaId, tipo);
    if (!['ConfirmarCarga','IniciarRendicion'].includes(op.tipo)) return aceptarSinBorrador(op, respuesta, s);
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
                borradores.put({ ...cifrada, revision:valor.revision, bloqueaRuta:tipo === 'rendicion' && aplicada },id);
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
    const response = await fetch('/api/repartidor/sincronizacion', { method:'POST', credentials:'same-origin',
        headers:{'Content-Type':'application/json'}, body:JSON.stringify({ operaciones }), signal:AbortSignal.timeout(15000) });
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
export async function sincronizarCargas() {
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

// CU-54: mismo almacén cifrado y mismo transporte, con un borrador separado del de carga.
export async function borradorRendicion(ruta) { return leerBorrador(ruta, 'rendicion'); }
async function exigirJornadaAbierta(ruta, copia = null) {
    const r = await borradorRendicion(ruta), h = copia ?? await cargar();
    if (h?.hoja.rendicion?.iniciada || r?.operacionId && r.resultado !== 'Rechazada')
        throw new Error('La ruta está en rendición. Ya no admite nuevas cargas.');
}
async function aceptarSinBorrador(op, respuesta, s) {
    const aplicada = ['Aplicada','YaAplicada'].includes(respuesta.resultado);
    const recibo = aplicada ? await cifrar('operacion',op.operacionId,op,s) : null, db = await open();
    try {
        const tx = db.transaction(['espacios','operaciones','recibos'],'readwrite'), done = completed(tx);
        comprobarToken(tx.objectStore('espacios'),s,() => {
            const store = tx.objectStore('operaciones'), r = store.get(op.operacionId);
            r.onsuccess = () => {
                if (!r.result) { tx.abort(); return; }
                if (aplicada) {
                    tx.objectStore('recibos').put({ ...recibo, estado:'Aplicada', secuencia:op.secuencia },op.operacionId);
                    store.delete(op.operacionId);
                } else store.put({ ...r.result, estado:'Rechazada', motivo:respuesta.motivo },op.operacionId);
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
    let enCurso = h.estadoRuta === 'EnCurso';
    const dependencias = [], filas = await listaOperaciones(s, true);
    for (const fila of filas) {
        const op = await descifrar(fila, 'operacion', fila.meta.id, s);
        if (op.rutaId !== ruta || !['ConfirmarCarga','RegistrarEntrega','RegistrarIntentoFallido'].includes(op.tipo)) continue;
        if (fila.estado === 'Rechazada') throw new Error('Revisá las operaciones rechazadas de la ruta antes de rendir.');
        dependencias.push(op.operacionId);
        if (op.tipo === 'ConfirmarCarga') {
            const leidos = new Set(op.carga.lecturas.map(l => l.codigoBulto));
            for (const envio of envios) {
                const completos = envio.bultos.every(b => leidos.has(b.codigo));
                for (const b of bultos.filter(b => b.envioId === envio.id)) {
                    if (b.situacion !== 'NoCargado' && b.situacion !== 'CargaParcial') continue;
                    if (completos) { b.situacion = 'NoIntentado'; b.requiereDeclaracion = true; enCurso = true; }
                    else if (leidos.has(b.codigo)) { b.situacion = 'CargaParcial'; b.requiereDeclaracion = true; }
                }
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
    if (anterior && !revisar && (!iniciada || anterior.resultado === 'Aplicada')) return anterior;
    if (revisar && anterior?.operacionId && anterior.resultado !== 'Rechazada') throw new Error('Esta rendición no admite cambios.');
    const base = await baseRendicion(ruta);
    const recibidas = new Map(base.iniciada?.lineas.map(l => [l.bultoId, l]) ?? []);
    const valor = { tipoBorrador:'rendicion', rutaId:ruta, revision:(anterior?.revision ?? 0)+1,
        bultos:base.bultos.map(b => ({ ...b, devuelve:recibidas.get(b.bultoId)?.declarada ?? (b.requiereDeclaracion ? null : false),
            observacion:recibidas.get(b.bultoId)?.observacion ?? null })), dependencias:base.dependencias,
        observaciones:base.iniciada?.observaciones ?? null, operacionId:base.iniciada?.id ?? null,
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
