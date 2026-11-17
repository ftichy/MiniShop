// Vzhled krok D: service worker = „vrátný“ mezi stránkou MiniShopu a sítí. Prohlížeč ho spustí na pozadí a každý
// požadavek stránky jde nejdřív přes něj. Pravidla:
//  1. Statické soubory (CSS, JS, knihovny, ikony, obrázky produktů) z mezipaměti telefonu – rychlý start.
//     Adresy CSS/JS nesou otisk obsahu (?v=…, asp-append-version), nová verze = nová adresa, takže stará se nepoužije.
//  2. Stránky (HTML) VŽDY ze sítě a nikdy do mezipaměti – jsou v nich objednávky, adresy, správa; na sdíleném telefonu
//     nesmí zůstat. Když síť není, ukáže se offline.html.
//  3. Vše ostatní (POST formuláře, /api, platba, Stripe, Google, jiné weby) service worker vůbec neobsluhuje.
// Soubor leží v kořeni webu (/sw.js), aby jeho dosah (scope) byl celý web. Server ho posílá s Cache-Control: no-cache,
// prohlížeč tak po nasazení najde novou verzi (Program.cs).

// Otisk souborů z PRECACHE (SHA-256 jejich obsahu za sebou – HTML bez znaků \r, ať nezáleží na koncích řádků
// Windows/Linux –, prvních 12 znaků). Změna souboru = nový otisk = nový
// service worker = nová mezipaměť. Po změně offline.html nebo ikony test PwaTests napíše správnou hodnotu.
const PRECACHE_VERSION = '53e96d2bfb9a';
const PRECACHE = ['/offline.html', '/icons/icon-192.png'];
const OFFLINE_CACHE = `minishop-offline-${PRECACHE_VERSION}`;
const STATIC_CACHE = 'minishop-static-v1';
const STATIC_MAX_ENTRIES = 150;
const STATIC_PATHS = ['/css/', '/js/', '/lib/', '/icons/', '/media/products/'];

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(OFFLINE_CACHE)
            .then(cache => cache.addAll(PRECACHE.map(url => new Request(url, { cache: 'reload' }))))
            .then(() => self.skipWaiting()));   // nová verze převezme řízení hned, ne až po zavření všech oken
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys
                .filter(key => key.startsWith('minishop-') && key !== OFFLINE_CACHE && key !== STATIC_CACHE)
                .map(key => caches.delete(key))))   // úklid mezipaměti starší verze
            .then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET') {
        return;   // formuláře, odhlášení, platba – jen síť, bez zásahu
    }

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) {
        return;   // Stripe, Google… – cizí weby neobsluhujeme
    }

    if (request.mode === 'navigate') {
        // Stránka: jen ze sítě; bez sítě offline stránka. Odpověď se nikam neukládá.
        event.respondWith(fetch(request).catch(() => caches.match('/offline.html', { cacheName: OFFLINE_CACHE })));
        return;
    }

    if (STATIC_PATHS.some(path => url.pathname.startsWith(path))) {
        event.respondWith(fromCacheOrNetwork(request));
    }
    // Ostatní (např. /api/catalog/suggest) jde normálně na síť.
});

async function fromCacheOrNetwork(request) {
    const cache = await caches.open(STATIC_CACHE);
    const cached = await cache.match(request);
    if (cached) {
        return cached;
    }

    const response = await fetch(request);
    if (response.ok && response.type === 'basic') {
        await cache.put(request, response.clone());
        await trim(cache);
    }

    return response;
}

// E-shop krok 23: oznámení push. Push služba (Google, Apple, Mozilla) doručí zašifrovanou zprávu od MiniShopu,
// prohlížeč ji rozšifruje a předá sem: { title, body, url, tag }. Stejný tag nahradí starší oznámení (opakované
// doručení se neukáže dvakrát). Bez showNotification by prohlížeč ukázal vlastní obecné oznámení.
self.addEventListener('push', event => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch {
        data = {};
    }

    event.waitUntil(self.registration.showNotification(data.title || 'MiniShop', {
        body: data.body || '',
        icon: '/icons/icon-192.png',
        badge: '/icons/badge-96.png',
        tag: data.tag || undefined,
        lang: 'cs',
        data: { url: data.url || '/' }
    }));
});

// Klepnutí na oznámení: otevřít stránku (jen na MiniShopu – cizí adresu ze zprávy neotevřeme). Když už je aplikace
// otevřená, přepne se do ní místo nového okna.
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const target = new URL((event.notification.data && event.notification.data.url) || '/', self.location.origin);
    const url = target.origin === self.location.origin ? target.href : self.location.origin + '/';

    event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(windows => {
        const existing = windows.find(client => new URL(client.url).origin === self.location.origin);
        if (existing) {
            return existing.focus().then(client => client.navigate(url)).catch(() => self.clients.openWindow(url));
        }

        return self.clients.openWindow(url);
    }));
});

// Mezipaměť nesmí růst donekonečna (staré ?v= verze CSS/JS, prohlížené obrázky) – nejstarší záznamy pryč.
async function trim(cache) {
    const keys = await cache.keys();
    for (let i = 0; i < keys.length - STATIC_MAX_ENTRIES; i++) {
        await cache.delete(keys[i]);
    }
}
