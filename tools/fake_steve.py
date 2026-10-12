"""Fake Subnautica for Steve mode: checks Minecraft's half of "Steve replaces Ryley" without the game.

Plays the BepInEx plugin's part against a running Minecraft with the SubnautiCraft mod (e.g. `gradlew runClient`):
sends a terrain chunk (a flat seafloor and a rock wall, as triangles), the ocean (water below y = 0), switches to
Steve mode, puts Steve in the water and then checks, from the "pov" Minecraft sends back every frame, that

1. Steve is in the water and sinks slowly (Minecraft water physics on Subnautica's ocean),
2. he lands on the triangle seafloor and stays on it (terrain collision),
3. W walks him forward and the rock wall stops him,
4. right-click places a block on the seafloor (crosshair on Subnautica's terrain),
5. E opens and closes the inventory (a Minecraft screen, cursor handed over),
6. a salmon swims in the ocean (fish path finding sees the water),
7. a sword swing is reported to Subnautica (it acts as the survival knife there).

    python tools/fake_steve.py [--port 25611]
"""
import argparse
import json
import os
import re
import struct
import sys
import time

import websocket

FLOOR_Y = -300.0  # deep: vanilla worlds stop at -64
WALL_Z = 12.0


def terrain_chunk():
    """A 40 x 40 m seafloor at FLOOR_Y and a 10 m rock wall facing Steve at z = WALL_Z (Minecraft coordinates)."""
    tris = []

    def quad(a, b, c, d):
        tris.extend([a, b, c, a, c, d])

    for x0 in range(-20, 20, 4):
        for z0 in range(-20, 20, 4):
            quad((x0, FLOOR_Y, z0), (x0 + 4, FLOOR_Y, z0), (x0 + 4, FLOOR_Y, z0 + 4), (x0, FLOOR_Y, z0 + 4))
    quad((-20, FLOOR_Y, WALL_Z), (20, FLOOR_Y, WALL_Z), (20, FLOOR_Y + 10, WALL_Z), (-20, FLOOR_Y + 10, WALL_Z))
    data = struct.pack("<iqi", 0x52524554, 424242, len(tris) // 3)
    for v in tris:
        data += struct.pack("<fff", *v)
    return data


class Link:
    def __init__(self, port):
        for _ in range(300):
            try:
                self.ws = websocket.create_connection("ws://127.0.0.1:%d/" % port, timeout=5)
                break
            except OSError:
                time.sleep(1)
        else:
            sys.exit("Minecraft with the SubnautiCraft mod isn't listening on port %d" % port)
        print("connected:", self.ws.recv())
        self.ws.settimeout(0.001)
        self.pov = None
        self.blocks = []
        self.track = []
        self.melee = []

    def send(self, obj):
        self.ws.send(json.dumps(obj))

    def pump(self, seconds, inp=None):
        """Send an "in" message every 1/60 s (that's also what keeps Steve mode alive) and collect replies."""
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": [0.0, 35.0]}
            if inp:
                msg.update(inp)
                inp = None  # key and button changes are sent once, like the plugin does
            self.send(msg)
            try:
                while True:
                    m = json.loads(self.ws.recv())
                    if m["t"] == "pov":
                        self.pov = m
                        self.track.append(m["f"])
                    elif m["t"] == "blocks":
                        self.blocks.append(m)
                    elif m["t"] == "melee":
                        self.melee.append(m)
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    ap.add_argument("--log", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mc", "run-client.log"),
                    help="Minecraft's log (command results are read from it)")
    args = ap.parse_args()
    link = Link(args.port)
    results = []

    def check(name, ok, detail):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + ": " + detail)

    link.send({"t": "view", "w": 960, "h": 540})
    link.send({"t": "window", "hide": True})
    link.send({"t": "fov", "v": 60.0})
    link.ws.send_binary(terrain_chunk())
    link.send({"t": "water", "y": 0.0})
    link.send({"t": "mode", "steve": True})
    link.send({"t": "tp", "p": [0.0, FLOOR_Y + 6.0, 0.0], "r": [0.0, 35.0]})
    link.pump(1.0)
    if not link.pov:
        sys.exit("no pov from Minecraft: Steve mode didn't start")
    y0 = link.pov["f"][1]
    check("in the ocean", link.pov["sw"], "isInWater=%s at feet y %.2f" % (link.pov["sw"], y0))
    # Minecraft players sink slowly in water: wait until he stops going down
    last = y0
    for _ in range(25):
        link.pump(1.0)
        if abs(link.pov["f"][1] - last) < 0.01:
            break
        last = link.pov["f"][1]
    feet = link.pov["f"]
    check("sank and landed on the triangle seafloor", abs(feet[1] - FLOOR_Y) < 0.3 and feet[1] < y0,
          "feet y %.3f (floor %.1f, started %.2f)" % (feet[1], FLOOR_Y, y0))

    z0 = feet[2]
    link.track.clear()
    link.pump(9.0, {"k": [[26, True]]})  # W, long enough to reach the wall
    link.pump(0.2, {"k": [[26, False]]})
    # vanilla water physics: pushing into a wall you rise along it (and over the top); never through it
    below_top = [f for f in link.track if f[1] < FLOOR_Y + 10 - 1.8]
    max_z = max(f[2] for f in below_top)
    top = max(f[1] for f in link.track)
    check("walked forward and the rock wall stopped him (he swims up it, as in Minecraft)", max_z > z0 + 2.0 and max_z < WALL_Z - 0.29,
          "z %.2f -> %.3f while below the wall's top (wall face %.1f - half-width 0.3); then rose to y %.2f (wall top %.1f)" % (
              z0, max_z, WALL_Z, top, FLOOR_Y + 10))

    link.send({"t": "tp", "p": [0.0, FLOOR_Y, 4.0], "r": [0.0, 35.0]})  # back from the wall, standing on the floor
    link.pump(1.0)
    link.pump(0.15, {"k": [[30, True]]})  # 1: the grass block (the world remembers the last slot)
    link.pump(0.15, {"k": [[30, False]]})
    link.blocks.clear()
    link.pump(0.15, {"b": [[3, True]]})  # right click: place the grass block in slot 1
    link.pump(0.8, {"b": [[3, False]]})
    placed = [s for m in link.blocks for s in zip(m["set"][0::3], m["set"][1::3], m["set"][2::3])]
    check("right-click placed a block on the seafloor", any(abs(y - FLOOR_Y) < 1.01 for _, y, _ in placed),
          "blocks set: %s" % placed[:4])

    link.pump(0.3, {"k": [[8, True]]})  # E
    link.pump(0.3, {"k": [[8, False]]})
    opened = link.pov["scr"]
    link.pump(0.3, {"k": [[8, True]]})
    link.pump(0.5, {"k": [[8, False]]})
    check("E opens and closes the inventory", opened and not link.pov["scr"], "open=%s, then open=%s" % (opened, link.pov["scr"]))

    def salmon_pos():
        link.send({"t": "cmd", "c": "data get entity @e[type=minecraft:salmon,limit=1,sort=nearest] Pos"})
        link.pump(0.6)
        with open(args.log, encoding="utf-8", errors="replace") as fh:
            found = re.findall(r"has the following entity data: \[(-?[\d.]+)d, (-?[\d.]+)d, (-?[\d.]+)d\]", fh.read())
        return tuple(map(float, found[-1])) if found else None

    link.send({"t": "cmd", "c": "kill @e[type=minecraft:salmon]"})
    link.send({"t": "cmd", "c": "summon minecraft:salmon 0 %d 6" % (FLOOR_Y + 4)})
    link.pump(1.0)
    a = salmon_pos()
    link.pump(10.0)
    b = salmon_pos()
    moved = a is not None and b is not None and ((a[0] - b[0]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5 > 1.0
    check("a salmon swims in the ocean", moved, "salmon at %s, 10 s later %s" % (a, b))
    link.send({"t": "cmd", "c": "kill @e[type=minecraft:salmon]"})

    link.melee.clear()
    link.pump(0.2, {"k": [[37, True]]})  # 8: the diamond sword
    link.pump(0.2, {"k": [[37, False]]})
    link.pump(0.2, {"b": [[1, True]]})  # left click
    link.pump(0.6, {"b": [[1, False]]})
    check("a sword swing reaches Subnautica (survival knife)", any(m.get("dmg", 0) >= 7 for m in link.melee), "melee messages: %s" % link.melee[:2])

    for c in ["fill -6 %d -6 6 %d 14 minecraft:air" % (FLOOR_Y - 1, FLOOR_Y + 12)]:
        link.send({"t": "cmd", "c": c})
    link.send({"t": "mode", "steve": False})
    link.send({"t": "terrainclear"})
    link.send({"t": "water", "y": None})
    link.pump(0.5)
    link.ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")
    sys.exit(0 if all(results) else 1)


if __name__ == "__main__":
    main()
