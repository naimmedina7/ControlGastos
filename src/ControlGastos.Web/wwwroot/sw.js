// Mis·Gastos — service worker.
// Privacidad primero: NUNCA se guardan páginas ni respuestas con datos del usuario en el dispositivo.
// Solo se guardan archivos estáticos (estilos, scripts, íconos) y una página "sin conexión".
"use strict";

const VERSION = "mis-gastos-v2"; // subir al cambiar site.js o site.css
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

  // Estáticos: se sirven de la caché y se actualizan en segundo plano.
  if (ESTATICOS.test(url.pathname)) {
    e.respondWith(
      caches.open(VERSION).then(async (cache) => {
        const guardada = await cache.match(req, { ignoreSearch: true });
        const red = fetch(req).then((resp) => {
          if (resp.ok) cache.put(req, resp.clone());
          return resp;
        }).catch(() => guardada);
        return guardada || red;
      })
    );
  }
});
