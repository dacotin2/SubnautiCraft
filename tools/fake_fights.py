"""Fake Subnautica for fights between Minecraft's mobs and Subnautica's creatures: checks Minecraft's half.

1. Minecraft reports its mobs near the player ("mobs"), so Subnautica can give them bodies,
2. a Subnautica creature reported here ("creatures") gets an invisible stand-in that a drowned hunts and hits; the hit
   comes back to Subnautica ("creaturehit"),
3. a Subnautica bite on a mob ("mobhit") hurts the mob in Minecraft,
4. path finding sees Subnautica's terrain: a cod near a rock wall keeps swimming and never ends up inside it.

    python tools/fake_fights.py --port 25612
"""
import argparse
import json
import os
import re
import sys
import time

import websocket

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fake_steve import terrain_chunk, FLOOR_Y, WALL_Z  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


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
    state = {"mobs": None, "hits": []}
    creature = {"pos": None}

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None):
        end = time.time() + seconds
        last_creature = 0.0
        while time.time() < end:
            msg = {"t": "in", "look": [0.0, 10.0]}
            if inp:
                msg.update(inp)
                inp = None
            send(msg)
            if creature["pos"] and time.time() - last_creature > 0.25:
                last_creature = time.time()
                x, y, z = creature["pos"]
                send({"t": "creatures", "c": [[4242, x, y, z, 1.5]]})
            try:
                while True:
                    m = json.loads(ws.recv())
                    if m["t"] == "mobs":
                        state["mobs"] = m["m"]
                    elif m["t"] == "creaturehit":
                        state["hits"].append(m)
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
    send({"t": "water", "y": 0.0})
    send({"t": "mode", "steve": True})
    send({"t": "cmd", "c": "gamemode creative @a"})  # monsters leave a creative player alone: only the creature is prey
    send({"t": "cmd", "c": "kill @e[type=!minecraft:player]"})
    send({"t": "tp", "p": [0.5, FLOOR_Y + 0.2, 0.5], "r": [0.0, 10.0]})
    pump(2.0)

    # 1. mobs reported
    send({"t": "cmd", "c": "summon minecraft:cod 2.5 %.1f 4.5" % (FLOOR_Y + 3)})
    pump(1.0)
    mobs = state["mobs"] or []
    check("Minecraft reports its mobs to Subnautica", len(mobs) >= 1, str(mobs[:2]))

    # 3. a Subnautica bite hurts the mob
    cod = mobs[0][0] if mobs else -1
    send({"t": "mobhit", "id": cod, "dmg": 100.0})
    pump(1.0)
    out = query("execute if entity @e[type=minecraft:cod]")
    check("a Subnautica bite kills the cod in Minecraft", "Test failed" in out, out.strip()[-80:])

    # 2. a drowned hunts a Subnautica creature's stand-in
    creature["pos"] = (-3.5, FLOOR_Y + 2.0, 3.5)
    pump(1.0)
    out = query("execute if entity @e[type=subnauticraft:subnautica_creature]")
    check("the Subnautica creature has a stand-in in Minecraft", "Test passed" in out, out.strip()[-80:])
    send({"t": "cmd", "c": "summon minecraft:drowned -3.5 %.1f 0.5" % (FLOOR_Y + 1)})
    state["hits"].clear()
    pump(12.0)
    check("a drowned attacks it, and the hit goes to Subnautica", any(h["id"] == 4242 for h in state["hits"]), str(state["hits"][:2]))
    creature["pos"] = None
    send({"t": "creatures", "c": []})
    pump(1.0)
    out = query("execute if entity @e[type=subnauticraft:subnautica_creature]")
    check("when the creature is gone, so is its stand-in", "Test failed" in out, out.strip()[-80:])

    # 4. a cod by the rock wall keeps clear of it
    send({"t": "cmd", "c": "kill @e[type=!minecraft:player]"})
    send({"t": "cmd", "c": "summon minecraft:cod 0.5 %.1f %.1f" % (FLOOR_Y + 2, WALL_Z - 1.5)})
    pump(1.0)  # reports from before the kill are gone
    inside = 0
    samples = 0
    for _ in range(10):
        pump(1.0)
        for m in [m for m in state["mobs"] or [] if abs(m[4] - 0.5) < 0.01 and abs(m[5] - 0.3) < 0.01]:  # the cod
            samples += 1
            if m[3] > WALL_Z + 0.1 and m[2] < FLOOR_Y + 10:
                inside += 1
    check("a cod by Subnautica's rock wall never swims into it", samples > 0 and inside == 0, "%d of %d samples past the wall" % (inside, samples))

    send({"t": "cmd", "c": "kill @e[type=!minecraft:player]"})
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": False})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
