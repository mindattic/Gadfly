# The bust's head on Socrates' body: examiner-bust-body.glb (GAD-T793)

| | |
|---|---|
| File | `examiner-bust-body.glb` (11.6 MB), committed |
| SHA-256 | `c8ec5eee18494973bad70102648d8862b46809a5c091e544c7043b9fa10e6f9f` |
| Made with | Blender 4.4.3 (headless), by `tools/examiner-bust/body.py` (with `head.py`), then `tools/examiner-m/postprocess.py`, `tools/examiner-m/alignrig.py` and `tools/examiner-socrates/trim.py` (see `tools/examiner-bust/README.md`) |
| Sculpt | `Socrates_Burt.glb`, a marble bust of Socrates generated with Meshy.ai: decimated to 7%, scaled onto the MPFB head, its head and neck kept as one shell (skin and hair) and the beard. Not committed (20.7 MB); see the README for where it lives |
| Body | From `examiner-socrates.glb`: the full MPFB body (its face drawn onto the bust's) and his shirt and trousers (`male_casualsuit06`, the shirt kept under the toga as the chiton), eyes `high-poly` with `brown_eye`, eyelashes (eyebrows dropped), teeth and tongue; 52 ARKit + 15 viseme targets on the face, 46 of them carried onto the bust's beard |
| Rig | As `examiner-socrates.glb`: Mixamo-style bones without the `mixamorig:` prefix, `LeftEye`/`RightEye` under `Head`, bone frames of TalkingHead's `mpfb.glb`. The bust's head and neck take the nearest MPFB vertex's weights |

## Licenses

- **The bust sculpt**: Meshy.ai output generated on the owner's paid Meshy plan. Under Meshy's terms the paid-plan user owns
  the output, so the owner (MindAttic) owns this model's bust geometry. It is not CC0 and may not be reused outside MindAttic
  without the owner's permission.
- **The toga** (`Toga`, GAD-T800): modelled for this project by `tools/examiner-bust/body.py` (no CC0 MakeHuman toga exists),
  from the CC0 `male_casualsuit06` surface and a generated skirt; owned by MindAttic like the rest of the model.
- **The body, clothes, face rig and their parts**: as `examiner-socrates.LICENSE.md`. MakeHuman base mesh, targets, clothes and
  system assets, `faceunits01`, `visemes02` and the `mpfb.glb` bone orientations are all **CC0 1.0**.

So the model as a whole belongs to the owner, and its MPFB parts are CC0.
