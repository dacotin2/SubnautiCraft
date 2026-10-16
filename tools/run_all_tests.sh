#!/usr/bin/env bash
# Starts the test Minecraft (port 25612) and runs every fake-Subnautica test against it.
cd "$(dirname "$0")/PatchCheck" || exit 1
echo "== patches (Subnautica's side, without the game)"
"${DOTNET:-$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe}" run -c Release 2>&1 | tail -2
cd ../../mc || exit 1
./gradlew build --offline -q 2>&1 | grep -v "^Note:" | tail -10
(./gradlew.bat --console=plain runClient -PsncPort=25612 > run-client.log 2>&1 &)
for i in $(seq 1 80); do grep -q "Sound engine started" run-client.log 2>/dev/null && break; sleep 3; done
sleep 4
cd ..
PY=tools/.venv/Scripts/python.exe
$PY -I - <<'EOF'
import json, time, websocket
ws = websocket.create_connection('ws://127.0.0.1:25612/', timeout=5); ws.recv()
ws.send(json.dumps({'t': 'save', 'slot': 'regress'}))
time.sleep(1); ws.close()
EOF
for i in $(seq 1 40); do grep -q "joined the game" mc/run-client.log && break; sleep 2; done
sleep 2
$PY -I - <<'EOF'
import json, time, websocket
ws = websocket.create_connection('ws://127.0.0.1:25612/', timeout=5); ws.recv()
for c in ['item replace entity @a hotbar.0 with minecraft:grass_block 64', 'item replace entity @a hotbar.7 with minecraft:diamond_sword', 'gamemode creative @a']:
    ws.send(json.dumps({'t': 'cmd', 'c': c}))
time.sleep(1); ws.close()
EOF
for t in fake_steve fake_sea fake_items fake_menus fake_fights fake_blueprints fake_pickups fake_dig; do
  echo "== $t"
  timeout 400 $PY -I tools/$t.py --port 25612 2>&1 | grep -E "^FAIL|ALL PASS|SOME FAILED"
done
