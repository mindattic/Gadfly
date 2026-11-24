# Socrates: examiner-socrates.glb (GAD-T724)

| | |
|---|---|
| File | `examiner-socrates.glb` (12.5 MB), committed |
| SHA-256 | `ac07a6c4cfe11660350396b2cfa153df47676ce1df2602999ebbddf9a912c262` |
| Made with | Blender 4.4.3 (headless) and the [MPFB](https://static.makehumancommunity.org/mpfb.html) 2.0.18 extension (a tool; none of its GPL code is in the file), by `tools/examiner-socrates/build.py`, then `tools/examiner-m/postprocess.py`, `tools/examiner-m/alignrig.py` and `tools/examiner-socrates/trim.py` |
| Character | MPFB human: male, age 0.85 (about seventy), muscle 0.45, weight 0.62, proportions 0.5, height 0.4; face modifiers after the Vatican Pio-Clementino bust of Socrates (Inv. 314): broad, flat, short, upturned nose with flared nostrils; wide, round face; raised forehead; full cheeks; slightly enlarged, wide-set eyes with eye bags. Bald |
| Assets | MakeHuman system assets: skin `old_caucasian_male` (the look replaces its color), eyes `high-poly` with `brown_eye` (so `avatar-look.js` finds the irises and makes them green), eyebrows `eyebrow001`, eyelashes `eyelashes01`, teeth `teeth_base`, tongue `tongue01`, clothes `male_casualsuit06`. bodyparts05 pack: `rehmanpolanski_beard_viking` and `rehmanpolanski_moustache_viking` (styled as hair by `avatar-look.js`) |
| Face shapes | `faceunits01` (52 ARKit) and `visemes02` (15 Oculus) on the body (67 targets); interpolated onto beard (50), moustache (48), eyebrows, eyelashes, teeth and tongue (the targets that do not move a part are left off it, as in `examiner-m.glb`) |
| Rig | As `examiner-m.glb`: MPFB's Mixamo rig without the `mixamorig:` prefix, `LeftEye`/`RightEye` bones under `Head`, bone frames of TalkingHead's `mpfb.glb`. Root object `Armature` |
| Lightened | Textures at most 1024 px, JPEG where there is no cut-out alpha; the beard and moustache keep their morph positions but not their morph normals (`trim.py`) |

Likeness reference only (nothing from it is in the file): the photograph
[Socrates Pio-Clementino Inv314.jpg](https://commons.wikimedia.org/wiki/File:Socrates_Pio-Clementino_Inv314.jpg), released
into the public domain by its uploader.

## Licenses

- **MakeHuman system assets** (`makehuman_system_assets_cc0.zip`): **CC0 1.0**, as in `examiner-m.LICENSE.md`.
- **MakeHuman base mesh and modelling targets** (shipped inside MPFB): **CC0 1.0**.
- **bodyparts05** (files.makehumancommunity.org/asset_packs/bodyparts05/bodyparts05_cc0.zip): `rehmanpolanski_beard_viking` and
  `rehmanpolanski_moustache_viking`, author RehmanPolanski, **CC0** in both the pack metadata and each `.mhclo` header.
- **faceunits01** and **visemes02** (files.makehumancommunity.org/functional/): **CC0 1.0**, author Mika Suominen.
- **Bone orientations** copied from `mpfb.glb` (TalkingHead sample): **CC0 1.0** (see `LICENSE.md`).

So the model as a whole is **CC0 1.0**.
