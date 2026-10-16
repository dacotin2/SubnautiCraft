"""Fake Subnautica for digging its terrain, its tools and the picture's margin: checks Minecraft's half.

1. swinging at Subnautica's terrain asks Subnautica about that cell ({"t":"dig"}); its answer puts an invisible block
   standing for the terrain there ({"t":"terrainblock"}), mined like sand and dropping sand, and mining it tells
   Subnautica to cut the terrain ({"t":"carve"}),
2. a Subnautica tool in Steve's hand is reported to Subnautica ("sn:PropulsionCannon"), so the diver holds the real one,
3. the overscan margin: Minecraft draws its world wider than Subnautica's screen, and says so in the frame header,
4. a tool's battery: its charge is the item's durability, it goes to Subnautica with the tool, Subnautica's spending
   comes back ({"t":"toolcharge"}), and R opens the battery screen, where a battery fills it.

    python tools/fake_dig.py --port 25612
"""
import argparse
import json
import math
import mmap
import os
import struct
import sys
import time

import websocket

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fake_steve import terrain_chunk, FLOOR_Y  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def frame_fov(port):
    name = "Local\\SubnautiCraftFrame_%d" % port
    m = mmap.mmap(-1, 4096, tagname=name, access=mmap.ACCESS_READ)
    for _ in range(100):
        slot, = struct.unpack_from("<i", m, 40)
        d = 256 + 128 * slot
        seq, = struct.unpack_from("<q", m, d)
        if seq & 1:
            continue
        fov, = struct.unpack_from("<f", m, d + 40)
        if struct.unpack_from("<q", m, d)[0] == seq:
            m.close()
            return fov
    m.close()
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    ap.add_argument("--log", default=os.path.join(HERE, "..", "mc", "run-client.log"))
    args = ap.parse_args()
    for _ in range(300):
        try:
            ws = websocket.create_connection("ws://127.0.0.1:%d/" % args.port, timeout=5)
            break
        except OSError:
            time.sleep(1)
    else:
        sys.exit("no Minecraft on port %d" % args.port)
    ws.recv()
    ws.settimeout(0.001)
    got = []
    pov = {}
    look = [0.0, 60.0]

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": look}
            if inp:
                msg.update(inp)
                inp = None
            send(msg)
            try:
                while True:
                    m = json.loads(ws.recv())
                    if m.get("t") in ("dig", "carve"):
                        got.append(m)
                    elif m.get("t") == "pov":
                        pov["tool"] = m.get("tool")
                        pov["tc"] = m.get("tc")
                        pov["scr"] = m.get("scr")
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    def query(c, wait=0.8):
        start = os.path.getsize(args.log)
        send({"t": "cmd", "c": c})
        pump(wait)
        with open(args.log, encoding="utf-8", errors="replace") as fh:
            fh.seek(start)
            return fh.read()

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "pause", "on": False})
    ws.send_binary(terrain_chunk())
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": True})
    pump(1.5)  # the seafloor reaches Minecraft before Steve stands on it
    send({"t": "cmd", "c": "gamemode survival @a"})
    send({"t": "cmd", "c": "clear @a"})
    floor = int(math.floor(FLOOR_Y))
    send({"t": "cmd", "c": "fill -4 %d -4 4 %d 4 minecraft:air" % (floor - 3, floor - 1)})  # nothing left from before
    send({"t": "tp", "p": [0.5, FLOOR_Y + 0.2, 0.5], "r": look})
    pump(3.0)

    # 1. digging the terrain
    got.clear()
    pump(1.0, {"b": [[1, True]]})  # hold left click on the seafloor ahead
    digs = [g for g in got if g["t"] == "dig"]
    check("swinging at the seafloor asks Subnautica about the cell", len(digs) >= 1, str(digs[:2]))
    if digs:
        p = digs[0]["p"]
        check("... the cell under the floor, aimed at from above", p[1] == int(math.floor(FLOOR_Y)) - 1 and digs[0]["f"] == "up", str(digs[0]))
        send({"t": "terrainblock", "p": p, "b": "minecraft:sand"})
        pump(1.5, {"b": [[1, True]]})
        out = query("execute if block %d %d %d subnauticraft:terrain_sand" % tuple(p), 0.3)
        sand = "Test passed" in out
        carved = [g for g in got if g["t"] == "carve" and g["p"] == p]
        check("an invisible block standing for the terrain goes there", sand or bool(carved), out.strip()[-80:])
        pump(4.0, {"b": [[1, True]]})  # keep mining it
        pump(0.3, {"b": [[1, False]]})
        carved = [g for g in got if g["t"] == "carve" and g["p"] == p]
        check("mining it tells Subnautica to cut its terrain there", bool(carved), str([g for g in got if g["t"] == "carve"][:2]))
        pump(1.0)
        out = query("execute if items entity @a container.* minecraft:sand")
        check("... and Steve gets sand for it", "Test passed" in out, out.strip()[-80:])
    pump(0.3, {"b": [[1, False]]})

    # 2. a Subnautica tool in hand
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "give", "tech": "PropulsionCannon", "name": "Cañón de propulsión", "n": 1})
    pump(0.3, {"k": [[30, True]]})
    pump(1.0, {"k": [[30, False]]})
    check("a Subnautica tool in hand is reported to Subnautica", pov.get("tool") == "sn:PropulsionCannon", "tool=%r" % pov.get("tool"))

    # 4. the tool's battery
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "give", "tech": "StasisRifle", "name": "Rifle de estasis", "n": 1, "charge": 0.4})
    send({"t": "cmd", "c": "item replace entity @a hotbar.1 with subnauticraft:battery 2"})
    pump(0.3, {"k": [[30, True]]})
    pump(1.0, {"k": [[30, False]]})
    out = query("data get entity @a[limit=1] SelectedItem.components")
    check("a tool comes with its battery's charge as durability", '"minecraft:damage": 600' in out, out.strip()[-160:])
    check("... and its charge goes to Subnautica with it", pov.get("tc") is not None and abs(pov["tc"] - 0.4) < 0.01, "tc=%r" % pov.get("tc"))
    send({"t": "toolcharge", "c": 0.25})
    pump(0.6)
    out = query("data get entity @a[limit=1] SelectedItem.components")
    check("what the real tool spends in Subnautica comes back", '"minecraft:damage": 750' in out, out.strip()[-160:])
    pump(0.2, {"k": [[21, True]]})  # R
    pump(1.0, {"k": [[21, False]]})
    check("R opens the battery screen", bool(pov.get("scr")), "screen=%r" % pov.get("scr"))
    left, top = (480 - 176) // 2, (270 - 132) // 2
    x, y = (left + 8 + 18 + 8) * 2, (top + 107 + 8) * 2  # hotbar slot 1 in a one-row chest screen (GUI scale 2)
    pump(0.15, {"c": [x, y]})
    pump(0.05, {"k": [[225, True]]})
    pump(0.1, {"c": [x, y], "b": [[1, True]]})
    pump(0.25, {"c": [x, y], "b": [[1, False]]})
    pump(0.05, {"k": [[225, False]]})
    pump(0.1, {"k": [[41, True]]})
    pump(0.6, {"k": [[41, False]]})
    out = query("data get entity @a[limit=1] SelectedItem.components")
    check("a battery put in fills the tool", '"minecraft:damage": 0' in out, out.strip()[-160:])
    out = query("execute if items entity @a hotbar.1 subnauticraft:battery[count=1]")
    check("... and is used up", "Test passed" in out, out.strip()[-80:])
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "give", "tech": "StasisRifle", "name": "Rifle de estasis", "n": 1})  # one from before batteries
    pump(0.3, {"k": [[30, True]]})
    pump(0.6, {"k": [[30, False]]})
    pump(0.2, {"k": [[21, True]]})  # R
    pump(1.0, {"k": [[21, False]]})
    check("R also opens for a tool from before batteries", bool(pov.get("scr")), "screen=%r" % pov.get("scr"))
    pump(0.1, {"k": [[41, True]]})
    pump(0.6, {"k": [[41, False]]})

    # 3. the overscan margin
    send({"t": "fov", "v": 60.0, "o": 1.3})
    pump(1.0)
    fov = frame_fov(args.port)
    want = math.degrees(2 * math.atan(1.3 * math.tan(math.radians(30.0))))
    check("Minecraft draws its world wider than the screen, and the frame says so", fov is not None and abs(fov - want) < 0.05,
          "header fov %r, expected %.2f" % (fov, want))
    send({"t": "fov", "v": 60.0, "o": 1.0})

    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "mode", "steve": False})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
