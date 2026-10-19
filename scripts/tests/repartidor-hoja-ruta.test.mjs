import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';
import { IDBFactory, IDBObjectStore } from 'fake-indexeddb';

Object.defineProperty(globalThis, 'crypto', { value: webcrypto, configurable: true });
globalThis.isSecureContext = true;
globalThis.BroadcastChannel = class { postMessage() {} };
const source = await readFile(new URL('../../src/Logistica.Repartidor.Pwa/wwwroot/js/hoja-ruta.js', import.meta.url), 'utf8');
const store = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
beforeEach(() => { globalThis.indexedDB = new IDBFactory(); });
const id = n => '10000000-0000-4000-8000-' + String(n).padStart(12, '0');
const sesion = (n = 1) => ({ usuarioId: id(n), operadorId: id(2), repartidorId: id(n + 2),
    fechaOperativa: '2026-10-17', zonaHorariaOperador: 'America/Montevideo', verificadaEn: '2026-10-17T12:00:00Z' });
function hoja(n = 1) {
    const s = sesion(n);
    return { ...s, schemaVersion: 1, rutaId: id(10), revisionRuta: 4, fechaRuta: s.fechaOperativa,
        estadoRuta: 'Despachada', generadaEn: s.verificadaEn, vehiculo: { id: id(11), matricula: 'ABC1234' },
        zonas: [{ id: id(12), codigo: 'MVD', nombre: 'Montevideo' }],
        paradas: [{ id: id(13), orden: 1, estado: 'Pendiente', direccion: 'Calle privada 10', desde: null, hasta: null,
            envios: [{ id: id(14), numero: 'ENV-001', estado: 'AsignadoARuta', destinatarioNombre: 'Nombre privado',
                destinatarioTelefono: '099987654', direccion: { calle: 'Calle privada', numero: '10', localidad: 'Montevideo',
                    departamento: 'Montevideo', codigoPostal: '11300', referencia: 'Referencia privada' },
                franja: null, bultos: [{ id: id(15), codigo: 'ENV-001-1' }],
                reglasEntrega: { origen: 'Provisorias', versionId: null, firmaObligatoria: true, exigeFoto: false,
                    exigeNombreYDocumento: false, maxIntentos: 3, plazoEntreIntentos: '1.00:00:00',
                    motivosNoEntrega: [{ id: id(16), codigo: 'AUSENTE', nombreParaMostrar: 'Ausente', reprogramable: true, exigeEvidencia: false }] }
            }] }]
    };
}
async function db() {
    return new Promise((resolve, reject) => {
        const r = indexedDB.open('logistica-repartidor-v1');
        r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error);
    });
}
async function registros(nombre) {
    const conexion = await db();
    try {
        return await new Promise((resolve, reject) => {
            const r = conexion.transaction(nombre).objectStore(nombre).getAll();
            r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error);
        });
    } finally { conexion.close(); }
}
test('guarda, cifra y recupera la hoja sin información personal en claro', async () => {
    const token = await store.activar(sesion());
    const copia = await store.guardar(hoja(), token);
    assert.deepEqual(copia.hoja, hoja());
    const cifradas = await registros('hojas');
    const disco = JSON.stringify(cifradas);
    for (const secreto of ['Nombre privado', '099987654', 'Calle privada', 'Referencia privada', 'ENV-001-1'])
        assert.equal(disco.includes(secreto), false);
    assert.ok(cifradas[0].contenido instanceof ArrayBuffer);
    const claves = await registros('claves');
    assert.equal(claves[0].extractable, false);
    await assert.rejects(crypto.subtle.exportKey('raw', claves[0]));
    assert.equal((await store.cargar()).hoja.rutaId, id(10));
});
test('cada escritura usa un IV nuevo y reemplaza solamente la hoja', async () => {
    const token = await store.activar(sesion());
    await store.guardar(hoja(), token);
    const anterior = (await registros('hojas'))[0].iv;
    await store.guardar(hoja(), token);
    const actuales = await registros('hojas');
    assert.equal(actuales.length, 1);
    assert.notDeepEqual(actuales[0].iv, anterior);
});
test('documento incompleto no reemplaza la copia anterior', async () => {
    const token = await store.activar(sesion());
    await store.guardar(hoja(), token);
    const incompleta = hoja(); incompleta.paradas[0].envios[0].bultos = [];
    await assert.rejects(store.guardar(incompleta, token));
    assert.deepEqual((await store.cargar()).hoja, hoja());
});
test('fallo de escritura conserva la copia completa anterior', async () => {
    const token = await store.activar(sesion());
    await store.guardar(hoja(), token);
    const put = IDBObjectStore.prototype.put;
    IDBObjectStore.prototype.put = function(...args) {
        if (this.name === 'hojas') { this.transaction.abort(); return; }
        return put.apply(this, args);
    };
    try { await assert.rejects(store.guardar(hoja(), token)); }
    finally { IDBObjectStore.prototype.put = put; }
    assert.deepEqual((await store.cargar()).hoja, hoja());
});
test('cerrar y cambiar de cuenta no exponen la hoja anterior', async () => {
    const token = await store.activar(sesion());
    await store.guardar(hoja(), token);
    await store.bloquear();
    assert.equal(await store.cargar(), null);
    assert.equal((await registros('hojas')).length, 1);
    await store.activar(sesion(20));
    assert.equal(await store.cargar(), null);
    await store.activar(sesion());
    assert.deepEqual((await store.cargar()).hoja, hoja());
});
test('una descarga anterior al cierre no puede reactivar la copia', async () => {
    const token = await store.activar(sesion());
    await store.bloquear();
    await assert.rejects(store.guardar(hoja(), token));
    assert.equal(await store.cargar(), null);
});
test('ciphertext alterado se rechaza sin mostrar datos parciales', async () => {
    const token = await store.activar(sesion());
    await store.guardar(hoja(), token);
    const conexion = await db();
    await new Promise((resolve, reject) => {
        const tx = conexion.transaction('hojas', 'readwrite'), s = tx.objectStore('hojas'), r = s.openCursor();
        r.onsuccess = () => {
            const cursor = r.result;
            const valor = cursor.value;
            new Uint8Array(valor.contenido)[0] ^= 1;
            cursor.update(valor);
        };
        tx.oncomplete = resolve; tx.onabort = () => reject(tx.error);
    });
    conexion.close();
    await assert.rejects(store.cargar());
});
test('204 conserva la copia identificada como anterior', async () => {
    const token = await store.activar(sesion());
    await store.guardar(hoja(), token);
    await store.sinRuta(token);
    await store.activar(sesion());
    const anterior = await store.cargar();
    assert.equal(anterior.sinRuta, true);
    assert.deepEqual(anterior.hoja, hoja());
});
test('dos guardados concurrentes comparten clave y dejan una copia legible', async () => {
    const token = await store.activar(sesion());
    await Promise.all([store.guardar(hoja(), token), store.guardar(hoja(), token)]);
    assert.equal((await registros('claves')).length, 1);
    assert.deepEqual((await store.cargar()).hoja, hoja());
});

