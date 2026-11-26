# Builds the bust's head on Socrates' own body (GAD-T793, Gadfly.Server/wwwroot/assets/avatars/examiner-bust-body.glb) in headless
# Blender: head.py's reshaped head (his MPFB face rig given the Meshy.ai bust's face, hair and beard), stopping at the neck, on
# examiner-socrates's full body, dressed in a himation (toga(), GAD-T800). Nothing moves, so his eyes stay at his own height and
# TalkingHead frames him as Socrates.
#   blender -b --python-exit-code 1 --python body.py -- examiner-socrates.glb Socrates_Burt.glb raw.glb [still.png]
#   python ../examiner-m/postprocess.py raw.glb tidy.glb
#   python ../examiner-m/alignrig.py ../../Gadfly.Server/wwwroot/assets/avatars/mpfb.glb tidy.glb aligned.glb
#   python ../examiner-socrates/trim.py aligned.glb examiner-bust-body.glb
import sys, os, math
import bpy, bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import head

args = sys.argv[sys.argv.index("--") + 1:]
examiner, bust_glb, out = args[:3]
still = args[3] if len(args) > 3 else None

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

arm, parts, bparts, marks = head.build(examiner, bust_glb, on_plinth=False)
eye = (marks["eyes"][0] + marks["eyes"][1]) / 2


def toga(arm, suit):
    """A himation (GAD-T800) modelled here, as no CC0 MakeHuman toga exists: a broad band of cloth over the left shoulder, running
    down across the chest and back to the right hip, over a long skirt that falls to the ankles. MakeHuman deletes the body under
    his clothes, so the band is his shirt's own surface lifted off it (with its weights, so it moves as he does) with folds running
    down it, and the shirt stays under it as the chiton. The skirt is a cone hung on the hips, over his trousers. Named Toga, so
    avatar-look.js styles it as clothing."""
    bone = lambda n: arm.matrix_world @ arm.data.bones[n].head_local
    shoulder, hips, ankle = bone("LeftArm"), bone("LeftUpLeg"), bone("LeftFoot")
    o = suit.copy(); o.data = suit.data.copy(); o.name = o.data.name = "Toga"
    bpy.context.scene.collection.objects.link(o)
    o.shape_key_clear()
    mw = o.matrix_world; mwi = mw.inverted_safe()
    skirt_top = hips.z + 0.06
    a = Vector((shoulder.x + 0.03, shoulder.z + 0.08)); b = Vector((-shoulder.x - 0.02, skirt_top - 0.02))
    along = (b - a).normalized(); across = Vector((-along.y, along.x))
    def worn(v):
        p = mw @ v.co
        q = Vector((p.x, p.z)) - a
        return abs(q.dot(across)) < 0.11 and -0.06 < q.dot(along) < (b - a).length + 0.04 and p.z > skirt_top - 0.06
    keep = {v.index for v in o.data.vertices if worn(v)}
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(v.index in keep for v in f.verts)], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    head._tidy_edges(bm, min_faces=20, passes=10)
    bm.normal_update()
    for v in bm.verts:
        p = mw @ v.co
        u = (Vector((p.x, p.z)) - a).dot(across) / 0.045
        v.co = v.co + v.normal * (0.014 + 0.005 * math.sin(u * 2 * math.pi))
    bm.to_mesh(o.data); bm.free()
    # The chiton under it falls in the same diagonal folds, softer.
    bm = bmesh.new(); bm.from_mesh(suit.data); bm.normal_update()
    smw = suit.matrix_world
    for v in bm.verts:
        p = smw @ v.co
        if p.z > skirt_top - 0.1:
            v.co = v.co + v.normal * 0.004 * math.sin((Vector((p.x, p.z)) - a).dot(across) / 0.06 * 2 * math.pi)
    bm.to_mesh(suit.data); bm.free()
    # The skirt: rings from the hips to the ankles, as wide as his trousers at the hips plus the cloth, with vertical folds.
    ws = [suit.matrix_world @ v.co for v in suit.data.vertices]
    def extent(z):
        near = [p for p in ws if abs(p.z - z) < 0.02 and abs(p.x) < 0.22]
        return max(abs(p.x) for p in near), min(p.y for p in near), max(p.y for p in near)
    bottom = ankle.z + 0.07
    rings, n = 14, 48
    rx, y0, y1 = extent(skirt_top)
    bm = bmesh.new(); uv = bm.loops.layers.uv.new(o.data.uv_layers[0].name if o.data.uv_layers else "UVMap")
    grid = []
    for r in range(rings + 1):
        z = skirt_top + (bottom - skirt_top) * r / rings
        flare = 0.025 + 0.06 * r / rings
        ring = []
        for k in range(n):
            t = 2 * math.pi * k / n
            fold = 0.01 * math.sin(t * 9) * r / rings
            ring.append(bm.verts.new(mwi @ Vector(((rx + flare + fold) * math.cos(t), (y0 + y1) / 2 + ((y1 - y0) / 2 + flare + fold) * math.sin(t), z))))
        grid.append(ring)
    for r in range(rings):
        for k in range(n):
            f = bm.faces.new((grid[r][k], grid[r][(k + 1) % n], grid[r + 1][(k + 1) % n], grid[r + 1][k]))
            for l, (rr, kk) in zip(f.loops, ((r, k), (r, k + 1), (r + 1, k + 1), (r + 1, k))):
                l[uv].uv = (kk / n, 1 - rr / rings)
    skirt_mesh = bpy.data.meshes.new("Toga.skirt"); bm.to_mesh(skirt_mesh); bm.free()
    s = bpy.data.objects.new("Toga.skirt", skirt_mesh); bpy.context.scene.collection.objects.link(s)
    head.rig(s, arm, lambda i, p: {"Hips": 1.0})
    # One object: the skirt joined onto the upper drape (the upper part keeps its own weights; the skirt hangs on the hips).
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True); s.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.join()
    o.data.materials.clear(); o.data.materials.append(head._material("Toga"))
    for poly in o.data.polygons: poly.material_index = 0
    print("TOGA", len(o.data.vertices), "verts")
    return o


parts["toga"] = toga(arm, parts["male_casualsuit06"])

for im in bpy.data.images:
    if im.size[0] > 1024:
        im.scale(1024, 1024)

keep = [arm] + list(parts.values()) + list(bparts.values())
head.export(out, keep)
print("EYE LEVEL", round(eye.z, 4), "(examiner-socrates: 1.68)")

if still:
    head.stills(arm, parts, bparts, keep, eye, still)
