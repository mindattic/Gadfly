# Lightens Socrates (GAD-T724) after alignrig.py: the beard and moustache keep their morph positions (they follow jawOpen and
# the visemes) but drop their morph normals, which the luminous look does not need on cut-out hair; unused accessors and buffer
# views (sparse ones included) are dropped and the binary chunk repacked. Then lists each mesh's morph targets and the eye bones as a check.
#   python trim.py in.glb out.glb
import json, re, struct, sys

src, dst = sys.argv[1], sys.argv[2]
b = open(src, "rb").read()
jlen = struct.unpack("<I", b[12:16])[0]
j = json.loads(b[20:20 + jlen])
binc = b[20 + jlen + 8:20 + jlen + 8 + struct.unpack("<I", b[20 + jlen:24 + jlen])[0]]

for m in j["meshes"]:
    if re.search(r"beard|moustache", m["name"].lower()):
        for p in m["primitives"]:
            for t in p.get("targets", []):
                t.pop("NORMAL", None)

used = set()
for m in j["meshes"]:
    for p in m["primitives"]:
        used.update(p["attributes"].values())
        if "indices" in p: used.add(p["indices"])
        for t in p.get("targets", []): used.update(t.values())
for s in j.get("skins", []):
    if "inverseBindMatrices" in s: used.add(s["inverseBindMatrices"])
amap = {old: new for new, old in enumerate(sorted(used))}
j["accessors"] = [j["accessors"][i] for i in sorted(used)]
for m in j["meshes"]:
    for p in m["primitives"]:
        p["attributes"] = {k: amap[v] for k, v in p["attributes"].items()}
        if "indices" in p: p["indices"] = amap[p["indices"]]
        p["targets"] = [{k: amap[v] for k, v in t.items()} for t in p.get("targets", [])]
        if not p["targets"]: del p["targets"]
for s in j.get("skins", []):
    if "inverseBindMatrices" in s: s["inverseBindMatrices"] = amap[s["inverseBindMatrices"]]

sparse = [a["sparse"][k] for a in j["accessors"] if "sparse" in a for k in ("indices", "values")]
views = sorted({a["bufferView"] for a in j["accessors"] if "bufferView" in a} | {s["bufferView"] for s in sparse}
               | {im["bufferView"] for im in j.get("images", [])})
vmap = {old: new for new, old in enumerate(views)}
old_views = j["bufferViews"]
data = [binc[old_views[i].get("byteOffset", 0):old_views[i].get("byteOffset", 0) + old_views[i]["byteLength"]] for i in views]
j["bufferViews"] = [old_views[i] for i in views]
for a in j["accessors"]:
    if "bufferView" in a: a["bufferView"] = vmap[a["bufferView"]]
for s in sparse: s["bufferView"] = vmap[s["bufferView"]]
for im in j.get("images", []): im["bufferView"] = vmap[im["bufferView"]]

buf = bytearray()
for v, d in zip(j["bufferViews"], data):
    while len(buf) % 4: buf.append(0)
    v["byteOffset"] = len(buf); v["byteLength"] = len(d); buf += d
while len(buf) % 4: buf.append(0)
j["buffers"][0]["byteLength"] = len(buf)
js = json.dumps(j, separators=(",", ":")).encode()
while len(js) % 4: js += b" "
with open(dst, "wb") as f:
    f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(buf)))
    f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
    f.write(struct.pack("<II", len(buf), 0x004E4942)); f.write(buf)

nodes = {n.get("name") for n in j["nodes"]}
print("wrote", dst, 12 + 8 + len(js) + 8 + len(buf), "root:", [j["nodes"][n]["name"] for n in j["scenes"][0]["nodes"]])
print("eye bones:", "LeftEye" in nodes, "RightEye" in nodes, "Head" in nodes)
for m in j["meshes"]:
    names = m.get("extras", {}).get("targetNames", [])
    print(f"{m['name']}: {len(names)} morph targets")
