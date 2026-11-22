// The examiner's luminous blue look (GAD-T503). The 3D examiner (<ma-avatar>, MindAttic.Avatar on TalkingHead and three.js) is
// shown as a smooth, stylized figure lit from within: soft electric-blue skin with an inner glow and a gentle bright rim at her
// silhouette, hair and clothing in deeper blues of the same family, and green eyes. Warm and attentive rather than mighty
// (GAD-T558, docs/research/attention-and-gaze.md 4.6): no transparency, no glow strong enough to wash out her features, and the
// eyes, brows, lips and mouth stay readable, so lip sync shows and the student can look her in the eye.
// Uncanniness comes from mismatched realism, worst in the eyes (MacDorman et al. 2009; Seyama & Nagayama 2007), so every part of
// the face is stylized to the same degree: the sclera is a clean, unglowed white with no veins, outside every glow, with a clear
// iris-sclera edge (Kobayashi & Kohshima 2001), and the iris is a smooth stylized green with a darker limbal ring, not a
// photographic texture on a glowing face. Her eyes are never enlarged: the listening expression's eye-widening is left out
// (useExaminerLook). No study shows green or bright irises draw longer looks, so they are not made to glow.
//
// The vendored avatar library under /lib/voice is copied in by tools/sync-voice-assets.ps1 and is never edited here: this module
// restyles the loaded model's materials from the outside, through the element's public `head` (the TalkingHead instance) and its
// `armature` (the model's three.js scene graph). It makes no network calls.

// ---- the palette (hex sRGB) and glow strengths: tune here ----------------------------------------------------------------------

/** Skin: a soft, saturated electric blue. */
export const LOOK_SKIN = 0x2779ea;
/** Clothing: a deeper blue of the same family, so the neckline reads. */
export const LOOK_CLOTHING = 0x1c4bb0;
/** Hair: deeper still; its strands keep their shape from the texture's alpha. */
export const LOOK_HAIR = 0x142f7a;
/** Eyebrows and lashes: deep navy, so the eyes and expression read on the bright face. */
export const LOOK_BROWS = 0x0b1736;
/** The irises: an emerald/jade green, the complement of her blue skin. */
export const LOOK_IRIS = 0x14b874;
/** The sclera (eye whites): a clean, soft white, unglowed and outside the skin's glow (GAD-T558). */
export const LOOK_SCLERA = 0xf2f4f7;
/** Teeth and tongue: kept natural-ish and darker than the skin so the open mouth (lip sync) reads. */
export const LOOK_TEETH = 0xeef3ff;
export const LOOK_TONGUE = 0x8a4f73;
/** The inner glow (emissive) on skin, hair and clothing. */
export const LOOK_GLOW = 0x1f6dff;
/** The soft bright rim around her silhouette (fresnel). */
export const LOOK_RIM = 0xb8ecff;

/** Emissive intensity of the skin's inner glow (clothing and hair take a share of it): gentle, warm rather than luminous-mighty. */
export const LOOK_GLOW_STRENGTH = 0.2;
/** How bright the silhouette rim is (0 turns it off) and how tightly it hugs the edge (higher is tighter). */
export const LOOK_RIM_STRENGTH = 0.42;
export const LOOK_RIM_POWER = 2.4;
/** How much of the original skin texture's light and shade shows through the blue (lips, brows, creases): 0 is flat color. */
export const LOOK_SKIN_DETAIL = 0.45;
/** Glow on the irises: none (GAD-T558). The eyes stay outside every glow, so her gaze reads like a person's. */
export const LOOK_IRIS_GLOW = 0;
/** How much of the room's reflected light reaches skin, hair and clothing: below 1 keeps the blue saturated, not washed out. */
export const LOOK_ENV_LIGHT = 0.5;
/** Eye surface roughness: low, so the light catches them (a catchlight) and they look alive. */
export const LOOK_EYE_ROUGHNESS = 0.08;

