let controles, vista, generacion=0;
async function lector() {
    if (!globalThis.ZXingBrowser) await new Promise((resolve,reject) => {
        const script=document.createElement('script');
        script.src='/repartidor/lib/zxing/zxing-browser.min.js';
        script.onload=resolve; script.onerror=() => reject(new Error('No se pudo abrir el lector de cámara.'));
        document.head.append(script);
    });
    return new ZXingBrowser.BrowserMultiFormatReader();
}
export async function iniciar(video,ref) {
    detener(); const version=generacion;
    if (!globalThis.isSecureContext || !navigator.mediaDevices?.getUserMedia)
        throw new Error('La cámara necesita HTTPS o localhost. Podés ingresar el código manualmente.');
    vista=video;
    const reader=await lector();
    if (version!==generacion) return;
    let ultimo='', procesando=false;
    const nuevos=await reader.decodeFromConstraints({ audio:false, video:{ facingMode:{ ideal:'environment' } } },video,
        async result => {
            if (!result || procesando || version!==generacion) return;
            const texto=result.getText();
            if (texto===ultimo) return;
            ultimo=texto; procesando=true;
            try { await ref.invokeMethodAsync('CodigoDetectado',texto); } finally { procesando=false; }
        });
    if (version!==generacion) nuevos.stop(); else controles=nuevos;
}
export function detener() {
    generacion++;
    controles?.stop(); controles=null;
    vista?.srcObject?.getTracks().forEach(t => t.stop());
    if (vista) vista.srcObject=null;
    vista=null;
}
