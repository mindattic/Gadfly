# examiner-bust (GAD-T792)

Builds `Gadfly.Server/wwwroot/assets/avatars/examiner-bust.glb`, the talking bust. It is the owner's Meshy.ai marble bust of
Socrates with the working face rig of `examiner-socrates.glb`.

## Source

`Socrates_Burt.glb` (20.7 MB, SHA-256 `9537f8506f883711e1157ff46eb68885cf3931aa78ba16f3f329a22484207cd7`) is the owner's Meshy.ai
export (paid plan, owner-owned). It is too large to commit. It lives in the owner's Downloads folder
(`C:\Users\ryand\Downloads\Socrates_Burt.glb`). Copy it somewhere before building, and never edit the original.

## Steps

Use Blender 4.4.3 from `tools/examiner-socrates/setup-blender-mpfb.ps1`. These steps don't need MPFB, because they start from
the committed `examiner-socrates.glb`.

```
blender -b --python-exit-code 1 --python build.py -- ../../Gadfly.Server/wwwroot/assets/avatars/examiner-socrates.glb Socrates_Burt.glb raw.glb still.png
python ../examiner-m/postprocess.py raw.glb tidy.glb
python ../examiner-m/alignrig.py ../../Gadfly.Server/wwwroot/assets/avatars/mpfb.glb tidy.glb aligned.glb
python ../examiner-socrates/trim.py aligned.glb ../../Gadfly.Server/wwwroot/assets/avatars/examiner-bust.glb
```

The head on Socrates' own body and clothes (GAD-T793, `examiner-bust-body.glb`) is built the same way, with `body.py` in place of
`build.py` and `examiner-bust-body.glb` as trim's output.

`still.png` is optional. Passing it renders stills using a stand-in for the app's blue look: `still-front`, `still-34`,
`still-viseme-aa-blink`, `still-viseme-o-look`, `still-turn-nod` and `still-turn-back` (Neck and Head turned while talking),
`still-nose` (a close-up of the face) and `still-full`.

## Parts

- `head.py` is the reusable step: `head.build(examiner_glb, bust_glb, on_plinth=True)`. It imports Socrates and drops his MPFB
  hair, beard and moustache. It decimates the bust and scales it onto his head (`BUST_SCALE`; the landmarks were read off
  orthographic renders).
  - It draws the MPFB face onto the bust's nose, brow, cheeks and forehead. The pull is smoothed over the mesh so the skin
    doesn't fold, and it leaves the lids and lips alone.
  - It then splits the bust into one shell, `Bust`, with `Bust.skin` and `Bust.hair` material slots, plus `Bust.beard` and
    `Bust.plinth`. `avatar-look.js` styles these as skin, hair, hair and clothing.
  - It cuts out the face's middle and lays the cut rims flush on the MPFB face.
  - The beard takes the face's morph targets from the nearest face point, so it opens with the jaw.
  - The bust's head and neck take the nearest MPFB vertex's weights, so the neck turns, nods and talks with the face.
  - With `on_plinth`, everything below the neck hangs on a root bone, `BustBase`. TalkingHead never moves that bone, so its
    spine sway doesn't tilt the stone.
  - The eye material is renamed `Human.high-poly-glow`, which gives the irises the green glow approved for this examiner.
  - With `on_plinth=False`, the head is built for Socrates' own body (GAD-T793).
- `build.py` is the bust on its plinth. It drops the suit and cuts the MPFB body below the jaw. It keeps Socrates' eye level
  (1.68 m). It also leaves a 0.1 mm triangle where his left sole was. TalkingHead lowers the hips every frame until the model's
  lowest point is on the floor, so without that speck the bust would sink out of frame.
- `body.py` is the head with `on_plinth=False` on his full body and clothes, unmoved, so his eye level and framing are
  Socrates'. The bust's shell stops at the neck, where its rim lies flush on the MPFB neck and takes that neck's weights.
- `head.stills()` renders the stills for both.

Below the nose, `conform` tucks the MPFB face (the upper lip's skin and the cheeks) `BUST_TUCK` behind the bust's carved
surface, so the moustache and beard stand in front. Everything of the bust there is beard, except the lips (`BUST_LIPS`), which
stay MPFB's and uncovered. The moustache takes the upper lip's morphs from the tucked skin under it. The pull above the nose
reaches `BUST_REACH` with `BUST_SMOOTHING` passes, so the nose's wings and tip, the brow and the cheekbones take the bust's shape.
