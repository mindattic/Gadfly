# Builds Socrates (GAD-T724, Gadfly.Server/wwwroot/assets/avatars/examiner-socrates.glb) with MPFB in headless Blender: the
# male examiner's recipe (tools/examiner-m/build.py) with an old man's phenotype and skin, the face shaped after the Vatican
# Pio-Clementino bust (broad flat upturned nose, wide round face, prominent eyes, high forehead), bald, and the CC0 viking beard
# and moustache from the bodyparts05 pack as proxies, so the ARKit and viseme shapes are interpolated onto them too.
#
# Needs what tools/examiner-m needs (Blender 4.2+, MPFB 2.0.18, the CC0 system assets, faceunits01, visemes02) plus the CC0
# bodyparts05 pack (files.makehumancommunity.org/asset_packs/bodyparts05/bodyparts05_cc0.zip) in MPFB's user data folder. Then:
#   blender -b --python build.py -- raw.glb [front.png]
#   python ../examiner-m/postprocess.py raw.glb tidy.glb
#   python ../examiner-m/alignrig.py ../../Gadfly.Server/wwwroot/assets/avatars/mpfb.glb tidy.glb examiner-socrates.glb
# With front.png it also renders a front still of the head for judging the likeness.
import sys, os, math
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

out = sys.argv[sys.argv.index("--") + 1]
still = sys.argv[sys.argv.index("--") + 2] if len(sys.argv) > sys.argv.index("--") + 2 else None

from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.exportservice import ExportService
from bl_ext.user_default.mpfb.services.targetservice import TargetService
from bl_ext.user_default.mpfb.services.faceservice import FaceService
from bl_ext.user_default.mpfb.services.objectservice import ObjectService
from bl_ext.user_default.mpfb.services.locationservice import LocationService

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

info = HumanService._create_default_human_info_dict()
# Age 0.8 is about sixty-five (MakeHuman: 0.5 is 25, 1.0 is 90); stocky. TalkingHead frames every examiner from one fixed camera
# distance and its pose puts the hips at 1 m, so his height is set for examiner-m's eye level (1.67 m): a shorter man sat low in
# the frame and was looked down on.
info["phenotype"].update({"gender": 1.0, "age": 0.8, "muscle": 0.45, "weight": 0.62, "proportions": 0.6,
                          "height": float(os.environ.get("SOCRATES_HEIGHT", "0.575"))})
info["rig"] = "mixamo"
info["eyes"] = "high-poly/high-poly.mhclo"
info["eyebrows"] = "eyebrow001/eyebrow001.mhclo"
info["eyelashes"] = "eyelashes01/eyelashes01.mhclo"
info["teeth"] = "teeth_base/teeth_base.mhclo"
info["tongue"] = "tongue01/tongue01.mhclo"
# The afro's curls, cut to a fringe round the sides and back below a bald crown (below). The CC0 viking beard and moustache
# (the only CC0 beards in bodyparts05 with full cheek-to-chin coverage; grinsegold_beard_sigmund_wip is a nicer solid shell
# but is AGPL3, not CC0, so it can't ship here) have the beard's long hanging drape shortened below (see the note there).
info["hair"] = "afro01/afro01.mhclo"
info["clothes"] = ["male_casualsuit06/male_casualsuit06.mhclo", "rehmanpolanski_beard_viking/rehmanpolanski_beard_viking.mhclo",
                   "rehmanpolanski_moustache_viking/rehmanpolanski_moustache_viking.mhclo"]
info["skin_mhmat"] = "old_caucasian_male/old_caucasian_male.mhmat"
info["skin_material_type"] = "MAKESKIN"
info["eyes_material_type"] = "MAKESKIN"
info["clothes_material_type"] = "MAKESKIN"
face = {
    # The snub nose: broad, flat, short and turned up, with flared nostrils.
    "nose-scale-horiz-incr": 0.9, "nose-flaring-incr": 0.9, "nose-nostrils-width-incr": 0.8, "nose-point-width-incr": 0.9,
    "nose-point-up": 0.9, "nose-scale-vert-decr": 0.5, "nose-scale-depth-decr": 0.4, "nose-hump-decr": 0.6,
    "nose-curve-concave": 0.6, "nose-volume-incr": 0.4, "nose-nostrils-angle-up": 0.5, "nose-greek-decr": 0.4,
    # A wide, round face under a high, domed bald head, and a heavy brow.
    "head-scale-horiz-incr": 0.45, "head-round": 0.5, "head-fat-incr": 0.2, "head-scale-vert-incr": 0.2,
    "forehead-scale-vert-incr": 0.35, "forehead-nubian-incr": 0.5, "forehead-temple-incr": 0.2,
    "eyebrows-trans-forward": 0.6,
}
for s in ("l", "r"):
    # Wide cheekbones; prominent, slightly bulging, wide-set eyes, open and alert: the upper lid raised well clear of the
    # iris, no eye bags, and no hooded overhang from the brow.
    face.update({f"{s}-cheek-bones-incr": 0.6, f"{s}-cheek-volume-incr": 0.3, f"{s}-eye-scale-incr": 0.35,
                 f"{s}-eye-push1-out": 0.4, f"{s}-eye-push2-out": 0.3, f"{s}-eye-eyefold-up": 0.8,
                 f"{s}-eye-height2-incr": 0.6, f"{s}-eye-bag-decr": 0.5, f"{s}-eye-trans-out": 0.2})
