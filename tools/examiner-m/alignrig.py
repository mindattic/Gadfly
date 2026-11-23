# Gives the male examiner's skeleton the female mpfb.glb's rest-bone orientations (GAD-T504):
#   python alignrig.py female.glb male.glb out.glb
# TalkingHead poses a body by writing absolute local rotations onto Hips/Spine/Neck/Head/arms (its pose templates). Those values
# were tuned for the female rig's bone frames; MPFB's stock Mixamo rig has the same bone names but differently oriented frames
# (a more curved spine, other arm rolls), so the same template bent his head down. Each shared joint keeps its position and gets
# the female joint's world rotation; the inverse bind matrices are recomputed so the mesh at rest is unchanged.
import json, struct, sys
import numpy as np

def read(p):
    b = open(p, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + n])
    o = 20 + n
    bl = struct.unpack("<I", b[o:o + 4])[0]
    return j, bytearray(b[o + 8:o + 8 + bl])

def qmat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])

def matq(m):
    t = np.trace(m)
    if t > 0:
        s = np.sqrt(t + 1) * 2; w = s / 4; x = (m[2, 1] - m[1, 2]) / s; y = (m[0, 2] - m[2, 0]) / s; z = (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = np.sqrt(1 + m[0, 0] - m[1, 1] - m[2, 2]) * 2; w = (m[2, 1] - m[1, 2]) / s; x = s / 4; y = (m[0, 1] + m[1, 0]) / s; z = (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = np.sqrt(1 + m[1, 1] - m[0, 0] - m[2, 2]) * 2; w = (m[0, 2] - m[2, 0]) / s; x = (m[0, 1] + m[1, 0]) / s; y = s / 4; z = (m[1, 2] + m[2, 1]) / s
    else:
        s = np.sqrt(1 + m[2, 2] - m[0, 0] - m[1, 1]) * 2; w = (m[1, 0] - m[0, 1]) / s; x = (m[0, 2] + m[2, 0]) / s; y = (m[1, 2] + m[2, 1]) / s; z = s / 4
    q = np.array([x, y, z, w]); return q / np.linalg.norm(q)

def local(n):
    m = np.eye(4)
    r = qmat(n.get("rotation", [0, 0, 0, 1])) * np.array(n.get("scale", [1, 1, 1]))
    m[:3, :3] = r; m[:3, 3] = n.get("translation", [0, 0, 0])
    return m

def worlds(j):
    par = {c: i for i, n in enumerate(j["nodes"]) for c in n.get("children", [])}
    w = {}
    def get(i):
        if i not in w: w[i] = (get(par[i]) if i in par else np.eye(4)) @ local(j["nodes"][i])
        return w[i]
    for i in range(len(j["nodes"])): get(i)
    return w, par

fj, _ = read(sys.argv[1])
mj, mbin = read(sys.argv[2])
fw, _ = worlds(fj)
f_by_name = {n.get("name"): i for i, n in enumerate(fj["nodes"])}
mw, mpar = worlds(mj)
skin = mj["skins"][0]
joints = skin["joints"]

# New world matrices: female rotation (where the joint exists in her rig), own position.
neww = {}
for i in joints:
    name = mj["nodes"][i]["name"]
    w = mw[i].copy()
    if name in f_by_name:
        r = fw[f_by_name[name]][:3, :3]
        r = r / np.linalg.norm(r, axis=0)
        w[:3, :3] = r
    neww[i] = w

changed = 0
for i in joints:
    p = mpar.get(i)
    pw = neww[p] if p in neww else (mw[p] if p is not None else np.eye(4))
    l = np.linalg.inv(pw) @ neww[i]
    n = mj["nodes"][i]
    n["translation"] = [float(x) for x in l[:3, 3]]
    n["rotation"] = [float(x) for x in matq(l[:3, :3])]
    n.pop("scale", None)
    changed += 1

# Inverse bind matrices: new IBM = inv(W'_j) @ W_j @ old IBM (mesh unchanged at rest).
acc = mj["accessors"][skin["inverseBindMatrices"]]
bv = mj["bufferViews"][acc["bufferView"]]
off = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
ibm = np.frombuffer(bytes(mbin[off:off + 64 * len(joints)]), dtype="<f4").reshape(len(joints), 4, 4).transpose(0, 2, 1).astype(np.float64)
for k, i in enumerate(joints):
    ibm[k] = np.linalg.inv(neww[i]) @ mw[i] @ ibm[k]
mbin[off:off + 64 * len(joints)] = ibm.transpose(0, 2, 1).astype("<f4").tobytes()

js = json.dumps(mj, separators=(",", ":")).encode()
while len(js) % 4: js += b" "
total = 12 + 8 + len(js) + 8 + len(mbin)
with open(sys.argv[3], "wb") as f:
    f.write(struct.pack("<III", 0x46546C67, 2, total))
    f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
    f.write(struct.pack("<II", len(mbin), 0x004E4942)); f.write(mbin)
print("aligned", changed, "joints ->", sys.argv[3], total)
