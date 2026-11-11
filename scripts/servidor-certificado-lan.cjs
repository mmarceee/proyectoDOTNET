// Sólo entrega la CA pública del ambiente de ensayo; nunca sirve directorios o claves privadas.
const fs = require('node:fs');
const http = require('node:http');
const crypto = require('node:crypto');
const [certificateFile, address] = process.argv.slice(2);
if (!certificateFile || !address) throw new Error('Uso: node servidor-certificado-lan.cjs <CA.cer> <IP LAN>');
const certificate = fs.readFileSync(certificateFile);
const fingerprint = new crypto.X509Certificate(certificate).fingerprint256;
http.createServer((req, res) => {
    if (req.url === '/proyectoDOTNET-LAN.cer') {
        res.writeHead(200, { 'Content-Type':'application/pkix-cert',
            'Content-Disposition':'attachment; filename="proyectoDOTNET-LAN.cer"', 'Cache-Control':'no-store' });
        return res.end(certificate);
    }
    if (req.url !== '/') { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'Content-Type':'text/html; charset=utf-8', 'Cache-Control':'no-store' });
    res.end(`<!doctype html><html lang="es"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Certificado de prueba LAN</title><body><h1>proyectoDOTNET · Prueba LAN</h1>
<p><a href="/proyectoDOTNET-LAN.cer">Descargar certificado público</a></p>
<p>Android: Ajustes → Seguridad → Cifrado y credenciales → Instalar un certificado → Certificado de CA.</p>
<p>Después de instalarlo: <a href="https://${address}:8443/repartidor/">abrir la PWA por HTTPS</a>.</p>
<p>Huella SHA-256:</p><code>${fingerprint}</code>
<p>Este certificado pertenece sólo al entorno de ensayo. Podés quitarlo al terminar las pruebas.</p></body></html>`);
}).listen(8081, address, () => console.log(`Certificado público: http://${address}:8081/`));
