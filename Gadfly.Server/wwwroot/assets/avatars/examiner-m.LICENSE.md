# Male examiner: examiner-m.glb (GAD-T504)

| | |
|---|---|
| File | `examiner-m.glb` (9.5 MB), committed |
| SHA-256 | `5e0e752cfc58ed5b6c83be9e8d6159a64327ef4057090608a6c1e9118af851c1` |
| Made with | Blender 4.4.3 (headless) and the [MPFB](https://static.makehumancommunity.org/mpfb.html) 2.0.18 extension (extensions.blender.org, GPL-3.0 code; it is a tool, none of its code is in the file), by `tools/examiner-m/build.py`, then `postprocess.py` and `alignrig.py` in the same folder |
| Character | MPFB human: male (gender 1.0), age 0.5, muscle 0.55, weight 0.5, proportions 0.6, height 0.55, both upper eyelid folds raised 0.4 (`eye-eyefold-up`: a more open, attentive lid; the eyeballs are not enlarged) |
| Assets | MakeHuman system assets: skin `young_caucasian_male` (the look replaces its color), eyes `high-poly` with `brown_eye` (the same eye sheet as the female's, so `avatar-look.js` finds the irises), eyebrows `eyebrow001`, eyelashes `eyelashes01`, teeth `teeth_base`, tongue `tongue01`, hair `short02`, clothes `male_casualsuit06` (plain t-shirt and jeans) |
| Face shapes | MPFB "Create export copy" steps: the `faceunits01` pack (the 52 ARKit blend shapes) and the `visemes02` pack (the 15 Oculus/Meta visemes, `viseme_sil` included), interpolated onto eyebrows, eyelashes, hair, teeth and tongue |
| Rig | MPFB's Mixamo rig with the `mixamorig:` prefix removed, plus `LeftEye`/`RightEye` bones (children of `Head`, at each eyeball's centre, the eyeballs weighted to them); every bone then takes the rest orientation of the same bone in TalkingHead's `mpfb.glb` (`alignrig.py`), because TalkingHead's pose templates were tuned for those frames. Root object `Armature` |
| Lightened | Textures at most 1024 px; textures without cut-out alpha as JPEG; skin, clothes, teeth and tongue opaque; hair, brows, lashes and eyes alpha-masked |

## Licenses

- **MakeHuman system assets** (`makehuman_system_assets_cc0.zip`, files.makehumancommunity.org/asset_packs/makehuman_system_assets/):
  **CC0 1.0**. Each asset file says "This asset was explicitly released as CC0 in september 2020", and the pack's metadata lists
  every asset with `"license": "CC0"`, author `makehuman_system`.
- **MakeHuman base mesh and modelling targets** (shipped inside MPFB): **CC0 1.0** (the MakeHuman project's asset license; only
  the program code is GPL/AGPL, and the output of the program is not covered by it).
- **faceunits01** and **visemes02** (files.makehumancommunity.org/functional/): **CC0 1.0**, author Mika Suominen (met4citizen,
  the TalkingHead author), per each pack's metadata (`"license": "CC0"`).
- **Bone orientations** copied from `mpfb.glb` (TalkingHead v1.8.2 sample): **CC0 1.0** (see `LICENSE.md`).

So the model as a whole is **CC0 1.0**: no attribution is required; it is given here for provenance.
