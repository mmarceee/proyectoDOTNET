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
beforeEach(t => {
    globalThis.indexedDB = new IDBFactory();
    t.mock.timers.enable({ apis:['Date'], now:new Date('2026-10-17T15:00:00Z') });
});
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
function hoja54(situacion = 'NoIntentado') {
    const h = hoja(); h.schemaVersion = 2; h.estadoRuta = 'EnCurso';
    h.paradas[0].envios[0].estado = situacion === 'EntregadoEnCalle' ? 'Entregado' : situacion === 'NoCargado' ? 'AsignadoARuta' : 'EnTransito';
    h.rendicion = { iniciada:null, bultos:h.paradas.flatMap(p => p.envios).flatMap(e => e.bultos.map(b => ({
        envioId:e.id, numeroEnvio:e.numero, bultoId:b.id, codigo:b.codigo, situacion,
        requiereDeclaracion:['NoIntentado','IntentoFallido','CargaParcial'].includes(situacion)
    }))) };
    return h;
}

test('CU-53 conserva posiciones cifradas sin red, limita pestañas a 30 segundos y no conserva recibos GPS', async t => {
    const token = await store.activar(sesion()), h = hoja54(); await store.guardar(h,token);
    const p = () => ({ latitud:-34.9011234,longitud:-56.1645678,capturadaEn:Date.now() });
    assert.equal((await store.rutaParaSeguimiento()).rutaId,h.rutaId);
    const resultados = await Promise.all([store.guardarPosicion(h.rutaId,token,p()),store.guardarPosicion(h.rutaId,token,p())]);
    assert.equal(resultados.filter(Boolean).length,1);
    assert.equal(await store.guardarPosicion(h.rutaId,token,p()),false);
    t.mock.timers.setTime(Date.now()+30000);
    assert.equal(await store.guardarPosicion(h.rutaId,token,p()),true);
    const pendientes = await store.pendientesCarga();
    assert.equal(pendientes.length,2); assert.equal(pendientes[0].tipo,'ReportarPosicion');
    assert.equal(pendientes[0].latitud,-34.901123); assert.equal(pendientes[0].longitud,-56.164568);
    const filas = await registros('operaciones');
    assert.ok(filas.every(r => !JSON.stringify(r).includes('latitud')));
    const anterior = globalThis.fetch;
    try {
        globalThis.fetch = async (_,r) => {
            const body = JSON.parse(r.body); assert.equal(body.pendientes,2);
            return {ok:true,json:async () => ({resultados:body.operaciones.map(op => ({operacionId:op.operacionId,resultado:'Aplicada'}))})};
        };
        await store.sincronizarCargas();
        assert.equal((await store.pendientesCarga()).length,0);
        assert.equal((await registros('recibos')).length,0);
    } finally { globalThis.fetch = anterior; }
});

test('CU-53 inicia con carga local completa y se detiene al confirmar rendición sin conexión', async () => {
    const token = await store.activar(sesion()), h = hoja54('NoCargado'); h.estadoRuta='Despachada';
    await store.guardar(h,token); assert.equal(await store.rutaParaSeguimiento(),null);
    let b = await store.abrirCarga(h.rutaId);
    b = await store.escanearCarga(h.rutaId,b.revision,'ENV-001-1'); await store.confirmarCarga(h.rutaId,b.revision,false);
    assert.equal((await store.rutaParaSeguimiento()).rutaId,h.rutaId);
    const capturaPendiente = {latitud:-34,longitud:-56,capturadaEn:Date.now()};
    let r = await store.abrirRendicion(h.rutaId);
    r = await store.declararBulto(h.rutaId,r.revision,id(15),true,null);
    await store.confirmarRendicion(h.rutaId,r.revision);
    assert.equal(await store.rutaParaSeguimiento(),null);
    assert.equal(await store.guardarPosicion(h.rutaId,token,capturaPendiente),false);
});

