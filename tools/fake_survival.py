"""Fake Subnautica for survival: checks Minecraft's half of health, pause, breathing, swimming and death.

Against a running Minecraft with the SubnautiCraft mod (e.g. `gradlew runClient -PsncPort=25612`), in survival:

1. the diver's damage reaches Steve ("hurt") and healing too ("heal"),
2. Subnautica's pause pauses Minecraft: Steve doesn't sink while it lasts,
3. Minecraft's own air never runs out (Subnautica's oxygen decides drowning): 20 s underwater cost no health,
4. sprint-swimming while looking up rises, as in vanilla Minecraft,
5. a deadly hit kills Steve and he respawns at once as a new player object (Subnautica then places him).

    python tools/fake_survival.py --port 25612
"""
import argparse
import json
import sys
import time

import websocket


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
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
    state = {"pov": None, "povs": []}

    def send(obj):
        ws.send(json.dumps(obj))

    def pump(seconds, inp=None, look=(0.0, 0.0)):
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
                        state["povs"].append(m)
                    elif m["t"] == "died":
                        state["died"] = True
            except (websocket.WebSocketTimeoutException, TimeoutError):
                pass
            time.sleep(1 / 60)

    results = []

    def check(name, ok, detail):
        results.append(ok)
        print(("PASS " if ok else "FAIL ") + name + ": " + detail)

    send({"t": "view", "w": 960, "h": 540})
    send({"t": "water", "y": 0.0})
    send({"t": "pause", "on": False})
    send({"t": "mode", "steve": True})
    send({"t": "cmd", "c": "gamemode survival @a"})
    send({"t": "tp", "p": [0.0, -40.0, 0.0], "r": [0.0, 0.0]})
    send({"t": "cmd", "c": "effect clear @a"})
    send({"t": "cmd", "c": "gamerule natural_health_regeneration false"})  # Minecraft's own healing would blur the numbers
    pump(1.0)
    send({"t": "heal", "hp": 20})
    pump(1.0)
    hp0 = state["pov"]["hp"]

    send({"t": "hurt", "kind": "Heat", "hp": 4.0})
    pump(1.0)
    hp1 = state["pov"]["hp"]
    check("the diver's damage hurts Steve", abs((hp0 - hp1) - 4.0) < 0.01, "health %.1f -> %.1f" % (hp0, hp1))
    send({"t": "heal", "hp": 2.0})
    pump(1.0)
    hp2 = state["pov"]["hp"]
    check("the diver's healing heals Steve", abs((hp2 - hp1) - 2.0) < 0.01, "health %.1f -> %.1f" % (hp1, hp2))

    send({"t": "pause", "on": True})
    pump(0.5)
    y0 = state["pov"]["f"][1]
    pump(2.0)
    y1 = state["pov"]["f"][1]
    send({"t": "pause", "on": False})
    pump(2.0)
    y2 = state["pov"]["f"][1]
    check("Subnautica's pause holds Steve still", abs(y1 - y0) < 0.01 and y2 < y1 - 0.2,
          "paused: y %.3f -> %.3f; resumed: %.3f" % (y0, y1, y2))

    send({"t": "tp", "p": [0.0, -40.0, 0.0], "r": [0.0, 0.0]})
    pump(0.5)
    hp_before = state["pov"]["hp"]
    pump(20.0)
    hp_after = state["pov"]["hp"]
    check("Minecraft's air doesn't drown him (Subnautica's oxygen decides)", hp_after >= hp_before,
          "20 s underwater: health %.1f -> %.1f" % (hp_before, hp_after))

    send({"t": "tp", "p": [0.0, -40.0, 0.0], "r": [0.0, -40.0]})
    pump(0.5, look=(0.0, -40.0))
    ys = state["pov"]["f"][1]
    pump(0.1, {"k": [[224, True], [26, True]]}, look=(0.0, -40.0))  # Ctrl + W: sprint-swim
    pump(2.5, look=(0.0, -40.0))
    swimming = state["pov"]["sw"]
    ye = state["pov"]["f"][1]
    pump(0.2, {"k": [[224, False], [26, False]]}, look=(0.0, -40.0))
    check("sprint-swimming looking up rises (vanilla)", ye > ys + 2.0, "y %.2f -> %.2f in 2.5 s (in water: %s)" % (ys, ye, swimming))

    pid = state["pov"]["pid"]
    state["died"] = False
    send({"t": "hurt", "kind": "Normal", "hp": 1000.0})
    pump(3.0)
    new_pid = state["pov"]["pid"]
    check("a deadly hit kills Steve (Subnautica is told) and he respawns at once as a new player", state["died"] and new_pid != pid and not state["pov"]["dead"],
          "died event: %s, player %d -> %d, alive now: %s" % (state["died"], pid, new_pid, not state["pov"]["dead"]))

    send({"t": "cmd", "c": "gamerule natural_health_regeneration true"})
    send({"t": "cmd", "c": "gamemode creative @a"})
    send({"t": "mode", "steve": False})
    send({"t": "water", "y": None})
    pump(0.5)
    ws.close()
    print("ALL PASS" if all(results) else "SOME FAILED")
    sys.exit(0 if all(results) else 1)


if __name__ == "__main__":
    main()
