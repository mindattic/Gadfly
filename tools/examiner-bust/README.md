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

`still.png` is optional. Passing it renders `still-front`, `still-34`, `still-viseme-aa-blink` and `still-viseme-o-look`, using
a stand-in for the app's blue look.

## Parts

- `head.py` is the reusable step. It imports Socrates and drops his MPFB hair, beard and moustache. It decimates the bust,
  scales it onto his head (`BUST_SCALE`; the landmarks were read off orthographic renders) and draws the MPFB face onto the
  bust's. It then splits the bust into `Bust.skin`, `Bust.hair`, `Bust.beard` and `Bust.torso`, named so `avatar-look.js`
  styles them as skin, hair, hair and clothing. The beard gets the face's morph targets from the nearest face point, so it
  opens with the jaw. Hair and skin are rigid to `Head`, blended to `Spine2` through the neck. The eye material is renamed
  `Human.high-poly-glow`, which gives the irises the brighter green glow approved for this examiner.
- `build.py` is the bust on its plinth. It drops the suit, cuts the MPFB body below the jaw, keeps the bust's shoulders and
  plinth rigid on `Spine2` and keeps Socrates' eye level (1.68 m).
