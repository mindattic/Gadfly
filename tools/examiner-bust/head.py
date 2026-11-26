# The reusable head step (GAD-T792): Socrates' working MPFB face rig (examiner-socrates.glb: 52 ARKit + 15 viseme morph targets,
# eyes, teeth, tongue, the Mixamo-style rig) given the Meshy.ai bust's face, skull, sculpted hair and beard. Imported by
# build.py here (the bust on its plinth) and meant for any other examiner built on the same head (GAD-T793: the head on Socrates'
# body and suit). Runs inside headless Blender:
#   import head; parts = head.build(examiner_glb, bust_glb)
# Every position here is in the examiner's space (metres, Z up, -Y is the face's front); the bust is scaled and moved onto it.
import os
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

# Landmarks read off orthographic renders of Socrates_Burt.glb as Blender imports it (tools/examiner-bust/README.md): the
# middle of the two carved eyes, and the size the bust is scaled by so its face fits the MPFB head (between its eye spacing,
# 0.068/0.219, and its eye-to-mouth height, 0.080/0.203: the bust's eyes are set wide, and the MPFB eyes stay where they are).
BUST_EYES = Vector((-0.009, 0.0, 0.525))
BUST_SCALE = float(os.environ.get("BUST_SCALE", "0.35"))
# The bust's chest begins under the beard at this height (bust units); below it are the bare shoulders and the plinth.
BUST_SHOULDERS_Z = 0.05
# The plinth's top (bust units): below it the plinth, its own mesh.
BUST_PLINTH_Z = -0.62
# How far above the eyes the MPFB face reaches into the bust's bald crown (metres), and how many passes smooth its fit.
FOREHEAD = 0.09
SMOOTHING = int(os.environ.get("BUST_SMOOTHING", "12"))
# How far the bust's surface must stand out from the MPFB face to count as beard or moustache (metres).
PROUD = float(os.environ.get("BUST_PROUD", "0.004"))
# The root bone the bust's shoulders and plinth hang on.
BASE_BONE = "BustBase"
# The beard's tip, this far below the eyes (metres, after scaling); the chest below it is skin.
BEARD_DROP = 0.26


def _world(o):
    return [o.matrix_world @ v.co for v in o.data.vertices]


