# Builds the male examiner (GAD-T504, Gadfly.Server/wwwroot/assets/avatars/examiner-m.glb) with MPFB in headless Blender.
# The recipe of TalkingHead's CC0 mpfb.glb (the female examiner): an MPFB human, the Mixamo rig plus LeftEye/RightEye bones, the
# ARKit faceunits01 and Meta visemes02 shape keys interpolated onto the body parts, helpers removed and the body under the
# clothes baked away. Then postprocess.py (alpha modes, names, JPEG textures) and alignrig.py (the female's bone frames).
#
# Needs Blender 4.2+ with the MPFB 2.0.18 extension, and in MPFB's user data folder the CC0 MakeHuman system assets
# (makehuman_system_assets_cc0.zip) and the CC0 faceunits01 and visemes02 packs (files.makehumancommunity.org). Then:
#   blender -b --python build.py -- raw.glb [male_casualsuit06]
#   python postprocess.py raw.glb tidy.glb
#   python alignrig.py <MindAttic.Avatar>/web/assets/avatars/mpfb.glb tidy.glb examiner-m.glb
# The defaults are the shipped choices; MALE_LASHES (eyelashes01), MALE_LID (0.4: upper eyelid fold raised, eyes not enlarged)
# and MALE_MASK (1: delete the body under the clothes) were for trying variants.
import sys, os, math
import bpy
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
suit = sys.argv[sys.argv.index("--") + 2] if len(sys.argv) > sys.argv.index("--") + 2 else "male_casualsuit06"

from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.exportservice import ExportService
from bl_ext.user_default.mpfb.services.targetservice import TargetService
from bl_ext.user_default.mpfb.services.faceservice import FaceService
from bl_ext.user_default.mpfb.services.objectservice import ObjectService
from bl_ext.user_default.mpfb.services.locationservice import LocationService

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

info = HumanService._create_default_human_info_dict()
info["phenotype"].update({"gender": 1.0, "age": 0.5, "muscle": 0.55, "weight": 0.5, "proportions": 0.6, "height": 0.55})
info["rig"] = "mixamo"
info["eyes"] = "high-poly/high-poly.mhclo"
info["eyebrows"] = "eyebrow001/eyebrow001.mhclo"
info["eyelashes"] = os.environ.get("MALE_LASHES", "eyelashes01") + "/" + os.environ.get("MALE_LASHES", "eyelashes01") + ".mhclo"
info["teeth"] = "teeth_base/teeth_base.mhclo"
info["tongue"] = "tongue01/tongue01.mhclo"
info["hair"] = "short02/short02.mhclo"
info["clothes"] = [f"{suit}/{suit}.mhclo"]
info["skin_mhmat"] = "young_caucasian_male/young_caucasian_male.mhmat"
info["skin_material_type"] = "MAKESKIN"
info["eyes_material_type"] = "MAKESKIN"
info["clothes_material_type"] = "MAKESKIN"
# Eyelids a touch more open (the upper fold raised), so his eyes read as attentive rather than hooded; the eyeballs keep their size.
lift = float(os.environ.get("MALE_LID", "0.4"))
info["targets"] = [{"target": f"{s}-eye-eyefold-up", "value": lift} for s in ("l", "r")] if lift > 0 else []
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
ExportService.bake_modifiers_remove_helpers(nb, bake_masks=os.environ.get("MALE_MASK", "1") == "1", bake_subdiv=False, remove_helpers=True, also_proxy=True)

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
