import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';
import { IDBFactory } from 'fake-indexeddb';
Object.defineProperty(globalThis, 'crypto', { value:webcrypto, configurable:true });
globalThis.isSecureContext = true;
globalThis.BroadcastChannel = class { postMessage() {} };
const source = await readFile(new URL('../../src/Logistica.Repartidor.Pwa/wwwroot/js/hoja-ruta.js', import.meta.url), 'utf8');
const store = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const id = n => '30000000-0000-4000-8000-' + String(n).padStart(12, '0');
const posicion = { latitud:-34.9, longitud:-56.1 };
const firma = { tipoContenido:'image/png', contenidoBase64:'iVBORw==' };
const foto = { tipoContenido:'image/jpeg', contenidoBase64:'/9j/4AAQ' };
beforeEach(t => { globalThis.indexedDB = new IDBFactory(); t.mock.timers.enable({ apis:['Date'], now:new Date('2026-11-08T15:00:00Z') }); });
async function preparar({ parcial=true, cargado=true, tercero='EnTransito' }={}) {
    const sesion = { usuarioId:id(1), operadorId:id(2), repartidorId:id(3), fechaOperativa:'2026-11-08', zonaHorariaOperador:'America/Montevideo', verificadaEn:'2026-11-08T14:00:00Z' };
    const token = await store.activar(sesion);
    const h = { ...sesion, schemaVersion:3, rutaId:id(10), revisionRuta:1, fechaRuta:sesion.fechaOperativa, estadoRuta:cargado ? 'EnCurso' : 'Despachada', generadaEn:sesion.verificadaEn,
        vehiculo:{ id:id(11), matricula:'DEMO001' }, zonas:[{id:id(12),codigo:'MVD',nombre:'Montevideo'}],
        paradas:[{id:id(13),orden:1,estado:'Pendiente',direccion:'Calle 1',desde:null,hasta:null,envios:[{
            id:id(14),numero:'ENV-001',estado:cargado ? 'EnTransito' : 'AsignadoARuta',destinatarioNombre:'Ana',destinatarioTelefono:'099000111',
            direccion:{calle:'Calle',numero:'1',localidad:'Montevideo',departamento:'Montevideo',codigoPostal:'11300'},franja:null,
            bultos:[1,2,3].map((n,i)=>({id:id(20+n),codigo:'ENV-001-'+n,estado:!cargado ? 'AsignadoARuta' : i===2 ? tercero : 'EnTransito'})),
            reglasEntrega:{origen:'Version',versionId:id(30),firmaObligatoria:true,exigeFoto:true,exigeNombreYDocumento:false,maxIntentos:3,plazoEntreIntentos:'1.00:00:00',admiteEntregaParcial:parcial,
                motivosNoEntrega:[{id:id(31),codigo:'RECHAZO',nombreParaMostrar:'Rechazado',reprogramable:true,exigeEvidencia:true},{id:id(32),codigo:'AUSENTE',nombreParaMostrar:'Ausente',reprogramable:true,exigeEvidencia:false}]}
        }]}] };
    h.rendicion={iniciada:null,bultos:h.paradas[0].envios[0].bultos.map(b=>({envioId:id(14),numeroEnvio:'ENV-001',bultoId:b.id,codigo:b.codigo,
        situacion:b.estado==='EnTransito'?'NoIntentado':'NoCargado',requiereDeclaracion:b.estado==='EnTransito'}))};
    await store.guardar(h,token);
    return h;
}
async function resultados() {
    let b=await store.abrirVisita(id(10),id(14));
    b=await store.resultadoVisita(id(10),id(14),b.revision,'ENV-001-1',true,null);
    b=await store.resultadoVisita(id(10),id(14),b.revision,'ENV-001-2',true,null);
    if(b.bultos.some(x=>x.codigoBulto==='ENV-001-3')) b=await store.resultadoVisita(id(10),id(14),b.revision,'ENV-001-3',false,id(31));
    return b;
}

