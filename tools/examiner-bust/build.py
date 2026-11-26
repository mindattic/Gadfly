# Builds the talking bust (GAD-T792, Gadfly.Server/wwwroot/assets/avatars/examiner-bust.glb) in headless Blender: head.py's
# reshaped head (Socrates' MPFB face rig given the Meshy.ai bust's face, hair and beard) on the bust's own bare shoulders and
# plinth, rigid, with no body below. The eyes stay at Socrates' height, so TalkingHead's camera meets them level.
#   blender -b --python-exit-code 1 --python build.py -- examiner-socrates.glb Socrates_Burt.glb raw.glb [still.png]
#   python ../examiner-m/postprocess.py raw.glb tidy.glb
#   python ../examiner-m/alignrig.py ../../Gadfly.Server/wwwroot/assets/avatars/mpfb.glb tidy.glb aligned.glb
#   python ../examiner-socrates/trim.py aligned.glb examiner-bust.glb
# With still.png it also renders front and three-quarter stills, and the mouth open (viseme_aa) with the eyes shut, in a stand-in
# for avatar-look.js's blue look.
import sys, os, math
import bpy
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
head.cut(parts["body"], lambda p: p.z < eye.z - 0.15)
head.rig(bparts["torso"], arm, lambda p: {"Spine2": 1.0})

for im in bpy.data.images:
    if im.size[0] > 1024:
        im.scale(1024, 1024)

keep = [arm] + list(parts.values()) + list(bparts.values())
head.export(out, keep)
print("EYE LEVEL", round(eye.z, 4), "(examiner-socrates: 1.68)")

if still:
    # A stand-in for the app's look: avatar-look.js's palette, with a soft glow, and the brown irises turned green.
    hexrgb = lambda h, k=1.0: tuple(((h >> s) & 255) / 255 * k for s in (16, 8, 0)) + (1,)
    look = {"skin": 0x2779ea, "hair": 0x142f7a, "beard": 0x142f7a, "torso": 0x1c4bb0, "body": 0x2779ea,
            "eyebrow001": 0x0b1736, "eyelashes01": 0x0b1736, "teeth_base": 0xeef3ff, "tongue01": 0x8a4f73}
    for k, o in list(parts.items()) + list(bparts.items()):
        if k == "high-poly":
            for m in o.data.materials:
                nt = m.node_tree; tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None)
                bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
                if tex:
                    hs = nt.nodes.new("ShaderNodeHueSaturation"); hs.inputs["Hue"].default_value = 0.83; hs.inputs["Saturation"].default_value = 1.6
                    nt.links.new(tex.outputs["Color"], hs.inputs["Color"]); nt.links.new(hs.outputs["Color"], bsdf.inputs["Base Color"])
                    nt.links.new(hs.outputs["Color"], bsdf.inputs["Emission Color"]); bsdf.inputs["Emission Strength"].default_value = 0.25
                bsdf.inputs["Roughness"].default_value = 0.08
            continue
        if k not in look: continue
        for m in o.data.materials:
            bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            for l in list(m.node_tree.links):
                if l.to_node == bsdf and l.to_socket.name == "Base Color": m.node_tree.links.remove(l)
            bsdf.inputs["Base Color"].default_value = hexrgb(look[k])
            if k in ("skin", "body", "hair", "beard", "torso"):
                bsdf.inputs["Emission Color"].default_value = hexrgb(0x1f6dff); bsdf.inputs["Emission Strength"].default_value = 0.2
    centre = eye + Vector((0, 0, -0.1))
    sc = bpy.context.scene
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam)
    cam.data.lens = 85
    for name, loc, energy in (("Key", (-0.6, -1.0, 0.5), 60), ("Fill", (0.7, -0.8, 0.1), 25), ("Rim", (0, 0.8, 0.6), 30)):
        lamp = bpy.data.objects.new(name, bpy.data.lights.new(name, "POINT")); lamp.data.energy = energy
        lamp.location = centre + Vector(loc); sc.collection.objects.link(lamp)
    sc.camera = cam
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x, sc.render.resolution_y = 600, 750
    base, ext = os.path.splitext(still)
    def shapes(values):
        for o in keep:
            if o.type == "MESH" and o.data.shape_keys:
                for kb in o.data.shape_keys.key_blocks[1:]:
                    kb.value = values.get(kb.name, 0.0)
    for suffix, yaw, values in (("-front", 0.0, {}), ("-34", 0.6, {}),
                                ("-viseme-aa-blink", 0.0, {"viseme_aa": 1.0, "jawOpen": 0.3, "eyeBlinkLeft": 1.0, "eyeBlinkRight": 1.0}),
                                ("-viseme-o-look", 0.3, {"viseme_O": 1.0, "eyeLookOutLeft": 0.6, "eyeLookInRight": 0.6})):
        shapes(values)
        cam.location = centre + Vector((-1.6 * math.sin(yaw), -1.6 * math.cos(yaw), 0))
        cam.rotation_euler = (math.pi / 2, 0, -yaw)
        sc.render.filepath = base + suffix + ext
        bpy.ops.render.render(write_still=True)
        print("RENDERED", sc.render.filepath)
    cam.data.lens = 50
    cam.location = Vector((0, -2.2, eye.z - 0.25)); cam.rotation_euler = (math.pi / 2, 0, 0)
    shapes({}); sc.render.filepath = base + "-full" + ext
    bpy.ops.render.render(write_still=True)
