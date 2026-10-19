import { copyFile, mkdir } from 'node:fs/promises';
const destino = new URL('../../src/Logistica.Repartidor.Pwa/wwwroot/lib/zxing/', import.meta.url);
await mkdir(destino,{ recursive:true });
for (const [origen,nombre] of [
    ['@zxing/browser/umd/zxing-browser.min.js','zxing-browser.min.js'],
    ['@zxing/browser/LICENSE','LICENSE-browser.txt'], ['@zxing/library/LICENSE','LICENSE-library.txt']])
    await copyFile(new URL(`node_modules/${origen}`,import.meta.url),new URL(nombre,destino));
