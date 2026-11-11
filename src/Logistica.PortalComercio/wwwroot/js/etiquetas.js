export async function descargar(referencia) {
    const datos = await referencia.arrayBuffer();
    const url = URL.createObjectURL(new Blob([datos], { type: 'application/pdf' }));
    const enlace = document.createElement('a');
    enlace.href = url;
    enlace.download = 'etiquetas.pdf';
    document.body.appendChild(enlace);
    enlace.click();
    enlace.remove();
    // Dar tiempo al navegador para iniciar la descarga antes de liberar la URL.
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}
