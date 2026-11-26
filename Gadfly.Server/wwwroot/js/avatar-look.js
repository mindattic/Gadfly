// The examiner's luminous blue look (GAD-T503). The 3D examiner (<ma-avatar>, MindAttic.Avatar on TalkingHead and three.js) is
// shown as a smooth, stylized figure lit from within: soft electric-blue skin with an inner glow and a gentle bright rim at her
// silhouette, hair and clothing in deeper blues of the same family, and green eyes. Warm and attentive rather than mighty
// (GAD-T558, docs/research/attention-and-gaze.md 4.6): no transparency, no glow strong enough to wash out her features, and the
// eyes, brows, lips and mouth stay readable, so lip sync shows and the student can look her in the eye.
// She is a digital presence (GAD-T690): faint circuit traces run under the blue skin, thin board-like lines in right-angle and
// 45-degree runs ending in small pads and vias, glowing a lighter cyan over the base glow, with a slow light drifting along them
// (none under prefers-reduced-motion). The pattern is generated here on a canvas once and tiled over the model's UVs. The face
// carries only a few, fainter traces and none at all around the eyes, brows and mouth; the eyes, brows, lashes, teeth and tongue
// never carry it. The clothing carries a fainter share of it. A student's own skin tone or clothing (customLookOptions) drops the
// traces from that part: circuitry belongs to the blue examiner only.
// Uncanniness comes from mismatched realism, worst in the eyes (MacDorman et al. 2009; Seyama & Nagayama 2007), so every part of
// the face is stylized to the same degree: the sclera is a clean, unglowed white with no veins, outside every glow, with a clear
// iris-sclera edge (Kobayashi & Kohshima 2001), and the iris is a smooth stylized green with a darker limbal ring, not a
// photographic texture on a glowing face. Her eyes are never enlarged: the listening expression's eye-widening is left out
// (useExaminerLook). No study shows green or bright irises draw longer looks, so they are not made to glow.
//
// The male examiner (GAD-T504, assets/avatars/examiner-m.glb) is built from the same MakeHuman parts and the same brown_eye sheet,
// so the same palette, part names and iris circles style him; only his hair is named by style (short02), not "hair".
//
// The vendored avatar library under /lib/voice is copied in by tools/sync-voice-assets.ps1 and is never edited here: this module
// restyles the loaded model's materials from the outside, through the element's public `head` (the TalkingHead instance) and its
// `armature` (the model's three.js scene graph). It makes no network calls.

// ---- the palette (hex sRGB) and glow strengths: tune here ----------------------------------------------------------------------

/** Skin: a saturated electric blue, deep enough that the examiner's lights don't wash it out to near-white close up. */
export const LOOK_SKIN = 0x1446c2;
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
/**
 * The exception, approved by the owner for the talking bust (GAD-T792): a model whose eye material is named "...-glow" gets this
 * glow on the iris only (the pupil stays dark and sharp, so where he looks still reads), in a deeper emerald that the glow
 * doesn't wash out to mint (GAD-T795, tuned at the close framing). The other examiners keep none.
 */
export const LOOK_IRIS_GLOW_OWN = 0.9;
export const LOOK_IRIS_OWN = 0x00b450;
/** How much of the room's reflected light reaches skin, hair and clothing: below 1 keeps the blue saturated, not washed out. */
export const LOOK_ENV_LIGHT = 0.3;
/** Eye surface roughness: low, so the light catches them (a catchlight) and they look alive. */
export const LOOK_EYE_ROUGHNESS = 0.08;

/** The circuit traces' glow: a light cyan-blue, lighter than the skin's inner glow. */
export const LOOK_CIRCUIT_COLOR = 0x7fdcff;
/** How bright the traces glow on the skin (emissive): present but faint, never brighter than her features. */
export const LOOK_CIRCUIT_STRENGTH = 0.32;
/** How many times the pattern tiles across the model's UV sheet (higher is finer). */
export const LOOK_CIRCUIT_SCALE = 3;
/** On the face (away from the eyes and mouth, which carry none) only the sparse main traces show, at this share of the strength. */
export const LOOK_CIRCUIT_FACE = 0.4;
/** The clothing's share of the traces' strength (0 turns them off there). */
export const LOOK_CIRCUIT_CLOTHING = 0.4;
/** The light drifting along the traces: how much brighter a trace gets as it passes (0 turns it off), and the seconds per cycle. */
export const LOOK_CIRCUIT_PULSE = 0.7;
export const LOOK_CIRCUIT_PULSE_SECONDS = 7;

