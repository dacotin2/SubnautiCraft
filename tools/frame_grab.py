"""Copy one frame (colour + depth + camera numbers) out of Minecraft's shared memory, read only.

    python tools/frame_grab.py frame.npz [Local\\SubnautiCraftFrame_25612]
    python tools/mesh_edges.py frame.npz 4      (old vs new depth-mesh edges, as McLayer builds them)
"""
import mmap, struct, sys
import numpy as np
out = sys.argv[1]
name = sys.argv[2] if len(sys.argv) > 2 else "Local\\SubnautiCraftFrame"
m = mmap.mmap(-1, 4096, tagname=name, access=mmap.ACCESS_READ)
magic, version, header, slots = struct.unpack_from("<iiii", m, 0)
assert magic == 0x46434E53, hex(magic)
stride, = struct.unpack_from("<q", m, 16)
m.close()
m = mmap.mmap(-1, header + stride * slots, tagname=name, access=mmap.ACCESS_READ)
for attempt in range(100):
    slot, = struct.unpack_from("<i", m, 40)
    d = 256 + 128 * slot
    seq, = struct.unpack_from("<q", m, d)
    if seq & 1:
        continue
    w, h, near, far, fov, flags = struct.unpack_from("<iifffi", m, d + 24)
    pos = struct.unpack_from("<ddd", m, d + 48)  # Minecraft's camera, Minecraft coordinates (Subnautica's = -x, y, z)
    rot = struct.unpack_from("<fff", m, d + 72)  # yaw, pitch, roll
    n = w * h * 4
    base = header + stride * slot
    color = np.frombuffer(m[base:base + n], np.uint8).reshape(h, w, 4).copy()
    depth = np.frombuffer(m[base + n:base + 2 * n], np.float32).reshape(h, w).copy()
    overlay = np.frombuffer(m[base + 2 * n:base + 3 * n], np.uint8).reshape(h, w, 4).copy()  # hand, hotbar, screens
    if struct.unpack_from("<q", m, d)[0] == seq:
        break
np.savez(out, color=color, depth=depth, overlay=overlay, near=near, far=far, fov=fov, flags=flags, pos=pos, rot=rot)
print("frame", w, h, "near", near, "far", far, "fov", fov, "flags", flags, "valid", float((depth > 0).mean()))
