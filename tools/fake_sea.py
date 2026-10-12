"""Fake Subnautica for the sea rules: checks Minecraft's half of water, fire, lights and boats.

Against a running Minecraft with the SubnautiCraft mod (`gradlew runClient -PsncPort=25612`), with the sea's surface
at y 190 in the empty test world:

1. under the sea a water bucket pours nothing and lava sets into obsidian; above it both work as usual,
2. under the sea flint and steel lights nothing and a campfire goes down unlit; above it fire and a lit campfire,
3. light-giving blocks reach Subnautica as "lights" (placed, removed, and all of them again on "blocksync"),
4. a boat falls onto the sea and floats at its surface; one placed from a ledge goes down on the water, and Steve
   rows it along the surface.

    python tools/fake_sea.py --port 25612
"""
import argparse
import json
import os
import re
import sys
import time

import websocket

SEA = 190.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    ap.add_argument("--log", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mc", "run-client.log"))
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
    state = {"lights": [], "look": [0.0, 90.0], "pov": None}

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": state["look"]}
            if inp:
                msg.update(inp)
                inp = None
            send(msg)
            try:
                while True:
                    m = json.loads(ws.recv())
                    if m["t"] == "lights":
                        state["lights"].append(m)
                    elif m["t"] == "pov":
                        state["pov"] = m
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    def cmd(c):
        send({"t": "cmd", "c": c})

    def query(c, wait=0.8):
        """A server command's answer, from Minecraft's log."""
        start = os.path.getsize(args.log)
        cmd(c)
        pump(wait)
        with open(args.log, encoding="utf-8", errors="replace") as fh:
            fh.seek(start)
            return fh.read()

    def block_is(x, y, z, block):
        return "Test passed" in query("execute if block %d %d %d %s" % (x, y, z, block))

    def boat_pos(selector):
        m = re.search(r"entity data: \[([-\d.E]+)d, ([-\d.E]+)d, ([-\d.E]+)d\]", query("data get entity %s Pos" % selector))
        return tuple(float(v) for v in m.groups()) if m else None

    def stand(x, y, z, yaw, pitch):
        state["look"] = [yaw, pitch]
        send({"t": "tp", "p": [x, y, z], "r": [yaw, pitch]})
        pump(1.2)

    def use(item, hold=0.05):
        cmd("item replace entity @a weapon.mainhand with " + item)
        pump(0.4)
        pump(hold, {"b": [[3, True]]})  # right click
        pump(0.5, {"b": [[3, False]]})

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "pause", "on": False})
    send({"t": "water", "y": SEA})
    send({"t": "mode", "steve": True})
    cmd("gamemode creative @a")
    cmd("kill @e[type=minecraft:oak_boat]")
    cmd("fill -3 180 -3 30 200 30 minecraft:air")
    # under the sea: a platform at 185 (Steve stands at 186, four blocks down); above it: a ledge at 191
    cmd("fill -1 185 -1 1 185 1 minecraft:stone")
    cmd("fill 20 191 20 21 191 20 minecraft:stone")
    pump(1.0)

    # 1. buckets
    stand(0.5, 186.0, 0.5, 0.0, 90.0)
    use("minecraft:water_bucket")
    check("water bucket under the sea pours nothing", block_is(0, 186, 0, "minecraft:air"))
    use("minecraft:lava_bucket")
    check("lava bucket under the sea sets into obsidian", block_is(0, 186, 0, "minecraft:obsidian"))
    cmd("setblock 0 186 0 minecraft:air")

    stand(20.5, 192.0, 20.5, 0.0, 90.0)
    use("minecraft:water_bucket")
    check("water bucket above the sea still pours water", block_is(20, 192, 20, "minecraft:water"))
    cmd("setblock 20 192 20 minecraft:air")
    pump(0.5)

    # 2. fire
    stand(0.5, 186.0, 0.5, 0.0, 90.0)
    use("minecraft:flint_and_steel")
    check("flint and steel under the sea lights nothing", block_is(0, 186, 0, "minecraft:air"))
    use("minecraft:fire_charge")
    check("fire charge under the sea lights nothing", block_is(0, 186, 0, "minecraft:air"))
    stand(0.5, 186.0, 0.5, -90.0, 58.0)  # at the top of the next block east
    use("minecraft:campfire")
    check("campfire under the sea goes down unlit", block_is(1, 186, 0, "minecraft:campfire[lit=false]"),
          "placed: %s" % block_is(1, 186, 0, "minecraft:campfire"))
    stand(0.5, 186.0, 0.5, 0.0, 90.0)
    cmd("setblock 0 187 0 minecraft:air")
    cmd("setblock 0 186 0 minecraft:fire")
    pump(0.5)
    check("fire put under the sea goes out", block_is(0, 186, 0, "minecraft:air"))
    cmd("setblock 1 186 0 minecraft:air")

    stand(20.5, 192.0, 20.5, 0.0, 90.0)
    use("minecraft:flint_and_steel")
    check("flint and steel above the sea still lights fire", block_is(20, 192, 20, "minecraft:fire"))
    cmd("setblock 20 192 20 minecraft:air")
    stand(20.5, 192.0, 20.5, -90.0, 58.0)
    use("minecraft:campfire")
    check("campfire above the sea goes down lit", block_is(21, 192, 20, "minecraft:campfire[lit=true]"))
    cmd("setblock 21 192 20 minecraft:air")
    cmd("setblock 21 191 20 minecraft:air")
    pump(0.5)

    # 3. lights
    state["lights"].clear()
    cmd("setblock -1 186 -1 minecraft:sea_lantern")
    cmd("setblock 1 186 1 minecraft:torch")
    pump(0.8)
    got = [s for m in state["lights"] for s in m["set"]]
    check("a sea lantern and a torch reach Subnautica as lights", [-1, 186, -1, 15, "sea_lantern"] in got and [1, 186, 1, 14, "torch"] in got, str(got))
    state["lights"].clear()
    cmd("setblock 1 186 1 minecraft:air")
    pump(0.8)
    cleared = [m["clear"] for m in state["lights"]]
    check("a torch taken away goes out", [1, 186, 1] in cleared, str(cleared))
    state["lights"].clear()
    send({"t": "blocksync", "r": 32})
    pump(1.5)
    resets = [m for m in state["lights"] if m.get("reset")]
    check("blocksync sends every light again", bool(resets) and [-1, 186, -1, 15, "sea_lantern"] in resets[-1]["set"], str(resets[-1:]))
    cmd("setblock -1 186 -1 minecraft:air")

    # 4. boats
    cmd("summon minecraft:oak_boat 8.5 196 8.5")
    pump(4.0)
    p = boat_pos("@e[type=minecraft:oak_boat,limit=1,x=8.5,y=190,z=8.5,distance=..8]")
    check("a boat dropped onto the sea floats at its surface", p is not None and SEA - 1.0 < p[1] < SEA + 0.3, str(p))
    pump(2.0)
    p2 = boat_pos("@e[type=minecraft:oak_boat,limit=1,x=8.5,y=190,z=8.5,distance=..8]")
    check("... and stays there", p2 is not None and p is not None and abs(p2[1] - p[1]) < 0.15, str(p2))

    cmd("kill @e[type=minecraft:oak_boat]")
    stand(20.5, 192.0, 20.5, -90.0, 60.0)  # looking down at the sea beyond the ledge
    use("minecraft:oak_boat")
    pump(1.5)
    near = "@e[type=minecraft:oak_boat,limit=1,x=22.6,y=190,z=20.5,distance=..2.5]"
    p = boat_pos(near)
    check("a boat placed from a ledge goes down on the sea", p is not None and SEA - 1.0 < p[1] < SEA + 0.3, str(p))
    if p is not None:
        cmd("ride @a[limit=1] mount " + near)
        pump(1.0)
        pump(2.5, {"k": [[26, True]]})  # W: row
        pump(0.3, {"k": [[26, False]]})
        p2 = boat_pos("@e[type=minecraft:oak_boat,limit=1]")
        moved = p2 is not None and abs(p2[0] - p[0]) + abs(p2[2] - p[2]) > 1.5
        check("Steve rows the boat along the surface", moved and SEA - 1.0 < p2[1] < SEA + 0.3, "%s -> %s" % (p, p2))
        cmd("ride @a[limit=1] dismount")

    cmd("kill @e[type=minecraft:oak_boat]")
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": False})
    pump(0.3)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