async function prepararCarga() {
    const token=await store.activar(sesion()); await store.guardar(hoja(),token);
    return store.abrirCarga(hoja().rutaId);
}
test('CU-51 guarda borrador cifrado, avisa repetidos y conserva traceparent/UUIDv7 al confirmar',async()=>{
    let b=await prepararCarga();
    b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    await assert.rejects(store.escanearCarga(b.rutaId,b.revision,'ENV-001-1'),/ya fue escaneado/);
    assert.deepEqual((await store.borradorCarga(b.rutaId)).lecturas,b.lecturas);
    b=await store.confirmarCarga(b.rutaId,b.revision,false);
    const [op]=await store.pendientesCarga();
    assert.match(op.operacionId,/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    assert.match(op.correlacionId,/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
    assert.notEqual(op.correlacionId,store.traceparent());
    assert.equal(op.operacionId,b.operacionId);
    const disco=JSON.stringify([await registros('borradores'),await registros('operaciones')]);
    assert.equal(disco.includes('ENV-001-1'),false);
    assert.equal(disco.includes(op.correlacionId),false);
    await assert.rejects(store.confirmarCarga(b.rutaId,b.revision,false),/Revisá el avance/);
});
test('CU-51 dos pestañas no pierden lecturas ni confirman dos veces el mismo borrador',async()=>{
    let b=await prepararCarga();
    const intentos=await Promise.allSettled([
        store.escanearCarga(b.rutaId,b.revision,'ENV-001-1'),store.escanearCarga(b.rutaId,b.revision,'SOBRANTE')]);
    assert.equal(intentos.filter(r=>r.status==='fulfilled').length,1);
    b=await store.borradorCarga(b.rutaId);
    const confirmaciones=await Promise.allSettled([
        store.confirmarCarga(b.rutaId,b.revision,true),store.confirmarCarga(b.rutaId,b.revision,true)]);
    assert.equal(confirmaciones.filter(r=>r.status==='fulfilled').length,1);
    assert.equal((await store.pendientesCarga()).length,1);
});
test('CU-51 faltantes requieren confirmación y sobrantes no completan el envío',async()=>{
    let b=await prepararCarga(); b=await store.escanearCarga(b.rutaId,b.revision,'SOBRANTE');
    await assert.rejects(store.confirmarCarga(b.rutaId,b.revision,false),/faltantes/);
    await store.confirmarCarga(b.rutaId,b.revision,true);
    assert.equal((await store.pendientesCarga())[0].carga.aceptaFaltantes,true);
});
test('CU-52 un corte conserva exactamente la operación y un ack confirmado la elimina',async()=>{
    let b=await prepararCarga(); b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    await store.confirmarCarga(b.rutaId,b.revision,false);
    const original=(await store.pendientesCarga())[0], fetchAnterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>{ throw new TypeError('sin red'); };
        await assert.rejects(store.sincronizarCargas(),/pendiente de sincronizar/);
        assert.deepEqual((await store.pendientesCarga())[0],original);
        globalThis.fetch=async(url,request)=>{
            assert.equal(url,'/api/repartidor/sincronizacion');
            assert.deepEqual(JSON.parse(request.body).operaciones,[original]);
            return {ok:true,json:async()=>({resultados:[{operacionId:original.operacionId,resultado:'YaAplicada'}]})};
        };
        await store.sincronizarCargas();
        assert.equal((await store.pendientesCarga()).length,0);
        assert.equal((await store.borradorCarga(b.rutaId)).resultado,'Aplicada');
    } finally { globalThis.fetch=fetchAnterior; }
});
test('CU-52 sesión vencida conserva la cola cifrada para el propietario sin entregársela a otro',async()=>{
    let b=await prepararCarga(); b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    await store.confirmarCarga(b.rutaId,b.revision,false);
    const original=(await store.pendientesCarga())[0], fetchAnterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>({ok:false,status:401});
        await assert.rejects(store.sincronizarCargas(),/Iniciá sesión/);
        assert.equal(await store.sesionLocal(),null);
        await store.activar(sesion(20)); assert.deepEqual(await store.pendientesCarga(),[]);
        await store.activar(sesion()); assert.deepEqual((await store.pendientesCarga())[0],original);
    } finally { globalThis.fetch=fetchAnterior; }
});
test('CU-52 conserva el rechazo y pide revisar sobre una hoja actualizada',async()=>{
    let b=await prepararCarga(); b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    await store.confirmarCarga(b.rutaId,b.revision,false);
    const original=(await store.pendientesCarga())[0], fetchAnterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>({ok:true,json:async()=>({resultados:[{operacionId:original.operacionId,resultado:'Conflicto',motivo:'RutaModificada'}]})});
        await store.sincronizarCargas();
        b=await store.borradorCarga(b.rutaId); assert.equal(b.resultado,'Rechazada');
        assert.equal((await registros('operaciones')).length,1);
        assert.deepEqual(await store.pendientesCarga(),[]);
        b=await store.revisarRechazo(b.rutaId,b.revision); assert.deepEqual(b.lecturas,[]);
        assert.equal((await registros('operaciones')).length,1);
    } finally { globalThis.fetch=fetchAnterior; }
});
test('CU-51 una escritura abortada no pierde el borrador ni genera confirmación parcial',async()=>{
    let b=await prepararCarga(); b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    const add=IDBObjectStore.prototype.add;
    try {
        IDBObjectStore.prototype.add=function(...args) { if(this.name==='operaciones') this.transaction.abort(); return add.apply(this,args); };
        await assert.rejects(store.confirmarCarga(b.rutaId,b.revision,false));
    } finally { IDBObjectStore.prototype.add=add; }
    assert.deepEqual(await store.borradorCarga(b.rutaId),b);
    assert.deepEqual(await store.pendientesCarga(),[]);
});
test('CU-51 migrar IndexedDB v1 conserva hoja y clave existentes',async()=>{
    const s=sesion(),h=hoja(), espacio=[s.usuarioId,s.operadorId,s.repartidorId].join(':');
    const clave=await crypto.subtle.generateKey({name:'AES-GCM',length:256},false,['encrypt','decrypt']);
    const iv=crypto.getRandomValues(new Uint8Array(12)),meta={espacio,schemaVersion:1,rutaId:h.rutaId,fechaRuta:h.fechaRuta};
    const contenido=await crypto.subtle.encrypt({name:'AES-GCM',iv,additionalData:new TextEncoder().encode(JSON.stringify(meta)),tagLength:128},clave,new TextEncoder().encode(JSON.stringify(h)));
    const legado=await new Promise((resolve,reject)=>{
        const r=indexedDB.open('logistica-repartidor-v1',1);
        r.onupgradeneeded=()=>{for(const nombre of ['espacios','claves','hojas'])r.result.createObjectStore(nombre);};
        r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);
    });
    const tx=legado.transaction(['claves','hojas'],'readwrite');
    tx.objectStore('claves').put(clave,espacio);
    tx.objectStore('hojas').put({meta,iv,contenido,descargadaEn:s.verificadaEn},espacio);
    await new Promise((resolve,reject)=>{tx.oncomplete=resolve;tx.onabort=reject;});legado.close();
    await store.activar(s);
    assert.deepEqual((await store.cargar()).hoja,h);
    assert.deepEqual((await store.abrirCarga(h.rutaId)).lecturas,[]);
});

