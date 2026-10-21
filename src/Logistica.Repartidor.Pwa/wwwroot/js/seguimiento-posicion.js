import { rutaParaSeguimiento, guardarPosicion, sincronizarCargas, pendientesCarga } from './hoja-ruta.js';

let referencia, timer, ocupado = false;
let fallos = 0, proximoIntento = 0;
function informar(mensaje) { referencia?.invokeMethodAsync('EstadoSeguimiento',mensaje).catch(() => {}); }
async function sincronizarPendientes() {
    if (navigator.onLine === false || Date.now() < proximoIntento || !(await pendientesCarga()).length) return;
    try {
        const resultado = await sincronizarCargas();
        fallos = 0; proximoIntento = Date.now()+30000;
        // ADR-0004 §2.8: traer los cambios de la ruta después de aplicar operaciones.
        if (resultado.aplicadas > 0) await referencia?.invokeMethodAsync('ActualizarHojaSeguimiento');
        informar(resultado.mensaje?.includes('rechazadas') ? 'Hay operaciones rechazadas. Revisá la sincronización de la ruta.'
            : 'Seguimiento activo. Sincronización realizada.');
    } catch {
        proximoIntento = Date.now()+Math.min(30000*2**Math.min(fallos++,4),300000);
        informar('Sin conexión al servidor. Las operaciones se conservan y se reintentará con espera creciente.');
    }
}
function posicion() {
    return new Promise((resolve,reject) => {
        if (!navigator.geolocation) { reject(new Error('Este dispositivo no informa su ubicación.')); return; }
        navigator.geolocation.getCurrentPosition(p => resolve({ latitud:p.coords.latitude, longitud:p.coords.longitude,
            capturadaEn:p.timestamp }), reject, { enableHighAccuracy:true, maximumAge:0, timeout:12000 });
    });
}
async function capturar() {
    const ruta = await rutaParaSeguimiento();
    if (!ruta) { informar(null); return; }
    if (document.visibilityState !== 'visible') {
        informar('Seguimiento pausado: mantené la PWA visible para registrar la ubicación.'); return;
    }
    let p;
    try { p = await posicion(); }
    catch (e) {
        informar(e.code === 1 ? 'Seguimiento desactivado: habilitá el permiso de ubicación.'
            : 'Ubicación no disponible. Se reintentará automáticamente.');
        return;
    }
    if (document.visibilityState !== 'visible') return;
    const guardada = await guardarPosicion(ruta.rutaId,ruta.token,p);
    if (!guardada) return;
    informar('Seguimiento activo. Posición guardada en el dispositivo.');
    if (navigator.onLine !== false) {
        await sincronizarPendientes();
    } else informar('Seguimiento activo sin conexión. Las posiciones quedan pendientes de sincronizar.');
}
export async function actualizar() {
    if (ocupado || !referencia) return;
    ocupado = true;
    try {
        // Los pendientes se reintentan aunque no haya ruta capturable, falte GPS o ya se haya rendido.
        await sincronizarPendientes();
        if (navigator.locks) await navigator.locks.request('logistica-capturar-posicion', { ifAvailable:true }, lock => lock ? capturar() : undefined);
        else await capturar();
    } catch (e) { informar(e.message || 'No se pudo guardar la posición. Revisá el almacenamiento del dispositivo.'); }
    finally { ocupado = false; }
}
function reconectar() { proximoIntento = 0; void actualizar(); }
export function iniciar(ref) {
    detener(); referencia = ref;
    timer = setInterval(actualizar,30000);
    document.addEventListener('visibilitychange',actualizar);
    globalThis.addEventListener('online',reconectar);
    void actualizar();
}
export function detener() {
    clearInterval(timer); timer = null; referencia = null;
    document.removeEventListener('visibilitychange',actualizar);
    globalThis.removeEventListener('online',reconectar);
}