def _bvh(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    t = BVHTree.FromBMesh(bm); bm.free()
    return t


def _smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def reshape(o, move):
    """Moves each vertex of o by move(index, world basis position), the same offset in every shape key, so every morph still applies."""
    me = o.data; mwi = o.matrix_world.inverted_safe().to_3x3()
    deltas = [mwi @ move(i, p) for i, p in enumerate(_world(o))]
    for kb in (me.shape_keys.key_blocks if me.shape_keys else []):
        for i, d in enumerate(deltas):
            kb.data[i].co += d
    for i, d in enumerate(deltas):
        me.vertices[i].co += d


def cut(o, drop):
    """Deletes the vertices of o whose world basis position drop() selects (shape keys kept on the rest)."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    mw = o.matrix_world
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if drop(mw @ v.co)], context="VERTS")
    bm.to_mesh(o.data); bm.free(); o.data.update()


def load_examiner(path):
    """Imports examiner-socrates.glb: returns the armature and its meshes by MPFB asset ("body", "high-poly", "teeth_base", ...)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = set(bpy.data.objects) - before
    arm = next(o for o in new if o.type == "ARMATURE")
    parts = {}
    for o in new:
        if o.type != "MESH": continue
        if o.parent is not arm:
            bpy.data.objects.remove(o, do_unlink=True); continue   # a stray importer helper (Icosphere), not part of him
        parts["body" if o.name == "Human" else o.name.split(".", 1)[1]] = o
    return arm, parts


def eye_centres(arm):
    return [arm.matrix_world @ arm.data.bones[n].head_local for n in ("LeftEye", "RightEye")]


def load_bust(path, human, eyes):
    """Imports the bust, lightens it (decimate) and moves it onto the examiner: eyes on his eyes, the face's depth on his."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    bust = next(o for o in set(bpy.data.objects) - before if o.type == "MESH")
    bust.parent = None
    bpy.context.view_layer.objects.active = bust
    dec = bust.modifiers.new("Decimate", "DECIMATE"); dec.ratio = float(os.environ.get("BUST_DECIMATE", "0.07"))
    bpy.ops.object.modifier_apply(modifier=dec.name)
    eye = (eyes[0] + eyes[1]) / 2
    me = bust.data; mw = bust.matrix_world
    for v in me.vertices:
        p = mw @ v.co
        v.co = (p - BUST_EYES) * BUST_SCALE + Vector((eye.x, 0, eye.z))
    bust.matrix_world.identity()
    # Depth: the bridge of the nose between the eyes on both, found by casting a ray from the front.
    hb, bb = _bvh(human), _bvh(bust)
    ray = lambda t: t.ray_cast(Vector((eye.x, -1.0, eye.z + 0.01)), Vector((0, 1, 0)))[0]
    dy = ray(hb).y - ray(bb).y
    for v in me.vertices:
        v.co.y += dy
    print("BUST", len(me.vertices), "verts, depth shift", round(dy, 4))
    return bust


def conform(human, bust, eyes, mouth):
    """Draws the MPFB face onto the bust's: nose, cheeks, brow and forehead, where the two surfaces are within a couple of
    centimetres. The lids and lips stay as MPFB made them, so blinks and the mouth shapes still close cleanly."""
    bb = _bvh(bust)
    eye = (eyes[0] + eyes[1]) / 2

    def pull(p):
        if p.y > eye.y + 0.03 or abs(p.x) > 0.08 or not (mouth.z - 0.03 < p.z < eye.z + FOREHEAD + 0.02):
            return Vector()
        de = min(Vector((p.x - e.x, p.z - e.z)).length for e in eyes)
        dm = Vector(((p.x - mouth.x) * 0.7, p.z - mouth.z)).length
        # Nothing below the nose: the bust's moustache and beard lie there, and the lips stay MPFB's.
        w = (_smooth(0.019, 0.03, de) * _smooth(0.02, 0.032, dm) * (1 - _smooth(0.06, 0.08, abs(p.x)))
             * _smooth(mouth.z + 0.014, mouth.z + 0.022, p.z) * (1 - _smooth(eye.z + FOREHEAD - 0.02, eye.z + FOREHEAD + 0.02, p.z)))
        q = bb.find_nearest(p)[0]
        if q is None or w <= 0: return Vector()
        d = q - p
        return d * w * (1 - _smooth(0.016, 0.024, d.length))
    # Each vertex snapping to its own nearest point folds the skin where neighbours land on different carved wrinkles (the cracks
    # on the nose and cheeks), so the pull is smoothed over the mesh before it is applied: one smooth surface, the bust's shape.
    ds = [pull(p) for p in _world(human)]
    nbrs = [[] for _ in ds]
    for e in human.data.edges:
        a, b = e.vertices; nbrs[a].append(b); nbrs[b].append(a)
    for _ in range(SMOOTHING):
        ds = [d * 0.5 + (sum((ds[j] for j in n), Vector()) / len(n)) * 0.5 if n else d for d, n in zip(ds, nbrs)]
    reshape(human, lambda i, p: ds[i])


def _material(name):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1); bsdf.inputs["Roughness"].default_value = 0.6
    return m


def split_bust(bust, human, eyes, mouth, nose_z, keep_torso=True):
    """Splits the bust into its parts, named so avatar-look.js styles them. "Bust" is one shell, so no cut edge shows between its
    parts: skin (Bust.skin: the crown, neck and, with keep_torso, the bare shoulders) and hair (Bust.hair) are material slots
    of it. The beard with the moustache (Bust.beard, styled as hair) and the plinth (Bust.plinth, styled as clothing) are their
    own meshes. The face's middle (eyes, lids, nose, cheeks, lips) is cut out so the MPFB face shows and moves there.
    Returns {"bust", "beard", "plinth"} (no plinth without keep_torso)."""
    hb = _bvh(human)
    eye = (eyes[0] + eyes[1]) / 2
    shoulders = eye.z + (BUST_SHOULDERS_Z - BUST_EYES.z) * BUST_SCALE
    plinth_top = eye.z + (BUST_PLINTH_Z - BUST_EYES.z) * BUST_SCALE

    def kind(p):
        q = hb.find_nearest(p)
        gap = q[3] if q[0] is not None else 1.0
        front = p.y < eye.y + 0.03
        if front and min(Vector((p.x - e.x, p.z - e.z)).length for e in eyes) < 0.024:
            return None                                                                          # eyes and lids
        if front and Vector(((p.x - mouth.x) * 0.75, p.z - mouth.z)).length < 0.02:
            return "beard" if p.z > mouth.z + 0.008 and gap > 0.003 else None                  # moustache, not the lips
        if (eye.z - BEARD_DROP < p.z < nose_z - 0.004 and p.y < eye.y + 0.07 and abs(p.x) < 0.085
                and (gap > PROUD or p.z < mouth.z - 0.04)):
            return "beard"
        if front and abs(p.x) < 0.075 and mouth.z - 0.03 < p.z < eye.z + FOREHEAD:
            return None                                                                          # the face: MPFB's, drawn on the bust's
        if p.z < plinth_top: return "plinth" if keep_torso else None
        if p.z < shoulders: return "skin" if keep_torso else None
        return "skin" if gap < 0.012 else "hair"

    kinds = [kind(p) for p in _world(bust)]
    bm0 = bmesh.new(); bm0.from_mesh(bust.data); bm0.verts.ensure_lookup_table()
    fkind = []
    for f in bm0.faces:
        ks = [kinds[v.index] for v in f.verts]
        fkind.append(None if None in ks else max(set(ks), key=ks.count))
    bm0.free()
    pieces = {"bust": ("skin", "hair"), "beard": ("beard",), "plinth": ("plinth",)}
    if not keep_torso: del pieces["plinth"]
    out = {}
    for name, ks in pieces.items():
        o = bust.copy(); o.data = bust.data.copy()
        o.name = o.data.name = "Bust" if name == "bust" else "Bust." + name
        bpy.context.scene.collection.objects.link(o)
        o.data.materials.clear()
        for k in ks: o.data.materials.append(_material("Bust." + k))
        bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
        for f in bm.faces:
            if fkind[f.index] in ks: f.material_index = ks.index(fkind[f.index])
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if fkind[f.index] not in ks], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(o.data); bm.free()
        out[name] = o
        print("PART", o.name, len(o.data.vertices))
    bpy.data.objects.remove(bust, do_unlink=True)
    for k in ("bust", "beard"):
        snap_rims(out[k], human)
    return out


def snap_rims(o, human, reach=0.016):
    """Lays the cut rims of o (round the face's hole, the beard's top edge) flush on the MPFB face, and the next ring halfway,
    so no lip or crack shows where the two surfaces meet."""
    hb = _bvh(human)
    bm = bmesh.new(); bm.from_mesh(o.data)
    rim = {v for e in bm.edges if e.is_boundary for v in e.verts}
    ring = {w for v in rim for e in v.link_edges for w in e.verts} - rim
    mw = o.matrix_world; mwi = mw.inverted_safe()
    for vs, k in ((rim, 1.0), (ring, 0.5)):
        for v in vs:
            p = mw @ v.co
            q, n, _, d = hb.find_nearest(p)
            if q is not None and d < reach:
                v.co = mwi @ (p.lerp(q + n * 0.0004, k))
    bm.to_mesh(o.data); bm.free(); o.data.update()


def rig(o, arm, groups):
    """Parents o to the armature, weighted by groups(index, world position) -> {bone: weight}."""
    for vg in list(o.vertex_groups): o.vertex_groups.remove(vg)
    vgs = {}
    for i, p in enumerate(_world(o)):
        for bone, w in groups(i, p).items():
            if w <= 0: continue
            if bone not in vgs: vgs[bone] = o.vertex_groups.new(name=bone)
            vgs[bone].add([i], w, "REPLACE")
    mw = o.matrix_world.copy(); o.parent = arm; o.matrix_world = mw
    mod = o.modifiers.new("Armature", "ARMATURE"); mod.object = arm


def add_base_bone(arm, at):
    """A root bone for the bust's shoulders and plinth (BustBase, not in TalkingHead's rig, so nothing ever moves it): the idle
    sway of the spine would otherwise tilt the whole stone bust."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    b = arm.data.edit_bones.new(BASE_BONE)
    ami = arm.matrix_world.inverted()
    b.head = ami @ at; b.tail = ami @ (at + Vector((0, 0, 0.1))); b.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")