const DEFAULTS = {
    skin: LOOK_SKIN, clothing: LOOK_CLOTHING, hair: LOOK_HAIR, brows: LOOK_BROWS, iris: LOOK_IRIS, sclera: LOOK_SCLERA,
    teeth: LOOK_TEETH, tongue: LOOK_TONGUE, glow: LOOK_GLOW, rim: LOOK_RIM,
    glowStrength: LOOK_GLOW_STRENGTH, rimStrength: LOOK_RIM_STRENGTH, rimPower: LOOK_RIM_POWER,
    skinDetail: LOOK_SKIN_DETAIL, envLight: LOOK_ENV_LIGHT, irisGlow: LOOK_IRIS_GLOW, eyeRoughness: LOOK_EYE_ROUGHNESS,
    circuitColor: LOOK_CIRCUIT_COLOR, circuitStrength: LOOK_CIRCUIT_STRENGTH, circuitScale: LOOK_CIRCUIT_SCALE,
    circuitFace: LOOK_CIRCUIT_FACE, circuitClothing: LOOK_CIRCUIT_CLOTHING, circuitPulse: LOOK_CIRCUIT_PULSE,
    circuitPulseSeconds: LOOK_CIRCUIT_PULSE_SECONDS,
};

// ---- the student's customization (GAD-T646, GAD-T649) ---------------------------------------------------------------------------

// The colours behind each bounded choice (ExaminerCustomization.cs), the same ones the get-ready page's swatches show (get-ready.css).
// A chosen colour replaces that part's blue and becomes its own inner glow, so the part reads as the colour chosen; the eyes, brows,
// lips and the silhouette rim are never changed, so the face stays as readable as the default (and the gaze check has the same face).
export const SKIN_TONES = {
    "light": 0xf3d9c6, "medium-light": 0xe0b593, "medium": 0xc08a62, "medium-dark": 0x8d5a3b, "dark": 0x5a3826,
};
export const HAIR_COLOURS = {
    "black": 0x1c1a1a, "dark-brown": 0x3b2a20, "brown": 0x6b4630, "auburn": 0x8a3b1f, "blonde": 0xd8b46a, "grey": 0xa7a7a7,
};
export const ATTIRES = {
    "navy-blazer": 0x1f2f57, "charcoal-suit": 0x3a3d42, "cardigan": 0x8a6a4f, "white-shirt": 0xf4f4f2,
};
/**
 * Framing presets to the avatar's camera view (<ma-avatar view>): every one keeps her face and eyes in view. The portrait gets the
 * matching bounded scale about her eyes (voice.css), never enough to crop her face.
 */
export const FRAMINGS = {
    "head-and-shoulders": { view: "upper", portraitScale: 1 },
    "close": { view: "head", portraitScale: 1.15 },
    "seated": { view: "mid", portraitScale: 0.86 },
};

const own = (table, key) => (typeof key === "string" && Object.prototype.hasOwnProperty.call(table, key) ? table[key] : undefined);

/**
 * The look options for a customization `{ skinTone, hair, attire, look }` (already validated on the server; anything not on a list
 * is ignored here too, so the default stays). Empty when nothing is customized.
 */
export function customLookOptions(c) {
    const o = {};
    if (!c) return o;
    const skin = own(SKIN_TONES, c.skinTone), hair = own(HAIR_COLOURS, c.hair), clothing = own(ATTIRES, c.attire);
    if (skin !== undefined) { o.skin = skin; o.skinGlow = skin; }
    if (hair !== undefined) { o.hair = hair; o.hairGlow = hair; }
    if (clothing !== undefined) { o.clothing = clothing; o.clothingGlow = clothing; }
    return o;
}

/** The framing preset for a customization's `look` (head-and-shoulders when none or unknown). */
export function framingOf(c) {
    return own(FRAMINGS, c && c.look) || FRAMINGS["head-and-shoulders"];
}

