# Builds the bust's head on Socrates' own body (GAD-T793, Gadfly.Server/wwwroot/assets/avatars/examiner-bust-body.glb) in headless
# Blender: head.py's reshaped head (his MPFB face rig given the Meshy.ai bust's face, hair and beard), stopping at the neck, on
# examiner-socrates's full body and suit. Nothing moves, so his eyes stay at his own height and TalkingHead frames him as Socrates.
#   blender -b --python-exit-code 1 --python body.py -- examiner-socrates.glb Socrates_Burt.glb raw.glb [still.png]
#   python ../examiner-m/postprocess.py raw.glb tidy.glb
#   python ../examiner-m/alignrig.py ../../Gadfly.Server/wwwroot/assets/avatars/mpfb.glb tidy.glb aligned.glb
#   python ../examiner-socrates/trim.py aligned.glb examiner-bust-body.glb
import sys, os
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import head

args = sys.argv[sys.argv.index("--") + 1:]
examiner, bust_glb, out = args[:3]
still = args[3] if len(args) > 3 else None

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

arm, parts, bparts, marks = head.build(examiner, bust_glb, on_plinth=False)
eye = (marks["eyes"][0] + marks["eyes"][1]) / 2

for im in bpy.data.images:
    if im.size[0] > 1024:
        im.scale(1024, 1024)

keep = [arm] + list(parts.values()) + list(bparts.values())
head.export(out, keep)
print("EYE LEVEL", round(eye.z, 4), "(examiner-socrates: 1.68)")

if still:
    head.stills(arm, parts, bparts, keep, eye, still)