test('CU-53 descarta una lectura tardía tras cambiar sesión y rechaza coordenadas o lecturas antiguas', async () => {
    const token = await store.activar(sesion()), h = hoja54(); await store.guardar(h,token);
    await assert.rejects(store.guardarPosicion(h.rutaId,token,{latitud:91,longitud:0,capturadaEn:Date.now()}),/reciente/);
    await assert.rejects(store.guardarPosicion(h.rutaId,token,{latitud:0,longitud:0,capturadaEn:Date.now()-60000}),/reciente/);
    await store.activar(sesion(20));
    assert.equal(await store.guardarPosicion(h.rutaId,token,{latitud:0,longitud:0,capturadaEn:Date.now()}),false);
    assert.equal(await store.rutaParaSeguimiento(),null);
    await store.activar(sesion()); assert.equal((await store.pendientesCarga()).length,0);
});

test('CU-53 no reactiva una ruta cerrada al seleccionar otra hoja', async () => {
    const token = await store.activar(sesion()), h = hoja54(); await store.guardar(h,token);
    await store.sinRuta(token);
    const otra = hoja(); otra.rutaId=id(30); await store.guardar(otra,token);
    assert.equal(await store.rutaParaSeguimiento(),null);
});

test('ADR-0004 dos disparadores comparten un único envío pendiente dentro de la pestaña', async () => {
    const token=await store.activar(sesion()),h=hoja54();await store.guardar(h,token);
    await store.guardarPosicion(h.rutaId,token,{latitud:0,longitud:0,capturadaEn:Date.now()});
    let continuar,comenzo,envios=0;
    const espera=new Promise(resolve => {continuar=resolve;}),inicio=new Promise(resolve => {comenzo=resolve;});
    const anterior=globalThis.fetch;
    try {
        globalThis.fetch=async (_,request) => {
            envios++;comenzo();await espera;
            return {ok:true,json:async () => ({resultados:JSON.parse(request.body).operaciones
                .map(op => ({operacionId:op.operacionId,resultado:'Aplicada'}))})};
        };
        const primera=store.sincronizarCargas(),segunda=store.sincronizarCargas();
        await inicio;assert.equal(envios,1);continuar();await Promise.all([primera,segunda]);
        assert.equal(envios,1);assert.equal((await store.pendientesCarga()).length,0);
    } finally {continuar();globalThis.fetch=anterior;}
});

test('Mis rutas conserva hojas y cargas separadas al alternar rutas, incluso de días anteriores', async t => {
    let token = await store.activar(sesion());
    const primera = hoja();
    await store.guardar(primera,token);
    let carga = await store.abrirCarga(primera.rutaId);
    carga = await store.escanearCarga(primera.rutaId,carga.revision,'ENV-001-1');
    await store.confirmarCarga(primera.rutaId,carga.revision,false);
    const [operacion] = await store.pendientesCarga();
    t.mock.timers.setTime(new Date('2026-10-19T15:00:00Z').getTime());
    token = await store.activar({ ...sesion(),fechaOperativa:'2026-10-19' });
    const segunda = hoja(); segunda.rutaId = id(30);
    await store.guardar(segunda,token);
    assert.equal((await store.rutasGuardadas()).length,2);
    assert.equal((await store.cargar()).hoja.rutaId,segunda.rutaId);
    await store.seleccionarRuta(primera.rutaId);
    assert.equal((await store.cargar()).hoja.fechaRuta,'2026-10-17');
    assert.equal((await store.abrirCarga(primera.rutaId)).resultado,'Pendiente');
    assert.deepEqual((await store.pendientesCarga())[0],operacion);
    assert.deepEqual(await store.estadosLocalesRutas(),[{rutaId:primera.rutaId,tipo:'carga',resultado:'Pendiente'}]);
    await assert.rejects(store.guardar(segunda,token),/sesión/);
    token = await store.activar(sesion());
    await store.sinRuta(token);
    assert.equal((await store.cargar()).sinRuta,true);
    assert.equal((await store.cargar(segunda.rutaId)).sinRuta,false);
    await store.seleccionarRuta(segunda.rutaId);
    assert.equal((await store.cargar()).hoja.rutaId,segunda.rutaId);
    await store.bloquear(); await store.activar(sesion(20));
    assert.equal(await store.rutaSeleccionada(),null);
    assert.deepEqual(await store.rutasGuardadas(),[]);
    assert.deepEqual(await store.estadosLocalesRutas(),[]);
});

