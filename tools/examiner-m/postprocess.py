# Post-processes the Blender export of the male examiner (GAD-T504) to match the female mpfb.glb's conventions and stay light:
#   python postprocess.py in.glb out.glb
# - material alpha modes like the female (skin, clothing, teeth, tongue opaque; hair, brows, lashes, eyes alpha-masked) and no
#   clearcoat extension (MakeSkin's zero clearcoat; three.js would compile a heavier shader for nothing);
# - node and mesh names without Blender's ".001" suffixes;
# - textures without alpha re-encoded as JPEG (quality 88); cut-out textures stay PNG.
import io, json, re, struct, sys
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
b = open(src, "rb").read()
jlen = struct.unpack("<I", b[12:16])[0]
j = json.loads(b[20:20 + jlen])
bin_off = 20 + jlen + 8
binc = b[bin_off:bin_off + struct.unpack("<I", b[20 + jlen:24 + jlen])[0]]

def strip(n): return re.sub(r"\.\d{3}$", "", n)
for coll in ("nodes", "meshes"):
    for x in j[coll]:
        if "name" in x: x["name"] = strip(x["name"])

masked = re.compile(r"eyebrow|eyelash|short0|afro|hair|beard|moustache|high-poly")
for m in j["materials"]:
    m.pop("extensions", None)
    if masked.search(m["name"].lower()):
        m["alphaMode"] = "MASK"; m["alphaCutoff"] = 0.3 if "high-poly" not in m["name"] else 0.5
    else:
        m.pop("alphaMode", None); m.pop("alphaCutoff", None)
    m["doubleSided"] = True
if "extensionsUsed" in j:
    j["extensionsUsed"] = [e for e in j["extensionsUsed"] if e != "KHR_materials_clearcoat"]
    if not j["extensionsUsed"]: del j["extensionsUsed"]

views = j["bufferViews"]
data = [binc[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]] for v in views]
for im in j["images"]:
    i = im["bufferView"]
    img = Image.open(io.BytesIO(data[i]))
    cut = img.mode in ("RGBA", "LA") and img.getchannel("A").getextrema()[0] < 250
    keep_png = (cut and bool(masked.search(im["name"].lower()) or "eyelash" in im["name"])) or "eye" in im["name"]
    if not keep_png:
        out = io.BytesIO(); img.convert("RGB").save(out, "JPEG", quality=88, optimize=True)
        data[i] = out.getvalue(); im["mimeType"] = "image/jpeg"
    else:
        out = io.BytesIO(); img.save(out, "PNG", optimize=True)
        if len(out.getvalue()) < len(data[i]): data[i] = out.getvalue()
    print(im["name"], im["mimeType"], len(data[i]))

buf = bytearray()
for v, d in zip(views, data):
    while len(buf) % 4: buf.append(0)
    v["byteOffset"] = len(buf); v["byteLength"] = len(d); buf += d
while len(buf) % 4: buf.append(0)
j["buffers"][0]["byteLength"] = len(buf)

js = json.dumps(j, separators=(",", ":")).encode()
while len(js) % 4: js += b" "
total = 12 + 8 + len(js) + 8 + len(buf)
with open(dst, "wb") as f:
    f.write(struct.pack("<III", 0x46546C67, 2, total))
    f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
    f.write(struct.pack("<II", len(buf), 0x004E4942)); f.write(buf)
print("wrote", dst, total, "root:", [j["nodes"][n]["name"] for n in j["scenes"][0]["nodes"]])
