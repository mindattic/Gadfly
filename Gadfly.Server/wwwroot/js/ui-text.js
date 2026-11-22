// Interface text for scripts (GAD-T543/T544): the page renders its strings once with <UiTextData Prefix="..."/> (a hidden
// [data-ui-text] element, in the page's language) and a module asks for them here. Each call names the English as its
// fallback, so a page without the element, or a key not translated yet, still reads in English.
//
//   import { t } from "./ui-text.js";
//   t("getReady.js.mic.quiet", "Background noise: quiet. Good.");
//   t("getReady.js.gaze.good", "Set up: about {deg}° accuracy.", { deg: 2.1 });

let cache = { raw: null, map: null };

function strings() {
    const nodes = document.querySelectorAll("[data-ui-text]");
    if (!nodes.length) return null;
    // Several pages' components may each render one: read them all, later ones winning.
    const raw = Array.from(nodes, (n) => n.getAttribute("data-ui-text") || "").join("\u0000");
    if (raw === cache.raw) return cache.map;
    const map = {};
    for (const n of nodes) {
        try { Object.assign(map, JSON.parse(n.getAttribute("data-ui-text") || "{}")); } catch { /* ignore a broken block */ }
    }
    cache = { raw, map };
    return map;
}

/** The text for `key` in the page's language, else `fallback` (English); `{name}` slots are filled from `vars`. */
export function t(key, fallback, vars) {
    const map = strings();
    let s = (map && typeof map[key] === "string" && map[key]) || fallback || key;
    if (vars) s = s.replace(/\{(\w+)\}/g, (m, k) => (vars[k] !== undefined && vars[k] !== null ? String(vars[k]) : m));
    return s;
}

/** The page's language ("en", "ko"). */
export function lang() {
    const n = document.querySelector("[data-ui-lang]");
    return (n && n.getAttribute("data-ui-lang")) || document.documentElement.lang || "en";
}
