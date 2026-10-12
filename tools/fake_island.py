"""Fake Subnautica island: checks that Steve can walk on terrain sent the way the plugin sends it.

The plugin can't read Subnautica's pooled terrain meshes, so it probes the world with lines of rays 0.5 m apart
along x, y and z (back faces included) and sends every hit as a 0.9 m patch lying on the surface
(subnautica/src/TerrainExporter.cs). This script does the same on an analytic island - rolling hills that end in
a 10 m cliff - and checks in a running Minecraft (dry land, no water) that Steve

1. lands on the hills,
2. walks up and down them following the ground (no falling through between patches),
3. is stopped by the cliff (it is far taller than a step).

    python tools/fake_island.py --port 25612
"""
import argparse
import json
import math
import struct
import sys
import time

import websocket

BASE = -300.0
CLIFF_Z = 14.0
CLIFF_TOP = BASE + 12.0
SPACING = 0.5
REGION = 8.0
HALF = 0.45


def hills(x, z):
    return BASE + 1.2 * math.sin(x / 4.0) * math.cos(z / 5.0) + 0.15 * z


def height(x, z):
    """Island surface: hills below the cliff line, a plateau above it."""
    return hills(x, z) if z < CLIFF_Z else CLIFF_TOP


def normal(x, z):
    e = 0.01
    dx = (height(x + e, z) - height(x - e, z)) / (2 * e)
    dz = (height(x, z + e) - height(x, z - e)) / (2 * e)
    n = (-dx, 1.0, -dz)
    l = math.sqrt(sum(c * c for c in n))
    return tuple(c / l for c in n)


def probe(x0, x1, z0, z1):
    """The plugin's ray lines: every surface crossing, as (point, normal). Unity space (x not mirrored yet)."""
    hits = []
    # y lines: the top surface
    x = x0 + SPACING / 2
    while x < x1:
        z = z0 + SPACING / 2
        while z < z1:
            hits.append(((x, height(x, z), z), normal(x, z)))
            z += SPACING
        x += SPACING
    # z lines at each height: the cliff face, and the hills' sides where a line crosses them
    y = BASE - 2 + SPACING / 2
    while y < CLIFF_TOP + 1:
        x = x0 + SPACING / 2
        while x < x1:
            z = z0
            inside = y < height(x, z)
            while z < z1:
                z2 = z + 0.05
                now = y < height(x, z2)
                if now != inside:
                    if abs(z2 - CLIFF_Z) < 0.06:
                        hits.append(((x, y, CLIFF_Z), (0.0, 0.0, -1.0)))
                    else:
                        hits.append(((x, y, z2), normal(x, z2)))
                    inside = now
                z = z2
            x += SPACING
        y += SPACING
    # x lines: crossings of the hills along x
    y = BASE - 2 + SPACING / 2
    while y < CLIFF_TOP + 1:
        z = z0 + SPACING / 2
        while z < z1:
            x = x0
            inside = y < height(x, z)
            while x < x1:
                x2 = x + 0.05
                now = y < height(x2, z)
                if now != inside:
                    hits.append(((x2, y, z), normal(x2, z)))
                    inside = now
                x = x2
            z += SPACING
        y += SPACING
    return hits


def patch(p, n):
    """TerrainExporter.Encode: a square on the surface, two triangles, corners mirrored into Minecraft space."""
    up = (0.0, 1.0, 0.0) if abs(n[1]) < 0.9 else (1.0, 0.0, 0.0)

    def cross(a, b):
        return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])

    def norm(v, s):
        l = math.sqrt(sum(c * c for c in v))
        return tuple(c / l * s for c in v)

    t = norm(cross(n, up), HALF)
    b = norm(cross(n, t), HALF)
    corner = lambda sx, sy: (p[0] + t[0] * sx + b[0] * sy, p[1] + t[1] * sx + b[1] * sy, p[2] + t[2] * sx + b[2] * sy)
    a, c, d, e = corner(-1, -1), corner(1, -1), corner(1, 1), corner(-1, 1)
    out = []
    for v in (a, c, d, a, d, e):
        out.extend((-v[0], v[1], v[2]))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    args = ap.parse_args()
    for _ in range(300):
        try:
            ws = websocket.create_connection("ws://127.0.0.1:%d/" % args.port, timeout=5)
            break
        except OSError:
            time.sleep(1)
    else:
        sys.exit("no Minecraft on port %d" % args.port)
    print("connected:", ws.recv())
    ws.settimeout(0.001)
    state = {"pov": None, "track": []}

    def pump(seconds, inp=None):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": [0.0, 20.0]}
            if inp:
                msg.update(inp)
                inp = None
            ws.send(json.dumps(msg))
            try:
                while True:
                    m = json.loads(ws.recv())
                    if m["t"] == "pov":
                        state["pov"] = m
                        state["track"].append(m["f"])
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    # the island in Unity space spans x -8..8 (Minecraft x is mirrored), z -8..24, sent per 8 m region like the plugin
    key = 777000
    total = 0
    for rx in range(-1, 1):
        for rz in range(-1, 3):
            x0, z0 = rx * REGION, rz * REGION
            hits = probe(x0, x0 + REGION, z0, z0 + REGION)
            verts = []
            for p, n in hits:
                verts.extend(patch(p, n))
            data = struct.pack("<iqi", 0x52524554, key, len(verts) // 9) + struct.pack("<%df" % len(verts), *verts)
            ws.send_binary(data)
            key += 1
            total += len(hits)
    print("sent %d patches" % total)

    results = []

    def check(name, ok, detail):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + ": " + detail)

    ws.send(json.dumps({"t": "water", "y": None}))
    ws.send(json.dumps({"t": "mode", "steve": True}))
    ws.send(json.dumps({"t": "tp", "p": [0.0, hills(0, 0) + 3.0, 0.0], "r": [0.0, 20.0]}))
    pump(3.0)
    f = state["pov"]["f"]
    ground = height(-f[0], f[2])
    check("lands on the hills", abs(f[1] - ground) < 0.35, "feet y %.3f, ground %.3f" % (f[1], ground))

    state["track"].clear()
    pump(6.0, {"k": [[26, True]]})  # W towards the cliff
    pump(0.3, {"k": [[26, False]]})
    errors = [abs(t[1] - height(-t[0], t[2])) for t in state["track"] if t[2] < CLIFF_Z - 1]
    check("walks over the hills following the ground", errors and max(errors) < 0.7 and min(t[1] for t in state["track"]) > BASE - 3,
          "%d samples, worst |feet - ground| %.2f m" % (len(errors), max(errors) if errors else -1))
    max_z = max(t[2] for t in state["track"])
    check("the cliff stops him", CLIFF_Z - 0.9 < max_z < CLIFF_Z, "furthest z %.3f (cliff face at %.1f)" % (max_z, CLIFF_Z))

    ws.send(json.dumps({"t": "mode", "steve": False}))
    ws.send(json.dumps({"t": "terrainclear"}))
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")
    sys.exit(0 if all(results) else 1)


if __name__ == "__main__":
    main()