/** The clothing colour as a CSS hex string, for the portrait's clothing tint, or null when the clothing is the default. */
export function attireCss(c) {
    const v = own(ATTIRES, c && c.attire);
    return v === undefined ? null : "#" + v.toString(16).padStart(6, "0");
}

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
    // MakeHuman hair assets are named by style (ponytail01, short02, bob01, afro01, long01, braid01), not "hair".
    if (/hair|beard|moustache|ponytail|braid|\bshort\d|\bbob\d|\bafro\d|\blong\d/.test(n)) return "hair";
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
    const circuit = circuitOf(root);
    let count = 0;
    root.traverse((mesh) => {
        if (!mesh.isMesh) return;
        for (const m of materialsOf(mesh)) {
            if (!m || !m.color || (m.userData && m.userData.gadflyLook)) continue;
            const part = partOf(m, mesh);
            const glowing = part === "eyes" && /-glow\b/.test(m.name || "");
            restyle(m, part, glowing ? { ...o, irisGlow: Math.max(o.irisGlow, LOOK_IRIS_GLOW_OWN), iris: LOOK_IRIS_OWN } : o, circuit, mesh);
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

const materialsOf = (mesh) => (Array.isArray(mesh.material) ? mesh.material : [mesh.material]);

function restyle(m, part, o, circuit, mesh) {
    const standard = !!m.isMeshStandardMaterial;
    const glow = (hex, intensity) => { if (m.emissive) { m.emissive.setHex(hex); m.emissiveIntensity = intensity; m.emissiveMap = null; } };
    if (standard && (part === "skin" || part === "clothing" || part === "hair")) m.envMapIntensity = o.envLight;
    switch (part) {
        case "skin":
            // The texture stays, but only its light and shade: the blue replaces its color (tinted in the shader below), so the
            // lips, brows and creases still read while the skin is one smooth blue.
            m.color.setHex(o.skin);
            glow(o.skinGlow ?? o.glow, o.glowStrength);
            if (standard) { m.roughness = 0.55; m.metalness = 0; }
            // Circuitry only on the default blue skin, and only when the face could be located to keep it off the eyes and mouth.
            shade(m, o, {
                tint: m.map ? o.skinDetail : 0, rim: true,
                circuit: circuit && circuit.face && o.skin === LOOK_SKIN ? { strength: o.circuitStrength, face: circuit.face } : null,
            }, circuit, mesh);
            break;
        case "clothing":
            m.map = null;        // the normal map stays: the folds still catch the light
            m.color.setHex(o.clothing);
            glow(o.clothingGlow ?? o.glow, o.glowStrength * 0.45);
            if (standard) { m.roughness = 0.7; m.metalness = 0; }
            shade(m, o, {
                rim: true, circuit: circuit && o.clothing === LOOK_CLOTHING ? { strength: o.circuitStrength * o.circuitClothing } : null,
            }, circuit, mesh);
            break;
        case "hair":
            m.color.setHex(o.hair);
            glow(o.hairGlow ?? o.glow, o.glowStrength * 0.4);
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
 *  - iris: the eye texture's irises are recolored to the iris green, with a faint glow on them only;
 *  - circuit: the circuit traces, added as emissive light (`face` masks them on the face: see circuitShader).
 */
function shade(m, o, { tint = 0, flat = false, rim = false, iris = false, circuit = null }, setup = null, mesh = null) {
    if (!m.isMeshStandardMaterial) return;
    const Color = m.color.constructor;
    const u = {
        lookRimColor: { value: new Color(o.rim) }, lookRimStrength: { value: rim ? o.rimStrength : 0 }, lookRimPower: { value: o.rimPower },
        lookTint: { value: tint }, lookTintRef: { value: 0.45 },
        lookIris: { value: new Color(o.iris) }, lookIrisGlow: { value: o.irisGlow }, lookSclera: { value: new Color(o.sclera) },
    };
    if (tint > 0) averageBrightness(m.map).then((v) => { if (v > 0.02) u.lookTintRef.value = v; });
    if (circuit) {
        const pulse = reducedMotion() ? 0 : o.circuitPulse;
        Object.assign(u, {
            lookCircuit: { value: setup.texture }, lookCircuitScale: { value: o.circuitScale },
            lookCircuitColor: { value: new Color(o.circuitColor) }, lookCircuitStrength: { value: circuit.strength },
            lookCircuitFace: { value: o.circuitFace }, lookPulse: { value: pulse }, lookPhase: circuitPhase,
        });
        if (circuit.face) {
            const f = circuit.face;
            Object.assign(u, { lookEyeL: { value: f.eyeL }, lookEyeR: { value: f.eyeR }, lookMouth: { value: f.mouth }, lookFaceSize: { value: f.size } });
        }
        if (pulse > 0) drivePulse(mesh, o.circuitPulseSeconds);
    }

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
        if (circuit) extra += circuitShader(!!circuit.face);
        if (needsIrisGlow) extra += `
#ifdef USE_MAP
totalEmissiveRadiance += lookGreen * ( lookIrisW * lookIrisGlow );
#endif`;
        if (extra) frag = frag.replace("#include <emissivemap_fragment>", "#include <emissivemap_fragment>" + extra);
        if (circuit) {
            frag = frag.replace("#include <common>", `#include <common>
uniform sampler2D lookCircuit; uniform float lookCircuitScale; uniform vec3 lookCircuitColor; uniform float lookCircuitStrength;
uniform float lookCircuitFace; uniform float lookPulse; uniform float lookPhase;
uniform vec3 lookEyeL; uniform vec3 lookEyeR; uniform vec3 lookMouth; uniform float lookFaceSize;
varying vec2 vLookUv; varying vec3 vLookPos;`);
            shader.vertexShader = shader.vertexShader
                .replace("#include <common>", "#include <common>\nvarying vec2 vLookUv; varying vec3 vLookPos;")
                .replace("#include <begin_vertex>", "#include <begin_vertex>\nvLookUv = uv; vLookPos = position;");
        }
        shader.fragmentShader = frag;
    };
    const c = circuit ? (circuit.face ? "cf" : "c") : "";
    m.customProgramCacheKey = () => `gadfly-look-${flat ? "f" : ""}${tint > 0 ? "t" : ""}${rim ? "r" : ""}${iris ? "i" : ""}${c}`;
}

/**
 * The circuit traces as emissive light. The texture holds every trace's glow in red, only the sparse main traces in green, and
 * in blue each trace's distance along itself (0..1, wrapping), which the drifting light follows.
 * With `face`, the mask works in the model's rest pose (the vertex `position`, before skinning and morphs, so it never slides
 * over the face as she talks), measured in eye spacings so both examiners get the same mask: around each eye (centred a little
 * above it, taking in the lids and brow) and around the mouth (an ellipse wider than tall, taking in the lips as they move) there
 * are no traces; the rest of the face shows only the main traces, at LOOK_CIRCUIT_FACE, back to the full pattern past the jaw.
 */
function circuitShader(face) {
    const mask = face ? `
float lookDe = min( distance( vLookPos, lookEyeL ), distance( vLookPos, lookEyeR ) ) / lookFaceSize;
float lookDm = length( ( vLookPos - lookMouth ) * vec3( 0.8, 1.3, 1.0 ) ) / lookFaceSize;
float lookFaceK = 1.0 - smoothstep( 1.4, 2.2, distance( vLookPos, ( lookEyeL + lookEyeR + lookMouth ) / 3.0 ) / lookFaceSize );
lookTrace = mix( lookC.r, lookC.g * lookCircuitFace, lookFaceK ) * smoothstep( 0.6, 1.0, lookDe ) * smoothstep( 0.55, 0.95, lookDm );` : "";
    return `
vec3 lookC = texture2D( lookCircuit, vLookUv * lookCircuitScale ).rgb;
float lookTrace = lookC.r;${mask}
float lookP = fract( lookC.b - lookPhase );
float lookPulseK = 1.0 - smoothstep( 0.0, 0.09, min( lookP, 1.0 - lookP ) );
totalEmissiveRadiance += lookCircuitColor * ( lookTrace * lookCircuitStrength * ( 1.0 + lookPulse * lookPulseK ) );`;
}

// The drifting light's phase (0..1), shared by every circuit material and advanced as the model renders.
const circuitPhase = { value: 0 };

function drivePulse(mesh, seconds) {
    if (!mesh || mesh.userData.gadflyPulse) return;
    mesh.userData.gadflyPulse = true;
    const before = mesh.onBeforeRender;
    mesh.onBeforeRender = function (...args) {
        circuitPhase.value = (performance.now() / 1000 / seconds) % 1;
        return before.apply(this, args);
    };
}

function reducedMotion() {
    try { return matchMedia("(prefers-reduced-motion: reduce)").matches; } catch { return false; }
}

/**
 * The circuit texture and where the face is, for a loaded model: null when it has no texture to borrow three.js's Texture class
 * from. `face` is null when the eyes or teeth can't be found, and then the skin gets no traces at all.
 */
function circuitOf(root) {
    let Texture = null, body = null, eyes = null, teeth = null;
    root.traverse((mesh) => {
        if (!mesh.isMesh) return;
        for (const m of materialsOf(mesh)) {
            if (!m) continue;
            const t = m.map || m.normalMap;
            if (!Texture && t && t.isTexture) Texture = t.constructor;
            const part = partOf(m, mesh);
            if (part === "skin") body = body || mesh;
            else if (part === "eyes") eyes = eyes || mesh;
            else if (part === "teeth") teeth = teeth || mesh;
        }
    });
    if (!Texture) return null;
    return { texture: circuitTexture(Texture), face: faceOf(body, eyes, teeth) };
}

/** The eyes (each raised toward its brow), the mouth and the eye spacing, in the body's rest-pose space. */
function faceOf(body, eyes, teeth) {
    const ep = eyes && eyes.geometry && eyes.geometry.attributes.position;
    const tp = teeth && teeth.geometry && teeth.geometry.attributes.position;
    if (!body || !ep || !tp || !tp.count) return null;
    const V = body.position.constructor;
    for (const mesh of [body, eyes, teeth]) mesh.updateMatrix();
    const toBody = body.matrix.clone().invert();
    const p = new V(), l = new V(), r = new V(), mouth = new V();
    let nl = 0, nr = 0;
    for (let i = 0; i < ep.count; i++) {
        p.fromBufferAttribute(ep, i);
        if (p.x >= 0) { l.add(p); nl++; } else { r.add(p); nr++; }
    }
    for (let i = 0; i < tp.count; i++) mouth.add(p.fromBufferAttribute(tp, i));
    if (!nl || !nr) return null;
    l.divideScalar(nl).applyMatrix4(eyes.matrix).applyMatrix4(toBody);
    r.divideScalar(nr).applyMatrix4(eyes.matrix).applyMatrix4(toBody);
    mouth.divideScalar(tp.count).applyMatrix4(teeth.matrix).applyMatrix4(toBody);
    const size = l.distanceTo(r);
    if (!(size > 0)) return null;
    l.y += 0.15 * size;
    r.y += 0.15 * size;
    return { eyeL: l, eyeR: r, mouth, size };
}

// The circuit pattern: a square tile, seamless at its edges, drawn once per page and shared by every examiner.
const CIRCUIT_SIZE = 1024;
const CIRCUIT_CELLS = 32;
let circuitCache = null;

function circuitTexture(Texture) {
    if (circuitCache) return circuitCache;
    const t = new Texture(circuitCanvas(CIRCUIT_SIZE, CIRCUIT_CELLS));
    t.wrapS = t.wrapT = 1000;   // THREE.RepeatWrapping
    t.anisotropy = 4;
    t.needsUpdate = true;
    return (circuitCache = t);
}

/**
 * Board-like traces on a grid of `cells` (wrapping, so the tile is seamless): each starts at a free cell, runs straight in one of
 * the four main directions, now and then turns 45 degrees (so runs go square, diagonal, square), stops before touching another
 * trace, and ends in pads (a ring via at its start, a solid pad at its end). A few lone vias sit between. Thin bright cores with
 * a soft halo. Channels: red is every trace's glow, green only the main traces' (about a third), blue the phase along each trace.
 */
function circuitCanvas(size, cells) {
    const rnd = seeded(0x6ad7c1);
    const step = size / cells;
    const used = new Uint8Array(cells * cells);
    const at = (x, y) => (((y % cells) + cells) % cells) * cells + (((x % cells) + cells) % cells);
    const dirs = [[1, 0], [1, 1], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1], [1, -1]];
    const glow = new Float32Array(size * size), main = new Float32Array(size * size), phase = new Float32Array(size * size);
    const halfWidth = size / 800, halo = size / 170, phaseLength = size * 0.7;

    const plot = (x, y, v, ph, major) => {
        const i = (((y % size) + size) % size) * size + (((x % size) + size) % size);
        if (v > glow[i]) { glow[i] = v; phase[i] = ph; }
        if (major && v > main[i]) main[i] = v;
    };
    const shapeOf = (d, core) => Math.max(1 - smoothstep(core - 0.75, core + 0.75, d), 0.3 * Math.max(0, 1 - d / (core + halo)) ** 2);
    const segment = (ax, ay, bx, by, ph0, ph1, major) => {
        const reach = halfWidth + halo + 1;
        const dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy || 1;
        for (let y = Math.floor(Math.min(ay, by) - reach); y <= Math.ceil(Math.max(ay, by) + reach); y++)
            for (let x = Math.floor(Math.min(ax, bx) - reach); x <= Math.ceil(Math.max(ax, bx) + reach); x++) {
                const k = Math.max(0, Math.min(1, ((x - ax) * dx + (y - ay) * dy) / len2));
                const d = Math.hypot(x - ax - k * dx, y - ay - k * dy);
                if (d < reach) plot(x, y, shapeOf(d, halfWidth), ph0 + (ph1 - ph0) * k, major);
            }
    };
    const pad = (cx, cy, radius, hole, ph, major) => {
        const reach = Math.ceil(radius + halo + 1);
        const ring = hole > 0 ? (radius - hole) / 2 : 0;
        for (let y = -reach; y <= reach; y++)
            for (let x = -reach; x <= reach; x++) {
                const r = Math.hypot(x, y);
                const d = ring > 0 ? Math.abs(r - (radius + hole) / 2) : Math.max(0, r - radius);
                plot(Math.round(cx) + x, Math.round(cy) + y, shapeOf(d, ring || halfWidth), ph, major);
            }
    };

    let traces = 0;
    for (let tries = 0; tries < 600 && traces < 64; tries++) {
        let x = Math.floor(rnd() * cells), y = Math.floor(rnd() * cells);
        if (used[at(x, y)]) continue;
        let d = Math.floor(rnd() * 4) * 2;
        const pts = [[x, y]];
        used[at(x, y)] = 1;
        const length = 4 + Math.floor(rnd() * 14);
        for (let s = 0; s < length; s++) {
            if (rnd() < (d % 2 ? 0.45 : 0.18)) d = (d + (rnd() < 0.5 ? 1 : 7)) % 8;
            const nx = x + dirs[d][0], ny = y + dirs[d][1];
            if (used[at(nx, ny)] || (d % 2 && used[at(x, ny)] && used[at(nx, y)])) break;
            x = nx; y = ny;
            used[at(x, y)] = 1;
            pts.push([x, y]);
        }
        if (pts.length < 3) continue;
        traces++;
        const major = rnd() < 0.3;
        let ph = rnd();
        const px = pts.map(([cx, cy]) => [(cx + 0.5) * step, (cy + 0.5) * step]);
        const start = ph;
        for (let i = 1; i < px.length; i++) {
            const [ax, ay] = px[i - 1], [bx, by] = px[i];
            const next = ph + Math.hypot(bx - ax, by - ay) / phaseLength;
            segment(ax, ay, bx, by, ph, next, major);
            ph = next;
        }
        pad(px[0][0], px[0][1], halfWidth * 3.2, halfWidth * 1.4, start, major);
        pad(px[px.length - 1][0], px[px.length - 1][1], halfWidth * 2.6, 0, ph, major);
    }
    for (let i = 0; i < 40; i++) {
        const x = Math.floor(rnd() * cells), y = Math.floor(rnd() * cells);
        if (!used[at(x, y)]) pad((x + 0.5) * step, (y + 0.5) * step, halfWidth * 2.2, halfWidth, rnd(), false);
    }

    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = size;
    const g = canvas.getContext("2d");
    const img = g.createImageData(size, size);
    for (let i = 0; i < size * size; i++) {
        img.data[i * 4] = Math.round(glow[i] * 255);
        img.data[i * 4 + 1] = Math.round(main[i] * 255);
        img.data[i * 4 + 2] = Math.round((phase[i] % 1) * 255);
        img.data[i * 4 + 3] = 255;
    }
    g.putImageData(img, 0, 0);
    return canvas;
}

const smoothstep = (a, b, x) => { const t = Math.max(0, Math.min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); };

/** A small seeded random generator (mulberry32), so every page draws the same pattern. */
function seeded(seed) {
    return () => {
        seed = (seed + 0x6d2b79f5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
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