test('Una carga de otro día no se abre ni continúa aunque la identidad sin conexión sea del día anterior', async t => {
    const token = await store.activar(sesion()), h = hoja();
    await store.guardar(h,token);
    let carga = await store.abrirCarga(h.rutaId);
    carga = await store.escanearCarga(h.rutaId,carga.revision,'ENV-001-1');
    t.mock.timers.setTime(new Date('2026-10-19T15:00:00Z').getTime());
    await assert.rejects(store.abrirCarga(h.rutaId),/fecha de hoy/);
    await assert.rejects(store.escanearCarga(h.rutaId,carga.revision,'OTRO'),/fecha de hoy/);
    await assert.rejects(store.confirmarCarga(h.rutaId,carga.revision,false),/fecha de hoy/);
    assert.deepEqual(await store.borradorCarga(h.rutaId),carga);
    assert.equal((await store.pendientesCarga()).length,0);
});

test('Una carga confirmada en su fecha conserva su operación y se sincroniza otro día', async t => {
    const token = await store.activar(sesion()), h = hoja(); await store.guardar(h,token);
    let carga = await store.abrirCarga(h.rutaId);
    carga = await store.escanearCarga(h.rutaId,carga.revision,'ENV-001-1');
    carga = await store.confirmarCarga(h.rutaId,carga.revision,false);
    const [op] = await store.pendientesCarga();
    t.mock.timers.setTime(new Date('2026-10-19T15:00:00Z').getTime());
    await store.activar({ ...sesion(),fechaOperativa:'2026-10-19' });
    assert.deepEqual(await store.abrirCarga(h.rutaId),carga);
    const anterior = globalThis.fetch;
    try {
        globalThis.fetch = async (_,request) => {
            assert.deepEqual(JSON.parse(request.body).operaciones,[op]);
            assert.match(op.ocurridoEn,/^2026-10-17/);
            return { ok:true,json:async () => ({ resultados:[{ operacionId:op.operacionId,resultado:'Aplicada' }] }) };
        };
        await store.sincronizarCargas();
        assert.equal((await store.pendientesCarga()).length,0);
        assert.equal((await store.borradorCarga(h.rutaId)).resultado,'Aplicada');
        await assert.rejects(store.escanearCarga(h.rutaId,carga.revision,'OTRO'),/fecha de hoy/);
    } finally { globalThis.fetch = anterior; }
});

test('Mis rutas no habilita carga futura y actualizar una copia no duplica el archivo', async () => {
    const token = await store.activar(sesion());
    const futura = hoja(); futura.fechaRuta = '2026-10-18';
    await store.guardar(futura,token);
    await assert.rejects(store.abrirCarga(futura.rutaId));
    const otra = hoja(); otra.rutaId = id(30);
    await store.guardar(otra,token);
    await store.guardar(futura,token);
    const copias = await store.rutasGuardadas();
    assert.equal(copias.length,2);
    assert.equal(copias.filter(c => c.hoja.rutaId === futura.rutaId).length,1);
    assert.equal((await registros('hojas')).length,2);
    assert.equal(JSON.stringify(await registros('hojas')).includes('Nombre privado'),false);
});