test('RF32 quitar una lectura errónea conserva las demás y no modifica una carga confirmada',async()=>{
    await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1',"ENV#.##'1'1",'ENV-001-2','ENV-001-3']) c=await store.escanearCarga(id(10),c.revision,codigo);
    const expected=c.lecturas.filter(l=>l.codigoBulto!=="ENV#.##'1'1");
    const oldRevision=c.revision;
    c=await store.quitarLecturaCarga(id(10),c.revision,"ENV#.##'1'1");
    assert.deepEqual(c.lecturas,expected);assert.equal(c.revision,oldRevision+1);
    assert.deepEqual((await store.abrirCarga(id(10))).lecturas,expected);
    assert.equal((await store.pendientesCarga()).length,0);
    await assert.rejects(store.quitarLecturaCarga(id(10),oldRevision,'ENV-001-1'),/cambió/);
    await store.confirmarCarga(id(10),c.revision,false);
    const operations=await store.pendientesCarga(),confirmed=await store.borradorCarga(id(10));
    await assert.rejects(store.quitarLecturaCarga(id(10),confirmed.revision,'ENV-001-1'),/ya fue confirmada/);
    assert.deepEqual(await store.pendientesCarga(),operations);
});

test('RF32 dos pestañas no eliminan lecturas sobre una misma revisión',async()=>{
    await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ERROR-A','ERROR-B'])c=await store.escanearCarga(id(10),c.revision,codigo);
    const removed=await Promise.allSettled(['ERROR-A','ERROR-B'].map(code=>store.quitarLecturaCarga(id(10),c.revision,code)));
    assert.equal(removed.filter(r=>r.status==='fulfilled').length,1);
    const actual=await store.borradorCarga(id(10));
    assert.equal(actual.lecturas.length,2);assert.ok(actual.lecturas.some(l=>l.codigoBulto==='ENV-001-1'));
    assert.equal((await store.pendientesCarga()).length,0);
});
test('RF32 dos entregados y un motivo se guardan juntos cifrados con una sola foto y la misma operación al reintentar',async()=>{
    await preparar(); const b=await resultados();
    await store.registrarVisita(id(10),id(14),b.revision,{firma,foto,...posicion});
    const [op]=await store.pendientesCarga(); assert.equal(op.tipo,'RegistrarVisita'); assert.equal(op.visita.bultos.length,3);
    assert.deepEqual(op.visita.foto,foto); assert.deepEqual(op.visita.firma,firma);
    const anterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>{throw new TypeError('sin red');};
        await assert.rejects(store.sincronizarCargas(),/pendiente/);
        assert.deepEqual(await store.pendientesCarga(),[op]);
        globalThis.fetch=async(_,request)=>{ assert.deepEqual(JSON.parse(request.body).operaciones,[op]);return {ok:true,json:async()=>({resultados:[{operacionId:op.operacionId,resultado:'YaAplicada'}]})};};
        await store.sincronizarCargas(); assert.equal((await store.estadosEnvios(id(10)))[0].estado,'Aplicada');
        assert.equal((await store.pendientesCarga()).length,0);
        await assert.rejects(store.abrirVisita(id(10),id(14)),/ya está registrado/);
    } finally {globalThis.fetch=anterior;}
});
test('RF32 el borrador sobrevive a la reapertura y no acepta duplicados ni bultos ajenos',async()=>{
    await preparar(); const b=await resultados(); assert.deepEqual(await store.abrirVisita(id(10),id(14)),b);
    await assert.rejects(store.resultadoVisita(id(10),id(14),b.revision,'ENV-001-1',true,null),/ya fue escaneado/);
    await assert.rejects(store.resultadoVisita(id(10),id(14),b.revision,'ENV-AJENO-1',true,null),/no pertenece/);
});
test('RF32 parcial deshabilitada bloquea el conjunto mixto y no crea operación',async()=>{
    await preparar({parcial:false});const b=await resultados();
    await assert.rejects(store.registrarVisita(id(10),id(14),b.revision,{firma,foto,...posicion}),/no admite entrega parcial/);
    assert.equal((await store.pendientesCarga()).length,0);
});
test('RF32 exige firma, foto y resultado de cada bulto; una visita sin entregas no exige firma',async()=>{
    await preparar();let b=await store.abrirVisita(id(10),id(14));
    await assert.rejects(store.registrarVisita(id(10),id(14),b.revision,{...posicion}),/cada entregado/);
    b=await resultados();
    await assert.rejects(store.registrarVisita(id(10),id(14),b.revision,{foto,...posicion}),/firma/);
    await assert.rejects(store.registrarVisita(id(10),id(14),b.revision,{firma,...posicion}),/foto/);
    for(const x of b.bultos) b=await store.resultadoVisita(id(10),id(14),b.revision,x.codigoBulto,false,id(31));
    await store.registrarVisita(id(10),id(14),b.revision,{foto,...posicion});
    assert.equal((await store.pendientesCarga())[0].visita.firma,null);
});
test('RF32 dos pestañas no confirman dos operaciones de la misma visita',async()=>{
    await preparar();const b=await resultados();
    const r=await Promise.allSettled([1,2].map(()=>store.registrarVisita(id(10),id(14),b.revision,{firma,foto,...posicion})));
    assert.equal(r.filter(x=>x.status==='fulfilled').length,1);assert.equal((await store.pendientesCarga()).length,1);
});
test('RF32 el rechazo conserva diagnóstico por bulto y evidencia sin volver a enviarlo',async()=>{
    await preparar();const b=await resultados();await store.registrarVisita(id(10),id(14),b.revision,{firma,foto,...posicion});
    const [op]=await store.pendientesCarga(),anterior=globalThis.fetch;
    try {globalThis.fetch=async()=>({ok:true,json:async()=>({resultados:[{operacionId:op.operacionId,resultado:'Conflicto',motivo:'BultoEnDeposito',detalle:'No aplicar',bultos:[{codigoBulto:'ENV-001-3',motivo:'BultoEnDeposito'}]}]})});
        await store.sincronizarCargas();const [r]=await store.estadosEnvios(id(10));assert.equal(r.estado,'Rechazada');assert.equal(r.bultos[0].codigoBulto,'ENV-001-3');assert.equal(r.detalle,'No aplicar');
        assert.equal((await store.pendientesCarga()).length,0);await assert.rejects(store.abrirRendicion(id(10)),/rechazadas/);
    }finally{globalThis.fetch=anterior;}
});
test('RF32 la rendición aplica los resultados individuales y excluye el tercero que no viajó',async()=>{
    await preparar({tercero:'AsignadoARuta'});const b=await resultados();assert.equal(b.bultos.length,2);assert.equal(b.excluidos.length,1);
    await store.registrarVisita(id(10),id(14),b.revision,{firma,foto,...posicion});
    const r=await store.abrirRendicion(id(10));assert.deepEqual(r.bultos.map(b=>b.situacion),['EntregadoEnCalle','EntregadoEnCalle','NoCargado']);
});
test('RF32 una carga de dos de tres exige motivo del faltante y permite registrar únicamente los que viajaron',async()=>{
    await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ENV-001-2']) c=await store.escanearCarga(id(10),c.revision,codigo);
    await assert.rejects(store.confirmarCarga(id(10),c.revision,true),/motivo/);
    c=await store.motivoFaltanteCarga(id(10),c.revision,'ENV-001-3','NoLocalizado');
    await store.confirmarCarga(id(10),c.revision,true);
    const [op]=await store.pendientesCarga();assert.equal(op.carga.porBulto,true);assert.equal(op.carga.faltantes[0].motivo,'NoLocalizado');
    await assert.rejects(store.abrirVisita(id(10),id(14)),/Comenzar ruta/);
    await store.comenzarRuta(id(10));
    const b=await store.abrirVisita(id(10),id(14));assert.equal(b.bultos.length,2);assert.equal(b.excluidos.length,1);
});
test('RF32 la carga incompleta de un operador sin parcial no habilita una visita ni devoluciones ficticias',async()=>{
    await preparar({cargado:false,parcial:false});let c=await store.abrirCarga(id(10));
    c=await store.escanearCarga(id(10),c.revision,'ENV-001-1');
    await assert.rejects(store.confirmarCarga(id(10),c.revision,true),/Completá los envíos/);
    await assert.rejects(store.abrirVisita(id(10),id(14)),/Comenzar ruta/);
    assert.equal((await store.pendientesCarga()).length,0);
});
test('RF32 un envío completo continúa sin pedir motivos por otro que no admite parcial',async()=>{
    const h=await preparar({cargado:false,parcial:false}),otro=structuredClone(h.paradas[0].envios[0]);
    otro.id=id(40);otro.numero='ENV-002';otro.bultos=[{id:id(41),codigo:'ENV-002-1',estado:'AsignadoARuta'}];
    h.paradas[0].envios.push(otro);h.rendicion.bultos.push({envioId:otro.id,numeroEnvio:otro.numero,bultoId:id(41),codigo:'ENV-002-1',situacion:'NoCargado',requiereDeclaracion:false});
    await store.guardar(h,(await store.sesionLocal()).token);let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ENV-002-1'])c=await store.escanearCarga(id(10),c.revision,codigo);
    await store.confirmarCarga(id(10),c.revision,false);
    const [op]=await store.pendientesCarga();assert.deepEqual(op.carga.faltantes,[]);assert.equal(op.carga.aceptaFaltantes,true);
    await store.comenzarRuta(id(10));
    await assert.rejects(store.abrirVisita(id(10),id(14)),/No hay bultos cargados/);
    assert.equal((await store.abrirVisita(id(10),otro.id)).bultos.length,1);
});
test('RF32 schema 3 exige estados y regla explícitos mientras las hojas anteriores siguen siendo legibles',async()=>{
    const h=await preparar();delete h.paradas[0].envios[0].reglasEntrega.admiteEntregaParcial;
    assert.throws(()=>store.validarHoja(h),/regla de entrega parcial/);
    h.schemaVersion=2;store.validarHoja(h);
    h.schemaVersion=3;h.paradas[0].envios[0].reglasEntrega.admiteEntregaParcial=true;delete h.paradas[0].envios[0].bultos[0].estado;
    assert.throws(()=>store.validarHoja(h),/estado del bulto/);
});
test('RF32 abrir visita con una carga sin confirmar no confunde ni sobrescribe los borradores',async()=>{
    await preparar();const carga=await store.abrirCarga(id(10));
    let visita=await store.abrirVisita(id(10),id(14));
    visita=await store.resultadoVisita(id(10),id(14),visita.revision,'ENV-001-1',true,null);
    assert.deepEqual(await store.borradorCarga(id(10)),carga);
    assert.deepEqual(await store.abrirVisita(id(10),id(14)),visita);
});
test('RF32 dos envíos de la misma parada conservan sus propios resultados y el borrador de carga',async()=>{
    const h=await preparar(),segundo=structuredClone(h.paradas[0].envios[0]);segundo.id=id(40);segundo.numero='ENV-002';
    segundo.bultos=segundo.bultos.map((b,i)=>({...b,id:id(41+i),codigo:'ENV-002-'+(i+1)}));
    h.paradas[0].envios.push(segundo);
    h.rendicion.bultos.push(...segundo.bultos.map(b=>({envioId:segundo.id,numeroEnvio:segundo.numero,bultoId:b.id,codigo:b.codigo,situacion:'NoIntentado',requiereDeclaracion:true})));
    await store.guardar(h,(await store.sesionLocal()).token);
    const carga=await store.abrirCarga(id(10));
    let primera=await store.abrirVisita(id(10),id(14));primera=await store.resultadoVisita(id(10),id(14),primera.revision,'ENV-001-1',true,null);
    let otra=await store.abrirVisita(id(10),segundo.id);otra=await store.resultadoVisita(id(10),segundo.id,otra.revision,'ENV-002-2',true,null);
    assert.deepEqual(await store.abrirVisita(id(10),id(14)),primera);assert.deepEqual(await store.abrirVisita(id(10),segundo.id),otra);
    assert.deepEqual(await store.borradorCarga(id(10)),carga);
});

