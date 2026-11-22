// Gadfly offline fallback (GAD-T308). It touches only GET page navigations: each goes to the network as usual, and only
// when the network fails is the cached offline page shown instead. No other request (scripts, the exam connection,
// API calls, form posts) is intercepted, and nothing else is cached.
const CACHE = 'gadfly-offline-v1';
const OFFLINE = ['/offline.html', '/site.css', '/app.css', '/assets/site/logo.png', '/assets/site/logo-192.png', '/favicon.ico'];

self.addEventListener('install', (event) => {
    event.waitUntil(caches.open(CACHE).then((c) => c.addAll(OFFLINE)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
    event.waitUntil(caches.keys()
        .then((keys) => Promise.all(keys.filter((k) => k.startsWith('gadfly-offline-') && k !== CACHE).map((k) => caches.delete(k))))
        .then(() => self.clients.claim()));
});

self.addEventListener('fetch', (event) => {
    const req = event.request;
    if (req.method !== 'GET') return;
    if (req.mode === 'navigate') {
        event.respondWith(fetch(req).catch(() => caches.match('/offline.html', { cacheName: CACHE })));
        return;
    }
    // The offline page's own styles and images, only while the network is down.
    const url = new URL(req.url);
    if (url.origin === location.origin && OFFLINE.includes(url.pathname)) {
        event.respondWith(fetch(req).catch(() => caches.match(url.pathname, { cacheName: CACHE })));
    }
});
