"""Fake Subnautica for its machines as Minecraft screens: checks Minecraft's half.

1. a locker opens a Minecraft chest, and what Steve puts in it is kept in the world (saved data "lockers"),
2. the fabricator opens a crafting table,
3. the vehicle bay lists a vehicle; clicking it with the materials asks Subnautica to build it ("build"), and once
   Subnautica says the bay started ("built"), the materials are taken.

Clicks are real mouse clicks in the screens (GUI scale 2 in a 960 x 540 window).

    python tools/fake_menus.py --port 25612
"""
import argparse
import gzip
import json
import os
import sys
import time

import websocket

HERE = os.path.dirname(os.path.abspath(__file__))
SCALE = 2  # Minecraft's automatic GUI scale for 960 x 540


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
    state = {"pov": None, "build": []}

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
                    if m["t"] == "pov":
                        state["pov"] = m
                    elif m["t"] == "build":
                        state["build"].append(m)
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

    def click(gx, gy, button=1):
        """Left click at GUI coordinates (window pixels / scale)."""
        x, y = gx * SCALE, gy * SCALE
        pump(0.15, {"c": [x, y]})
        pump(0.1, {"c": [x, y], "b": [[button, True]]})
        pump(0.25, {"c": [x, y], "b": [[button, False]]})

    def screen_open():
        return bool(state["pov"] and state["pov"].get("scr"))

    def close_screen():
        pump(0.1, {"k": [[41, True]]})  # Escape
        pump(0.5, {"k": [[41, False]]})

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "pause", "on": False})
    send({"t": "save", "slot": "test_menus"})
    wait_for("joined the game", 90)
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": True})
    send({"t": "cmd", "c": "gamemode survival @a"})
    send({"t": "cmd", "c": "setblock 0 99 0 minecraft:stone"})
    send({"t": "tp", "p": [0.5, 100.0, 0.5], "r": [0.0, 10.0]})
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "item replace entity @a hotbar.0 with minecraft:diamond 5"})
    pump(2.0)

    locker = "locker-test-%d" % int(time.time())  # a new one each run
    # 1. a locker: 3 rows; chest screen 176 x 168 GUI px centred in 480 x 270
    send({"t": "menu", "kind": "chest", "id": locker, "rows": 3, "title": "Taquilla"})
    pump(1.0)
    check("a locker opens a Minecraft chest", screen_open())
    left, top = (480 - 176) // 2, (270 - 168) // 2
    click(left + 16, top + 143 + 8)   # hotbar slot 0: pick up the diamonds
    click(left + 16, top + 18 + 8)    # chest slot 0: put them down
    close_screen()
    check("the chest closes", not screen_open())
    path = os.path.join(args.game_dir, "saves", "subnauticraft_test_menus", "dimensions", "minecraft", "overworld", "data", "subnauticraft", "lockers.dat")
    saved = b""
    for _ in range(20):  # the world saves its data in the background, a moment later
        if os.path.exists(path):
            raw = open(path, "rb").read()
            try:
                saved = gzip.decompress(raw)
            except OSError:
                saved = raw
        if locker.encode() in saved:
            break
        pump(0.5)
    check("what was put in the locker is kept in the world", locker.encode() in saved and b"diamond" in saved, path)
    out = query("execute if items entity @a container.* minecraft:diamond")
    check("... and is no longer in Steve's inventory", "Test failed" in out)
    send({"t": "menu", "kind": "chest", "id": locker, "rows": 3, "title": "Taquilla"})
    pump(1.0)
    click(left + 16, top + 18 + 8)    # chest slot 0: take the diamonds back
    click(left + 16, top + 143 + 8)   # hotbar slot 0
    close_screen()
    out = query("execute if items entity @a container.* minecraft:diamond[count=5]")
    check("reopening the locker gives them back", "Test passed" in out)

    # 2. the fabricator
    send({"t": "menu", "kind": "crafting", "title": "Fabricador"})
    pump(1.0)
    check("the fabricator opens a crafting table", screen_open())
    close_screen()

    # 3. the vehicle bay
    send({"t": "cmd", "c": "give @a subnauticraft:titanium_ingot 2"})
    pump(0.5)
    option = {"tech": "Seamoth", "item": "seamoth", "name": "Seamoth",
              "ingredients": [["titanium_ingot", 2, "Lingote de titanio"]]}
    send({"t": "menu", "kind": "vehicles", "id": "bay-1", "title": "Creador de vehículos", "options": [option]})
    pump(1.0)
    check("the vehicle bay opens its screen", screen_open())
    top1 = (270 - (114 + 18)) // 2
    state["build"].clear()
    click(left + 16, top1 + 18 + 8)   # the Seamoth card
    pump(0.5)
    asked = state["build"]
    check("clicking the Seamoth asks Subnautica to build it", any(b["tech"] == "Seamoth" and b["id"] == "bay-1" for b in asked), str(asked))
    out = query("execute if items entity @a container.* subnauticraft:titanium_ingot[count=2]")
    check("... the materials stay until the bay starts", "Test passed" in out)
    send({"t": "built", "tech": "Seamoth", "ok": True})
    pump(0.8)
    out = query("execute if items entity @a container.* subnauticraft:titanium_ingot")
    check("once it starts, the materials are taken", "Test failed" in out)
    if screen_open():
        close_screen()

    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "mode", "steve": False})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
