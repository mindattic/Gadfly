// The examiner's 3D model, downloaded once per browser (GAD-T698). The file (36 MB for her, 9.5 MB for him) is kept in Cache Storage
// under its absolute URL, and each page lifetime turns it into one blob object URL, so Blazor's in-document navigations (landing →
// get-ready → exam room) hand <ma-avatar> the same bytes without asking the network again. Both examiners' models stay cached, so
// switching examiner only downloads the other one the first time.
//
// Versioning: the cached response keeps the server's validators (ETag, Last-Modified, Content-Length). Once per browser session
// (sessionStorage) a HEAD request compares them with the server's; a different model replaces the cached one. This needs nothing from
// the server and works for a hosted copy (Gadfly:Voice:AvatarModelUrl) too, where Last-Modified and Content-Length are readable across
// origins. Any failure (no Cache API, private mode, quota, offline) falls back to the plain URL, which the HTTP cache still serves.

const CACHE = "gadfly-models-v1";
const CHECKED = "gadfly-model-checked:";
const objectUrls = new Map();   // absolute URL -> Promise<string>

function absolute(url) {
    return new URL(url, location.href).href;
}

function credentials(abs) {
    return new URL(abs).origin === location.origin ? "same-origin" : "omit";
}

/** One string for "this exact file": the same for a cached response and a fresh one when the model hasn't changed. */
function validator(res) {
    const h = res.headers;
    return h.get("etag") || h.get("last-modified") || h.get("content-length") || "";
}

function checkedThisSession(abs) {
    try { return sessionStorage.getItem(CHECKED + abs) === "1"; } catch { return false; }
}

function markChecked(abs) {
    try { sessionStorage.setItem(CHECKED + abs, "1"); } catch { }
}

let cleaned = null;
/** Older cache generations of this file are dropped once. */
function dropOldGenerations() {
    cleaned ??= caches.keys()
        .then((keys) => Promise.all(keys.filter((k) => k.startsWith("gadfly-models-") && k !== CACHE).map((k) => caches.delete(k))))
        .catch(() => { });
    return cleaned;
}

/** True when the cached copy is still the server's file (or the server can't be asked right now). */
async function stillCurrent(abs, cached) {
    if (checkedThisSession(abs)) return true;
    try {
        const head = await fetch(abs, { method: "HEAD", cache: "no-cache", credentials: credentials(abs) });
        if (!head.ok) return true;
        const fresh = validator(head);
        if (!fresh || fresh === validator(cached)) { markChecked(abs); return true; }
        return false;
    } catch {
        return true;   // offline or blocked: the cached model is the best there is
    }
}

async function load(abs) {
    if (!("caches" in self)) return abs;
    await dropOldGenerations();
    const cache = await caches.open(CACHE);
    const cached = await cache.match(abs);
    if (cached && await stillCurrent(abs, cached)) return URL.createObjectURL(await cached.blob());
    if (cached) await cache.delete(abs);
    const res = await fetch(abs, { cache: "no-cache", credentials: credentials(abs) });
    if (!res.ok) throw new Error(`model ${res.status}`);
    try { await cache.put(abs, res.clone()); markChecked(abs); } catch { /* quota or private mode: this page still gets the bytes */ }
    return URL.createObjectURL(await res.blob());
}

/**
 * An object URL for the model at `url`, read from Cache Storage (downloaded into it the first time). Every call for the same model in
 * this page lifetime shares one promise and one object URL. Falls back to `url` itself on any error.
 */
export async function getModelUrl(url) {
    if (!url) return url;
    let abs;
    try { abs = absolute(url); } catch { return url; }
    let p = objectUrls.get(abs);
    if (!p) {
        p = load(abs).catch(() => {
            objectUrls.delete(abs);   // a later call may try again
            return url;
        });
        objectUrls.set(abs, p);
    }
    return p;
}

/** Starts downloading (or reading back) the model without waiting for it. */
export function prefetch(url) {
    void getModelUrl(url);
}

/** Removes every cached model except `keepUrls` from Cache Storage and this page's memory. */
export async function evictOthers(keepUrls) {
    const keep = new Set((keepUrls || []).filter(Boolean).map(absolute));
    for (const [abs, p] of objectUrls) {
        if (keep.has(abs)) continue;
        objectUrls.delete(abs);
        p.then((u) => { if (u.startsWith("blob:")) URL.revokeObjectURL(u); });
    }
    try {
        const cache = await caches.open(CACHE);
        for (const req of await cache.keys()) if (!keep.has(req.url)) await cache.delete(req);
    } catch { }
}