test('CU-54 declara por bulto, exige observación, cifra y conserva la misma operación hasta el ack', async () => {
    const token = await store.activar(sesion()), h = hoja54(); await store.guardar(h,token);
    let b = await store.abrirRendicion(h.rutaId);
    await assert.rejects(store.confirmarRendicion(h.rutaId,b.revision), /Indicá/);
    b = await store.declararBulto(h.rutaId,b.revision,id(15),false,null);
    await assert.rejects(store.confirmarRendicion(h.rutaId,b.revision), /explicá/);
    b = await store.declararBulto(h.rutaId,b.revision,id(15),false,'Observación privada de regreso');
    b = await store.confirmarRendicion(h.rutaId,b.revision);
    const [op] = await store.pendientesCarga();
    assert.equal(op.tipo,'IniciarRendicion'); assert.equal(op.envioId,null);
    assert.match(op.operacionId,/^[0-9a-f-]{14}7/); assert.match(op.correlacionId,/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
    assert.equal(b.resultado,'Pendiente');
    assert.equal(JSON.stringify(await registros('borradores')).includes('Observación privada'),false);
    await assert.rejects(store.declararBulto(h.rutaId,b.revision,id(15),true,null));
    await assert.rejects(store.abrirCarga(h.rutaId),/rendición/);
    const anterior = globalThis.fetch;
    try {
        globalThis.fetch = async () => { throw new TypeError('Sin red'); };
        await assert.rejects(store.sincronizarCargas(),/guardada/);
        assert.deepEqual((await store.pendientesCarga())[0],op);
        globalThis.fetch = async (_,request) => {
            assert.deepEqual(JSON.parse(request.body).operaciones,[op]);
            return { ok:true, json:async () => ({ resultados:[{ operacionId:op.operacionId,resultado:'Aplicada' }] }) };
        };
        await store.sincronizarCargas();
        assert.equal((await store.borradorRendicion(h.rutaId)).resultado,'Aplicada');
        assert.equal((await store.pendientesCarga()).length,0);
        assert.equal((await registros('recibos')).length,1);
        assert.equal(JSON.stringify(await registros('recibos')).includes('Observación privada'),false);
    } finally { globalThis.fetch = anterior; }
});

test('CU-54 permite rendición sin devoluciones y recupera el borrador separado de la carga', async () => {
    const token = await store.activar(sesion()), h = hoja54('EntregadoEnCalle'); await store.guardar(h,token);
    const b = await store.abrirRendicion(h.rutaId);
    assert.equal(b.bultos[0].requiereDeclaracion,false);
    const confirmada = await store.confirmarRendicion(h.rutaId,b.revision);
    assert.equal(confirmada.resultado,'Pendiente');
    assert.equal(await store.borradorCarga(h.rutaId),null);
    assert.deepEqual(await store.abrirRendicion(h.rutaId),confirmada);
});

test('CU-54 una carga pendiente se proyecta antes de rendir y ambas se envían en orden', async () => {
    const token = await store.activar(sesion()), h = hoja54('NoCargado'); h.estadoRuta = 'Despachada'; await store.guardar(h,token);
    let carga = await store.abrirCarga(h.rutaId);
    carga = await store.escanearCarga(h.rutaId,carga.revision,'ENV-001-1');
    carga = await store.confirmarCarga(h.rutaId,carga.revision,false);
    let r = await store.abrirRendicion(h.rutaId);
    assert.equal(r.bultos[0].situacion,'NoIntentado');
    r = await store.declararBulto(h.rutaId,r.revision,id(15),true,null);
    await store.confirmarRendicion(h.rutaId,r.revision);
    const anterior = globalThis.fetch;
    try {
        globalThis.fetch = async (_,request) => {
            const ops = JSON.parse(request.body).operaciones;
            assert.deepEqual(ops.map(o => o.tipo),['ConfirmarCarga','IniciarRendicion']);
            assert.deepEqual(ops[1].rendicion.dependencias,[ops[0].operacionId]);
            assert.notEqual(ops[0].correlacionId,ops[1].correlacionId);
            return { ok:true,json:async () => ({ resultados:ops.map(o => ({ operacionId:o.operacionId,resultado:'Aplicada' })) }) };
        };
        await store.sincronizarCargas();
        assert.equal((await store.borradorRendicion(h.rutaId)).resultado,'Aplicada');
        assert.equal((await store.borradorCarga(h.rutaId)).resultado,'Aplicada');
    } finally { globalThis.fetch = anterior; }
});

test('CU-54 sincronización drena más de un lote y deja la rendición después de sus dependencias', async () => {
    const token = await store.activar(sesion()); let h;
    for (let i = 0; i < 21; i++) {
        h = hoja54('NoCargado'); h.rutaId = id(100+i); h.estadoRuta = 'Despachada'; await store.guardar(h,token);
        let carga = await store.abrirCarga(h.rutaId);
        carga = await store.escanearCarga(h.rutaId,carga.revision,'ENV-001-1');
        await store.confirmarCarga(h.rutaId,carga.revision,false);
    }
    let r = await store.abrirRendicion(h.rutaId);
    r = await store.declararBulto(h.rutaId,r.revision,id(15),true,null);
    await store.confirmarRendicion(h.rutaId,r.revision);
    const anterior = globalThis.fetch, lotes = [];
    try {
        globalThis.fetch = async (_,request) => {
            const ops = JSON.parse(request.body).operaciones; lotes.push(ops);
            if (lotes.length === 2) throw new TypeError('Corte después del primer lote');
            return { ok:true,json:async () => ({ resultados:ops.map(o => ({ operacionId:o.operacionId,resultado:'Aplicada' })) }) };
        };
        await assert.rejects(store.sincronizarCargas(),/guardada/);
        assert.equal((await store.pendientesCarga()).length,2);
        assert.equal((await registros('recibos')).length,20);
        await store.sincronizarCargas();
        assert.deepEqual(lotes.map(l => l.length),[20,2,2]);
        assert.deepEqual(lotes[1],lotes[2]);
        assert.equal(lotes[1][1].tipo,'IniciarRendicion');
        assert.deepEqual(lotes[1][1].rendicion.dependencias,[lotes[1][0].operacionId]);
        assert.equal((await store.pendientesCarga()).length,0);
    } finally { globalThis.fetch = anterior; }
});

test('CU-54 una escritura abortada y dos pestañas no pierden la declaración anterior', async () => {
    const token = await store.activar(sesion()), h = hoja54(); await store.guardar(h,token);
    const base = await store.abrirRendicion(h.rutaId);
    const cambios = await Promise.allSettled([
        store.declararBulto(h.rutaId,base.revision,id(15),true,null),
        store.declararBulto(h.rutaId,base.revision,id(15),false,'No vuelve')
    ]);
    assert.equal(cambios.filter(c => c.status === 'fulfilled').length,1);
    let r = await store.borradorRendicion(h.rutaId);
    r = await store.declararBulto(h.rutaId,r.revision,id(15),true,null);
    const add = IDBObjectStore.prototype.add;
    try {
        IDBObjectStore.prototype.add = function(...args) { if (this.name === 'operaciones') this.transaction.abort(); return add.apply(this,args); };
        await assert.rejects(store.confirmarRendicion(h.rutaId,r.revision));
    } finally { IDBObjectStore.prototype.add = add; }
    assert.deepEqual(await store.borradorRendicion(h.rutaId),r);
    assert.equal((await store.pendientesCarga()).length,0);
});

test('CU-54 no inicia con una copia antigua sin base de rendición ni expone datos al cambiar cuenta', async () => {
    const token = await store.activar(sesion()); await store.guardar(hoja(),token);
    await assert.rejects(store.abrirRendicion(id(10)),/Actualizá/);
    await store.guardar(hoja54(),token); let r = await store.abrirRendicion(id(10));
    r = await store.declararBulto(id(10),r.revision,id(15),true,null);
    await store.confirmarRendicion(id(10),r.revision);
    await store.bloquear(); await store.activar(sesion(20));
    assert.equal(await store.borradorRendicion(id(10)),null);
    assert.equal((await store.pendientesCarga()).length,0);
    await store.activar(sesion());
    assert.equal((await store.borradorRendicion(id(10))).resultado,'Pendiente');
});
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

// Fixture: una operación en calle que guardaría su pantalla, usando el formato cifrado compartido de CU-52.
async function encolarEnCalle(op) {
    const s = sesion(), espacio = [s.usuarioId,s.operadorId,s.repartidorId].join(':');
    const meta = { espacio,tipo:'operacion',id:op.operacionId }, iv = crypto.getRandomValues(new Uint8Array(12));
    const clave = (await registros('claves'))[0];
    const contenido = await crypto.subtle.encrypt({ name:'AES-GCM',iv,additionalData:new TextEncoder().encode(JSON.stringify(meta)) },
        clave,new TextEncoder().encode(JSON.stringify(op)));
    const conexion = await db();
    try {
        await new Promise((resolve,reject) => {
            const tx = conexion.transaction(['operaciones','instalacion'],'readwrite');
            tx.oncomplete = resolve; tx.onerror = tx.onabort = () => reject(tx.error);
            tx.objectStore('operaciones').add({ meta,iv,contenido,estado:'Pendiente',secuencia:op.secuencia },op.operacionId);
            tx.objectStore('instalacion').put({ dispositivoId:op.dispositivoId,secuencia:op.secuencia },'actual');
        });
    } finally { conexion.close(); }
}

for (const tipo of ['RegistrarEntrega','RegistrarIntentoFallido']) {
    test(`CU-54 proyecta ${tipo} pendiente y confirma la rendición después sin requerir un borrador de carga`, async () => {
        const token = await store.activar(sesion()), h = hoja54(); await store.guardar(h,token);
        const op = { operacionId:store.uuidv7(),dispositivoId:id(90),secuencia:1,tipo,rutaId:h.rutaId,envioId:id(14),
            ocurridoEn:new Date().toISOString(),latitud:-34.9,longitud:-56.1,correlacionId:store.traceparent() };
        await encolarEnCalle(op);
        let r = await store.abrirRendicion(h.rutaId);
        assert.equal(r.bultos[0].situacion,tipo === 'RegistrarEntrega' ? 'EntregadoEnCalle' : 'IntentoFallido');
        if (tipo === 'RegistrarIntentoFallido') r = await store.declararBulto(h.rutaId,r.revision,id(15),true,null);
        await store.confirmarRendicion(h.rutaId,r.revision);
        const anterior = globalThis.fetch;
        try {
            globalThis.fetch = async (_,request) => {
                const ops = JSON.parse(request.body).operaciones;
                assert.deepEqual(ops.map(o => o.tipo),[tipo,'IniciarRendicion']);
                assert.deepEqual(ops[1].rendicion.dependencias,[op.operacionId]);
                return { ok:true,json:async () => ({ resultados:ops.map(o => ({ operacionId:o.operacionId,resultado:'Aplicada' })) }) };
            };
            await store.sincronizarCargas();
            assert.equal((await store.borradorRendicion(h.rutaId)).resultado,'Aplicada');
            assert.equal((await registros('operaciones')).length,0);
            assert.equal((await registros('recibos')).length,2);
        } finally { globalThis.fetch = anterior; }
    });
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

// CU-17 y CU-18: la entrega y el intento fallido se guardan cifrados en la misma cola, sin conexión.
const FIRMA = { tipoContenido:'image/png', contenidoBase64:'iVBORw0KGgo=' };
const AQUI = { latitud:-34.9011, longitud:-56.1645 };
async function prepararEnCalle(ajustar = () => {}) {
    const token = await store.activar(sesion()), h = hoja();
    h.estadoRuta = 'EnCurso'; h.paradas[0].envios[0].estado = 'EnTransito'; ajustar(h);
    await store.guardar(h, token);
    return { ruta:h.rutaId, envio:h.paradas[0].envios[0].id };
}
test('CU-17 sin conexión la entrega queda en la cola con firma, hora y posición, y después se sincroniza', async () => {
    const { ruta, envio } = await prepararEnCalle();
    assert.equal((await store.registrarEntrega(ruta, envio, { firma:FIRMA, nombreReceptor:' Ana ', ...AQUI })).estado, 'Pendiente');
    const [op] = await store.pendientesCarga();
    assert.equal(op.tipo, 'RegistrarEntrega'); assert.equal(op.envioId, envio); assert.equal(op.rutaId, ruta);
    assert.deepEqual(op.entrega, { firma:FIRMA, foto:null, nombreReceptor:'Ana', documentoReceptor:null });
    assert.equal(op.ocurridoEn, '2026-10-17T15:00:00.000Z');
    assert.deepEqual([op.latitud, op.longitud], [AQUI.latitud, AQUI.longitud]);
    assert.deepEqual(await store.estadosEnvios(ruta), [{ envioId:envio, tipo:'RegistrarEntrega', estado:'Pendiente', motivo:null }]);
    const fetchAnterior = globalThis.fetch;
    try {
        globalThis.fetch = async () => { throw new TypeError('sin red'); };
        await assert.rejects(store.sincronizarCargas(), /pendiente de sincronizar/);
        assert.deepEqual(await store.pendientesCarga(), [op]);
        globalThis.fetch = async (_, request) => {
            assert.deepEqual(JSON.parse(request.body).operaciones, [op]);
            return { ok:true, json:async () => ({ resultados:[{ operacionId:op.operacionId, resultado:'Aplicada' }] }) };
        };
        await store.sincronizarCargas();
        assert.deepEqual(await store.pendientesCarga(), []);
        assert.equal((await store.estadosEnvios(ruta))[0].estado, 'Aplicada');
    } finally { globalThis.fetch = fetchAnterior; }
});
test('CU-17 exige la firma, lo que piden las reglas del envío y una posición, y no registra dos veces', async () => {
    const { ruta, envio } = await prepararEnCalle(h => Object.assign(h.paradas[0].envios[0].reglasEntrega,
        { exigeFoto:true, exigeNombreYDocumento:true }));
    const completa = { firma:FIRMA, foto:{ tipoContenido:'image/jpeg', contenidoBase64:'/9j/4AAQ' },
        nombreReceptor:'Ana', documentoReceptor:'1234567-8', ...AQUI };
    await assert.rejects(store.registrarEntrega(ruta, envio, { ...completa, firma:null }), /firma/);
    await assert.rejects(store.registrarEntrega(ruta, envio, { ...completa, foto:null }), /foto/);
    await assert.rejects(store.registrarEntrega(ruta, envio, { ...completa, documentoReceptor:' ' }), /documento/);
    await assert.rejects(store.registrarEntrega(ruta, envio,
        { ...completa, firma:{ tipoContenido:'image/gif', contenidoBase64:'R0lG' } }), /PNG o JPEG/);
    await assert.rejects(store.registrarEntrega(ruta, envio, { ...completa, latitud:null }), /ubicación/);
    assert.deepEqual(await store.pendientesCarga(), []);
    await store.registrarEntrega(ruta, envio, completa);
    await assert.rejects(store.registrarEntrega(ruta, envio, completa), /ya está registrado/);
    await assert.rejects(store.registrarIntentoFallido(ruta, envio, { motivoId:id(16), ...AQUI }), /ya está registrado/);
    assert.equal((await store.pendientesCarga()).length, 1);
});
test('CU-18 el motivo sale del catálogo del envío y uno que exige evidencia pide la foto', async () => {
    const { ruta, envio } = await prepararEnCalle(h => h.paradas[0].envios[0].reglasEntrega.motivosNoEntrega.push(
        { id:id(17), codigo:'SIN_ACCESO', nombreParaMostrar:'Sin acceso', reprogramable:true, exigeEvidencia:true }));
    await assert.rejects(store.registrarIntentoFallido(ruta, envio, { motivoId:id(99), ...AQUI }), /motivo/);
    await assert.rejects(store.registrarIntentoFallido(ruta, envio, { motivoId:id(17), ...AQUI }), /exige una foto/);
    await store.registrarIntentoFallido(ruta, envio, { motivoId:id(16), observaciones:' Nadie atendió ', ...AQUI });
    const [op] = await store.pendientesCarga();
    assert.equal(op.tipo, 'RegistrarIntentoFallido');
    assert.deepEqual(op.intentoFallido, { motivoId:id(16), foto:null, observaciones:'Nadie atendió' });
    assert.deepEqual(await store.estadosEnvios(ruta),
        [{ envioId:envio, tipo:'RegistrarIntentoFallido', estado:'Pendiente', motivo:null }]);
});
test('CU-17 sin conexión acepta un envío que la hoja muestra asignado si este dispositivo confirmó su carga', async () => {
    const token = await store.activar(sesion()), h = hoja();
    await store.guardar(h, token);
    const ruta = h.rutaId, envio = h.paradas[0].envios[0].id;
    await assert.rejects(store.registrarEntrega(ruta, envio, { firma:FIRMA, ...AQUI }), /no salió en el vehículo/);
    let c = await store.abrirCarga(ruta); c = await store.escanearCarga(ruta, c.revision, 'ENV-001-1');
    await store.confirmarCarga(ruta, c.revision, false);
    await store.registrarEntrega(ruta, envio, { firma:FIRMA, ...AQUI });
    // La carga va primero en la secuencia: el servidor la aplica antes que la entrega.
    assert.deepEqual((await store.pendientesCarga()).map(o => o.tipo), ['ConfirmarCarga', 'RegistrarEntrega']);
});
test('CU-54 la rendición toma como entregado en la calle lo registrado en este dispositivo', async () => {
    const token = await store.activar(sesion()), h = hoja54('NoIntentado');
    await store.guardar(h, token);
    const local = await store.registrarEntrega(h.rutaId, h.paradas[0].envios[0].id, { firma:FIRMA, ...AQUI });
    assert.equal(local.estado, 'Pendiente');
    const rendicion = await store.abrirRendicion(h.rutaId);
    assert.equal(rendicion.bultos[0].situacion, 'EntregadoEnCalle');
    assert.equal(rendicion.bultos[0].requiereDeclaracion, false);
    assert.equal(rendicion.dependencias.length, 1);
});
