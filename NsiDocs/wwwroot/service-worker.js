const CACHE_VERSION = "nsi-docs-v11";
const CACHE_SHELL = `${CACHE_VERSION}-shell`;
const CACHE_API = `${CACHE_VERSION}-api`;
const PRECACHE_URLS = [
    "/",
    "/index.html",
    "/manifest.webmanifest",
    "/icons/icon-192.png",
    "/icons/icon-512.png",
    "/icons/icon-512-maskable.png"
];
const API_GET_ROUTES = new Set([
    "/api/status",
    "/api/documentos",
    "/api/ollama/configuracao"
]);
const API_MUTATION_ROUTES = new Set([
    "/api/chat/perguntar",
    "/api/documentos/upload",
    "/api/documentos/recarregar",
    "/api/ollama/testar-conexao",
    "/api/ollama/conectar"
]);

self.addEventListener("install", evento => {
    evento.waitUntil(
        caches.open(CACHE_SHELL).then(cache => cache.addAll(PRECACHE_URLS))
    );
    self.skipWaiting();
});

self.addEventListener("activate", evento => {
    evento.waitUntil((async () => {
        const nomes = await caches.keys();
        await Promise.all(
            nomes
                .filter(nome => !nome.startsWith(CACHE_VERSION))
                .map(nome => caches.delete(nome))
        );
        await self.clients.claim();
    })());
});

async function obterFallbackIndex() {
    const cache = await caches.open(CACHE_SHELL);
    const resposta = await cache.match("/index.html");
    return resposta || new Response("Offline", { status: 503, statusText: "Offline" });
}

async function tratarNavegacao(request) {
    const cache = await caches.open(CACHE_SHELL);
    const emCache = await cache.match(request, { ignoreSearch: true });
    if (emCache) {
        return emCache;
    }

    try {
        const respostaRede = await fetch(request);
        if (respostaRede.ok) {
            cache.put(request, respostaRede.clone());
        }
        return respostaRede;
    } catch {
        return obterFallbackIndex();
    }
}

async function tratarApiLeitura(request) {
    const cache = await caches.open(CACHE_API);
    try {
        const respostaRede = await fetch(request);
        if (respostaRede.ok) {
            cache.put(request, respostaRede.clone());
        }
        return respostaRede;
    } catch {
        const emCache = await cache.match(request);
        if (emCache) {
            return emCache;
        }
        return new Response(
            JSON.stringify({ mensagem: "Sem conexao. Nao foi possivel consultar agora." }),
            {
                status: 503,
                headers: { "Content-Type": "application/json; charset=utf-8" }
            }
        );
    }
}

async function tratarAsset(request) {
    const cache = await caches.open(CACHE_SHELL);
    const emCache = await cache.match(request);
    if (emCache) {
        return emCache;
    }

    try {
        const respostaRede = await fetch(request);
        if (respostaRede.ok && request.url.startsWith(self.location.origin)) {
            cache.put(request, respostaRede.clone());
        }
        return respostaRede;
    } catch {
        return emCache || new Response("Offline", { status: 503, statusText: "Offline" });
    }
}

self.addEventListener("fetch", evento => {
    const request = evento.request;
    const url = new URL(request.url);

    if (request.method !== "GET") {
        evento.respondWith(fetch(request));
        return;
    }

    if (url.origin !== self.location.origin) {
        evento.respondWith(fetch(request));
        return;
    }

    if (request.mode === "navigate") {
        evento.respondWith(tratarNavegacao(request));
        return;
    }

    if (API_GET_ROUTES.has(url.pathname)) {
        evento.respondWith(tratarApiLeitura(request));
        return;
    }

    if (API_MUTATION_ROUTES.has(url.pathname)) {
        evento.respondWith(fetch(request));
        return;
    }

    evento.respondWith(tratarAsset(request));
});