test('CU-51 nueva confirmación conserva bultos ya leídos del envío que sigue incompleto',async()=>{
    const h=hoja(); h.paradas[0].envios[0].bultos.push({id:id(99),codigo:'ENV-001-2'});
    const token=await store.activar(sesion()); await store.guardar(h,token);
    let b=await store.abrirCarga(h.rutaId); b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    await store.confirmarCarga(b.rutaId,b.revision,true);
    const [op]=await store.pendientesCarga(),fetchAnterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>({ok:true,json:async()=>({resultados:[{operacionId:op.operacionId,resultado:'Aplicada'}]})});
        await store.sincronizarCargas();
    } finally { globalThis.fetch=fetchAnterior; }
    await store.guardar({...h,revisionRuta:h.revisionRuta+1},token);
    b=await store.abrirCarga(h.rutaId);
    assert.equal(b.operacionId,null); assert.equal(b.lecturas[0].codigoBulto,'ENV-001-1');
    b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-2');
    await store.confirmarCarga(b.rutaId,b.revision,false);
    assert.equal((await store.pendientesCarga())[0].carga.lecturas.length,2);
});
test('CU-52 cerrar sesión mientras llega el ack no descarta la operación del propietario',async()=>{
    let b=await prepararCarga(); b=await store.escanearCarga(b.rutaId,b.revision,'ENV-001-1');
    await store.confirmarCarga(b.rutaId,b.revision,false);
    const [op]=await store.pendientesCarga(),fetchAnterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>{
            await store.bloquear();
            return {ok:true,json:async()=>({resultados:[{operacionId:op.operacionId,resultado:'Aplicada'}]})};
        };
        await assert.rejects(store.sincronizarCargas(),/Iniciá sesión/);
        await store.activar(sesion());
        assert.deepEqual((await store.pendientesCarga())[0],op);
    } finally { globalThis.fetch=fetchAnterior; }
});
