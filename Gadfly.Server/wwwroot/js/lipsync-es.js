// Spanish lip-sync for the examiner's 3D head (GAD-T608). TalkingHead (vendored, lib/voice/talkinghead) ships lip-sync modules for
// English, Finnish, German, French, Lithuanian and Brazilian Portuguese only; this is Gadfly's own module for Spanish, with the same
// interface as those (preProcessText, wordsToVisemes), registered on a loaded head at runtime the way lipsync-ko.js is
// (head.lipsync.es = new LipsyncEs), so the vendored files stay untouched.
//
// How it reads Spanish: Spanish spelling is regular, so a letter table plus a few context rules gives the mouth shapes of neutral
// Latin American Spanish (seseo: c before e/i and z are [s]; yeísmo: ll and y are the same sound):
//   - digraphs: ch [tʃ] (CH), ll [ʝ] (CH, like y before a vowel), rr [r] (RR), qu before e/i [k] with a silent u (kk)
//   - gu before e/i [g] with a silent u; gü [gw] keeps the u (pingüino); g before e/i and j are [x], back of the tongue (kk)
//   - c before e/i and z are [s] (SS), else c is [k]; h is silent (hola); ñ [ɲ] (nn, then the i-glide); x is [ks] (kk SS)
//   - y before a vowel is a consonant (CH: yo, ayer), else the vowel i (y, hoy); b and v are the same sound (PP); w is u
//   - accented vowels (á é í ó ú) and ü have the mouth shape of their base vowel
// and each letter maps to one of the Oculus visemes TalkingHead animates (sil PP FF TH DD kk CH SS nn RR aa E I O U). Spanish is
// syllable-timed: vowels carry the syllable, consonants are short. TalkingHead spreads the visemes over each word's time from the
// ElevenLabs character alignment (assembled into words by the avatar library). Numbers are read out in Spanish.

const VOWELS = { a: "aa", á: "aa", e: "E", é: "E", i: "I", í: "I", o: "O", ó: "O", u: "U", ú: "U", ü: "U" };
const FRONT = "eéií";

const LETTERS = {
    b: "PP", v: "PP", p: "PP", m: "PP", f: "FF", d: "DD", t: "DD", k: "kk", j: "kk", s: "SS", z: "SS", n: "nn", l: "nn",
    r: "RR", w: "U",
};

// Relative durations (1 = average): vowels carry the syllable; a tapped r is the shortest, a trilled rr longer.
const DURATIONS = { aa: 1.0, E: 0.95, I: 0.9, O: 1.0, U: 0.95, PP: 0.8, FF: 0.85, TH: 0.8, DD: 0.7, kk: 0.8, CH: 0.9, SS: 0.95, nn: 0.75, RR: 0.55 };
const TRILL = 0.9;
const GLIDE = 0.35;
const SPECIAL = { " ": 1, ",": 3, "-": 0.5 };

const UNITS = ["cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece",
    "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés",
    "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"];
const TENS = ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];
const HUNDREDS = ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos",
    "ochocientos", "novecientos"];

/** Reads a whole number below 1,000,000 in Spanish (longer numbers and codes with a leading zero digit by digit). */
export function numberToSpanish(text) {
    const s = String(text || "");
    const n = Number.parseInt(s, 10);
    if (!Number.isFinite(n) || n < 0) return s;
    if (n >= 1000000 || (s.length > 1 && s[0] === "0")) return [...s].map((d) => UNITS[+d] ?? d).join(" ");
    const below1000 = (x) => {
        if (x === 100) return "cien";
        const parts = [];
        const h = Math.floor(x / 100), r = x % 100;
        if (h) parts.push(HUNDREDS[h]);
        if (r >= 30) { const t = Math.floor(r / 10), u = r % 10; parts.push(u ? `${TENS[t]} y ${UNITS[u]}` : TENS[t]); }
        else if (r > 0 || x === 0) parts.push(UNITS[r]);
        return parts.join(" ");
    };
    const th = Math.floor(n / 1000), un = n % 1000;
    if (th === 0) return below1000(un);
    const thousands = th === 1 ? "mil" : `${below1000(th).replace(/uno$/, (u) => "ún").replace(/ ún$/, " un")} mil`;   // veintiún mil, treinta y un mil
    return un ? `${thousands} ${below1000(un)}` : thousands;
}