async function simularColisionAnterior(ruta,envio) {
    const s=await store.sesionLocal(),prefijo=[s.usuarioId,s.operadorId,s.repartidorId].join(':');
    const vieja=prefijo+':'+ruta,nueva=vieja+':visita:'+envio;
    const db=await new Promise((resolve,reject)=>{const r=indexedDB.open('logistica-repartidor-v1');r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});
    const leer=(tabla,clave)=>new Promise((resolve,reject)=>{const r=db.transaction(tabla,'readonly').objectStore(tabla).get(clave);r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error);});
    try {
        const registro=await leer('borradores',nueva),clave=await leer('claves',prefijo);
        const bytes=await crypto.subtle.decrypt({name:'AES-GCM',iv:registro.iv,additionalData:new TextEncoder().encode(JSON.stringify(registro.meta))},clave,registro.contenido);
        const meta={...registro.meta,id:vieja},iv=crypto.getRandomValues(new Uint8Array(12));
        const contenido=await crypto.subtle.encrypt({name:'AES-GCM',iv,additionalData:new TextEncoder().encode(JSON.stringify(meta))},clave,bytes);
        await new Promise((resolve,reject)=>{const tx=db.transaction('borradores','readwrite');tx.objectStore('borradores').put({...registro,meta,iv,contenido},vieja);tx.objectStore('borradores').delete(nueva);tx.oncomplete=resolve;tx.onabort=()=>reject(tx.error);});
    } finally {db.close();}
}
test('RF32 recupera una visita antigua que colisionó con carga sin alterar la operación confirmada',async()=>{
    await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ENV-001-2','ENV-001-3']) c=await store.escanearCarga(id(10),c.revision,codigo);
    const confirmada=await store.confirmarCarga(id(10),c.revision,false);
    await store.comenzarRuta(id(10));
    let v=await store.abrirVisita(id(10),id(14));v=await store.resultadoVisita(id(10),id(14),v.revision,'ENV-001-2',true,null);
    const operaciones=await store.pendientesCarga();
    await simularColisionAnterior(id(10),id(14));
    assert.deepEqual(await store.abrirVisita(id(10),id(14)),v);
    const recuperada=await store.borradorCarga(id(10));assert.equal(recuperada.operacionId,confirmada.operacionId);
    assert.deepEqual(recuperada.lecturas,confirmada.lecturas);assert.deepEqual(await store.pendientesCarga(),operaciones);
});
test('RF32 recuperar una visita antigua sin operación de carga conserva la selección y permite revisar carga',async()=>{
    await preparar();const v=await store.abrirVisita(id(10),id(14));
    await simularColisionAnterior(id(10),id(14));
    const c=await store.borradorCarga(id(10));assert.equal(c.revisionRuta,1);assert.equal(c.operacionId,null);assert.deepEqual(c.lecturas,[]);
    assert.match(c.detalle,/revisarse/);assert.deepEqual(await store.abrirVisita(id(10),id(14)),v);
});

