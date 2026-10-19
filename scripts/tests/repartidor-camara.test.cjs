const {test}=require('node:test');
const assert=require('node:assert/strict');
const ZXing=require('@zxing/library');
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
    assert.equal(new incluido.BrowserMultiFormatReader().decodeBitmap(bitmap).getText(),texto);
});