/** The visemes of one Spanish word, each { viseme, duration }, in order. Letters outside Spanish are read with the same table. */
export function wordVisemes(word) {
    const out = [];
    const push = (v, d) => { if (v) out.push({ viseme: v, duration: d ?? DURATIONS[v] ?? 1 }); };
    const chars = [...String(word || "").toLowerCase().normalize("NFC")];
    const isVowel = (c) => c !== undefined && Object.prototype.hasOwnProperty.call(VOWELS, c);
    const isFront = (c) => c !== undefined && FRONT.includes(c);
    let i = 0;
    while (i < chars.length) {
        const c = chars[i], c1 = chars[i + 1], c2 = chars[i + 2];
        if (c === "c" && c1 === "h") { push("CH"); i += 2; continue; }                       // chico
        if (c === "l" && c1 === "l") { push("CH"); i += 2; continue; }                       // llamar (yeísmo: like y)
        if (c === "r" && c1 === "r") { push("RR", TRILL); i += 2; continue; }                // perro
        if (c === "r" && i === 0) { push("RR", TRILL); i++; continue; }                      // rojo: a word-initial r is trilled
        if (c === "q") { push("kk"); i += c1 === "u" ? 2 : 1; continue; }                    // que, qui: silent u
        if (c === "g") {
            if (c1 === "u" && isFront(c2)) { push("kk"); i += 2; continue; }                 // guerra, guitarra: silent u
            if (c1 === "ü") { push("kk"); push("U", GLIDE); i += 2; continue; }              // pingüino: the u is heard
            push("kk"); i++; continue;                                                       // gato, gente ([x], also back)
        }
        if (c === "c") { push(isFront(c1) ? "SS" : "kk"); i++; continue; }                   // cena vs. casa (seseo)
        if (c === "h") { i++; continue; }                                                    // hola: silent
        if (c === "ñ") { push("nn"); push("I", GLIDE); i++; continue; }                      // niño
        if (c === "x") { push("kk"); push("SS"); i++; continue; }                            // examen
        if (c === "y") {
            if (isVowel(c1)) push("CH");                                                     // yo, ayer: a consonant
            else push("I");                                                                  // y, hoy, muy: the vowel i
            i++; continue;
        }
        if (isVowel(c)) { push(VOWELS[c]); i++; continue; }
        const v = LETTERS[c] ?? LETTERS[c.normalize("NFD")[0]];
        if (v) push(v);
        else if (c === "ç") push("SS");
        i++;
    }
    return out;
}

/** TalkingHead lip-sync processor for Spanish: the same interface as its own LipsyncEn/LipsyncPt_br. */
export class LipsyncEs {
    /** Cleans text for lip-sync: drops marks that are not spoken (¿ ¡ included), reads numbers and common symbols out in Spanish. */
    preProcessText(s) {
        return String(s || "")
            .replace(/[#_*'"“”‘’«»:;()\[\]{}<>~…·¿¡]/g, "")
            .replace(/%/g, " por ciento ")
            .replace(/&/g, " y ")
            .replace(/\+/g, " más ")
            .replace(/(\d)[.](?=\d{3}(?!\d))/g, "$1")
            .replace(/(\d),(\d)/g, "$1 coma $2")
            .replace(/\d+/g, (d) => numberToSpanish(d))
            .replace(/\s+/g, " ")
            .replace(/ ([,.?!])/g, "$1")
            .normalize("NFC")
            .trim();
    }

    /** Visemes for a word (TalkingHead's lip-sync object): { words, visemes, times, durations } in relative units. */
    wordsToVisemes(w) {
        const o = { words: w, visemes: [], times: [], durations: [] };
        let t = 0;
        // Runs of letters are read as words (so a word-initial r is the first letter of its run); anything else is a pause or nothing.
        for (const part of String(w || "").match(/\p{L}+|[^\p{L}]/gu) || []) {
            if (!/\p{L}/u.test(part)) { t += SPECIAL[part] || 0; continue; }
            for (const { viseme, duration } of wordVisemes(part)) {
                const last = o.visemes.length - 1;
                if (last >= 0 && o.visemes[last] === viseme) {
                    // The same mouth shape twice running (an n then an n) holds it rather than closing and reopening.
                    const d = 0.7 * duration;
                    o.durations[last] += d;
                    t += d;
                } else {
                    o.visemes.push(viseme);
                    o.times.push(t);
                    o.durations.push(duration);
                    t += duration;
                }
            }
        }
        return o;
    }
}

/**
 * Registers Spanish on a loaded TalkingHead (`head.lipsync` is where TalkingHead keeps its language processors by code). True when
 * the head is there to register on.
 */
export function registerSpanish(head) {
    if (!head || typeof head !== "object") return false;
    if (!head.lipsync || typeof head.lipsync !== "object") head.lipsync = {};
    if (!(head.lipsync.es instanceof LipsyncEs)) head.lipsync.es = new LipsyncEs();
    return true;
}
