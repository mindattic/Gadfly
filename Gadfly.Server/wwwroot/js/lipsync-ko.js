// Korean lip-sync for the examiner's 3D head (GAD-T531). TalkingHead (vendored, lib/voice/talkinghead) ships lip-sync modules for
// English, Finnish, German, French, Lithuanian and Brazilian Portuguese only; this is Gadfly's own module for Korean, with the same
// interface as those (preProcessText, wordsToVisemes), registered on a loaded head at runtime the way TalkingHead stores its own
// (head.lipsync[lang] = new LipsyncXx), so the vendored files stay untouched.
//
// How it reads Korean: a precomposed Hangul syllable (U+AC00 to U+D7A3) is decomposed by Unicode arithmetic into its initial
// consonant (choseong), vowel (jungseong) and optional final consonant (jongseong):
//     index = code - 0xAC00;  initial = floor(index / 588);  vowel = floor((index % 588) / 28);  final = index % 28
// and each jamo maps to one of the Oculus visemes TalkingHead animates (sil PP FF TH DD kk CH SS nn RR aa E I O U). Diphthongs with
// an on-glide (ㅑ ㅕ ㅛ ㅠ: y-; ㅘ ㅝ ㅙ ㅞ ㅚ ㅟ: w-) get a short glide viseme first. Finals follow Korean coda neutralization (only
// k, n, t, l, m, p, ng are heard at the end of a syllable). Standalone compatibility jamo (ㄱ, ㅏ...) and Latin letters (English terms
// inside a Korean answer or question) are mapped too. TalkingHead spreads the visemes over each word's time from the ElevenLabs
// character alignment (assembled into words by the avatar library); Korean is syllable-timed, so syllables share a word evenly.

const SBASE = 0xac00, SLAST = 0xd7a3, VCOUNT = 21, TCOUNT = 28, NCOUNT = VCOUNT * TCOUNT; // 588

// Initial consonants, choseong order: ㄱ ㄲ ㄴ ㄷ ㄸ ㄹ ㅁ ㅂ ㅃ ㅅ ㅆ ㅇ ㅈ ㅉ ㅊ ㅋ ㅌ ㅍ ㅎ. ㅇ is silent at the start of a syllable and
// ㅎ is breath only: the vowel shapes the mouth (null = no viseme of its own).
const INITIALS = ["kk", "kk", "nn", "DD", "DD", "RR", "PP", "PP", "PP", "SS", "SS", null, "CH", "CH", "CH", "kk", "DD", "PP", null];

// Vowels, jungseong order: ㅏ ㅐ ㅑ ㅒ ㅓ ㅔ ㅕ ㅖ ㅗ ㅘ ㅙ ㅚ ㅛ ㅜ ㅝ ㅞ ㅟ ㅠ ㅡ ㅢ ㅣ. Each is [glide?, vowel]. ㅓ [ʌ] is open and
// unrounded (aa); ㅡ [ɯ] is close and unrounded, lips spread (I).
const VOWELS = [
    [null, "aa"], [null, "E"], ["I", "aa"], ["I", "E"], [null, "aa"], [null, "E"], ["I", "aa"], ["I", "E"],
    [null, "O"], ["U", "aa"], ["U", "E"], ["U", "E"], ["I", "O"], [null, "U"], ["U", "aa"], ["U", "E"],
    ["U", "I"], ["I", "U"], [null, "I"], [null, "I"], [null, "I"],
];

// Final consonants, jongseong order (0 = none): ㄱ ㄲ ㄳ ㄴ ㄵ ㄶ ㄷ ㄹ ㄺ ㄻ ㄼ ㄽ ㄾ ㄿ ㅀ ㅁ ㅂ ㅄ ㅅ ㅆ ㅇ ㅈ ㅊ ㅋ ㅌ ㅍ ㅎ.
// Neutralized: ㄱ-type k (kk), ㄴ n (nn), ㄷ ㅅ ㅆ ㅈ ㅊ ㅌ t (DD), ㄹ l (nn, tongue up like TalkingHead's own "l"), ㅁ m and
// ㅂ ㅍ p (PP), ㅇ ng (kk: back of the tongue), ㅎ silent. Clusters keep the consonant that is usually heard.
const FINALS = [null, "kk", "kk", "kk", "nn", "nn", "nn", "DD", "nn", "kk", "PP", "nn", "nn", "nn", "PP", "nn",
    "PP", "PP", "PP", "DD", "DD", "kk", "DD", "DD", "kk", "DD", "PP", null];

// Compatibility jamo (U+3131 to U+3163) on their own: consonants then vowels.
const COMPAT_CONSONANTS = {
    "ㄱ": "kk", "ㄲ": "kk", "ㄳ": "kk", "ㄴ": "nn", "ㄵ": "nn", "ㄶ": "nn", "ㄷ": "DD", "ㄸ": "DD", "ㄹ": "RR", "ㄺ": "kk", "ㄻ": "PP",
    "ㄼ": "nn", "ㄽ": "nn", "ㄾ": "nn", "ㄿ": "PP", "ㅀ": "nn", "ㅁ": "PP", "ㅂ": "PP", "ㅃ": "PP", "ㅄ": "PP", "ㅅ": "SS", "ㅆ": "SS",
    "ㅇ": null, "ㅈ": "CH", "ㅉ": "CH", "ㅊ": "CH", "ㅋ": "kk", "ㅌ": "DD", "ㅍ": "PP", "ㅎ": null,
};
const COMPAT_VOWEL_BASE = 0x314f; // ㅏ; ㅏ..ㅣ are in jungseong order

