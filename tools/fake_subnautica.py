"""Fake Subnautica: drives the SubnautiCraft Fabric mod without the game and checks Minecraft's frames.

It builds a known scene in Minecraft (a gold wall, a diamond pillar, a stone floor), poses Minecraft's camera exactly
like the BepInEx plugin does (a Unity camera pose converted to Minecraft coordinates), reads the frame back from the
shared memory and rebuilds every pixel's 3D point with the same maths as subnautica/src/McLayer.cs. If the coordinate
mapping, the angles (yaw, pitch, roll), the projection or the depth decoding were wrong, the points would miss the
blocks. Pictures of the colour, overlay and depth layers are saved next to the report.

Needs: Minecraft running with the SubnautiCraft mod (e.g. `gradlew runClient` in mc/), Python with
websocket-client, numpy and pillow.

    python tools/fake_subnautica.py [--out DIR] [--port 25611]
"""
import argparse
import json
import math
import mmap
import os
import struct
import sys
import time

import numpy as np
import websocket
from PIL import Image

NAME = "Local\\SubnautiCraftFrame"
MAGIC = 0x46434E53


def unity_rotation(pitch, yaw, roll):
    """Matrix of Unity's Quaternion.Euler(pitch, yaw, roll): roll about z, then pitch about x, then yaw about y."""
    p, y, r = (math.radians(a) for a in (pitch, yaw, roll))
    rx = np.array([[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]])
    ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    rz = np.array([[math.cos(r), -math.sin(r), 0], [math.sin(r), math.cos(r), 0], [0, 0, 1]])
    return ry @ rx @ rz


def to_mc(u):
    return [-u[0], u[1], u[2]]


class Frames:
    def __init__(self):
        self.mm = None

    def open(self):
        if self.mm is None:
            head = mmap.mmap(-1, 4096, tagname=NAME, access=mmap.ACCESS_READ)
            magic, _, header, slots, stride = struct.unpack_from("<iiiiq", head, 0)
            if magic != MAGIC:
                raise RuntimeError("shared memory isn't SubnautiCraft's frame export (magic %08x)" % magic)
            head.close()
            self.mm = mmap.mmap(-1, header + stride * slots, tagname=NAME, access=mmap.ACCESS_READ)
        return self.mm

    def latest(self):
        mm = self.open()
        _, _, header, slots, stride = struct.unpack_from("<iiiiq", mm, 0)
        publish, slot = struct.unpack_from("<qi", mm, 32)
        if slot < 0:
            return None
        desc = 256 + 128 * slot
        seq = struct.unpack_from("<q", mm, desc)[0]
        if seq & 1:
            return None
        mc_frame, host_frame, w, h, near, far, fov, flags = struct.unpack_from("<qqiifffi", mm, desc + 8)
        x, y, z, yaw, pitch, roll = struct.unpack_from("<dddfff", mm, desc + 48)
        layer = w * h * 4
        base = header + stride * slot
        color = np.frombuffer(mm[base:base + layer], dtype=np.uint8).reshape(h, w, 4).copy()
        depth = np.frombuffer(mm[base + layer:base + 2 * layer], dtype=np.float32).reshape(h, w).copy()
        overlay = np.frombuffer(mm[base + 2 * layer:base + 3 * layer], dtype=np.uint8).reshape(h, w, 4).copy()
        if struct.unpack_from("<q", mm, desc)[0] != seq:
            return None
        return dict(publish=publish, host_frame=host_frame, w=w, h=h, near=near, far=far, fov=fov, flags=flags,
                    pos=(x, y, z), angles=(yaw, pitch, roll), color=color, depth=depth, overlay=overlay)


def linear(f, d):
    """McFrame.Linear: view-axis distance in metres, NaN where Minecraft drew nothing."""
    n, fa = f["near"], f["far"]
    if f["flags"] & 4:
        z = n * fa / (n + d * (fa - n))
        z[d <= 0] = np.nan
    else:
        z = n * fa / (fa - d * (fa - n))
        z[d >= 1] = np.nan
    z[z > fa * 0.98] = np.nan
    return z