info["targets"] = [{"target": t, "value": v} for t, v in face.items()]
settings = HumanService.get_default_deserialization_settings()
settings["subdiv_levels"] = 0
basemesh = HumanService.deserialize_from_dict(info, settings)
print("BASEMESH", basemesh.name)

# Export copy, as MPFB's "Create export copy" does with: bake shapekeys, ARKit faceunits, Meta visemes, interpolate, bake masks.
coll = bpy.data.collections.new("export copy"); bpy.context.scene.collection.children.link(coll)
copy_root = ExportService.create_character_copy(basemesh, name_suffix="", place_in_collection=coll)
nb = ObjectService.find_object_of_type_amongst_nearest_relatives(copy_root)
TargetService.bake_targets(nb)
FaceService.load_targets(nb, load_microsoft_visemes=False, load_meta_visemes=True, load_arkit_faceunits=True)
FaceService.interpolate_targets(nb)
ExportService.bake_modifiers_remove_helpers(nb, bake_masks=True, bake_subdiv=False, remove_helpers=True, also_proxy=True)

# Drop the original character; keep only the export copy.
keep = set(coll.all_objects)
for o in list(bpy.data.objects):
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)

arm = next(o for o in keep if o.type == "ARMATURE")
arm.name = "Armature"; arm.data.name = "Armature"
meshes = [o for o in keep if o.type == "MESH"]
for o in meshes:
    print("MESH", o.name, len(o.data.vertices), o.data.shape_keys and len(o.data.shape_keys.key_blocks), [m.name for m in o.data.materials])

# Bone names like the female (no mixamorig: prefix).
for b in arm.data.bones:
    if b.name.startswith("mixamorig:"):
        b.name = b.name[len("mixamorig:"):]

# Mesh names like the female: body "Human", parts "Human.<asset>".
eyes = None
for o in meshes:
    n = o.name.lower()
    if o is nb:
        o.name = "Human"; o.data.name = "base"
    if "high-poly" in n or "eyes" in n:
        eyes = o

# LeftEye / RightEye bones (TalkingHead and the exam room's gaze read their positions), children of Head at each eyeball's
# centre, and the eyeballs weighted to them.
mw = eyes.matrix_world
verts = [mw @ v.co for v in eyes.data.vertices]
left = [v for v in verts if v.x > 0]; right = [v for v in verts if v.x < 0]
cl = sum(left, Vector()) / len(left); cr = sum(right, Vector()) / len(right)
bpy.context.view_layer.objects.active = arm
for o in bpy.context.selected_objects: o.select_set(False)
arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
ami = arm.matrix_world.inverted()
for name, c in (("LeftEye", cl), ("RightEye", cr)):
    b = eb.new(name)
    b.head = ami @ c
    b.tail = ami @ (c + Vector((0, -0.02, 0)))   # pointing forward (-Y in Blender is the character's front)
    b.parent = eb["Head"]
    b.use_deform = True
bpy.ops.object.mode_set(mode="OBJECT")
for vg in list(eyes.vertex_groups):
    eyes.vertex_groups.remove(vg)
gl = eyes.vertex_groups.new(name="LeftEye"); gr = eyes.vertex_groups.new(name="RightEye")
for v in eyes.data.vertices:
    ((gl if (mw @ v.co).x > 0 else gr)).add([v.index], 1.0, "REPLACE")

# The eyes take the brown MakeHuman eye texture (the same sheet as the female's, so avatar-look.js finds the irises).
brown = LocationService.get_user_data("eyes/materials/brown_eye.png")
img = bpy.data.images.load(brown)
for m in eyes.data.materials:
    for node in m.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None and "normal" not in node.image.name.lower():
            print("EYE TEX", node.image.name, "->", img.name)
            node.image = img

print("EYE LEVEL", round(cl.z, 4), "(examiner-m: 1.6693)")

def reshape(o, move):
    """Moves each vertex of o by move(world basis position), the same offset in every shape key, so the mouth shapes still open it."""
    me = o.data; mw = o.matrix_world; mwi = mw.inverted_safe().to_3x3()
    basis = [mw @ v.co for v in me.vertices]
    deltas = [mwi @ move(p) for p in basis]
    for kb in me.shape_keys.key_blocks:
        for i, d in enumerate(deltas):
            kb.data[i].co += d
    for i, d in enumerate(deltas):
        me.vertices[i].co += d

