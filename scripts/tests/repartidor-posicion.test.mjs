import { test, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

let tracker, mensajes, capturas, sincronizaciones, gps, ruta, fallar, refrescos;
const originalNavigator = Object.getOwnPropertyDescriptor(globalThis,'navigator');
beforeEach(async t => {
    mensajes=[]; capturas=[]; sincronizaciones=0; refrescos=0; fallar=false; ruta={rutaId:'ruta',token:'sesion'};
    gps = (ok) => ok({coords:{latitude:-34,longitude:-56},timestamp:Date.now()});
    globalThis.document = Object.assign(new EventTarget(),{visibilityState:'visible'});
    const eventos = new EventTarget();
    globalThis.addEventListener=eventos.addEventListener.bind(eventos);
    globalThis.removeEventListener=eventos.removeEventListener.bind(eventos);
    Object.defineProperty(globalThis,'navigator',{configurable:true,value:{onLine:false,
        geolocation:{getCurrentPosition:(...args) => gps(...args)}}});
    const deps={rutaParaSeguimiento:async () => ruta,guardarPosicion:async (...args) => {capturas.push(args);return true;},
        pendientesCarga:async () => [1], sincronizarCargas:async () => {
            sincronizaciones++; if (fallar) throw new TypeError('Sin servidor'); return {aplicadas:1};}};
    globalThis.__posicionTest=deps;
    const source=(await readFile(new URL('../../src/Logistica.Repartidor.Pwa/wwwroot/js/seguimiento-posicion.js',import.meta.url),'utf8'))
        .replace("import { rutaParaSeguimiento, guardarPosicion, sincronizarCargas, pendientesCarga } from './hoja-ruta.js';",
            'const { rutaParaSeguimiento, guardarPosicion, sincronizarCargas, pendientesCarga } = globalThis.__posicionTest;');
    tracker=await import('data:text/javascript;base64,'+Buffer.from(source+`\n// ${Math.random()}`).toString('base64'));
    t.mock.timers.enable({apis:['setInterval','Date'],now:new Date('2026-10-21T12:00:00Z')});
    tracker.iniciar({invokeMethodAsync:async (metodo,mensaje) => {
        if (metodo==='EstadoSeguimiento') mensajes.push(mensaje);
        if (metodo==='ActualizarHojaSeguimiento') refrescos++;
    }});
    await new Promise(resolve => setImmediate(resolve));
});
afterEach(() => {
    tracker.detener();delete globalThis.__posicionTest;delete globalThis.document;
    delete globalThis.addEventListener;delete globalThis.removeEventListener;
    if (originalNavigator) Object.defineProperty(globalThis,'navigator',originalNavigator);
    else delete globalThis.navigator;
});

test('CU-53 obtiene GPS sin conexión y repite la captura a los 30 segundos',async t => {
    assert.equal(capturas.length,1);assert.equal(sincronizaciones,0);assert.match(mensajes.at(-1),/sin conexión/);
    t.mock.timers.tick(29999);await new Promise(resolve => setImmediate(resolve));assert.equal(capturas.length,1);
    t.mock.timers.tick(1);await new Promise(resolve => setImmediate(resolve));assert.equal(capturas.length,2);
});
test('CU-53 no captura con la PWA oculta y vuelve a hacerlo cuando queda visible',async () => {
    document.visibilityState='hidden';await tracker.actualizar();assert.equal(capturas.length,1);
    assert.match(mensajes.at(-1),/pausado/);
    document.visibilityState='visible';await tracker.actualizar();assert.equal(capturas.length,2);
});
test('CU-53 avisa permiso denegado o GPS no disponible y permite reintentar',async () => {
    gps=(_,error) => error({code:1});await tracker.actualizar();assert.match(mensajes.at(-1),/permiso/);
    gps=(_,error) => error({code:3});await tracker.actualizar();assert.match(mensajes.at(-1),/no disponible/);
    assert.equal(capturas.length,1);
});
test('CU-53 sincroniza al disponer de red y borra el indicador cuando termina la jornada',async () => {
    navigator.onLine=true;await tracker.actualizar();assert.equal(sincronizaciones,1);
    ruta=null;await tracker.actualizar();assert.equal(capturas.length,2);assert.equal(mensajes.at(-1),null);
});

test('ADR-0004 reintenta pendientes aunque no haya ruta o el GPS esté denegado y refresca la hoja',async t => {
    navigator.onLine=true; ruta=null; await tracker.actualizar();
    assert.equal(sincronizaciones,1); assert.equal(refrescos,1);
    ruta={rutaId:'ruta',token:'sesion'}; gps=(_,error) => error({code:1});
    t.mock.timers.tick(30000); await new Promise(resolve => setImmediate(resolve));
    assert.equal(sincronizaciones,2); assert.equal(refrescos,2); assert.equal(capturas.length,1);
});

test('ADR-0004 aplica espera creciente al fallar el envío, aun después de rendir',async t => {
    navigator.onLine=true; ruta=null; fallar=true;
    await tracker.actualizar(); assert.equal(sincronizaciones,1);
    t.mock.timers.tick(30000); await new Promise(resolve => setImmediate(resolve)); assert.equal(sincronizaciones,2);
    t.mock.timers.tick(30000); await new Promise(resolve => setImmediate(resolve)); assert.equal(sincronizaciones,2);
    t.mock.timers.tick(30000); await new Promise(resolve => setImmediate(resolve)); assert.equal(sincronizaciones,3);
    assert.equal(capturas.length,1);
});
