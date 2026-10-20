// CU-17 y CU-18: la firma en pantalla, la foto comprimida y la posición del dispositivo. Este módulo sólo arma la
// evidencia; guardarla cifrada y encolarla es de hoja-ruta.js, que también valida las reglas del envío.
import { registrarEntrega, registrarIntentoFallido } from './hoja-ruta.js';

const MAX_BYTES = 1024 * 1024;
const firmas = new WeakMap();

// El recuadro de la firma: se dibuja con el dedo, el lápiz o el mouse, sobre fondo blanco para que el PNG se lea.
export function iniciarFirma(canvas) {
    const ctx = canvas.getContext('2d');
    canvas.width = canvas.clientWidth;
    canvas.height = canvas.clientHeight;
    canvas.style.touchAction = 'none';
    const estado = { trazos:0, dibujando:false };
    const limpiar = () => {
        ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.lineWidth = 2.5; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.strokeStyle = '#000';
        estado.trazos = 0;
    };
    const punto = e => {
        const r = canvas.getBoundingClientRect();
        return [(e.clientX - r.left) * canvas.width / r.width, (e.clientY - r.top) * canvas.height / r.height];
    };
    canvas.addEventListener('pointerdown', e => {
        estado.dibujando = true; canvas.setPointerCapture(e.pointerId);
        const [x, y] = punto(e); ctx.beginPath(); ctx.moveTo(x, y); e.preventDefault();
    });
    canvas.addEventListener('pointermove', e => {
        if (!estado.dibujando) return;
        const [x, y] = punto(e); ctx.lineTo(x, y); ctx.stroke(); estado.trazos++; e.preventDefault();
    });
    const terminar = () => { estado.dibujando = false; };
    canvas.addEventListener('pointerup', terminar);
    canvas.addEventListener('pointercancel', terminar);
    limpiar();
    firmas.set(canvas, { estado, limpiar });
}

export function limpiarFirma(canvas) { firmas.get(canvas)?.limpiar(); }

// null si el recuadro está vacío: unos pocos puntos sueltos no son una firma.
function firmaPng(canvas) {
    const firma = firmas.get(canvas);
    if (!firma || firma.estado.trazos < 5) return null;
    return { tipoContenido:'image/png', contenidoBase64:canvas.toDataURL('image/png').split(',')[1] };
}

// La foto de la cámara se achica y se pasa a JPEG hasta entrar en 1 MB, el máximo que acepta el servidor.
async function comprimirFoto(input, ladoMaximo = 1600) {
    const archivo = input?.files?.[0];
    if (!archivo) return null;
    if (!archivo.type.startsWith('image/')) throw new Error('Elegí una imagen.');
    const imagen = await createImageBitmap(archivo);
    const escala = Math.min(1, ladoMaximo / Math.max(imagen.width, imagen.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(imagen.width * escala);
    canvas.height = Math.round(imagen.height * escala);
    canvas.getContext('2d').drawImage(imagen, 0, 0, canvas.width, canvas.height);
    imagen.close();
    for (const calidad of [0.8, 0.6, 0.4]) {
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', calidad));
        if (blob && blob.size <= MAX_BYTES) return { tipoContenido:'image/jpeg', contenidoBase64:await base64(blob) };
    }
    throw new Error('La foto es demasiado grande incluso comprimida. Sacá otra con menos resolución.');
}

async function base64(blob) {
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let binario = '';
    for (let i = 0; i < bytes.length; i += 0x8000) binario += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(binario);
}

// El GPS funciona sin conexión. Se acepta una posición de hasta un minuto, para no demorar al repartidor.
function posicion() {
    return new Promise((resolve, reject) => {
        if (!navigator.geolocation) { reject(new Error('Este dispositivo no informa su ubicación.')); return; }
        navigator.geolocation.getCurrentPosition(
            p => resolve({ latitud:p.coords.latitude, longitud:p.coords.longitude }),
            () => reject(new Error('No pudimos obtener tu ubicación. Activá la ubicación del dispositivo e intentá de nuevo.')),
            { enableHighAccuracy:true, timeout:15000, maximumAge:60000 });
    });
}

export async function confirmarEntrega(ruta, envioId, canvas, inputFoto, nombreReceptor, documentoReceptor) {
    const firma = firmaPng(canvas);
    if (!firma) throw new Error('Pedí la firma de quien recibe en el recuadro.');
    const foto = await comprimirFoto(inputFoto);
    return registrarEntrega(ruta, envioId, { firma, foto, nombreReceptor, documentoReceptor, ...await posicion() });
}

export async function confirmarIntento(ruta, envioId, motivoId, inputFoto, observaciones) {
    const foto = await comprimirFoto(inputFoto);
    return registrarIntentoFallido(ruta, envioId, { motivoId, foto, observaciones, ...await posicion() });
}
