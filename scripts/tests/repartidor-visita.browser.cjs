// Ensayo del build publicado con service worker real y API simulada; no usa datos del usuario.
// Requiere Playwright instalado (o PLAYWRIGHT_MODULE_PATH) y Chrome.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const repo = path.resolve(__dirname, '../..');
const published = path.resolve(process.env.PWA_PUBLISH_DIR || path.join(repo, 'TestResults/rf32-cierre-publish/wwwroot/repartidor'));
const output = path.join(repo, 'TestResults/rf32-cierre-browser');
const id = n => '40000000-0000-4000-8000-' + String(n).padStart(12, '0');
const now = new Date(), day = new Intl.DateTimeFormat('en-CA', { timeZone:'America/Montevideo' }).format(now);
const sesion = { usuarioId:id(1), operadorId:id(2), repartidorId:id(3), fechaOperativa:day,
    zonaHorariaOperador:'America/Montevideo', verificadaEn:now.toISOString() };
const envio = { id:id(14), numero:'ENV-000042', estado:'EnTransito', destinatarioNombre:'Ana Pérez',
    destinatarioTelefono:'099123456', direccion:{ calle:'Av. Italia', numero:'1234', localidad:'Montevideo',
        departamento:'Montevideo', codigoPostal:'11300' }, franja:null,
    bultos:[1,2,3].map(n => ({ id:id(20+n), codigo:'ENV-000042-'+n, estado:'EnTransito' })),
    reglasEntrega:{ origen:'Version', versionId:id(30), firmaObligatoria:true, exigeFoto:true,
        exigeNombreYDocumento:true, maxIntentos:3, plazoEntreIntentos:'1.00:00:00', admiteEntregaParcial:true,
        motivosNoEntrega:[{ id:id(31), codigo:'RECHAZADO', nombreParaMostrar:'Rechazado por destinatario',
            reprogramable:true, exigeEvidencia:true }] } };
const hoja = { ...sesion, schemaVersion:3, rutaId:id(10), revisionRuta:1, fechaRuta:day, estadoRuta:'EnCurso',
    generadaEn:now.toISOString(), vehiculo:{ id:id(11), matricula:'DEMO001' },
    zonas:[{ id:id(12), codigo:'MVD', nombre:'Montevideo' }],
    paradas:[{ id:id(13), orden:1, estado:'Pendiente', direccion:'Av. Italia 1234', envios:[envio] }],
    rendicion:{ iniciada:null, bultos:envio.bultos.map(b => ({ envioId:envio.id, numeroEnvio:envio.numero,
        bultoId:b.id, codigo:b.codigo, situacion:'NoIntentado', requiereDeclaracion:true })) } };
const mime = { '.js':'text/javascript', '.json':'application/json', '.html':'text/html', '.css':'text/css',
    '.wasm':'application/wasm', '.png':'image/png', '.dat':'application/octet-stream', '.webmanifest':'application/manifest+json' };
let mode = 'lose';
const received = [], applied = new Map();
const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname.startsWith('/api/')) {
        res.setHeader('Content-Type', 'application/json');
        res.setHeader('Cache-Control', 'no-store');
        if (url.pathname.endsWith('/sesion')) return res.end(JSON.stringify(sesion));
        if (url.pathname.endsWith('/hoja-de-ruta')) return res.end(JSON.stringify(hoja));
        if (url.pathname.endsWith('/sincronizacion')) {
            let body = '';
            req.on('data', chunk => body += chunk);
            req.on('end', () => {
                const ops = JSON.parse(body).operaciones;
                received.push(...ops);
                const resultados = ops.map(op => {
                    if (mode === 'reject' && op.tipo === 'RegistrarVisita') return { operacionId:op.operacionId, resultado:'Conflicto',
                        motivo:'BultoEnDeposito', bultos:[{ codigoBulto:'ENV-000042-3', motivo:'BultoEnDeposito' }] };
                    const repeated = applied.has(op.operacionId);
                    if (repeated) assert.deepEqual(op, applied.get(op.operacionId));
                    else applied.set(op.operacionId, op);
                    return { operacionId:op.operacionId, resultado:repeated ? 'YaAplicada' : 'Aplicada' };
                });
                if (mode === 'lose') return req.socket.destroy();
                res.end(JSON.stringify({ resultados }));
            });
            return;
        }
        return res.end('{}');
    }
    if (url.pathname === '/cuenta/estilo.css') { res.setHeader('Content-Type', 'text/css'); return res.end(''); }
    let relative = url.pathname.replace(/^\/repartidor\//, '');
    if (!path.extname(relative)) relative = 'index.html';
    const file = path.resolve(published, relative);
    if (!file.startsWith(published + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
        res.writeHead(404); return res.end();
    }
    res.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
    res.setHeader('Cache-Control', 'no-cache');
    fs.createReadStream(file).pipe(res);
});

