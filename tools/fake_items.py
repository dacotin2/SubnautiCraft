"""Fake Subnautica for its materials as Minecraft items: checks Minecraft's half.

1. the items exist (a pickup gives one) and Subnautica's recipes loaded,
2. icons sent the way the plugin sends them ({"t":"icon"} + {"t":"icons"}) become a switched-on resource pack and show
   in the hotbar (the overlay is saved as items_hotbar.png to look at).

    python tools/fake_items.py --port 25612
"""
import argparse
import base64
import io
import json
import os
import subprocess
import sys
import threading
import time

import numpy as np
import websocket
from PIL import Image

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
    hello = json.loads(ws.recv())
    ws.settimeout(0.001)

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": [0.0, 20.0]}
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

    results = []

    def check(name, ok, detail=""):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + (": " + detail if detail else ""))

    with open(args.log, encoding="utf-8", errors="replace") as fh:
        boot = fh.read()
    bad = [l for l in boot.splitlines() if "subnauticraft" in l and ("recipe" in l.lower() and ("error" in l.lower() or "couldn't" in l.lower()))]
    check("Subnautica's recipes load without errors", not bad, "; ".join(bad[:3]))

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "pause", "on": False})
    send({"t": "water", "y": None})
    send({"t": "mode", "steve": True})
    send({"t": "hud", "hidden": False})
    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "cmd", "c": "setblock 0 199 0 minecraft:stone"})
    send({"t": "tp", "p": [0.5, 200.0, 0.5], "r": [0.0, 20.0]})
    pump(1.5)

    # what the plugin sends for a picked-up copper ore
    send({"t": "cmd", "c": "clear @a"})
    send({"t": "cmd", "c": "give @a subnauticraft:copper_ore 1"})
    send({"t": "cmd", "c": "give @a subnauticraft:copper_ore 1"})
    pump(0.5)
    out = query("execute if items entity @a container.* subnauticraft:copper_ore[count=2]")
    check("two picked-up copper ores are in Steve's inventory", "Test passed" in out, out.strip()[-120:])
    send({"t": "cmd", "c": "recipe take @a *"})  # so they can be unlocked again on every run
    pump(0.3)
    out = query("recipe give @a subnauticraft:copper_wire")
    check("the copper wire recipe exists", "Unlocked" in out or "nlocked" in out, out.strip()[-120:])
    out = query("recipe give @a subnauticraft:computer_chip")
    check("the computer chip recipe exists", "nlocked" in out, out.strip()[-120:])

    # icons as the plugin sends them: a striking test picture, so it's obvious in the hotbar
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for y in range(32):
        for x in range(32):
            if (x // 4 + y // 4) % 2 == 0 and 4 <= x < 28 and 4 <= y < 28:
                img.putpixel((x, y), (255, 40, 200, 255))
            elif 4 <= x < 28 and 4 <= y < 28:
                img.putpixel((x, y), (40, 255, 255, 255))
    buf = io.BytesIO()
    img.save(buf, "PNG")
    png = base64.b64encode(buf.getvalue()).decode()
    start = os.path.getsize(args.log)
    send({"t": "icon", "n": "copper_wire", "png": png})
    send({"t": "icons", "names": {"copper_wire": "Cable de cobre (de Subnautica)"}})
    pump(8.0)
    with open(args.log, encoding="utf-8", errors="replace") as fh:
        fh.seek(start)
        log = fh.read()
    pack = os.path.join(args.game_dir, "resourcepacks", "SubnautiCraft Icons")
    written = os.path.exists(os.path.join(pack, "assets", "subnauticraft", "textures", "item", "copper_wire.png"))
    check("the icon is written into the resource pack", written, pack)
    check("the pack is switched on (or already was)", "switched on" in log or "changed, reloading" in log or "up to date" in log,
          [l for l in log.splitlines() if "icons:" in l][-1:].__str__())

    send({"t": "cmd", "c": "item replace entity @a hotbar.0 with subnauticraft:copper_wire"})
    send({"t": "cmd", "c": "item replace entity @a hotbar.1 with subnauticraft:titanium"})
    pump(0.3, {"k": [[30, True]]})  # 1
    pump(1.5, {"k": [[30, False]]})
    grab = os.path.join(HERE, "..", "mc", "run", "items_frame.npz")
    name = hello.get("shm", "Local\\SubnautiCraftFrame")
    t = threading.Thread(target=lambda: subprocess.run([sys.executable, "-I", os.path.join(HERE, "frame_grab.py"), grab, name], capture_output=True))
    t.start()
    while t.is_alive():
        pump(0.1)
    f = np.load(grab)
    over = f["overlay"][::-1]
    h, w = over.shape[:2]
    crop = Image.fromarray(over[h - 50:h, w // 2 - 200:w // 2 + 200]).resize((1200, 150), Image.NEAREST)
    crop.save(os.path.join(HERE, "..", "mc", "run", "items_hotbar.png"))
    region = over[h - 45:h, w // 2 - 190:w // 2 - 135, :3].reshape(-1, 3).astype(int)  # the first slot (GUI scale 2)
    magenta = np.sum((region[:, 0] > 200) & (region[:, 1] < 90) & (region[:, 2] > 160))
    check("the test icon shows in the hotbar's first slot", magenta > 10, "%d test-icon pixels" % magenta)

    send({"t": "mode", "steve": False})
    pump(0.3)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