def _nearest(human):
    from mathutils.kdtree import KDTree
    ws = _world(human)
    kd = KDTree(len(ws))
    for i, p in enumerate(ws): kd.insert(p, i)
    kd.balance()
    return kd


def neck_weights(human, arm, base=None):
    """Weights for the bust's head and neck: the nearest MPFB vertex's own, so the bust's neck turns, nods and talks with the face
    it meets. With a base bone (the bust on its plinth): only Head and Neck (the rest folded into the base, so neither the spine's
    sway nor an arm gesture pulls at it), all base from the shoulders down, and the MPFB neck's own weights folded the same way."""
    names = {g.index: g.name for g in human.vertex_groups}
    kd = _nearest(human)
    if base is None:
        return lambda i, p: {names[g.group]: g.weight for g in human.data.vertices[kd.find(p)[1]].groups}
    neck = (arm.matrix_world @ arm.data.bones["Neck"].head_local).z
    def folded(i):
        w = {}
        for g in human.data.vertices[i].groups:
            b = names[g.group] if names[g.group] in ("Head", "Neck") else base
            w[b] = w.get(b, 0) + g.weight
        t = sum(w.values()) or 1
        return {b: x / t for b, x in w.items()}
    fold = [folded(i) for i in range(len(human.data.vertices))]
    def groups(i, p):
        t = _smooth(neck - 0.06, neck - 0.01, p.z)
        w = {b: x * t for b, x in fold[kd.find(p)[1]].items()}
        w[base] = w.get(base, 0) + (1 - t)
        return w
    # The MPFB neck's own weights, folded the same way so it moves as the bust round it does.
    for vg in list(human.vertex_groups):
        if vg.name not in ("Head", "Neck"): human.vertex_groups.remove(vg)
    for b in ("Head", "Neck", base):
        if b not in human.vertex_groups: human.vertex_groups.new(name=b)
    for i, w in enumerate(fold):
        for b, x in w.items():
            human.vertex_groups[b].add([i], x, "REPLACE")
    return groups


