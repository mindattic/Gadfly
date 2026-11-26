# Builds the talking bust (GAD-T792, Gadfly.Server/wwwroot/assets/avatars/examiner-bust.glb) in headless Blender: head.py's
# reshaped head (Socrates' MPFB face rig given the Meshy.ai bust's face, hair and beard) on the bust's own bare shoulders and
# plinth, rigid, with no body below. The eyes stay at Socrates' height, so TalkingHead's camera meets them level.
#   blender -b --python-exit-code 1 --python build.py -- examiner-socrates.glb Socrates_Burt.glb raw.glb [still.png]
#   python ../examiner-m/postprocess.py raw.glb tidy.glb
#   python ../examiner-m/alignrig.py ../../Gadfly.Server/wwwroot/assets/avatars/mpfb.glb tidy.glb aligned.glb
#   python ../examiner-socrates/trim.py aligned.glb examiner-bust.glb
# With still.png it also renders front and three-quarter stills, and the mouth open (viseme_aa) with the eyes shut, in a stand-in
# for avatar-look.js's blue look.
import sys, os
import bpy, bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import head

args = sys.argv[sys.argv.index("--") + 1:]
examiner, bust_glb, out = args[:3]
still = args[3] if len(args) > 3 else None

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

arm, parts, bparts, marks = head.build(examiner, bust_glb)
eye = (marks["eyes"][0] + marks["eyes"][1]) / 2

# No body below the bust: his suit goes, and the MPFB body is cut a little under the jaw (inside the bust's neck from there on).
bpy.data.objects.remove(parts.pop("male_casualsuit06"), do_unlink=True)
# TalkingHead lowers the hips every frame until the model's lowest point is on the floor (its "hip-feet balance"), which would
# sink a bust out of frame. A speck of the plinth (a triangle a tenth of a millimetre across, too small to see) stays where his
# left sole was, on the toe bone, so the model still reaches the floor as a full body does and the bust stays at eye level.
sole = min((parts["body"].matrix_world @ v.co for v in parts["body"].data.vertices if v.co.x > 0), key=lambda p: p.z)
plinth = bparts["plinth"]
bm = bmesh.new(); bm.from_mesh(plinth.data)
speck = [bm.verts.new(plinth.matrix_world.inverted() @ (sole + Vector(d))) for d in ((0, 0, 0), (0.0001, 0, 0), (0, 0.0001, 0))]
bm.faces.new(speck)
bm.to_mesh(plinth.data); bm.free()
toe = plinth.vertex_groups.get("LeftToeBase") or plinth.vertex_groups.new(name="LeftToeBase")
ids = list(range(len(plinth.data.vertices) - 3, len(plinth.data.vertices)))
toe.add(ids, 1.0, "REPLACE"); plinth.vertex_groups[head.BASE_BONE].remove(ids)
head.cut(parts["body"], lambda p: p.z < eye.z - 0.15)

for im in bpy.data.images:
    if im.size[0] > 1024:
        im.scale(1024, 1024)

keep = [arm] + list(parts.values()) + list(bparts.values())
head.export(out, keep)
print("EYE LEVEL", round(eye.z, 4), "(examiner-socrates: 1.68)")

if still:
    head.stills(arm, parts, bparts, keep, eye, still)