const DEFAULTS = {
    skin: LOOK_SKIN, clothing: LOOK_CLOTHING, hair: LOOK_HAIR, brows: LOOK_BROWS, iris: LOOK_IRIS, sclera: LOOK_SCLERA,
    teeth: LOOK_TEETH, tongue: LOOK_TONGUE, glow: LOOK_GLOW, rim: LOOK_RIM,
    glowStrength: LOOK_GLOW_STRENGTH, rimStrength: LOOK_RIM_STRENGTH, rimPower: LOOK_RIM_POWER,
    skinDetail: LOOK_SKIN_DETAIL, envLight: LOOK_ENV_LIGHT, irisGlow: LOOK_IRIS_GLOW, eyeRoughness: LOOK_EYE_ROUGHNESS,
};

// Where the irises are on the MPFB eye texture (brown_eye: two eyeballs on one 1024 x 1024 sheet), in UV (glTF: v runs down,
// as three.js keeps it), and the radius taking in the dark limbal ring. Only texels inside these circles that are iris-colored
// (reddish brown, not the white or the veins outside) are recolored; the pupil stays black and the ring stays dark.
const IRIS_CENTERS = [[0.7031, 0.2979], [0.2881, 0.7100]];
const IRIS_RADIUS = 0.118;

/** Which part of the model a material belongs to, from its (MPFB/MakeHuman, or Ready Player Me) material or mesh name. */
function partOf(material, mesh) {
    const n = `${material.name || ""} ${mesh.name || ""}`.toLowerCase();
    if (/eyebrow|eyelash|lash/.test(n)) return "brows";
    if (/teeth|tooth/.test(n)) return "teeth";
    if (/tongue/.test(n)) return "tongue";
    if (/high-poly|wolf3d_eye|\beye/.test(n)) return "eyes";
    if (/hair|ponytail|braid/.test(n)) return "hair";
    if (/body|skin|wolf3d_body|wolf3d_head/.test(n)) return "skin";
    return "clothing";
}

/** The three.js object to restyle: an <ma-avatar> element, a TalkingHead instance, or any three.js Object3D. */
function rootOf(target) {
    if (!target) return null;
    if (target.isObject3D) return target;
    if (target.armature && target.armature.isObject3D) return target.armature;                                 // TalkingHead
    if (target.head && target.head.armature && target.head.armature.isObject3D) return target.head.armature;   // <ma-avatar>
    return null;
}

/**
 * Restyles the loaded examiner in the luminous blue look. Safe to call more than once (each material is restyled once) and a
 * no-op when the avatar is the 2D portrait or not loaded yet. Returns the number of materials restyled.
 * @param {HTMLElement|object} target the <ma-avatar> element (once its mode is "3d"), its TalkingHead, or a three.js Object3D
 * @param {Partial<typeof DEFAULTS>} [options] colors (hex numbers) and strengths to override
 */
export function applyExaminerLook(target, options = {}) {
    const root = rootOf(target);
    if (!root) return 0;
    const o = { ...DEFAULTS, ...options };
    let count = 0;
    root.traverse((mesh) => {
        if (!mesh.isMesh) return;
        const list = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        for (const m of list) {
            if (!m || !m.color || (m.userData && m.userData.gadflyLook)) continue;
            restyle(m, partOf(m, mesh), o);
            m.userData = { ...(m.userData || {}), gadflyLook: true };
            m.needsUpdate = true;
            count++;
        }
    });
    return count;
}

/**
 * Applies the look to an <ma-avatar> now if its 3D model is up, and again whenever it (re)loads one. Call it once, right after
 * the element is created; a portrait fallback is left alone.
 */
export function useExaminerLook(el, options) {
    if (!el || el.gadflyLookWatch) return;
    el.gadflyLookWatch = true;
    keepEyesNatural(el);
    const apply = () => { try { if (el.mode === "3d") applyExaminerLook(el, options); } catch { /* the default look is fine */ } };
    el.addEventListener("ready", apply);
    apply();
}

/**
 * No enlarged eyes (GAD-T558): the avatar's listening expression widens the eyes a little (eyeWideLeft/Right), which reads as
 * staring. This element's own expression keeps the raised brows and the small smile and leaves the eyes their natural size. The
 * vendored library is not edited; only this one element's method is wrapped.
 */
