// Mis·Gastos — service worker.
// Privacidad primero: NUNCA se guardan páginas ni respuestas con datos del usuario en el dispositivo.
// Solo se guardan archivos estáticos (estilos, scripts, íconos) y una página "sin conexión".
"use strict";

const VERSION = "mis-gastos-v4"; // subir al cambiar site.js o site.css
const PRECARGA = [
  "/offline.html",
  "/css/site.css",
  "/js/site.js",
  "/lib/bootstrap/dist/css/bootstrap.min.css",
  "/lib/bootstrap/dist/js/bootstrap.bundle.min.js",
  "/icons/icon-192.png",
];
const ESTATICOS = /^\/(css|js|lib|icons)\//;

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.addAll(PRECARGA)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(
    caches.keys()
      .then((claves) => Promise.all(claves.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  // Páginas: siempre de la red. Sin conexión, la página de aviso (sin datos).
  if (req.mode === "navigate") {
    e.respondWith(fetch(req).catch(() => caches.match("/offline.html")));
    return;
  }

  // Estáticos. Los que llevan versión en la URL (?v=…, de asp-append-version) cambian de URL con cada deploy:
  // se busca la versión EXACTA en caché y, si no está, se trae de la red. Cualquier versión guardada de ese
  // archivo se usa solo sin conexión. Así nunca se mezcla HTML nuevo con CSS o JS viejos.
  if (ESTATICOS.test(url.pathname)) {
    e.respondWith(
      caches.open(VERSION).then(async (cache) => {
        const exacta = await cache.match(req);
        if (exacta) return exacta;
        try {
          const resp = await fetch(req);
          if (resp.ok) cache.put(req, resp.clone());
          return resp;
        } catch (err) {
          const cualquiera = await cache.match(req, { ignoreSearch: true });
          if (cualquiera) return cualquiera;
          throw err;
        }
      })
    );
  }
});
