// Fixture de aceptación manual: sólo usa el entorno LAN aislado y sus operaciones normales.
const fs = require('node:fs');
const path = require('node:path');
const https = require('node:https');
const assert = require('node:assert/strict');
const repo = path.resolve(__dirname, '..');
const lan = path.join(repo, 'TestResults/lan');
const settings = Object.fromEntries(fs.readFileSync(path.join(lan, 'ambiente.env'), 'utf8').trim().split(/\r?\n/).map(line => {
    const i = line.indexOf('='); return [line.slice(0, i), line.slice(i+1)];
}));
const base = `https://${settings.LAN_IP}:8443`;
const ca = fs.readFileSync(path.join(lan, 'public/root.crt'));
const file = path.join(lan, 'escenario-rf32.json');
let state = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : {};
const save = () => fs.writeFileSync(file, JSON.stringify(state, null, 2));
const operator = '11111111-1111-1111-1111-111111111111';
function client() {
    const cookies = new Map();
    let csrf = null;
    async function request(route, data = null, form = false) {
        const body = data === null ? null : form ? new URLSearchParams(data).toString() : JSON.stringify(data);
        const result = await new Promise((resolve, reject) => {
            const req = https.request(new URL(route, base), { ca, method:body === null ? 'GET' : 'POST', headers:{
                Cookie:[...cookies].map(([k,v]) => `${k}=${v}`).join('; '),
                ...(body !== null && !form && csrf ? { RequestVerificationToken:csrf } : {}),
                ...(body === null ? {} : { 'Content-Type':form ? 'application/x-www-form-urlencoded' : 'application/json', 'Content-Length':Buffer.byteLength(body) })
            } }, res => {
                for (const cookie of res.headers['set-cookie'] || []) { const part=cookie.split(';')[0], i=part.indexOf('='); cookies.set(part.slice(0,i),part.slice(i+1)); }
                const chunks=[]; res.on('data', c => chunks.push(c));
                res.on('end', () => { const bytes=Buffer.concat(chunks); resolve({ status:res.statusCode, text:bytes.toString(), bytes, location:res.headers.location }); });
            });
            req.setTimeout(30000, () => req.destroy(new Error('Tiempo de espera agotado; no se reintenta una escritura automáticamente.')));
            req.on('error', reject); if (body !== null) req.write(body); req.end();
        });
        if (result.status >= 400) throw new Error(`${route}: ${result.status} ${result.text.replace(/<[^>]*>/g, ' ').slice(0,700)}`);
        const token = result.text.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
        if (token) csrf=token[1];
        return result;
    }
    async function postForm(route, values) {
        const page = await request(route.split('?')[0]);
        const token = page.text.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
        assert.ok(token, `Falta antiforgery en ${route}`);
        return request(route, { ...values, __RequestVerificationToken:token[1] }, true);
    }
    return { request, postForm,
        json:async (route, data = null) => JSON.parse((await request(route, data)).text),
        login:async (email, route = '/cuenta/ingresar') => {
            const response = await postForm(route, { Email:email, Contrasena:settings.LAN_DEMO_PASSWORD });
            assert.equal(response.status,302,`No inició sesión ${email}`);
        }
    };
}
async function main() {
    const driver=client(); await driver.login('repartidor@operador-demo.uy','/repartidor/ingresar');
    const session = await driver.json('/api/repartidor/sesion');
    assert.equal(session.operadorId, operator);
    if (state.fecha && state.fecha !== session.fechaOperativa) {
        if (!process.argv.includes('--nuevo-dia')) throw new Error('El escenario pertenece a otra fecha; usar --nuevo-dia para crear otro sin modificar la ruta anterior.');
        const previousFile=path.join(lan,`escenario-rf32-${state.fecha}.json`);
        if (!fs.existsSync(previousFile)) fs.copyFileSync(file,previousFile);
        state={reglasPublicadas:state.reglasPublicadas === true};
        console.log('Se conserva el escenario anterior; se prepara uno para la fecha operativa actual.');
    }
    state.fecha = session.fechaOperativa; save();

    if (!state.reglasPublicadas) {
        const admin=client(); await admin.login('admin@operador-demo.uy');
        await admin.postForm('/backoffice/reglas?handler=Nueva', {});
        const response = await admin.postForm('/backoffice/reglas/borrador?handler=Valores', {
            MaxIntentos:3, PlazoEntreIntentosHoras:24, PlazoEstandarHoras:72, PlazoUrgenteHoras:24,
            AnticipacionReprogramacionHoras:24, MaxParadasPorRuta:20, PlazoDevolucionDias:5,
            ExigeFoto:true, ExigeNombreYDocumento:true, DevolverTrasIntentosAgotados:true,
            AdmiteEntregaParcial:true, Pendientes:'NuevaVisitaSinRecargo', RecargoNuevaVisita:'0'
        });
        assert.equal(response.status,302,'No se guardaron los valores del borrador.');
        const published = await admin.postForm('/backoffice/reglas/borrador?handler=Publicar', { Fecha:'' });
        assert.equal(published.status,302,'No se publicaron las reglas.');
        assert.equal(published.location,'/backoffice/reglas');
        state.reglasPublicadas=true; save(); console.log('Reglas de entrega parcial publicadas.');
    }

    const commerce=client(); await commerce.login('comercio@tiendademo.uy','/portal/ingresar');
    await commerce.postForm('/cuenta/operador', { operadorId:operator });
    if (!state.envioId) {
        const marker='RF32 ANDROID '+state.fecha;
        const existing=await commerce.json('/api/envios/portal?texto='+encodeURIComponent(marker));
        assert.equal(existing.totalRegistros,0,'Hay un envío de ensayo sin manifiesto local; revisar antes de duplicarlo.');
        const created = await commerce.json('/api/envios/', {
            destinatario:{nombre:marker+' · Prueba',telefono:'099123456',email:null,documento:null},
            direccion:{calle:'Av. Italia',numero:'1234',localidad:'Montevideo',departamento:'Montevideo',codigoPostal:'11300',referencia:'Ensayo RF32 con datos ficticios'},
            bultos:[1,2,3].map(() => ({pesoKg:1,largoCm:10,anchoCm:10,altoCm:10})),modalidad:'Estandar'
        });
        state.envioId=created.id; state.numero=created.numero; save(); console.log('Envío creado:',state.numero);
    }
    const detail=await commerce.json('/api/envios/portal/'+state.numero);
    assert.equal(detail.bultos.length,3); state.codigos=detail.bultos.map(b => b.codigo); save();
    if (!state.recibido) {
        const depot=client(); await depot.login('deposito@operador-demo.uy');
        for (const b of detail.bultos.filter(b => b.estado === 'Admitido')) await depot.json('/api/deposito/recepciones',{codigoBulto:b.codigo});
        state.recibido=true; save(); console.log('Tres bultos recibidos en depósito.');
    }
    const dispatch=client(); await dispatch.login('despacho@operador-demo.uy');
    await dispatch.request('/backoffice/rutas');
    if (!state.rutaId) {
        const resources=await dispatch.json('/api/planificacion/recursos?fecha='+state.fecha);
        assert.ok(resources.repartidores.some(r => r.id === session.repartidorId));
        const vehicle=resources.vehiculos.find(v => v.id === '51111111-1111-1111-1111-111111111111') || resources.vehiculos[0];
        assert.ok(vehicle);
        const route=await dispatch.json('/api/planificacion/rutas',{fecha:state.fecha,repartidorId:session.repartidorId,vehiculoId:vehicle.id,envioIds:[state.envioId]});
        state.rutaId=route.id; save(); console.log('Ruta creada.');
    }
    let route=await dispatch.json('/api/planificacion/rutas/'+state.rutaId);
    if (route.estado === 'Planificada') route=await dispatch.json('/api/planificacion/rutas/'+state.rutaId+'/despacho',{revisionEsperada:route.revision});
    const sheet=await driver.json('/api/repartidor/rutas/'+state.rutaId+'/hoja-de-ruta');
    const shipment=sheet.paradas.flatMap(p => p.envios).find(e => e.id === state.envioId);
    assert.equal(shipment.reglasEntrega.admiteEntregaParcial,true); assert.equal(shipment.bultos.length,3);
    state.estadoRuta=route.estado;
    state.urlCarga=base+'/repartidor/carga?rutaId='+state.rutaId;
    state.urlHoja=base+'/repartidor/hoja-de-ruta?rutaId='+state.rutaId;
    const labels=await commerce.request('/api/envios/portal/'+state.numero+'/etiquetas');
    assert.equal(labels.bytes.subarray(0,4).toString(),'%PDF');
    state.etiquetas=path.join(lan,`etiquetas-rf32-${state.fecha}.pdf`);
    fs.writeFileSync(state.etiquetas,labels.bytes);
    save();
    console.log(JSON.stringify(state,null,2));
}
if (require.main === module) main().catch(e => { console.error(e); process.exitCode=1; });
module.exports = { client };
