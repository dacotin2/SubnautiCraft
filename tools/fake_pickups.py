"""Fake Subnautica for what Steve picks up in Subnautica's world: checks Minecraft's half.

1. a material with its own Minecraft item arrives as that ({"t":"give","item":...}),
2. anything else (sulphur, "CrashPowder") arrives as a "Subnautica item" with its kind, its name and its icon's model,
3. Subnautica's food and drink can be eaten, and what they give the diver goes back ("ate" with food and water),
4. the first aid kit heals Steve and is used up,
5. metal salvage into titanium needs no blueprint (Subnautica never locks it),
6. Q on one of Subnautica's things sends it to Subnautica's world ({"t":"drop"}) instead of dropping it in Minecraft,
7. right click with the mobile vehicle bay places it in Subnautica ({"t":"place"}),
8. the rest of Subnautica's fabricator ({"t":"recipes"}) becomes recipes in the world: locked until the blueprint is
   known, then they craft, Subnautica items in and out.

    python tools/fake_pickups.py --port 25612
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
    ate = []
    got = []

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
                    m = json.loads(ws.recv())
                    if m.get("t") == "ate":
                        ate.append(m)
                    elif m.get("t") in ("drop", "place"):
                        got.append(m)
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

    def health():
        out = query("data get entity @a[limit=1] Health")
        m = re.findall(r"has the following entity data: ([\d.]+)f", out)
        return float(m[-1]) if m else None

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
    send({"t": "cmd", "c": "setblock 0 99 0 minecraft:stone"})
    send({"t": "tp", "p": [0.5, 100.0, 0.5], "r": [0.0, 10.0]})
    send({"t": "cmd", "c": "clear @a"})
    pump(2.0)

    # 1. a material
    send({"t": "give", "item": "copper_ore", "n": 2})
    pump(0.5)
    out = query("execute if items entity @a container.* subnauticraft:copper_ore[count=2]")
    check("a material arrives as its own Minecraft item", "Test passed" in out, out.strip()[-80:])

    # 2. anything else
    send({"t": "give", "tech": "CrashPowder", "name": "Sedimento sulfúrico", "n": 1})
    pump(0.5)
    out = query('execute if items entity @a container.* subnauticraft:subnautica_item[custom_data~{tech:"CrashPowder"},custom_model_data={strings:["tech_crashpowder"]}]')
    check("sulphur arrives as a Subnautica item with its kind and picture", "Test passed" in out, out.strip()[-80:])
    out = query("data get entity @a[limit=1] Inventory")
    check("... named as Subnautica names it", "Sedimento sulf" in out, out.strip()[-200:])
    send({"t": "give", "tech": "CrashPowder", "name": "Sedimento sulfúrico", "n": 1})
    pump(0.5)
    out = query('execute if items entity @a container.* subnauticraft:subnautica_item[custom_data~{tech:"CrashPowder"},count=2]')
    check("... and two of them stack", "Test passed" in out, out.strip()[-80:])

    # 3. food and drink
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "give", "tech": "FilteredWater", "name": "Agua filtrada", "n": 1, "food": 0.0, "water": 20.0})
    pump(0.5)
    out = query("execute if items entity @a hotbar.0 subnauticraft:subnautica_item[minecraft:food]")
    check("filtered water can be drunk in Minecraft", "Test passed" in out, out.strip()[-80:])
    ate.clear()
    pump(0.3, {"k": [[30, True]]})  # 1: hotbar slot 0
    pump(0.2, {"k": [[30, False]]})
    pump(2.5, {"b": [[3, True]]})  # hold right click: drink (a drink needs no hunger)
    pump(0.3, {"b": [[3, False]]})
    check("drinking it tells Subnautica which drink (it gives the diver its water)", any(a.get("tech") == "FilteredWater" for a in ate), str(ate))
    out = query("execute if items entity @a container.* subnauticraft:subnautica_item")
    check("... and the bottle is gone", "Test failed" in out, out.strip()[-80:])

    # 4. the first aid kit
    send({"t": "cmd", "c": "item replace entity @a hotbar.0 with subnauticraft:first_aid_kit 2"})
    send({"t": "cmd", "c": "damage @p 12 minecraft:generic"})
    pump(1.0)
    before = health()
    pump(0.3, {"b": [[3, True]]})
    pump(0.5, {"b": [[3, False]]})
    after = health()
    check("the first aid kit heals Steve", before is not None and after is not None and after >= before + 9.9, "%s -> %s" % (before, after))
    out = query("execute if items entity @a hotbar.0 subnauticraft:first_aid_kit[count=1]")
    check("... and one kit is used up", "Test passed" in out, out.strip()[-80:])

    # 5. salvage into titanium, no blueprint known
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "item replace entity @a hotbar.0 with subnauticraft:scrap_metal 1"})
    send({"t": "menu", "kind": "crafting", "title": "Fabricador"})
    pump(1.0)
    left, top = (480 - 176) // 2, (270 - 166) // 2
    click(left + 8 + 8, top + 142 + 8)                # hotbar slot 0: pick up the salvage
    click(left + 30 + 8, top + 17 + 8)                # into the grid
    click(left + 124 + 8, top + 35 + 8, shift=True)   # the result, into the inventory
    pump(0.1, {"k": [[41, True]]})
    pump(0.6, {"k": [[41, False]]})
    out = query("execute if items entity @a container.* subnauticraft:titanium[count=4]")
    check("metal salvage crafts into 4 titanium without a blueprint", "Test passed" in out, out.strip()[-80:])

    # 6. Q sends it to Subnautica
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "item replace entity @a hotbar.0 with subnauticraft:quartz 3"})
    send({"t": "give", "tech": "CrashPowder", "name": "Sedimento sulfúrico", "n": 1})
    pump(0.5)
    got.clear()
    pump(0.3, {"k": [[30, True]]})  # 1: hotbar slot 0
    pump(0.2, {"k": [[30, False]]})
    pump(0.2, {"k": [[20, True]]})  # Q
    pump(0.6, {"k": [[20, False]]})
    check("Q on quartz sends one to Subnautica", any(g == {"t": "drop", "item": "quartz", "n": 1} for g in got), str(got))
    out = query("execute if items entity @a hotbar.0 subnauticraft:quartz[count=2]")
    check("... two are left", "Test passed" in out, out.strip()[-80:])
    out = query('execute if entity @e[type=minecraft:item,nbt={Item:{id:"subnauticraft:quartz"}}]')
    check("... and no quartz fell in Minecraft", "Test failed" in out, out.strip()[-80:])
    got.clear()
    pump(0.3, {"k": [[31, True]]})  # 2: hotbar slot 1, the sulphur
    pump(0.2, {"k": [[31, False]]})
    pump(0.2, {"k": [[20, True]]})
    pump(0.6, {"k": [[20, False]]})
    check("Q on a Subnautica item sends that kind", any(g.get("t") == "drop" and g.get("tech") == "CrashPowder" for g in got), str(got))

    # 7. placing the mobile vehicle bay
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "give", "tech": "Constructor", "name": "Creador de vehículos móvil", "n": 1})
    pump(0.5)
    got.clear()
    pump(0.3, {"k": [[30, True]]})
    pump(0.2, {"k": [[30, False]]})
    pump(0.2, {"b": [[3, True]]})
    pump(0.6, {"b": [[3, False]]})
    check("right click places the vehicle bay in Subnautica", any(g.get("t") == "place" and g.get("tech") == "Constructor" for g in got), str(got))
    out = query("execute if items entity @a container.* subnauticraft:subnautica_item")
    check("... and it leaves the inventory", "Test failed" in out, out.strip()[-80:])

    # 8. the rest of the fabricator
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "recipe take @a *"})
    start = os.path.getsize(args.log)
    send({"t": "recipes", "r": [
        {"out": {"tech": "Constructor", "name": "Creador de vehículos móvil"}, "n": 1,
         "in": [{"item": "titanium_ingot", "n": 1}, {"item": "lubricant", "n": 1}, {"item": "power_cell", "n": 1}]},
        {"out": {"tech": "Beacon", "name": "Baliza"}, "n": 1,
         "in": [{"item": "copper_wire", "n": 1}, {"tech": "CrashPowder", "name": "Sedimento sulfúrico", "n": 1}]},
    ]})
    reloaded = False
    for _ in range(40):
        pump(0.5)
        with open(args.log, encoding="utf-8", errors="replace") as fh:
            fh.seek(start)
            text = fh.read()
        if "data packs reloaded" in text or "all up to date" in text:
            reloaded = True
            break
    check("Subnautica's recipes are written and loaded in the world", reloaded, text.strip()[-160:])

    def craft_beacon():
        send({"t": "cmd", "c": "clear @a"})
        send({"t": "cmd", "c": "item replace entity @a hotbar.0 with subnauticraft:copper_wire 1"})
        send({"t": "give", "tech": "CrashPowder", "name": "Sedimento sulfúrico", "n": 1})
        pump(0.5)
        send({"t": "menu", "kind": "crafting", "title": "Fabricador"})
        pump(1.0)
        click(left + 8 + 8, top + 142 + 8)               # hotbar 0: the wire
        click(left + 30 + 8, top + 17 + 8)               # grid 0
        click(left + 8 + 18 + 8, top + 142 + 8)          # hotbar 1: the sulphur
        click(left + 30 + 18 + 8, top + 17 + 8)          # grid 1
        click(left + 124 + 8, top + 35 + 8, shift=True)  # the result
        pump(0.1, {"k": [[41, True]]})
        pump(0.6, {"k": [[41, False]]})
        return query('execute if items entity @a container.* subnauticraft:subnautica_item[custom_data~{tech:"Beacon"}]')

    out = craft_beacon()
    check("a fabricator recipe is locked before its blueprint", "Test failed" in out, out.strip()[-80:])
    send({"t": "known", "new": True, "items": ["tech_beacon"]})
    pump(1.0)
    out = craft_beacon()
    check("with the blueprint, copper wire + sulphur (a Subnautica item) craft a beacon (another)", "Test passed" in out, out.strip()[-80:])
    out = query("execute if entity @a[advancements={subnauticraft:subnautica/tech_beacon=true}]")
    check("... and the beacon shows in the Subnautica advancements tab", "Test passed" in out, out.strip()[-80:])

    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "mode", "steve": False})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