test('carga con revisión nueva conserva lecturas y motivos al revisar antes de confirmar',async()=>{
    const h=await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    c=await store.escanearCarga(id(10),c.revision,'ENV-001-1');
    c=await store.motivoFaltanteCarga(id(10),c.revision,'ENV-001-2','SinEspacio');
    h.revisionRuta=2;await store.guardar(h,(await store.sesionLocal()).token);
    await assert.rejects(store.confirmarCarga(id(10),c.revision,true),/Actualizar hoja y revisar carga/);
    const revisada=await store.abrirCarga(id(10));
    assert.equal(revisada.revisionRuta,2);assert.deepEqual(revisada.lecturas,c.lecturas);assert.deepEqual(revisada.faltantes,c.faltantes);
    c=await store.motivoFaltanteCarga(id(10),revisada.revision,'ENV-001-3','NoLocalizado');
    await store.confirmarCarga(id(10),c.revision,true);
    assert.equal((await store.pendientesCarga())[0].carga.revisionRuta,2);
});

test('actualizar la hoja nunca reemplaza una confirmación de carga pendiente ni cambia su operación',async()=>{
    const h=await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ENV-001-2','ENV-001-3'])c=await store.escanearCarga(id(10),c.revision,codigo);
    const confirmada=await store.confirmarCarga(id(10),c.revision,false),ops=await store.pendientesCarga();
    h.revisionRuta=2;await store.guardar(h,(await store.sesionLocal()).token);
    assert.deepEqual(await store.abrirCarga(id(10)),confirmada);
    assert.deepEqual(await store.pendientesCarga(),ops);
});