def cut(o, drop):
    """Deletes the vertices of o whose world basis position drop() selects."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    mw = o.matrix_world
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if drop(mw @ v.co)], context="VERTS")
    bm.to_mesh(o.data); bm.free(); o.data.update()

eye = (cl + cr) / 2
bvh = BVHTree.FromObject(nb, bpy.context.evaluated_depsgraph_get())
for o in meshes:
    n = o.name.lower()
    if "afro" in n:
        # A bald crown and forehead; the curls kept round the sides and back from the temples down, pulled in close to the
        # scalp so they read as a tight fringe rather than an afro.
        crown = eye.z + float(os.environ.get("SOCRATES_CROWN", "0.055"))
        cut(o, lambda p: p.z > crown + 0.03 * max(0.0, p.y - eye.y - 0.08) / 0.1 or (p.y < eye.y - 0.03 and p.z > eye.z - 0.01)
            or p.z < eye.z - 0.15)
        def hug(p):
            q = bvh.find_nearest(p)[0]
            return (q - p) * 0.55 if q is not None else Vector()
        reshape(o, hug)
    # The beard is strand/card geometry (hair cards), not a solid shell: pushing their vertices outward to try to "fill
    # them out" just splays and lengthens individual strands into a scraggly hang (tried and reverted; see
    # tools/examiner-socrates/build.py history for GAD-T730's render and the note left on GAD-T731). No other CC0 beard
    # in bodyparts05 has full cheek-to-chin coverage (culturalibre_faun_beard is a thin goatee; grinsegold_beard_sigmund_wip
    # and wdg_scruffy_beard are both AGPL3, confirmed from their own .mhclo license fields, not just the pack index), so
    # GAD-T731 instead shortens the drape: vertices below an assumed chin line (measured down from eye level, not from
    # the mesh's own highest vertex - a stray sideburn wisp reaches almost to the cheekbone, so using the mesh's own
    # bounding box as the anchor would compress the whole beard, not just the drape) are pulled up toward that line,
    # proportionally to how far below it they are. Cheek and jaw strands, at or above the line, are left alone; only
    # the long chin-tip strands get pulled into a shorter, denser, rounder hang.
    if "beard" in n:
        chin = eye.z - 0.12
        def shorten(p):
            drop = chin - p.z
            return Vector((0, 0, drop * 0.5)) if drop > 0 else Vector()
        reshape(o, shorten)

# Lighter textures: at most 1024 px (the look keeps only the skin's light and shade and drops the clothing color); the hair
# and beard alpha-cutout masks only need to read at a distance, so they are capped tighter, at 512, to keep the file near
# examiner-m's size despite the extra hair and moustache meshes bald Socrates didn't have.
for im in bpy.data.images:
    cap = 512 if any(s in im.name.lower() for s in ("afro", "beard", "moustache")) else 1024
    if im.size[0] > cap:
        im.scale(cap, cap)
        print("SCALED", im.name, cap)

bpy.ops.object.select_all(action="DESELECT")
for o in keep: o.select_set(True)
bpy.ops.export_scene.gltf(
    filepath=out, export_format="GLB", use_selection=True,
    export_apply=False, export_skins=True, export_morph=True, export_morph_normal=True, export_morph_tangent=False,
    export_animations=False, export_yup=True, export_texcoords=True, export_normals=True, export_tangents=False,
    export_image_format="AUTO", export_extras=False, export_def_bones=False)
print("WROTE", out, os.path.getsize(out))

if still:
    # Head and shoulders, front and three-quarter.
    centre = eye + Vector((0, 0, -0.08))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); bpy.context.scene.collection.objects.link(cam)
    cam.data.lens = 85
    for name, loc, energy in (("Key", (-0.6, -1.0, 0.5), 60), ("Fill", (0.7, -0.8, 0.1), 25), ("Rim", (0, 0.8, 0.6), 30)):
        lamp = bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT")); lamp.data.energy = energy
        lamp.location = centre + Vector(loc); bpy.context.scene.collection.objects.link(lamp)
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x, sc.render.resolution_y = 600, 750
    base, ext = os.path.splitext(still)
    for suffix, yaw in (("", 0.0), ("-34", 0.6)):
        cam.location = centre + Vector((-1.6 * math.sin(yaw), -1.6 * math.cos(yaw), 0))
        cam.rotation_euler = (math.pi / 2, 0, -yaw)
        sc.render.filepath = base + suffix + ext
        bpy.ops.render.render(write_still=True)
        print("RENDERED", sc.render.filepath)