def transfer_morphs(o, human, min_delta=0.0006, fade=None):
    """Gives o (the beard) the face's morph targets: each vertex moves as the nearest point of the MPFB face does, so the beard
    opens and closes with the jaw and the lips. fade(world position) scales it (1: fully); keys that barely move it are left off."""
    keys = human.data.shape_keys.key_blocks
    basis = keys[0]
    hmw = human.matrix_world; hm3 = hmw.to_3x3()
    hb = _bvh(human)
    ws = _world(o)
    near = []
    for p in ws:
        q, _, f, _ = hb.find_nearest(p)
        poly = human.data.polygons[f]
        vids = list(poly.vertices)
        near.append((vids, poly_3d_calc([hmw @ basis.data[v].co for v in vids], q), fade(p) if fade else 1.0))
    if not o.data.shape_keys:
        o.shape_key_add(name=basis.name, from_mix=False)
    omi = o.matrix_world.inverted_safe().to_3x3()
    kept = 0
    for kb in keys[1:]:
        ds = []
        for vids, ws_, k in near:
            d = Vector()
            for v, w in zip(vids, ws_):
                d += (kb.data[v].co - basis.data[v].co) * w
            ds.append(omi @ (hm3 @ d) * k)
        if max(d.length for d in ds) < min_delta: continue
        sk = o.shape_key_add(name=kb.name, from_mix=False)
        for i, d in enumerate(ds):
            sk.data[i].co = o.data.vertices[i].co + d
        kept += 1
    print("MORPHS", o.name, kept, "of", len(keys) - 1)


def build(examiner_glb, bust_glb, on_plinth=True):
    """The head on its own: returns (armature, examiner parts, bust parts, landmarks). Socrates' MPFB hair, beard and moustache are
    dropped (the bust's sculpt replaces them); his brows and lashes stay, so the expressions still read. on_plinth keeps the bust's
    shoulders and plinth, rigid on their own root bone; without it the bust stops at the neck, for Socrates' own body (GAD-T793)."""
    arm, parts = load_examiner(examiner_glb)
    for k in ("afro01", "rehmanpolanski_beard_viking", "rehmanpolanski_moustache_viking"):
        bpy.data.objects.remove(parts.pop(k), do_unlink=True)
    human = parts["body"]
    eyes = eye_centres(arm)
    tz = [p.z for p in _world(parts["teeth_base"])]
    ty = [p.y for p in _world(parts["teeth_base"])]
    mouth = Vector((0, min(ty) - 0.012, (max(tz) + min(tz)) / 2 + 0.006))
    nose_z = mouth.z + 0.022
    bust = load_bust(bust_glb, human, eyes)
    conform(human, bust, eyes, mouth)
    bparts = split_bust(bust, human, eyes, mouth, nose_z, keep_torso=on_plinth)
    transfer_morphs(bparts["beard"], human, fade=lambda p: 1 - _smooth(nose_z - 0.01, nose_z, p.z))
    if on_plinth:
        add_base_bone(arm, Vector((0, eyes[0].y, min(p.z for p in _world(bparts["plinth"])))))
    weights = neck_weights(human, arm, base=BASE_BONE if on_plinth else None)
    rig(bparts["bust"], arm, weights)
    rig(bparts["beard"], arm, weights)
    if on_plinth:
        rig(bparts["plinth"], arm, lambda i, p: {BASE_BONE: 1.0})
    # The look's iris glow (GAD-T792) is per model: avatar-look.js gives eyes whose material is named "...-glow" a stronger glow.
    for m in parts["high-poly"].data.materials:
        m.name = "Human.high-poly-glow"
    return arm, parts, bparts, {"eyes": eyes, "mouth": mouth, "nose_z": nose_z}


def export(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects: o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True,
        export_apply=False, export_skins=True, export_morph=True, export_morph_normal=True, export_morph_tangent=False,
        export_animations=False, export_yup=True, export_texcoords=True, export_normals=True, export_tangents=False,
        export_image_format="AUTO", export_extras=False, export_def_bones=False)
    print("WROTE", path, os.path.getsize(path))
