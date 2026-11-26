# The talking bust: examiner-bust.glb (GAD-T792)

| | |
|---|---|
| File | `examiner-bust.glb` (8.9 MB), committed |
| SHA-256 | `c399e8af1c67af7cfdc70284539226c6ab1dda5da174de9d9003c507ba235d8f` |
| Made with | Blender 4.4.3 (headless), by `tools/examiner-bust/build.py` (with `head.py`), then `tools/examiner-m/postprocess.py`, `tools/examiner-m/alignrig.py` and `tools/examiner-socrates/trim.py` (see `tools/examiner-bust/README.md`) |
| Sculpt | `Socrates_Burt.glb`, a marble bust of Socrates generated with Meshy.ai: decimated to 7%, scaled onto the MPFB head, split into one shell (skin and hair), the beard and the plinth. Not committed (20.7 MB); see the README for where it lives |
| Face rig | From `examiner-socrates.glb`: the MPFB body cut below the jaw (its face drawn onto the bust's), eyes `high-poly` with `brown_eye`, eyebrows, eyelashes, teeth and tongue; 52 ARKit + 15 viseme targets on the face, 44 of them carried onto the bust's beard |
| Rig | As `examiner-socrates.glb`: Mixamo-style bones without the `mixamorig:` prefix, `LeftEye`/`RightEye` under `Head`, bone frames of TalkingHead's `mpfb.glb`, plus a `BustBase` root bone that holds the shoulders and plinth still |

## Licenses

- **The bust sculpt**: Meshy.ai output generated on the owner's paid Meshy plan. Under Meshy's terms the paid-plan user owns
  the output, so the owner (MindAttic) owns this model's bust geometry. It is not CC0 and may not be reused outside MindAttic
  without the owner's permission.
- **The face rig and its parts**: as `examiner-socrates.LICENSE.md`. MakeHuman base mesh, targets and system assets,
  `faceunits01`, `visemes02` and the `mpfb.glb` bone orientations are all **CC0 1.0**.

So the model as a whole belongs to the owner, and its MPFB parts are CC0.
