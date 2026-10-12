"""Fake Subnautica for arrows and outcrop ores: checks Minecraft's half.

1. the command the plugin sends for a broken outcrop (summon a Minecraft ore item) makes the item,
2. an arrow Steve shoots is reported in flight ("proj"),
3. "projhit" with stick stops it where Subnautica says it hit.

    python tools/fake_combat.py --port 25612
"""
import argparse
import json
import os
import re
import sys
import time

import websocket


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
        sys.exit("no Minecraft")
    ws.recv()
    ws.settimeout(0.001)
    state = {"proj": []}

    def send(o):
        ws.send(json.dumps(o))

    def pump(seconds, inp=None):
        end = time.time() + seconds
        while time.time() < end:
            msg = {"t": "in", "look": [0.0, -10.0]}
            if inp:
                msg.update(inp)
                inp = None
            send(msg)
            try:
                while True:
                    m = json.loads(ws.recv())
                    if m["t"] == "proj":
                        state["proj"].append(m["p"])
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    results = []

    def check(name, ok, detail):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + ": " + detail)

    send({"t": "water", "y": None})
    send({"t": "mode", "steve": True})
    send({"t": "tp", "p": [0.0, 80.0, 0.0], "r": [0.0, -10.0]})
    pump(1.0)

    # exactly what Bridge.DropMinecraftItem sends
    command = 'summon minecraft:item 0.50 79.00 3.00 {Item:{id:"minecraft:raw_copper",count:1},PickupDelay:10}'
    ws.send('{"t":"cmd","c":"' + command.replace('"', '\\"') + '"}')
    send({"t": "cmd", "c": "execute if entity @e[type=minecraft:item,nbt={Item:{id:\"minecraft:raw_copper\"}}]"})
    pump(1.0)
    with open(args.log, encoding="utf-8", errors="replace") as fh:
        log = fh.read()
    check("an outcrop's ore appears as a Minecraft item", "Test passed" in log.split("raw_copper")[-1] or "Test passed, count: 1" in log,
          "summon + execute if entity: %s" % ("found" if "Test passed" in log else "not found"))
    send({"t": "cmd", "c": "kill @e[type=minecraft:item]"})

    send({"t": "cmd", "c": "item replace entity @a weapon.mainhand with minecraft:bow"})
    send({"t": "cmd", "c": "give @a minecraft:arrow 4"})
    pump(0.5)
    state["proj"].clear()
    pump(1.2, {"b": [[3, True]]})  # draw the bow
    pump(0.5, {"b": [[3, False]]})  # loose
    flights = [a for frame in state["proj"] for a in frame]
    check("an arrow in flight is reported", len(flights) > 0, "%d reports, first %s" % (len(flights), flights[:1]))
    if flights:
        arrow_id = flights[-1][0]
        send({"t": "projhit", "id": arrow_id, "pos": [0.0, 80.0, 6.0], "stick": True})
        pump(0.5)
        state["proj"].clear()
        pump(1.0)
        still = [a for frame in state["proj"] for a in frame if a[0] == arrow_id]
        check("a stuck arrow stops (no longer in flight)", not still, "reports after the hit: %d" % len(still))

    send({"t": "cmd", "c": "kill @e[type=minecraft:arrow]"})
    send({"t": "mode", "steve": False})
    pump(0.3)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")


if __name__ == "__main__":
    main()
