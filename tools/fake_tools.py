"""Fake Subnautica for saves, tools and food: checks Minecraft's half.

1. each Subnautica save gets its own world: "save" opens subnauticraft_<slot> (made the first time), and another save
   makes Minecraft save, leave and open that one,
2. the flashlight in Steve's hand is reported to Subnautica ("tool") and drains its battery; the Seaglide gives Dolphin's
   Grace in the water; a battery recharges them,
3. eating a Subnautica fish is reported ("ate"), so the diver's food bar fills; raw fish cook in a furnace.

    python tools/fake_tools.py --port 25612
"""
import argparse
import json
import os
import re
import sys
import time

import websocket

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    ap.add_argument("--log", default=os.path.join(HERE, "..", "mc", "run-client.log"))
    ap.add_argument("--game-dir", default=os.path.join(HERE, "..", "mc", "run"))
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
    state = {"pov": None, "ate": []}

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None, look=(0.0, 30.0)):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": list(look)}
            if inp:
                msg.update(inp)
                inp = None
            send(msg)
            try:
                while True:
                    m = json.loads(ws.recv())
                    if m["t"] == "pov":
                        state["pov"] = m
                    elif m["t"] == "ate":
                        state["ate"].append(m)
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    def log_since(start):
        with open(args.log, encoding="utf-8", errors="replace") as fh:
            fh.seek(start)
            return fh.read()

    def query(c, wait=0.8):
        start = os.path.getsize(args.log)
        send({"t": "cmd", "c": c})
        pump(wait)
        return log_since(start)

    def wait_for(text, seconds):
        start = os.path.getsize(args.log)
        end = time.time() + seconds
        while time.time() < end:
            pump(0.5)
            if text in log_since(start):
                return True
        return False

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "pause", "on": False})

    # 1. a world per save
    saves = os.path.join(args.game_dir, "saves")
    start = os.path.getsize(args.log)
    send({"t": "save", "slot": "test_a"})
    joined = wait_for("joined the game", 90)
    check("save test_a opens world subnauticraft_test_a", joined and os.path.isdir(os.path.join(saves, "subnauticraft_test_a")),
          [l for l in log_since(start).splitlines() if "world" in l and "subnauticraft" in l][-2:].__str__())
    send({"t": "cmd", "c": "setblock 3 100 3 minecraft:gold_block"})
    pump(1.0)
    send({"t": "save", "slot": "test_b"})
    left = wait_for("leaving world", 20)
    joined = wait_for("joined the game", 90)
    check("save test_b: Minecraft leaves test_a and opens its own world", left and joined and os.path.isdir(os.path.join(saves, "subnauticraft_test_b")))
    out = query("execute if block 3 100 3 minecraft:gold_block")
    check("... which doesn't have test_a's blocks", "Test failed" in out)
    send({"t": "save", "slot": "test_a"})
    wait_for("leaving world", 20)
    wait_for("joined the game", 90)
    out = query("execute if block 3 100 3 minecraft:gold_block")
    check("back to test_a: its gold block is still there", "Test passed" in out)

    # 2. tools
    send({"t": "water", "y": 150.0})
    send({"t": "mode", "steve": True})
    send({"t": "cmd", "c": "gamemode survival @a"})
    send({"t": "cmd", "c": "setblock 0 99 0 minecraft:stone"})
    send({"t": "tp", "p": [0.5, 100.0, 0.5], "r": [0.0, 30.0]})
    pump(2.0)
    send({"t": "cmd", "c": "item replace entity @a weapon.mainhand with subnauticraft:flashlight"})
    pump(4.5)
    tool = state["pov"].get("tool") if state["pov"] else None
    check("the flashlight in hand is reported to Subnautica", tool == "flashlight", "tool=%r" % tool)
    out = query("data get entity @a[limit=1] SelectedItem.components")
    m = re.search(r'"minecraft:damage": (\d+)', out)
    check("the flashlight drains its battery while held", m is not None and int(m.group(1)) >= 2, out.strip()[-160:])
    out = query("recipe give @a subnauticraft:flashlight_recharge")
    check("a battery recharges it (recipe)", "nlocked" in out or "No new" in out or "couldn't" in out.lower(), out.strip()[-100:])
    send({"t": "cmd", "c": "item replace entity @a weapon.mainhand with subnauticraft:seaglide"})
    pump(1.5)
    out = query("data get entity @a[limit=1] active_effects")
    check("the Seaglide in the water gives Dolphin's Grace", "dolphins_grace" in out, out.strip()[-160:])
    check("... and is reported as the tool in hand", state["pov"].get("tool") == "seaglide", "tool=%r" % state["pov"].get("tool"))

    # 3. food
    send({"t": "cmd", "c": "effect give @a minecraft:hunger 3 200 true"})
    pump(3.5)
    send({"t": "cmd", "c": "item replace entity @a weapon.mainhand with subnauticraft:cooked_peeper 4"})
    pump(0.5)
    state["ate"].clear()
    pump(2.5, {"b": [[3, True]]})  # hold right click: eat
    pump(0.3, {"b": [[3, False]]})
    ate = state["ate"]
    check("eating a cooked peeper is reported to Subnautica", any(a["item"] == "subnauticraft:cooked_peeper" and a["n"] == 5 for a in ate), str(ate))
    out = query("recipe give @a subnauticraft:cooked_peeper_from_smelting")
    check("raw peepers cook in a furnace", "nlocked" in out, out.strip()[-100:])

    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": False})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