def reconstruct(f):
    """Every pixel's point in Subnautica's (Unity) world, the way McLayer.Build places its vertices."""
    w, h = f["w"], f["h"]
    z = linear(f, f["depth"].astype(np.float64))
    tan_half = math.tan(math.radians(f["fov"]) * 0.5)
    aspect = w / h
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    ndc_x, ndc_y = np.meshgrid(u * 2 - 1, v * 2 - 1)
    rays = np.stack([ndc_x * tan_half * aspect, ndc_y * tan_half, np.ones_like(ndc_x)], axis=-1) * z[..., None]
    pos_u = np.array([-f["pos"][0], f["pos"][1], f["pos"][2]])  # Coords.FromMc
    yaw, pitch, roll = f["angles"]
    rot = unity_rotation(pitch, yaw, roll)  # Coords.Rotation
    return pos_u + rays @ rot.T


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "fake_out"))
    ap.add_argument("--yaw", type=float, default=15.0)
    ap.add_argument("--pitch", type=float, default=8.0)
    ap.add_argument("--roll", type=float, default=4.0)
    ap.add_argument("--y0", type=int, default=-300, help="scene depth (vanilla worlds stop at -64)")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    ws = None
    for _ in range(600):
        try:
            ws = websocket.create_connection("ws://127.0.0.1:%d/" % args.port, timeout=5)
            break
        except OSError:
            time.sleep(1)
    if ws is None:
        sys.exit("Minecraft with the SubnautiCraft mod isn't listening on port %d" % args.port)
    hello = json.loads(ws.recv())
    print("connected:", hello)
    global NAME
    NAME = hello.get("shm", NAME)
    ws.settimeout(0.001)

    # a Unity camera 30 m down, a little east of the scene's middle, turned, tilted and rolled
    y0 = args.y0
    cam_u = [-2.0, y0 - 30.0, 3.0]
    p = to_mc(cam_u)
    frame_no = [0]

    def send_cam():
        frame_no[0] += 1
        ws.send(json.dumps({"t": "cam", "f": frame_no[0], "p": p, "r": [args.yaw, args.pitch, args.roll], "fov": 60.0}))

    def pump(seconds):
        end = time.time() + seconds
        while time.time() < end:
            send_cam()
            try:
                while True:
                    ws.recv()
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    ws.send(json.dumps({"t": "view", "w": 960, "h": 540}))
    ws.send(json.dumps({"t": "hud", "hidden": False}))
    pump(3.0)  # the player is now at the camera, so the scene's chunks are loaded
    for c in ["fill -12 %d -2 12 %d 24 minecraft:air" % (y0 - 45, y0 - 15),
              "fill -8 %d 20 8 %d 20 minecraft:gold_block" % (y0 - 40, y0 - 20),    # wall: north face at z = 20
              "fill 6 %d 14 6 %d 14 minecraft:diamond_block" % (y0 - 40, y0 - 30),  # pillar: x 6..7, z 14..15 (east of the camera)
              "fill -8 %d 0 8 %d 20 minecraft:stone" % (y0 - 41, y0 - 41)]:          # floor: top face at y0 - 40
        ws.send(json.dumps({"t": "cmd", "c": c}))
    pump(2.5)
    start = frame_no[0]
    frames = Frames()
    f = None
    for _ in range(300):
        pump(1 / 60)
        f = frames.latest()
        if f and f["host_frame"] > start:
            break
    if not f:
        sys.exit("no frame from Minecraft")

    print("frame %dx%d fov %.1f near %.3f far %.1f flags %d host frame %d pose %s %s" % (
        f["w"], f["h"], f["fov"], f["near"], f["far"], f["flags"], f["host_frame"], f["pos"], f["angles"]))
    Image.fromarray(f["color"][::-1, :, :3]).save(os.path.join(args.out, "color.png"))
    Image.fromarray(f["overlay"][::-1]).save(os.path.join(args.out, "overlay.png"))
    z = linear(f, f["depth"].astype(np.float64))
    viz = np.nan_to_num(np.clip(1 - z / 60, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(viz[::-1]).save(os.path.join(args.out, "depth.png"))

    world_u = reconstruct(f)
    mc = world_u * np.array([-1.0, 1.0, 1.0])  # back to Minecraft coordinates
    rgb = f["color"][..., :3].astype(int)
    covered = ~np.isnan(z)
    gold = covered & (rgb[..., 0] > 180) & (rgb[..., 1] > 140) & (rgb[..., 2] < 110)
    diamond = covered & (rgb[..., 2] > 150) & (rgb[..., 1] > 150) & (rgb[..., 0] < 140)
    ok = True
    report = ["coverage %.1f%% of pixels" % (100 * covered.mean())]

    def check(name, mask, axis, lo, hi, tol=0.15):
        nonlocal ok
        n = int(mask.sum())
        if n < 50:
            report.append("%s: only %d pixels seen - can't check" % (name, n))
            ok = False
            return
        vals = mc[mask][:, axis]
        inside = ((vals >= lo - tol) & (vals <= hi + tol)).mean()
        report.append("%s: %d px, %s in %.2f..%.2f (median %.3f, 5%%..95%% %.3f..%.3f): %.1f%% inside +-%.2f" % (
            name, n, "xyz"[axis], lo, hi, np.median(vals), np.percentile(vals, 5), np.percentile(vals, 95),
            100 * inside, tol))
        if inside < 0.9:
            ok = False

    check("gold wall face", gold, 2, 20.0, 20.0)
    check("diamond pillar x", diamond, 0, 6.0, 7.0)
    check("diamond pillar z", diamond, 2, 14.0, 15.0)
    report.append("PASS: Minecraft's picture lands on its blocks" if ok else "FAIL: see above")
    text = "\n".join(report)
    print(text)
    with open(os.path.join(args.out, "report.txt"), "w") as fh:
        fh.write(text + "\n")
    ws.close()
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
