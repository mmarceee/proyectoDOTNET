"use strict";

const clave = document.querySelector("#clave");
document.querySelector("#borrar").addEventListener("click", () => {
  clave.value = "";
  clave.focus();
});
window.addEventListener("pagehide", () => { clave.value = ""; });

function agregar(padre, etiqueta, texto, clase) {
  const nodo = document.createElement(etiqueta);
  if (texto !== undefined) nodo.textContent = texto;
  if (clase) nodo.className = clase;
  padre.append(nodo);
  return nodo;
}

function desplegable(padre, titulo) {
  const bloque = agregar(padre, "details");
  agregar(bloque, "summary", titulo);
  return bloque;
}

function mostrarEsquema(padre, esquema) {
  if (!esquema) return;
  if (esquema.$ref?.startsWith("#/components/schemas/")) {
    const modelo = esquema.$ref.split("/").at(-1);
    const enlace = agregar(padre, "a", `Ver modelo ${modelo}`);
    enlace.href = `#modelo-${modelo}`;
    enlace.addEventListener("click", () => {
      document.getElementById(`modelo-${modelo}`).open = true;
    });
  } else {
    agregar(padre, "pre", JSON.stringify(esquema, null, 2));
  }
}

function armarOperacion(ruta, metodo, operacion) {
  const titulo = operacion.summary ?? `${metodo.toUpperCase()} ${ruta}`;
  const id = operacion.operationId;
  const enlace = agregar(document.querySelector("#navegacion"), "a", titulo);
  enlace.href = `#${id}`;

  const seccion = agregar(document.querySelector("#operaciones"), "section");
  seccion.id = id;
  agregar(seccion, "h2", titulo);
  const linea = agregar(seccion, "p", undefined, "ruta");
  agregar(linea, "span", metodo.toUpperCase(), `metodo ${metodo}`);
  agregar(linea, "code", ruta);
  if (operacion.description) agregar(seccion, "p", operacion.description);

  const entrada = operacion.requestBody?.content?.["application/json"];
  if (entrada) mostrarEsquema(desplegable(seccion, "Esquema de la solicitud"), entrada.schema);
  const respuestas = desplegable(seccion, "Códigos y esquemas de respuesta");
  for (const [codigo, respuesta] of Object.entries(operacion.responses ?? {})) {
    const bloque = desplegable(respuestas, `${codigo} · ${respuesta.description}`);
    for (const [nombre, cabecera] of Object.entries(respuesta.headers ?? {}))
      agregar(bloque, "p", `${nombre}: ${cabecera.description}`);
    for (const [tipo, contenido] of Object.entries(respuesta.content ?? {})) {
      agregar(bloque, "p", tipo);
      mostrarEsquema(bloque, contenido.schema);
    }
  }

  const form = agregar(seccion, "form");
  agregar(form, "h3", "Probar en el ambiente de prueba");
  const parametros = agregar(form, "div", undefined, "parametros");
  const campos = (operacion.parameters ?? []).filter(p => p.in === "path" || p.in === "query").map(p => {
    const campo = agregar(parametros, "div");
    const etiqueta = agregar(campo, "label", p.name);
    const input = agregar(campo, "input");
    input.id = `${id}-${p.name}`;
    etiqueta.htmlFor = input.id;
    input.required = p.required === true;
    if (p.schema?.type === "integer") input.type = "number";
    if (p.schema?.format === "date") input.type = "date";
    if (p.example !== undefined) input.placeholder = String(p.example);
    if (p.description) {
      const ayuda = agregar(campo, "p", p.description, "ayuda");
      ayuda.id = `${input.id}-ayuda`;
      input.setAttribute("aria-describedby", ayuda.id);
    }
    return { parametro: p, input };
  });

  let cuerpo;
  if (entrada) {
    const etiqueta = agregar(form, "label", "Cuerpo JSON");
    cuerpo = agregar(form, "textarea");
    cuerpo.id = `${id}-cuerpo`;
    etiqueta.htmlFor = cuerpo.id;
    cuerpo.required = true;
    cuerpo.spellcheck = false;
    cuerpo.value = JSON.stringify(entrada.example ?? {}, null, 2);
  }

  const ejecutar = agregar(form, "button", "Ejecutar prueba");
  const estado = agregar(form, "p", undefined, "estado");
  estado.setAttribute("role", "status");
  const resultado = agregar(form, "pre");
  resultado.hidden = true;
  form.addEventListener("submit", async evento => {
    evento.preventDefault();
    resultado.hidden = true;
    resultado.textContent = "";
    const secreto = clave.value.trim();
    if (!secreto.startsWith("log_prueba_")) {
      estado.textContent = "Ingresá una clave de prueba del Portal.";
      clave.focus();
      return;
    }

    let destino = ruta.startsWith("/api/v1/pruebas/") ? ruta : ruta.replace(/^\/api\/v1\//, "/api/v1/pruebas/");
    const consulta = new URLSearchParams();
    for (const { parametro, input } of campos) {
      const valor = input.value.trim();
      if (parametro.in === "path") {
        if (!/^[A-Za-z0-9_-]+$/.test(valor)) {
          estado.textContent = "Ingresá el número público de un envío.";
          input.focus();
          return;
        }
        destino = destino.replace(`{${parametro.name}}`, encodeURIComponent(valor));
      } else if (valor !== "") {
        consulta.set(parametro.name, valor);
      }
    }
    const url = new URL(destino, location.origin);
    if (url.origin !== location.origin || !url.pathname.startsWith("/api/v1/pruebas/")) {
      estado.textContent = "La operación no tiene un destino de prueba.";
      return;
    }
    url.search = consulta.toString();

    const cabeceras = { "X-Api-Key": secreto };
    if (cuerpo) {
      try { JSON.parse(cuerpo.value); }
      catch {
        estado.textContent = "Revisá el formato del cuerpo JSON.";
        cuerpo.focus();
        return;
      }
      cabeceras["Content-Type"] = "application/json";
    }

    ejecutar.disabled = true;
    estado.textContent = "Enviando solicitud…";
    try {
      const respuesta = await fetch(url, {
        method: metodo.toUpperCase(), headers: cabeceras, body: cuerpo?.value,
        cache: "no-store", credentials: "omit", redirect: "error",
      });
      const espera = respuesta.headers.get("Retry-After");
      estado.textContent = `HTTP ${respuesta.status}${espera ? ` · Reintentar en ${espera} s` : ""}`;
      const texto = await respuesta.text();
      if (texto) {
        try { resultado.textContent = JSON.stringify(JSON.parse(texto), null, 2); }
        catch { resultado.textContent = texto; }
        resultado.hidden = false;
      }
    } catch {
      estado.textContent = "No se pudo completar la solicitud. Verificá que la API esté disponible.";
    } finally {
      ejecutar.disabled = false;
    }
  });
}

async function cargar() {
  const respuesta = await fetch("/api/openapi/v1.json", { credentials: "omit" });
  if (!respuesta.ok) throw new Error("No se pudo cargar OpenAPI");
  const documento = await respuesta.json();
  document.querySelector("#titulo").textContent = documento.info.title;
  document.querySelector("#version").textContent = documento.info.version;
  document.querySelector("#descripcion").textContent = documento.info.description;
  for (const [ruta, operaciones] of Object.entries(documento.paths)) {
    for (const [metodo, operacion] of Object.entries(operaciones)) {
      if (metodo === "get" || metodo === "post") armarOperacion(ruta, metodo, operacion);
    }
  }
  for (const [nombre, esquema] of Object.entries(documento.components?.schemas ?? {})) {
    const bloque = desplegable(document.querySelector("#esquemas"), nombre);
    bloque.id = `modelo-${nombre}`;
    agregar(bloque, "pre", JSON.stringify(esquema, null, 2));
  }
  const destino = document.getElementById(location.hash.slice(1));
  if (destino) destino.scrollIntoView();
}

cargar().catch(() => {
  document.querySelector("#descripcion").textContent =
    "No se pudo cargar la documentación. Recargá la página o abrí el archivo OpenAPI.";
});
