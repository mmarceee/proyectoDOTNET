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
        const r = indexedDB.open('logistica-repartidor-v1', 1);
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
