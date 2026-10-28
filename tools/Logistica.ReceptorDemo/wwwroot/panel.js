const clave = new URLSearchParams(location.hash.slice(1)).get('clave') || '';
const $ = id => document.getElementById(id);
let rutaAvisos = '';
let ultimaVersion = '';
let ocupada = false;

async function api(ruta, opciones = {}) {
  const respuesta = await fetch(ruta, {
    ...opciones,
    headers: { 'X-Panel-Clave': clave, ...opciones.headers },
    cache: 'no-store'
  });
  if (!respuesta.ok) {
    throw new Error(respuesta.status === 401
      ? 'Abrí el enlace completo del panel que aparece en la terminal (incluye #clave=…).'
      : `Error del receptor: HTTP ${respuesta.status}.`);
  }
  return respuesta.status === 204 ? null : respuesta.json();
}

function mostrarUrl() {
  const base = $('base-publica').value.trim();
  try {
    const url = new URL(base);
    if (url.protocol !== 'https:' || url.username || url.password || url.pathname !== '/' || url.search || url.hash)
      throw new Error();
    $('url-aviso').textContent = url.origin + rutaAvisos;
  } catch {
    $('url-aviso').textContent = base ? 'Ingresá sólo la base HTTPS del túnel, sin ruta ni parámetros.' : '';
  }
}

async function actualizar() {
  const datos = await api('/api/estado');
  $('estado').textContent = datos.codigo === 200 ? '200 · Éxito' : '503 · Fallo';
  $('estado').className = datos.codigo === 200 ? 'exito' : 'fallo';
  $('exito').setAttribute('aria-pressed', String(datos.codigo === 200));
  $('fallo').setAttribute('aria-pressed', String(datos.codigo === 503));
  rutaAvisos = datos.rutaAvisos;
  $('ruta').textContent = `Ruta receptora: ${rutaAvisos}`;
  $('cantidad').textContent = `(${datos.total} POST desde la última limpieza)`;
  mostrarUrl();
  const version = JSON.stringify(datos.recibidos);
  if (version !== ultimaVersion) {
    ultimaVersion = version;
    $('recibidos').replaceChildren();
    if (!datos.recibidos.length) $('recibidos').textContent = 'Todavía no llegaron avisos.';
    for (const aviso of datos.recibidos) {
      const detalle = document.createElement('details');
      const resumen = document.createElement('summary');
      let cuerpo = aviso.cuerpo;
      let identificacion = '';
      try {
        const json = JSON.parse(cuerpo);
        cuerpo = JSON.stringify(json, null, 2);
        identificacion = ` · ${json.numeroEnvio || ''} · id: ${json.id || '—'}`;
      } catch { /* También se muestran cuerpos que no son JSON. */ }
      resumen.textContent = `#${aviso.numero} · ${new Date(aviso.fecha).toLocaleTimeString()} · HTTP ${aviso.codigoRespuesta}${identificacion}`;
      const pre = document.createElement('pre');
      pre.textContent = `${cuerpo}\n\nX-Aviso-Fecha: ${aviso.fechaFirma}\nX-Aviso-Firma: ${aviso.firma}`;
      detalle.append(resumen, pre);
      $('recibidos').append(detalle);
    }
  }
  $('mensaje').textContent = 'Conectado · Actualización automática cada segundo';
}

async function accion(operacion) {
  if (ocupada) return;
  ocupada = true;
  for (const id of ['exito', 'fallo', 'limpiar']) $(id).disabled = true;
  try {
    await operacion();
    await actualizar();
  } catch (error) {
    $('mensaje').textContent = error.message;
  } finally {
    ocupada = false;
    for (const id of ['exito', 'fallo', 'limpiar']) $(id).disabled = false;
  }
}

for (const [id, codigo] of [['exito', 200], ['fallo', 503]]) {
  $(id).addEventListener('click', () => accion(() => api('/api/respuesta', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ codigo })
  })));
}
$('limpiar').addEventListener('click', () => accion(() => api('/api/recibidos', { method: 'DELETE' })));
$('base-publica').addEventListener('input', mostrarUrl);

async function seguir() {
  if (!ocupada) {
    try { await actualizar(); } catch (error) { $('mensaje').textContent = error.message; }
  }
  setTimeout(seguir, 1000);
}
seguir();