test('Comenzar ruta separa carga mixta de entrega, bloquea carga y GPS sólo sigue después del inicio',async()=>{
    const h=await preparar({cargado:false}),otro=structuredClone(h.paradas[0].envios[0]);
    otro.id=id(40);otro.numero='ENV-002';otro.bultos=[{id:id(41),codigo:'ENV-002-1',estado:'AsignadoARuta'}];
    h.paradas[0].envios.push(otro);h.rendicion.bultos.push({envioId:otro.id,numeroEnvio:otro.numero,bultoId:id(41),codigo:'ENV-002-1',situacion:'NoCargado',requiereDeclaracion:false});
    await store.guardar(h,(await store.sesionLocal()).token);
    await assert.rejects(store.comenzarRuta(id(10)),/Confirmá una carga/);
    let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ENV-002-1'])c=await store.escanearCarga(id(10),c.revision,codigo);
    await assert.rejects(store.comenzarRuta(id(10)),/Confirmá primero/);
    for(const codigo of ['ENV-001-2','ENV-001-3'])c=await store.motivoFaltanteCarga(id(10),c.revision,codigo,'NoLocalizado');
    c=await store.confirmarCarga(id(10),c.revision,true);
    assert.equal((await store.estadoInicioRuta(id(10))).iniciada,false);
    assert.equal(await store.rutaParaSeguimiento(),null);
    await assert.rejects(store.abrirVisita(id(10),otro.id),/Comenzar ruta/);
    await assert.rejects(store.abrirRendicion(id(10)),/en curso/);
    await store.comenzarRuta(id(10));await store.comenzarRuta(id(10));
    assert.equal((await store.estadoInicioRuta(id(10))).resultado,'Pendiente');
    assert.equal((await store.rutaParaSeguimiento()).rutaId,id(10));
    await assert.rejects(store.escanearCarga(id(10),c.revision,'ENV-001-2'),/carga está cerrada/);
    await assert.rejects(store.motivoFaltanteCarga(id(10),c.revision,'ENV-001-2','Otro'),/carga está cerrada/);
    assert.deepEqual((await store.pendientesCarga()).map(o=>o.tipo),['ConfirmarCarga','IniciarRuta']);
    let v=await store.abrirVisita(id(10),otro.id);
    v=await store.resultadoVisita(id(10),otro.id,v.revision,'ENV-002-1',true,null);
    await store.registrarVisita(id(10),otro.id,v.revision,{firma,foto,...posicion});
    const ops=await store.pendientesCarga();
    assert.deepEqual(ops.map(o=>o.tipo),['ConfirmarCarga','IniciarRuta','RegistrarVisita']);
    const anterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>{throw new TypeError('sin red');};
        await assert.rejects(store.sincronizarCargas(),/pendiente/);
        assert.deepEqual(await store.pendientesCarga(),ops);
        globalThis.fetch=async(_,request)=>{
            assert.deepEqual(JSON.parse(request.body).operaciones,ops);
            return {ok:true,json:async()=>({resultados:ops.map(o=>({operacionId:o.operacionId,resultado:'YaAplicada'}))})};
        };
        await store.sincronizarCargas();
        assert.equal((await store.estadoInicioRuta(id(10))).resultado,'Aplicada');
        await assert.rejects(store.escanearCarga(id(10),c.revision,'ENV-001-2'),/carga está cerrada/);
    } finally {globalThis.fetch=anterior;}
});