async function run() {
    assert.ok(fs.existsSync(path.join(published, 'service-worker-assets.js')), 'Publicar la PWA antes del ensayo.');
    fs.mkdirSync(output, { recursive:true });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const origin = 'http://127.0.0.1:' + server.address().port;
    const target = origin + '/repartidor/entrega?rutaId=' + id(10) + '&envioId=' + id(14);
    const browser = await chromium.launch({ channel:'chrome', headless:true });
    const errors = [], dialogs = [], expected = [];
    const context = await browser.newContext({ viewport:{ width:390, height:844 },
        geolocation:{ latitude:-34.9, longitude:-56.1 }, permissions:['geolocation'] });
    let p = await context.newPage();
    const contexts = [context];
    p.setDefaultTimeout(20000);
    p.on('pageerror', err => errors.push(err.message));
    p.on('dialog', async dialog => {
        dialogs.push({ type:dialog.type(), message:dialog.message() });
        const action = expected.shift();
        if (!action) errors.push('Diálogo no esperado: ' + dialog.type());
        if (action === 'accept') await dialog.accept(); else await dialog.dismiss();
    });
    const button = name => p.getByRole('button', { name, exact:true });
    const back = () => p.getByRole('link', { name:'Volver a la ruta', exact:true });
    async function ready() { await p.locator('#codigo-entrega').waitFor({ timeout:60000 }); }
    async function scan(code) {
        await button('Registrar').waitFor({ state:'visible' });
        await p.waitForFunction(() => !Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Registrar')?.disabled);
        await p.locator('#codigo-entrega').fill(code); await button('Registrar').click();
        await p.locator('section').filter({ has:p.getByText(code, { exact:true }) }).getByText('Escaneado para entregar', { exact:true }).waitFor();
    }
    async function sign() {
        await p.waitForFunction(() => { const c = document.querySelector('#firma'); return c && c.width === c.clientWidth && c.height === c.clientHeight; });
        await p.locator('#firma').scrollIntoViewIfNeeded();
        const box = await p.locator('#firma').boundingBox();
        await p.mouse.move(box.x+25, box.y+40); await p.mouse.down();
        for (let n=1; n<=12; n++) await p.mouse.move(box.x+25+n*8, box.y+40+n%3*10);
        await p.mouse.up();
        await p.getByText('Tenés captura sin confirmar. Confirmá la visita para guardar la firma, foto y textos.', { exact:true }).waitFor();
    }
    async function pending() { return p.evaluate(async () => (await (await import('/repartidor/js/hoja-ruta.js')).pendientesCarga()).filter(op => op.tipo === 'RegistrarVisita')); }
    try {
        await p.goto(target); await ready();
        const image = Buffer.from(await p.evaluate(() => {
            const c = document.createElement('canvas'); c.width=320; c.height=240;
            const ctx = c.getContext('2d'); ctx.fillStyle='#dfebf5'; ctx.fillRect(0,0,320,240);
            ctx.fillStyle='#123'; ctx.font='24px sans-serif'; ctx.fillText('Presencia RF 32',30,120);
            return c.toDataURL('image/png').split(',')[1];
        }), 'base64');
        await p.evaluate(async () => { await navigator.serviceWorker.ready; });
        await p.reload(); await ready();
        assert.equal(await p.evaluate(() => !!navigator.serviceWorker.controller), true);
        console.log('Shell publicado controlado por service worker.');
        await scan('ENV-000042-1'); await scan('ENV-000042-2');
        await p.locator('#motivo-ENV-000042-3').selectOption(id(31));

        // Los resultados persistidos no generan advertencia.
        await back().click(); await p.waitForURL('**/hoja-de-ruta?**');
        assert.equal(dialogs.length, 0);
        await p.goto(target); await ready();
        assert.equal(await p.getByText('Escaneado para entregar', { exact:true }).count(), 2);
        console.log('Escaneos persistidos sin advertencia.');

        // Texto sin confirmar: cancelar conserva la captura; aceptar conserva sólo resultados.
        await p.locator('#observaciones').fill('No confirmado');
        expected.push('dismiss'); await back().click();
        assert.match(p.url(), /\/entrega\?/);
        assert.equal(await p.locator('#observaciones').inputValue(), 'No confirmado');
        assert.match(dialogs.at(-1).message, /resultados por bulto quedan guardados/);
        expected.push('accept'); await back().click(); await p.waitForURL('**/hoja-de-ruta?**');
        await p.goto(target); await ready();
        assert.equal(await p.locator('#observaciones').inputValue(), '');
        console.log('Advertencia de texto: cancelar y aceptar.');

        // Firma sin otros datos: se advierte; al borrar o cambiar su alcance deja de advertirse.
        await sign(); expected.push('dismiss'); await back().click();
        await button('Borrar firma').click();
        const beforeClear = dialogs.length;
        await back().click(); await p.waitForURL('**/hoja-de-ruta?**');
        assert.equal(dialogs.length, beforeClear);
        await p.goto(target); await ready(); await sign();
        await button('Quitar de entregados').first().click();
        const beforeChange = dialogs.length;
        await back().click(); await p.waitForURL('**/hoja-de-ruta?**');
        assert.equal(dialogs.length, beforeChange);
        await p.goto(target); await ready(); await scan('ENV-000042-1');
        await button('Confirmar visita').click();
        await p.getByRole('alert').filter({ hasText:'Pedí la firma' }).waitFor();
        assert.equal((await pending()).length, 0);
        console.log('Advertencia de firma e invalidación del conjunto.');

        // El archivo por sí solo también activa la advertencia.
        await p.locator('#foto-visita').setInputFiles({ name:'presencia.png', mimeType:'image/png', buffer:image });
        expected.push('dismiss'); await back().click();
        assert.equal(dialogs.at(-1).type, 'confirm');

        // Recarga real: beforeunload y conservación de resultados, sin persistir foto/textos/firma.
        await p.locator('#nombre').fill('Ana');
        await p.locator('#documento').fill('1234567');
        expected.push('accept'); await p.reload(); await ready();
        assert.equal(dialogs.at(-1).type, 'beforeunload');
        assert.equal(await p.locator('#nombre').inputValue(), '');
        assert.equal(await p.locator('#foto-visita').evaluate(el => el.files.length), 0);
        console.log('Foto y beforeunload verificados.');

        await sign();
        await p.locator('#foto-visita').setInputFiles({ name:'presencia.png', mimeType:'image/png', buffer:image });
        await p.locator('#nombre').fill('Ana'); await p.locator('#documento').fill('1234567');
        await context.setOffline(true);
        await button('Confirmar visita').click();
        await p.getByText('Visita guardada en este dispositivo, pendiente de sincronizar.', { exact:true }).waitFor();
        const [op] = await pending();
        assert.equal(op.tipo, 'RegistrarVisita'); assert.equal(op.visita.bultos.length, 3);
        assert.ok(op.visita.firma.contenidoBase64); assert.equal(op.visita.foto.tipoContenido, 'image/jpeg');
        assert.equal(op.visita.nombreReceptor, 'Ana');
        const beforeConfirm = dialogs.length;
        await p.reload();
        await p.getByText('Visita guardada en este dispositivo, pendiente de sincronizar.', { exact:true }).waitFor({ timeout:60000 });
        assert.equal(dialogs.length, beforeConfirm);
        assert.deepEqual(await pending(), [op]);
        console.log('Visita con evidencia reabierta sin red.');
        await p.screenshot({ path:path.join(output, 'visita-offline.png'), fullPage:true });

        // Pérdida de respuesta: mismo ID y contenido en la segunda sincronización.
        await context.setOffline(false);
        await p.evaluate(async () => { try { await (await import('/repartidor/js/hoja-ruta.js')).sincronizarCargas(); } catch {} });
        assert.equal([...applied.values()].filter(o => o.tipo === 'RegistrarVisita').length, 1);
        assert.deepEqual(applied.get(op.operacionId), op);
        mode = 'ack';
        await p.evaluate(async () => (await import('/repartidor/js/hoja-ruta.js')).sincronizarCargas());
        await p.reload();
        await p.getByText('Visita confirmada por el servidor.', { exact:true }).waitFor();
        const visits = received.filter(o => o.tipo === 'RegistrarVisita');
        assert.ok(visits.length >= 2);
        assert.equal([...applied.values()].filter(o => o.tipo === 'RegistrarVisita').length, 1);
        assert.ok(visits.every(o => o.operacionId === op.operacionId));
        assert.equal((await pending()).length, 0);

        // Otro dispositivo ficticio: errores de captura y rechazo con diagnóstico conservado.
        mode = 'reject';
        const other = await browser.newContext({ viewport:{ width:390, height:844 },
            geolocation:{ latitude:-34.9, longitude:-56.1 }, permissions:['geolocation'] });
        contexts.push(other); p = await other.newPage(); p.setDefaultTimeout(20000);
        p.on('pageerror', err => errors.push(err.message));
        await p.goto(target); await ready();
        await p.evaluate(() => {
            navigator.mediaDevices.getUserMedia = async () => { throw new DOMException('Permiso denegado', 'NotAllowedError'); };
        });
        await button('Escanear con cámara').click();
        await p.getByRole('alert').filter({ hasText:'Permiso denegado' }).waitFor();
        await scan('ENV-000042-1'); await scan('ENV-000042-2');
        await p.locator('#motivo-ENV-000042-3').selectOption(id(31));
        await sign();
        await p.locator('#nombre').fill('Ana'); await p.locator('#documento').fill('1234567');
        await p.locator('#foto-visita').setInputFiles({ name:'invalida.png', mimeType:'image/png', buffer:Buffer.from('imagen ilegible') });
        await button('Confirmar visita').click();
        await p.getByRole('alert').filter({ hasText:'No pudimos leer la foto' }).waitFor();
        assert.equal((await pending()).length, 0);
        await p.locator('#foto-visita').setInputFiles({ name:'presencia.png', mimeType:'image/png', buffer:image });
        await p.evaluate(() => {
            window.posicionOriginal = navigator.geolocation.getCurrentPosition.bind(navigator.geolocation);
            navigator.geolocation.getCurrentPosition = (_, error) => error({ code:1 });
        });
        await button('Confirmar visita').click();
        await p.getByRole('alert').filter({ hasText:'No pudimos obtener tu ubicación' }).waitFor();
        assert.equal((await pending()).length, 0);
        await p.evaluate(() => { navigator.geolocation.getCurrentPosition = window.posicionOriginal; delete window.posicionOriginal; });
        await button('Confirmar visita').click();
        await p.getByRole('status').filter({ hasText:'Visita rechazada: no se aplicó ningún resultado de esta visita. Avisá a despacho.' }).waitFor();
        await p.getByRole('heading', { name:'Detalle del rechazo', exact:true }).waitFor();
        const rejection = await p.evaluate(async () => (await (await import('/repartidor/js/hoja-ruta.js')).estadosEnvios('40000000-0000-4000-8000-000000000010'))[0]);
        assert.equal(rejection.estado, 'Rechazada');
        assert.deepEqual(rejection.bultos, [{ codigoBulto:'ENV-000042-3', motivo:'BultoEnDeposito' }]);
        assert.equal((await pending()).length, 0);
        await p.screenshot({ path:path.join(output, 'visita-rechazada.png'), fullPage:true });

        assert.deepEqual(expected, []); assert.deepEqual(errors, []);
        const report = { fecha:new Date().toISOString(), navegador:await browser.version(), viewport:'390x844',
            serviceWorkerReal:true, api:'simulada', verificaciones:[
                'resultados persistidos no advierten', 'cancelar/aceptar salida con texto', 'firma sola y borrado',
                'cambio del conjunto invalida firma', 'foto sola advierte', 'beforeunload al recargar',
                'captura no confirmada se pierde como fue acordado', 'confirmacion offline con evidencia',
                'shell publicado reabre sin red', 'operacion identica tras reapertura y perdida de respuesta',
                'sin advertencia tras persistir la visita', 'camara denegada permite ingreso manual',
                'foto ilegible y error de posicion no encolan una visita', 'rechazo visible con diagnostico por bulto'
            ], dialogos:dialogs.map(d => d.type), errores:errors };
        fs.writeFileSync(path.join(output, 'resultado.json'), JSON.stringify(report, null, 2));
        console.log(JSON.stringify(report, null, 2));
    } catch (err) {
        console.error('URL al fallar:', p.url());
        await p.screenshot({ path:path.join(output, 'fallo.png'), fullPage:true }).catch(() => {});
        fs.writeFileSync(path.join(output, 'fallo.txt'), await p.locator('body').innerText().catch(() => 'Sin DOM'));
        throw err;
    } finally { for (const ctx of contexts) await ctx.close(); await browser.close(); }
}
run().catch(err => { console.error(err); process.exitCode=1; }).finally(() => server.close());
