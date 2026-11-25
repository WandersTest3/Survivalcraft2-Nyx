import struct, sys, os, zipfile, re

try:
    import lz4.block
    import dnfile
except ImportError:
    sys.exit("pip install lz4 dnfile")

apk, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
blob = zipfile.ZipFile(apk).read("lib/arm64-v8a/libassemblies.arm64-v8a.blob.so")
base = blob.find(b"XABA")
magic, version, local_count, global_count, store_id = struct.unpack_from("<IIIII", blob, base)
table = base + 20 + local_count * 24
count = 0
for n in range(local_count):
    _, offset, size, *_ = struct.unpack_from("<7I", blob, table + n * 28)
    data = blob[base + offset: base + offset + size]
    if data[:4] == b"XALZ":
        data = lz4.block.decompress(data[12:], uncompressed_size=struct.unpack_from("<I", data, 8)[0])
    if data[:2] != b"MZ":
        continue
    tmp = os.path.join(out, "_tmp.dll")
    open(tmp, "wb").write(data)
    try:
        name = dnfile.dnPE(tmp).net.mdtables.Assembly.rows[0].Name.value
    except Exception:
        name = "unknown_%03d" % n
    os.replace(tmp, os.path.join(out, name + ".dll"))
    count += 1
print("extraídas", count, "assemblies em", out)
