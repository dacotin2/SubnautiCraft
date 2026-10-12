"""Measures Minecraft's frame rate while Steve swims across chunks (one "pov" arrives per Minecraft frame).

    python tools/fps_probe.py --port 25612 --distance 8 --view 1440x810
"""
import argparse
import json
import sys
import time

import websocket


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=25611)
    ap.add_argument("--distance", type=int, default=8)
    ap.add_argument("--view", default="1440x810")
    ap.add_argument("--seconds", type=float, default=20.0)
    args = ap.parse_args()
    w, h = (int(v) for v in args.view.split("x"))
    ws = websocket.create_connection("ws://127.0.0.1:%d/" % args.port, timeout=5)
    ws.recv()
    ws.settimeout(0.0005)
    send = lambda o: ws.send(json.dumps(o))
    send({"t": "view", "w": w, "h": h})
    send({"t": "distance", "v": args.distance})
    send({"t": "water", "y": 0.0})
    send({"t": "pause", "on": False})
    send({"t": "mode", "steve": True})
    send({"t": "tp", "p": [1000.0, -30.0, 1000.0], "r": [0.0, 0.0]})
    counts = []
    last_send = 0.0
    start = time.time()
    second = start
    n = 0
    pressed = False
    while time.time() - start < args.seconds:
        now = time.time()
        if now - last_send > 1 / 60:
            last_send = now
            msg = {"t": "in", "look": [((now - start) * 20.0) % 360.0 - 180.0, 0.0]}
            if not pressed and now - start > 2.0:
                msg["k"] = [[224, True], [26, True]]  # sprint-swim forward, turning: new chunks all the time
                pressed = True
            send(msg)
        try:
            while True:
                m = ws.recv()
                if m.startswith('{"t":"pov"'):
                    n += 1
        except (websocket.WebSocketTimeoutException, TimeoutError):
            pass
        if now - second >= 1.0:
            counts.append(n)
            n = 0
            second = now
    send({"t": "in", "look": [0, 0], "k": [[224, False], [26, False]]})
    send({"t": "mode", "steve": False})
    send({"t": "water", "y": None})
    time.sleep(0.3)
    ws.close()
    steady = counts[3:]
    print("distance %d, view %dx%d: fps per second %s -> average %.1f, worst %d" % (
        args.distance, w, h, counts, sum(steady) / max(1, len(steady)), min(steady) if steady else -1))


if __name__ == "__main__":
    main()
