"""Fake Subnautica for blueprints and the scanner: checks Minecraft's half.

1. a Subnautica recipe can't be crafted before its blueprint is known (2 copper ore in a crafting table: no result),
2. "known" (what Subnautica sends) unlocks the recipe and its entry in the "Subnautica" advancements tab, and then it
   crafts,
3. "scanning" spends the scanner's battery.

Clicks are real mouse clicks in the crafting table (GUI scale 2 in a 960 x 540 window).

    python tools/fake_blueprints.py --port 25612
"""
import argparse
import json
import os
import re
import sys
import time

import websocket

HERE = os.path.dirname(os.path.abspath(__file__))
SCALE = 2


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

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": [0.0, 10.0]}
            if inp:
                msg.update(inp)
                inp = None
            send(msg)
            try:
                while True:
                    ws.recv()
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

    def click(gx, gy, button=1, shift=False):
        x, y = gx * SCALE, gy * SCALE
        pump(0.15, {"c": [x, y]})
        if shift:
            pump(0.05, {"k": [[225, True]]})  # left shift
        pump(0.1, {"c": [x, y], "b": [[button, True]]})
        pump(0.25, {"c": [x, y], "b": [[button, False]]})
        if shift:
            pump(0.05, {"k": [[225, False]]})

    left, top = (480 - 176) // 2, (270 - 166) // 2

    def craft_two_copper():
        """Two copper ores into the crafting grid, then shift-click whatever the result is."""
        send({"t": "menu", "kind": "crafting", "title": "Fabricador"})
        pump(1.0)
        click(left + 8 + 8, top + 142 + 8)          # hotbar slot 0: pick up both ores
        click(left + 30 + 8, top + 17 + 8, 3)        # grid 0: drop one (right click)
        click(left + 30 + 18 + 8, top + 17 + 8, 3)   # grid 1: drop the other
        click(left + 124 + 8, top + 35 + 8, shift=True)  # the result, into the inventory
        pump(0.1, {"k": [[41, True]]})
        pump(0.6, {"k": [[41, False]]})

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "pause", "on": False})
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": True})
    send({"t": "cmd", "c": "gamemode survival @a"})
    send({"t": "cmd", "c": "recipe take @a *"})
    send({"t": "cmd", "c": "advancement revoke @a everything"})
    send({"t": "cmd", "c": "setblock 0 99 0 minecraft:stone"})
    send({"t": "tp", "p": [0.5, 100.0, 0.5], "r": [0.0, 10.0]})
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "item replace entity @a hotbar.0 with subnauticraft:copper_ore 2"})
    pump(2.0)

    craft_two_copper()
    out = query("execute if items entity @a container.* subnauticraft:copper_wire")
    check("without its blueprint, copper wire can't be crafted", "Test failed" in out)
    out = query("execute if items entity @a container.* subnauticraft:copper_ore[count=2]")
    check("... and the ores come back", "Test passed" in out)

    send({"t": "known", "new": True, "items": ["copper_wire"]})
    pump(1.0)
    out = query("execute if entity @a[advancements={subnauticraft:subnautica/copper_wire=true}]")
    check("the blueprint shows in the Subnautica advancements tab", "Test passed" in out, out.strip()[-80:])
    craft_two_copper()
    out = query("execute if items entity @a container.* subnauticraft:copper_wire")
    check("with the blueprint known, it crafts", "Test passed" in out)

    send({"t": "cmd", "c": "item replace entity @a weapon.mainhand with subnauticraft:scanner"})
    pump(0.5)
    for _ in range(3):
        send({"t": "scanning"})
        pump(0.3)
    out = query("data get entity @a[limit=1] SelectedItem.components")
    m = re.search(r'"minecraft:damage": (\d+)', out)
    check("scanning spends the scanner's battery", m is not None and int(m.group(1)) == 3, out.strip()[-120:])

    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "mode", "steve": False})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