function keepEyesNatural(el) {
    if (typeof el.express !== "function") return;
    const original = el.express;
    el.express = function (kind, engaged = false) {
        if (kind !== "attentive" || typeof this.animate !== "function") return original.call(this, kind, engaged);
        const k = engaged ? 1 : 0.6;
        this.animate("ma-expression", [400, 3200, 600], { browInnerUp: [0.22 * k, 0.22 * k, 0], mouthSmile: [0.14 * k, 0.14 * k, 0] });
    };
}

function restyle(m, part, o) {
    const standard = !!m.isMeshStandardMaterial;
    const glow = (hex, intensity) => { if (m.emissive) { m.emissive.setHex(hex); m.emissiveIntensity = intensity; m.emissiveMap = null; } };
    if (standard && (part === "skin" || part === "clothing" || part === "hair")) m.envMapIntensity = o.envLight;
    switch (part) {
        case "skin":
            // The texture stays, but only its light and shade: the blue replaces its color (tinted in the shader below), so the
            // lips, brows and creases still read while the skin is one smooth blue.
            m.color.setHex(o.skin);
            glow(o.glow, o.glowStrength);
            if (standard) { m.roughness = 0.55; m.metalness = 0; }
            shade(m, o, { tint: m.map ? o.skinDetail : 0, rim: true });
            break;
        case "clothing":
            m.map = null;        // the normal map stays: the folds still catch the light
            m.color.setHex(o.clothing);
            glow(o.glow, o.glowStrength * 0.45);
            if (standard) { m.roughness = 0.7; m.metalness = 0; }
            shade(m, o, { rim: true });
            break;
        case "hair":
            m.color.setHex(o.hair);
            glow(o.glow, o.glowStrength * 0.4);
            if (standard) { m.roughness = 0.88; m.metalness = 0; }
            shade(m, o, { flat: true, rim: true });
            break;
        case "brows":
            m.map = null;
            m.color.setHex(o.brows);
            glow(0x000000, 0);
            break;
        case "eyes":
            m.color.setHex(0xffffff);   // stylized in the shader below: clean sclera, smooth iris; never glowing
            glow(0x000000, 0);
            if (standard) { m.roughness = o.eyeRoughness; m.metalness = 0; }
            if (m.map) shade(m, o, { iris: true });
            break;
        case "teeth":
            m.map = null;
            m.color.setHex(o.teeth);
            break;
        case "tongue":
            m.map = null;
            m.color.setHex(o.tongue);
            break;
    }
}

/**
 * Shader additions, in the material's own (standard) shader:
 *  - tint: the diffuse texture contributes only its brightness relative to its average, `tint` of it (0..1), times the color;
 *  - flat: the texture's color is dropped and only its alpha kept (cut-out hair stays cut out, in one uniform color);
 *  - rim: the soft bright fresnel edge, added as emissive light;
 *  - iris: the eye texture's irises are recolored to the iris green, with a faint glow on them only.
 */
