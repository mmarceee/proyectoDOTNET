// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /(^|\/)service-worker\.js$/ ];

// La PWA se sirve en /repartidor/ (addendum 5), pero las URLs del manifiesto ya incluyen ese prefijo
// ("repartidor/_framework/..."), así que se resuelven desde la raíz del sitio y no desde la ubicación del
// service worker; si no, se pedirían como /repartidor/repartidor/... y la instalación fallaría.
const base = "/";
const baseUrl = new URL(base, self.origin);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(new URL(asset.url, baseUrl), { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.mode === 'navigate') {
        // Con red, el servidor decide si corresponde abrir la app o redirigir al login.
        // No guardar el HTML del login en la caché de la aplicación.
        try {
            return await fetch(event.request);
        } catch (error) {
            // El login necesita conexión; nunca reemplazarlo por la app previamente descargada.
            const path = new URL(event.request.url).pathname.replace(/\/$/, '');
            if (path === '/repartidor/ingresar') throw error;
            const cache = await caches.open(cacheName);
            const shell = await cache.match('index.html');
            if (shell) return shell;
            throw error;
        }
    }
    let cachedResponse = null;
    if (event.request.method === 'GET') {
        const request = event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}
