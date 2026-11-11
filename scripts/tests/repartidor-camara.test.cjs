const {test}=require('node:test');
const assert=require('node:assert/strict');
const {readFile}=require('node:fs/promises');
const ZXing=require('@zxing/library');
// Desde @zxing/browser 0.2 el paquete UMD lee window al cargarse. En la PWA lo carga un <script> de la página
// (camara-carga.js), donde window existe; en Node se simula el global del navegador.
globalThis.window??=globalThis;
const incluido=require('../../src/Logistica.Repartidor.Pwa/wwwroot/lib/zxing/zxing-browser.min.js');

test('el lector incluido en la PWA decodifica un código CODE-128 de bulto sin CDN',()=>{
    const texto='ENV-000014-1', valores=[104,...[...texto].map(c=>c.charCodeAt(0)-32)];
    valores.push(valores.reduce((total,c,i)=>total+c*(i||1),0)%103,106);
    const fila=Array(30).fill(255);
    for(const valor of valores) {
        let negro=true;
        for(const ancho of ZXing.Code128Reader.CODE_PATTERNS[valor]) {
            fila.push(...Array(ancho*3).fill(negro?0:255)); negro=!negro;
        }
    }
    fila.push(...Array(30).fill(255));
    const pixeles=new Uint8ClampedArray(fila.length*120);
    for(let y=0;y<120;y++)pixeles.set(fila,y*fila.length);
    const bitmap=new ZXing.BinaryBitmap(new ZXing.HybridBinarizer(new ZXing.RGBLuminanceSource(pixeles,fila.length,120)));
    const reader=new incluido.BrowserMultiFormatReader();
    reader.possibleFormats=[incluido.BarcodeFormat.CODE_128];
    assert.equal(reader.decodeBitmap(bitmap).getText(),texto);
});

test('la cámara restringe CODE-128, descarta otros formatos y conserva códigos ajenos válidos',async()=>{
    const source=await readFile(new URL('../../src/Logistica.Repartidor.Pwa/wwwroot/js/camara-carga.js',require('node:url').pathToFileURL(__filename)), 'utf8');
    const camera=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
    const originals=new Map(['ZXingBrowser','navigator','isSecureContext'].map(k=>[k,Object.getOwnPropertyDescriptor(globalThis,k)]));
    let callback,formats,stops=0;
    class Reader {
        set possibleFormats(value){formats=value;}
        async decodeFromConstraints(_,video,cb){callback=cb;video.srcObject={getTracks:()=>[]};return {stop:()=>stops++};}
    }
    try {
        Object.defineProperty(globalThis,'ZXingBrowser',{configurable:true,value:{BrowserMultiFormatReader:Reader,BarcodeFormat:incluido.BarcodeFormat}});
        Object.defineProperty(globalThis,'navigator',{configurable:true,value:{mediaDevices:{getUserMedia(){}}}});
        Object.defineProperty(globalThis,'isSecureContext',{configurable:true,value:true});
        const detected=[],video={srcObject:null};
        await camera.iniciar(video,{invokeMethodAsync:async(name,value)=>detected.push({name,value})});
        assert.deepEqual(formats,[incluido.BarcodeFormat.CODE_128]);
        const result=(value,format=incluido.BarcodeFormat.CODE_128)=>({getText:()=>value,getBarcodeFormat:()=>format});
        await callback(result("ENV#.##'1'1",incluido.BarcodeFormat.CODE_93));
        assert.deepEqual(detected,[]);
        await callback(result('ENV-000002-1'));await callback(result('ENV-000002-1'));
        await callback(result('ENV-000999-1'));
        assert.deepEqual(detected,[{name:'CodigoDetectado',value:'ENV-000002-1'},{name:'CodigoDetectado',value:'ENV-000999-1'}]);
        camera.detener();await callback(result('ENV-000002-2'));
        assert.equal(stops,1);assert.equal(detected.length,2);
    }finally{
        camera.detener();
        for(const [key,descriptor] of originals){if(descriptor)Object.defineProperty(globalThis,key,descriptor);else delete globalThis[key];}
    }
});