function shade(m, o, { tint = 0, flat = false, rim = false, iris = false }) {
    if (!m.isMeshStandardMaterial) return;
    const Color = m.color.constructor;
    const u = {
        lookRimColor: { value: new Color(o.rim) }, lookRimStrength: { value: rim ? o.rimStrength : 0 }, lookRimPower: { value: o.rimPower },
        lookTint: { value: tint }, lookTintRef: { value: 0.45 },
        lookIris: { value: new Color(o.iris) }, lookIrisGlow: { value: o.irisGlow }, lookSclera: { value: new Color(o.sclera) },
    };
    if (tint > 0) averageBrightness(m.map).then((v) => { if (v > 0.02) u.lookTintRef.value = v; });

    let map = "#include <map_fragment>";
    if (flat) map += "\ndiffuseColor.rgb = diffuse;";
    else if (tint > 0) map = `#ifdef USE_MAP
    vec4 lookTex = texture2D( map, vMapUv );
    float lookLum = dot( lookTex.rgb, vec3( 0.2126, 0.7152, 0.0722 ) );
    diffuseColor.rgb *= clamp( mix( 1.0, lookLum / lookTintRef, lookTint ), 0.0, 1.25 );
    diffuseColor.a *= lookTex.a;
#endif`;
    else if (iris) map = `#ifdef USE_MAP
    vec4 lookTex = texture2D( map, vMapUv );
    float lookIrisR = 1.0;
    ${IRIS_CENTERS.map(([x, y]) => `lookIrisR = min( lookIrisR, distance( vMapUv, vec2( ${x.toFixed(4)}, ${y.toFixed(4)} ) ) );`).join("\n    ")}
    float lookRed = lookTex.r - max( lookTex.g, lookTex.b );
    float lookIrisW = ( 1.0 - smoothstep( ${(IRIS_RADIUS - 0.012).toFixed(4)}, ${IRIS_RADIUS.toFixed(4)}, lookIrisR ) ) * smoothstep( 0.01, 0.08, lookRed );
    float lookVal = max( lookTex.r, max( lookTex.g, lookTex.b ) );
    // A smooth stylized iris (GAD-T558): brighter toward the pupil, a darker limbal ring at its edge, no photographic detail.
    float lookIrisN = clamp( lookIrisR / ${IRIS_RADIUS.toFixed(4)}, 0.0, 1.0 );
    vec3 lookGreen = lookIris * ( 0.8 + 0.3 * ( 1.0 - lookIrisN ) ) * ( 1.0 - 0.5 * smoothstep( 0.8, 1.0, lookIrisN ) );
    // A clean sclera: the light, unsaturated texels outside the irises (the whites and their veins) become one soft white.
    float lookSat = lookVal - min( lookTex.r, min( lookTex.g, lookTex.b ) );
    float lookScleraW = ( 1.0 - lookIrisW ) * smoothstep( 0.45, 0.65, lookVal ) * step( ${(IRIS_RADIUS + 0.004).toFixed(4)}, lookIrisR )
        * ( 1.0 - smoothstep( 0.35, 0.6, lookSat ) );
    vec3 lookEyeRgb = mix( mix( lookTex.rgb, lookSclera, lookScleraW ), lookGreen, lookIrisW );
    diffuseColor *= vec4( lookEyeRgb, lookTex.a );
#endif`;

    const needsIrisGlow = iris && o.irisGlow > 0;
    m.onBeforeCompile = (shader) => {
        Object.assign(shader.uniforms, u);
        let frag = shader.fragmentShader
            .replace("#include <common>", `#include <common>
uniform vec3 lookRimColor; uniform float lookRimStrength; uniform float lookRimPower;
uniform float lookTint; uniform float lookTintRef; uniform vec3 lookIris; uniform float lookIrisGlow; uniform vec3 lookSclera;`)
            .replace("#include <map_fragment>", map);
        let extra = "";
        if (rim) extra += `
float lookRim = 1.0 - saturate( abs( dot( normal, normalize( vViewPosition ) ) ) );
totalEmissiveRadiance += lookRimColor * ( pow( lookRim, lookRimPower ) * lookRimStrength );`;
        if (needsIrisGlow) extra += `
#ifdef USE_MAP
totalEmissiveRadiance += lookGreen * ( lookIrisW * lookIrisGlow );
#endif`;
        if (extra) frag = frag.replace("#include <emissivemap_fragment>", "#include <emissivemap_fragment>" + extra);
        shader.fragmentShader = frag;
    };
    m.customProgramCacheKey = () => `gadfly-look-${flat ? "f" : ""}${tint > 0 ? "t" : ""}${rim ? "r" : ""}${iris ? "i" : ""}`;
}

/** The texture's average brightness in linear light (what the shader samples), so the tint keeps the skin's overall blue. */
async function averageBrightness(texture) {
    try {
        const img = texture && texture.image;
        if (!img) return 0;
        const c = document.createElement("canvas");
        c.width = c.height = 32;
        const g = c.getContext("2d", { willReadFrequently: true });
        g.drawImage(img, 0, 0, 32, 32);
        const d = g.getImageData(0, 0, 32, 32).data;
        const lin = (x) => { x /= 255; return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4); };
        let sum = 0, n = 0;
        for (let i = 0; i < d.length; i += 4) {
            if (d[i + 3] < 128) continue;
            sum += 0.2126 * lin(d[i]) + 0.7152 * lin(d[i + 1]) + 0.0722 * lin(d[i + 2]);
            n++;
        }
        return n ? sum / n : 0;
    } catch { return 0; }
}
