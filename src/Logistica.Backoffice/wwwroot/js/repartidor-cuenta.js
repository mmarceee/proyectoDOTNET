// Invalida la identidad offline al cambiar/cerrar una sesión. No elimina hojas cifradas ni futuras operaciones pendientes.
async function bloquearEspacio() {
    if (typeof BroadcastChannel === 'function') {
        const canal = new BroadcastChannel('logistica-repartidor');
        canal.postMessage('espacio-cerrado'); canal.close();
    }
    if (!globalThis.indexedDB) return;
    await new Promise((resolve, reject) => {
        const r = indexedDB.open('logistica-repartidor-v1');
        let inexistente = false;
        r.onupgradeneeded = () => { inexistente = true; r.transaction.abort(); };
        r.onerror = () => inexistente ? resolve() : reject(r.error);
        r.onsuccess = () => {
            const db = r.result;
            if (!db.objectStoreNames.contains('espacios')) { db.close(); resolve(); return; }
            const tx = db.transaction('espacios', 'readwrite');
            tx.objectStore('espacios').delete('activo');
            tx.oncomplete = () => { db.close(); resolve(); };
            tx.onabort = tx.onerror = () => { db.close(); reject(tx.error); };
        };
    });
}
if (document.querySelector('[data-inicio-sesion]')) bloquearEspacio().catch(() => {});
for (const form of document.querySelectorAll('[data-inicio-sesion], [data-cerrar-espacio]')) {
    form.addEventListener('submit', async event => {
        if (form.dataset.espacioBloqueado === 'true') return;
        event.preventDefault();
        try {
            await bloquearEspacio();
            form.dataset.espacioBloqueado = 'true';
            form.requestSubmit(event.submitter);
        } catch {
            let aviso = form.querySelector('[data-error-espacio]');
            if (!aviso) { aviso = document.createElement('p'); aviso.dataset.errorEspacio = ''; aviso.setAttribute('role', 'alert'); aviso.className = 'text-danger'; form.append(aviso); }
            aviso.textContent = 'No pudimos bloquear la copia local. Cerrá las otras pestañas y volvé a intentarlo.';
        }
    });
}