test('dos pestañas no duplican el inicio y un rechazo se conserva e impide entregar',async()=>{
    await preparar({cargado:false});let c=await store.abrirCarga(id(10));
    for(const codigo of ['ENV-001-1','ENV-001-2','ENV-001-3'])c=await store.escanearCarga(id(10),c.revision,codigo);
    await store.confirmarCarga(id(10),c.revision,false);
    await Promise.allSettled([store.comenzarRuta(id(10)),store.comenzarRuta(id(10))]);
    const ops=await store.pendientesCarga();assert.equal(ops.filter(o=>o.tipo==='IniciarRuta').length,1);
    const anterior=globalThis.fetch;
    try {
        globalThis.fetch=async()=>({ok:true,json:async()=>({resultados:ops.map(o=>({operacionId:o.operacionId,
            resultado:o.tipo==='IniciarRuta'?'Conflicto':'Aplicada',motivo:o.tipo==='IniciarRuta'?'RutaNoAsignada':null}))})});
        await store.sincronizarCargas();
        assert.deepEqual(await store.estadoInicioRuta(id(10)),{iniciada:false,resultado:'Rechazada',detalle:'RutaNoAsignada'});
        await assert.rejects(store.abrirVisita(id(10),id(14)),/Comenzar ruta/);
        await assert.rejects(store.comenzarRuta(id(10)),/rechazado/);
    } finally {globalThis.fetch=anterior;}
});