// Latin letters (an English term in a Korean sentence), a simple letter-to-viseme reading like TalkingHead's Finnish module.
const LATIN = {
    a: "aa", e: "E", i: "I", o: "O", u: "U", y: "I", b: "PP", c: "kk", d: "DD", f: "FF", g: "kk", h: null, j: "CH", k: "kk", l: "nn",
    m: "PP", n: "nn", p: "PP", q: "kk", r: "RR", s: "SS", t: "DD", v: "FF", w: "U", x: "SS", z: "SS",
};

// Relative durations (1 = average). Korean is syllable-timed: the vowel carries the syllable, consonants are shorter, glides brief.
const DURATIONS = { aa: 1.0, E: 0.95, I: 0.9, O: 1.0, U: 0.95, PP: 0.8, FF: 0.8, TH: 0.8, DD: 0.7, kk: 0.75, CH: 0.85, SS: 0.9, nn: 0.75, RR: 0.6 };
const GLIDE = 0.35;
const SPECIAL = { " ": 1, ",": 3, "-": 0.5 };

// Sino-Korean digits, for numbers read out (preProcessText).
const DIGITS = ["영", "일", "이", "삼", "사", "오", "육", "칠", "팔", "구"];

/** The jamo of one precomposed Hangul syllable as indices { initial, vowel, final }, or null for any other character. */
export function decompose(ch) {
    const code = String(ch || "").codePointAt(0);
    if (!(code >= SBASE && code <= SLAST)) return null;
    const index = code - SBASE;
    return { initial: Math.floor(index / NCOUNT), vowel: Math.floor((index % NCOUNT) / TCOUNT), final: index % TCOUNT };
}

/** The visemes of one character (in order), each { viseme, duration }; empty for a character with no mouth shape of its own. */
export function charVisemes(ch) {
    const out = [];
    const push = (v, d) => { if (v) out.push({ viseme: v, duration: d ?? DURATIONS[v] ?? 1 }); };
    const s = decompose(ch);
    if (s) {
        push(INITIALS[s.initial]);
        const [glide, vowel] = VOWELS[s.vowel];
        if (glide) push(glide, GLIDE);
        push(vowel);
        push(FINALS[s.final]);
        return out;
    }
    if (Object.prototype.hasOwnProperty.call(COMPAT_CONSONANTS, ch)) { push(COMPAT_CONSONANTS[ch]); return out; }
    const code = String(ch).codePointAt(0);
    if (code >= COMPAT_VOWEL_BASE && code < COMPAT_VOWEL_BASE + VCOUNT) {
        const [glide, vowel] = VOWELS[code - COMPAT_VOWEL_BASE];
        if (glide) push(glide, GLIDE);
        push(vowel);
        return out;
    }
    const latin = LATIN[String(ch).toLowerCase()];
    if (latin) push(latin);
    return out;
}

/** Reads a whole number in Sino-Korean, digit by place (만 and above read digit by digit: enough for a spoken question). */
function numberToKorean(text) {
    const n = Number.parseInt(text, 10);
    if (!Number.isFinite(n) || n < 0 || n >= 10000) return [...text].map((d) => DIGITS[+d] ?? d).join("");
    if (n === 0) return DIGITS[0];
    const places = [[1000, "천"], [100, "백"], [10, "십"]];
    let rest = n, out = "";
    for (const [value, name] of places) {
        const d = Math.floor(rest / value);
        if (d > 0) out += (d === 1 ? "" : DIGITS[d]) + name;
        rest %= value;
    }
    if (rest > 0) out += DIGITS[rest];
    return out;
}

/** TalkingHead lip-sync processor for Korean: the same interface as its own LipsyncEn/LipsyncFi. */
export class LipsyncKo {
    /** Cleans text for lip-sync: drops marks that are not spoken, reads numbers out in Korean, keeps Hangul and Latin letters. */
    preProcessText(s) {
        return String(s || "")
            .replace(/[#_*'"“”‘’:;()\[\]{}<>~…·]/g, "")
            .replace(/%/g, " 퍼센트 ")
            .replace(/\d+/g, (d) => numberToKorean(d))
            .replace(/\s+/g, " ")
            .trim();
    }

    /** Visemes for a word (TalkingHead's lip-sync object): { words, visemes, times, durations } in relative units. */
    wordsToVisemes(w) {
        const o = { words: w, visemes: [], times: [], durations: [] };
        let t = 0;
        for (const ch of String(w || "")) {
            const vs = charVisemes(ch);
            if (vs.length === 0) { t += SPECIAL[ch] || 0; continue; }
            for (const { viseme, duration } of vs) {
                const last = o.visemes.length - 1;
                if (last >= 0 && o.visemes[last] === viseme) {
                    // The same mouth shape twice running (a final ㄴ then an initial ㄴ) holds it rather than closing and reopening.
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
 * Registers Korean on a loaded TalkingHead (`head.lipsync` is where TalkingHead keeps its language processors by code). True when
 * the head is there to register on.
 */
export function registerKorean(head) {
    if (!head || typeof head !== "object") return false;
    if (!head.lipsync || typeof head.lipsync !== "object") head.lipsync = {};
    if (!(head.lipsync.ko instanceof LipsyncKo)) head.lipsync.ko = new LipsyncKo();
    return true;
}

/** True when the text has any Hangul (syllables or jamo). */
export function hasHangul(text) {
    return /[가-힣ㄱ-ㆎᄀ-ᇿ]/.test(String(text || ""));
}
