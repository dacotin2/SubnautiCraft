# SubnautiCraft: Minecraft inside Subnautica

by **dacotin2**

You play Subnautica **as Steve**: you move and swim with Minecraft's physics, with Minecraft's hand, hotbar and
inventory, and you place and break blocks on Subnautica's real seafloor. Minecraft's blocks behave like part of
Subnautica's world:

- They are lit by Subnautica (its sun, caustics, water fog, lamps), cast shadows, and hide behind its rocks.
- To Subnautica they are terrain: creatures can't swim through them and steer around them, the Seamoth bumps into them,
  and you can build bases on them.
- Torches, sea lanterns, glowstone, lava... are real lights in Subnautica.
- Swords work as Subnautica's survival knife, bows hit its creatures, and TNT and creepers hurt its fish (and you).
- Breaking limestone, sandstone or shale drops the matching Minecraft ore (copper → raw copper, titanium → raw iron...).
- What hurts the diver hurts Steve: bites (yes, Reapers too), heat, explosions, running out of oxygen.
- Minecraft fish swim in Subnautica's ocean, Minecraft boats float on its surface, and Steve can drive the Seamoth and
  use Subnautica's things with right click.

It follows the design of [SkyCraft](https://github.com/chasmlol/SkyCraft) (Minecraft inside Skyrim) and of the
`minecraft-gta5-passthrough` example in [universal-modder](https://github.com/rehan-remade/universal-modder). Both are
MIT licensed; some code is adapted from them (each file says so), and their licenses are in
`THIRD-PARTY-LICENSE-SkyCraft.txt` and `THIRD-PARTY-LICENSE-universal-modder.txt`. SubnautiCraft itself is MIT
licensed too (`LICENSE`).

## How it works

Two games run side by side and talk to each other: Minecraft (with a Fabric mod) runs Steve, and Subnautica (with a
BepInEx plugin) draws Minecraft's world inside its own and feeds Minecraft its terrain, sea and events.

```
Subnautica (BepInEx plugin, C#)                          Minecraft 26.3 + Fabric (Java mod)
  keyboard + mouse + where you look    -- WebSocket 127.0.0.1:25611 -->  Steve moves, swims, builds, opens his inventory
  terrain triangles (colliders)        ------------------------------->  1/8-block collision + aiming at the terrain
  ocean level (dry inside bases)       ------------------------------->  "virtual water": Minecraft swims in it
  camera and diver follow Steve        <-------------------------------  "pov": Steve's camera and feet, every frame
  colliders, lights, explosion damage  <-------------------------------  blocks placed/removed, lights, explosions, sword hits
  depth mesh + overlay                 <-- shared memory "Local\SubnautiCraftFrame" --  colour + depth, hand + HUD
```

- **Steve is in charge** (as in SkyCraft): Minecraft decides position and physics. Subnautica switches off the diver's
  controls, hides him and moves him to Steve every frame, like a puppet, so world streaming, biomes, depth, oxygen and
  creatures keep working. Subnautica turns the camera with the mouse (no lag) and Steve looks where it looks.
- **Terrain:** like SkyCraft's "stage A", the plugin probes the solid world around Steve with lines of rays every 0.5 m
  along all three axes, back faces included, so caves, overhangs and cliffs come out right. Each hit is sent as a small
  patch stuck to the surface. It covers terrain, wrecks, large rocks and bases, but not creatures, items, vehicles or
  plants. Minecraft turns the patches into 1/8-block collision shapes, which are not blocks (you can still place blocks
  anywhere), and the crosshair can aim at them. Subnautica's terrain meshes can't be read directly: they come from a pool.
- **Water:** below the ocean level Minecraft treats air as water for physics and path finding, without drawing it.
  Steve swims, floats and sinks as in Minecraft, and fish swim. There is no water inside bases, the lifepod or dry
  Precursor rooms. Minecraft boats float on the sea's surface (place one by looking at the water) and row as usual.
  Under water a water bucket pours nothing, lava turns into obsidian, flint and steel and fire charges light nothing,
  fire goes out and campfires are placed unlit.
- **Lights:** light-giving blocks (torches, sea lanterns, glowstone, campfires...) become point lights in Subnautica in
  the block's colour, lighting its seafloor, fish and bases. The nearest ones are lit (`MaxBlockLights`).
- **Coordinates:** 1 metre = 1 block, no offset; Minecraft = (-x, y, z) of Unity. The mod stretches the Overworld to
  y = -2032..256 so the whole ocean fits.
- **Drawing:** Minecraft's picture becomes a 3D mesh (each pixel pushed out to its depth) drawn with `MarmosetUBER`,
  Subnautica's own shader, so the game occludes it, lights it and fogs it like its own models. With
  `Lighting = Subnautica`, Minecraft draws its blocks without light of its own (every side equally bright, no ambient
  occlusion, a white lightmap) and the mesh carries each face's real direction, so Subnautica's sun, caustics, lamps and
  shadows fall on them; they also cast shadows. Only light-giving blocks glow: their pixels are found by projecting each
  such block into the picture. Edges and folds are cut at the exact pixel where the depth jumps or the face changes,
  instead of along whole mesh cells, so block edges stay straight and corners crisp. Minecraft's hand, hotbar and
  screens are drawn on top.
- **Survival** (as in SkyCraft, Minecraft owns health): whatever would hurt the diver (bites, heat, explosions, lack of
  oxygen, hunger) is worked out with Subnautica's rules and applied to Steve; Subnautica's health bar shows Steve's and
  first aid kits heal him. Minecraft's own air never runs out: Subnautica's oxygen and tanks decide. If Steve dies,
  the diver dies and Subnautica respawns him; Steve appears with him.
- **Pause and loading:** in the pause menu, the main menu, a loading screen, or while the terrain arrives after placing
  Steve, Minecraft pauses too, so Steve doesn't sink or fall.

## Install

You need:

- **Minecraft 26.3** with **Fabric Loader 0.19.5** and **Fabric API 0.161.0+26.3** (e.g. a Prism Launcher instance), plus
  `subnauticraft-0.1.0.jar` in its `mods` folder.
- **Subnautica** with **BepInEx 5**, plus `SubnautiCraft.dll` in `Subnautica\BepInEx\plugins\SubnautiCraft\`.

Build both from source (see [Build](#build)).

## Play

1. **Minecraft:** start it. It opens a world called "subnauticraft" on its own (an empty creative world, made the
   first time). Leave it running. (Start Minecraft with `-Dsubnauticraft.noAutoWorld=true` to skip that.)
2. **Subnautica:** start it and load your save. "SubnautiCraft: conectado a Minecraft" appears, Minecraft's window moves
   off the screen (it keeps running there; `ShowMinecraftWindow` leaves it where it is) and **you are Steve** where the
   diver was.
3. The controls are Minecraft's:
   - WASD to move, Space to swim up or jump, Shift to go down, Ctrl to sprint or swim fast.
   - Left click breaks and attacks, right click places blocks (or uses Subnautica's things: doors, fabricators, the
     Seamoth, breaking outcrops), 1-9 and the mouse wheel change slot.
   - E opens the inventory (the cursor appears), T the chat (commands like `/gamemode survival` work) and F5 third
     person.
   - From Subnautica, **Tab** (PDA) and **Esc** (pause; with a Minecraft screen open, Esc closes it) remain.
   - **F7** switches between Steve and the diver. As the diver you play Subnautica normally and Minecraft's blocks stay
     in the world.

The start order doesn't matter: if Minecraft closes, the plugin hands control back to the diver and reconnects by
itself.

### Settings

`Subnautica\BepInEx\config\dev.subnauticraft.cfg` (created on the first start):

| Setting | What it does |
|---|---|
| `Port` | WebSocket port of the Minecraft mod (25611; Minecraft's `-Dsubnauticraft.port` must match). |
| `StartAsSteve` | Play as Steve as soon as Minecraft connects (on by default). |
| `ShowMinecraftWindow` | Leave Minecraft's window where it is (e.g. on a second screen) instead of moving it off the desktop. |
| `Toggle` | Key that switches Steve / diver (F7). |
| `SubnauticaMaxFps` | Frame cap for Subnautica (60), so Minecraft gets enough of the graphics card. |
| `MinecraftDistance` | Minecraft's render and simulation distance in chunks while connected (6). |
| `ResolutionScale` | Size of Minecraft's picture relative to the screen (0.5); lower it if things are slow. |
| `GridStep` | Pixels between depth-mesh vertices (4). Smaller = finer, more CPU. |
| `Shader` | `MarmosetUBER` (Subnautica's lighting), `Standard`, or `Sprites/Default` (flat, unlit). |
| `Lighting` | Who lights the blocks: `Subnautica` (like the game's own models: its light, shadows and water) or `Minecraft` (Minecraft's own light). |
| `Shadows` | Minecraft's blocks cast shadows in Subnautica (with the game's shadows on). |
| `GlowingBlocks` | How brightly torches, sea lanterns, lava and other light-giving blocks glow (1). |
| `TerrainRadius` | Metres of terrain around you sent to Minecraft as collision (24). |
| `BlockColliders` | Minecraft's blocks are solid in Subnautica. |
| `BlockLights` / `BlockLightStrength` / `MaxBlockLights` | Real lights for light-giving blocks: on/off, brightness (1) and how many at once (32). |
| `DamageScale` | How much of the diver's damage Steve takes (0.2: Subnautica's 100 health to Minecraft's 20). |
| `ExplosionDamage` | Damage TNT and creepers do around them in Subnautica (you included). |
| `ArrowDamage` | Damage a Minecraft arrow does to the creature it hits (20; the knife does 25). |
| `OutcropDrops` | Breaking an outcrop as Steve drops the Minecraft ore, Subnautica's chunk, or `Both`. |
| `SwordDamageScale` | Swords act as the knife: damage = the sword's Minecraft damage × this (diamond 7 × 3.5 ≈ the knife's 25). |

## Build

Requirements: JDK 25 or newer, the .NET SDK, and Subnautica with BepInEx installed (the plugin compiles against the
game's own assemblies).

```bash
cd mc && ./gradlew build
```
Makes `mc/build/libs/subnauticraft-0.1.0.jar`; copy it into your Minecraft instance's `mods` folder.

```bash
cd subnautica && dotnet build -c Release
```
Builds `SubnautiCraft.dll` and copies it into `Subnautica\BepInEx\plugins\SubnautiCraft\`. Subnautica's folder defaults
to Steam's (`-p:GameDir=...` for another one); `-p:Deploy=false` builds without installing (while Subnautica runs the
DLL is locked).

## Testing without Subnautica

`mc/gradlew runClient -PsncPort=25612` opens a development Minecraft with the mod (offline account, world in `mc/run`)
on another port, so it never touches your real game. Then, with a Python environment that has `websocket-client`,
`numpy` and `pillow`, the scripts in `tools/` play a fake Subnautica against it:

| Script | Checks |
|---|---|
| `fake_steve.py` | Steve mode: Steve sinks in the sea, lands on terrain triangles, walks until a rock wall stops him, places a block on the seafloor, opens the inventory; a salmon swims; a sword hit reaches Subnautica with its damage. |
| `fake_subnautica.py` | Diver mode and the picture. |
| `fake_island.py` | An island with hills and a cliff, probed the way the plugin does it: Steve lands, walks along the ground and the cliff stops him. |
| `fake_survival.py` | Damage and healing, pause, Minecraft's air never drowning Steve, sprint-swimming up, death and respawn. |
| `fake_combat.py` | Outcrop ores appear as Minecraft items; Steve's arrows are reported in flight and stick where Subnautica says they hit. |
| `fake_sea.py` | Buckets, fire and campfires under and above the sea; lights reach Subnautica; boats float on the surface and can be rowed. |

```bash
python tools/fake_steve.py --port 25612
```

`frame_grab.py` copies one of Minecraft's frames out of shared memory (read only), and `mesh_edges.py` rebuilds the
depth mesh from it the way `McLayer.cs` does and lights it, to check edges and face directions without the game.

## Ideas

- Minecraft mobs against Subnautica's wildlife.
- Steve's own Minecraft model swimming in third person (F5).
- Minecraft mobs that swim more gracefully in the "virtual water".
