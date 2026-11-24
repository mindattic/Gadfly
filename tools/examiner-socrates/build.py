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
import bpy
from mathutils import Vector

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
# Age 0.85 is about seventy (MakeHuman: 0.5 is 25, 1.0 is 90); short and stocky.
info["phenotype"].update({"gender": 1.0, "age": 0.85, "muscle": 0.45, "weight": 0.62, "proportions": 0.5, "height": 0.4})
info["rig"] = "mixamo"
info["eyes"] = "high-poly/high-poly.mhclo"
info["eyebrows"] = "eyebrow001/eyebrow001.mhclo"
info["eyelashes"] = "eyelashes01/eyelashes01.mhclo"
info["teeth"] = "teeth_base/teeth_base.mhclo"
info["tongue"] = "tongue01/tongue01.mhclo"
info["hair"] = ""
info["clothes"] = ["male_casualsuit06/male_casualsuit06.mhclo", "rehmanpolanski_beard_viking/rehmanpolanski_beard_viking.mhclo",
                   "rehmanpolanski_moustache_viking/rehmanpolanski_moustache_viking.mhclo"]
info["skin_mhmat"] = "old_caucasian_male/old_caucasian_male.mhmat"
info["skin_material_type"] = "MAKESKIN"
info["eyes_material_type"] = "MAKESKIN"
info["clothes_material_type"] = "MAKESKIN"
face = {
    # The snub nose: broad, flat, short and turned up, with flared nostrils.
    "nose-scale-horiz-incr": 0.6, "nose-flaring-incr": 0.7, "nose-nostrils-width-incr": 0.5, "nose-point-width-incr": 0.6,
    "nose-point-up": 0.7, "nose-scale-vert-decr": 0.3, "nose-scale-depth-decr": 0.25, "nose-hump-decr": 0.4,
    "nose-curve-concave": 0.4, "nose-volume-incr": 0.3,
    # A wide, round face under a high, domed forehead.
    "head-scale-horiz-incr": 0.45, "head-round": 0.5, "head-fat-incr": 0.2,
    "forehead-scale-vert-incr": 0.2, "forehead-nubian-incr": 0.3, "forehead-temple-incr": 0.2,
}
for s in ("l", "r"):
    # Full cheeks; prominent, wide-set eyes with an open upper lid and an old man's bags.
    face.update({f"{s}-cheek-bones-incr": 0.3, f"{s}-cheek-volume-incr": 0.3, f"{s}-eye-scale-incr": 0.25,
                 f"{s}-eye-eyefold-up": 0.3, f"{s}-eye-bag-incr": 0.5, f"{s}-eye-trans-out": 0.2})
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

# Lighter textures: at most 1024 px (the look keeps only the skin's light and shade and drops the clothing color).
for im in bpy.data.images:
    if im.size[0] > 1024:
        im.scale(1024, 1024)
        print("SCALED", im.name)

bpy.ops.object.select_all(action="DESELECT")
for o in keep: o.select_set(True)
bpy.ops.export_scene.gltf(
    filepath=out, export_format="GLB", use_selection=True,
    export_apply=False, export_skins=True, export_morph=True, export_morph_normal=True, export_morph_tangent=False,
    export_animations=False, export_yup=True, export_texcoords=True, export_normals=True, export_tangents=False,
    export_image_format="AUTO", export_extras=False, export_def_bones=False)
print("WROTE", out, os.path.getsize(out))

if still:
    head = (arm.matrix_world @ arm.data.bones["Head"].head_local)
    centre = head + Vector((0, 0, 0.09))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); bpy.context.scene.collection.objects.link(cam)
    cam.data.lens = 85
    cam.location = centre + Vector((0, -1.1, 0))
    cam.rotation_euler = (math.pi / 2, 0, 0)
    for name, loc, energy in (("Key", (-0.6, -1.0, 0.5), 60), ("Fill", (0.7, -0.8, 0.1), 25), ("Rim", (0, 0.8, 0.6), 30)):
        lamp = bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT")); lamp.data.energy = energy
        lamp.location = centre + Vector(loc); bpy.context.scene.collection.objects.link(lamp)
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x, sc.render.resolution_y = 600, 750
    sc.render.filepath = still
    bpy.ops.render.render(write_still=True)
    print("RENDERED", still)
