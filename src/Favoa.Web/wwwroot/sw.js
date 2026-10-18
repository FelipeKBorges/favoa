// Service worker do Fávoa: instalar como app e receber os lembretes (notificações).
// Não guarda dados (o Fávoa vive no seu PC); se o PC estiver desligado, mostra uma página explicando.
const VERSAO = "favoa-v2";
const OFFLINE = "/offline.html";

self.addEventListener("install", (e) => {
    e.waitUntil(caches.open(VERSAO).then((c) => c.addAll([OFFLINE, "/icones/favoa-192.png"])));
    self.skipWaiting();
});

self.addEventListener("activate", (e) => {
    e.waitUntil(
        caches.keys()
            .then((chaves) => Promise.all(chaves.filter((k) => k !== VERSAO).map((k) => caches.delete(k))))
            .then(() => self.clients.claim()));
});

self.addEventListener("fetch", (e) => {
    // Só as navegações; o resto (Blazor, arquivos) vai direto para a rede.
    if (e.request.mode !== "navigate") return;
    e.respondWith(fetch(e.request).catch(() => caches.match(OFFLINE)));
});

// Lembrete enviado pelo Fávoa (já decifrado pelo navegador).
self.addEventListener("push", (e) => {
    let dados = {};
    try { dados = e.data ? e.data.json() : {}; } catch { dados = { body: e.data ? e.data.text() : "" }; }
    e.waitUntil(self.registration.showNotification(dados.title || "Fávoa", {
        body: dados.body || "",
        icon: "/icones/favoa-192.png",
        badge: "/icones/favoa-192.png",
        data: { url: dados.url || "/" },
        tag: dados.tag,
    }));
});

// Toque na notificação: abre (ou traz para frente) o Fávoa na página certa.
self.addEventListener("notificationclick", (e) => {
    e.notification.close();
    const destino = new URL((e.notification.data && e.notification.data.url) || "/", self.location.origin).href;
    e.waitUntil((async () => {
        const abertas = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        for (const c of abertas) {
            if (new URL(c.url).origin === self.location.origin) {
                await c.focus();
                if ("navigate" in c) await c.navigate(destino);
                return;
            }
        }
        await self.clients.openWindow(destino);
    })());
});
