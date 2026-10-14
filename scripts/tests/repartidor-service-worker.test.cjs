const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname,
    '../../src/Logistica.Repartidor.Pwa/wwwroot/service-worker.published.js'), 'utf8');

function worker(fetch, match) {
    return vm.runInNewContext(source + '\nonFetch;', {
        self: { origin: 'https://localhost', assetsManifest: { version: 'test', assets: [] },
            importScripts() {}, addEventListener() {} },
        URL, fetch, caches: { async open() { return { match }; } }
    });
}
function navigate(url = 'https://localhost/repartidor/') {
    return { request: { method: 'GET', mode: 'navigate', url } };
}

test('con red respeta la respuesta del servidor y no abre el shell cacheado', async () => {
    const response = { status: 302, location: '/repartidor/ingresar' };
    const onFetch = worker(async () => response, async () => assert.fail('No debe usar la caché'));
    assert.equal(await onFetch(navigate()), response);
});
test('sin red permite abrir el shell previamente descargado', async () => {
    const shell = { status: 200 };
    const onFetch = worker(async () => { throw new Error('sin red'); }, async key => {
        assert.equal(key, 'index.html'); return shell;
    });
    assert.equal(await onFetch(navigate()), shell);
});
test('sin red no reemplaza el login por el shell', async () => {
    const onFetch = worker(async () => { throw new Error('sin red'); }, async () => assert.fail('Login no usa caché'));
    await assert.rejects(onFetch(navigate('https://localhost/repartidor/ingresar')), /sin red/);
});
