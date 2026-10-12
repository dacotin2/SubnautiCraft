# SubnautiCraft: Minecraft dentro de Subnautica

Juegas Subnautica **siendo Steve**: te mueves y nadas con la física de Minecraft, tienes su mano, su hotbar y su
inventario, y pones y rompes bloques sobre el fondo marino de verdad de Subnautica. Los bloques se esconden detrás de las
rocas y reciben la luz y la niebla del agua del juego. Además, para Subnautica son terreno: las criaturas no los atraviesan
y los esquivan, el Seamoth choca y puedes construir bases encima. Las espadas funcionan como el cuchillo de Subnautica:
hacen daño y cortan muestras y semillas. La TNT y los creepers hacen daño a los peces. Los peces de Minecraft nadan en el
océano de Subnautica.

Sigue el diseño de [SkyCraft](https://github.com/chasmlol/SkyCraft) (Minecraft dentro de Skyrim) y del ejemplo
`minecraft-gta5-passthrough` de [universal-modder](https://github.com/rehan-remade/universal-modder). Las dos son MIT, ver
`THIRD-PARTY-LICENSE-SkyCraft.txt` y `THIRD-PARTY-LICENSE-universal-modder.txt`. Partes del código vienen adaptadas de
ellos (se indica en cada archivo). SubnautiCraft también es MIT (`LICENSE`).

## Cómo funciona

```
Subnautica (BepInEx plugin, C#)                          Minecraft 26.3 + Fabric (mod Java)
  teclado + ratón + hacia dónde mira  -- WebSocket 127.0.0.1:25611 -->  Steve se mueve, nada, construye, abre el inventario
  triángulos del terreno (colliders)  ------------------------------->  colisión de 1/8 de bloque + apuntar sobre el terreno
  nivel del océano (seco en bases)    ------------------------------->  "agua virtual": Minecraft nada en ella
  cámara y buceador siguen a Steve    <-------------------------------  "pov": cámara y pies de Steve, cada frame
  colliders, daño de explosiones      <-------------------------------  bloques puestos/quitados, explosiones, espadazos
  malla de profundidad + overlay      <-- memoria compartida "Local\SubnautiCraftFrame" --  color + profundidad, mano + HUD
```

- **Steve manda** (como en SkyCraft): Minecraft decide la posición y la física. Subnautica apaga los controles del
  buceador, lo esconde y lo mueve cada frame a donde está Steve, como una marioneta. Así la carga del mundo, los
  biomas, la profundidad, el oxígeno y las criaturas siguen funcionando. El ratón lo gira Subnautica, sin latencia, y
  Steve mira a donde mira la cámara.
- **Terreno:** como la "etapa A" de SkyCraft, el plugin sondea el mundo sólido alrededor de Steve con líneas de rayos
  cada 0,5 m en los tres ejes, contando caras traseras, así que salen cuevas, salientes y acantilados. Cada impacto se
  manda como un parche pegado a la superficie. Cubre terreno, naufragios, rocas grandes y bases, pero no criaturas,
  objetos, vehículos ni plantas. Minecraft convierte los parches en formas de colisión de 1/8 de bloque, que no son
  bloques (puedes poner bloques en cualquier sitio), y el punto de mira puede apuntar a ellos. Las mallas de terreno de
  Subnautica no se pueden leer: vienen de un pool y el plugin se las saltaba todas.
- **Agua:** por debajo del nivel del océano, Minecraft trata el aire como agua para la física y para la búsqueda de
  caminos, sin dibujarla. Steve nada, flota y se hunde como en Minecraft, y los peces nadan. Dentro de bases, la cápsula
  y salas precursoras secas no hay agua. Los botes de Minecraft flotan en la superficie del mar (se colocan mirando al
  agua y se reman como en Minecraft). Bajo el agua, el cubo de agua no vierte nada, la lava se vuelve obsidiana, el
  mechero y las cargas ígneas no encienden, el fuego se apaga y las hogueras se colocan apagadas.
- **Luces:** antorchas, linternas marinas, piedra luminosa, hogueras... son luces de verdad en Subnautica (iluminan su
  fondo marino, los peces y las bases), con el color del bloque. Se encienden las más cercanas (`MaxBlockLights`).
- **Coordenadas:** 1 metro = 1 bloque, sin desplazamiento. Minecraft = (-x, y, z) de Unity. El mod amplía el Overworld a
  y = -2032..256 para que quepa todo el océano.
- **Dibujo:** la imagen de Minecraft se convierte en una malla 3D (cada píxel empujado hasta su profundidad) dibujada
  con `MarmosetUBER`, el shader de Subnautica. Así el juego le aplica oclusión, luz y niebla del agua. La mano, la
  hotbar y las pantallas de Minecraft se dibujan encima. Con `Lighting = Subnautica`, Minecraft dibuja sus bloques sin
  luz propia (todas las caras igual de claras, sin oclusión ambiental, lightmap blanco) y la malla lleva la dirección
  real de cada cara, así que el sol, las cáusticas, la niebla, las lámparas y las sombras de Subnautica caen sobre ellos
  como sobre sus propios modelos, y proyectan sombra. Solo brillan los bloques que dan luz (antorcha, linterna marina,
  lava...): sus píxeles se localizan proyectando cada bloque en la imagen. Los bordes y pliegues se cortan en el píxel
  exacto donde salta la profundidad o cambia la cara, en vez de en celdas enteras de la malla.

- **Supervivencia** (como SkyCraft, la vida la manda Minecraft): lo que haría daño a Ryley (mordiscos, calor,
  explosiones, falta de oxígeno, hambre) se calcula con las reglas de Subnautica y se lo hace a Steve; la barra de vida
  de Subnautica muestra la de Steve y los botiquines le curan. El aire de Minecraft no se gasta: manda el oxígeno de
  Subnautica, con sus tanques. Si Steve muere, muere Ryley y Subnautica hace su reaparición; Steve aparece con él.
- **Pausa y carga:** con el menú de pausa, el menú principal, una pantalla de carga o mientras llega el terreno tras
  colocar a Steve, Minecraft se pausa, así que Steve no se hunde ni se cae.

## Uso

1. **Minecraft:** abre Prism Launcher → instancia **SubnautiCraft** (Minecraft 26.3 + Fabric 0.19.5 + Fabric API + el
   mod, ya creada) → Iniciar. Se abre sola en un mundo creativo vacío ("subnauticraft"). Déjala abierta.
2. **Subnautica:** ábrelo desde Steam y entra en tu partida. Sale "SubnautiCraft: conectado a Minecraft", la ventana de
   Minecraft se va fuera de la pantalla (sigue funcionando ahí) y **ya eres Steve** donde estaba Ryley.
3. Controles, los de Minecraft:
   - WASD para moverte, Espacio para nadar hacia arriba o saltar, Shift para bajar, Ctrl para esprintar o nadar rápido.
   - Clic izquierdo rompe y ataca, clic derecho pone bloques, 1-9 y la rueda cambian de hueco.
   - E abre el inventario (aparece el cursor), T el chat (puedes escribir comandos como `/gamemode survival`) y F5 la
     tercera persona.
   - De Subnautica quedan **Tab** (PDA) y **Esc** (pausa; con una pantalla de Minecraft abierta, Esc la cierra).
   - **F7** cambia entre Steve y Ryley. Ryley juega Subnautica normal y los bloques de Minecraft siguen visibles. Para
     usar cosas de Subnautica (puertas, fabricadores, vehículos), cambia a Ryley con F7.

El orden de arranque da igual: si Minecraft se cierra, el plugin devuelve el control a Ryley y se reconecta solo.

### Configuración

`Subnautica\BepInEx\config\dev.subnauticraft.cfg` (se crea en el primer arranque):

| Opción | Qué hace |
|---|---|
| `StartAsSteve` | Empezar siendo Steve en cuanto conecta (por defecto sí). |
| `Toggle` | Tecla para cambiar Steve/Ryley (F7). |
| `TerrainRadius` | Metros de terreno alrededor que se mandan a Minecraft como colisión. |
| `ResolutionScale` | Tamaño de la imagen de Minecraft respecto a la pantalla. Por defecto 0.75; bájalo si va lento. |
| `GridStep` | Píxeles entre vértices de la malla. Menos = bordes más finos y más CPU. |
| `Shader` | `MarmosetUBER` (luz de Subnautica), `Standard` o `Sprites/Default` (plano, sin luz). |
| `Lighting` | Quién ilumina los bloques: `Subnautica` (como modelos del juego: su luz, sombras y agua) o `Minecraft` (su propia luz, como en Minecraft). |
| `Shadows` | Los bloques de Minecraft proyectan sombra en Subnautica (con las sombras del juego activadas). |
| `GlowingBlocks` | Cuánto brillan las antorchas, linternas marinas, lava y demás bloques que dan luz (1). |
| `BlockColliders` | Si los bloques de Minecraft son sólidos en Subnautica. |
| `BlockLights` / `BlockLightStrength` / `MaxBlockLights` | Luces reales de las antorchas y demás bloques que dan luz: encendidas, su brillo (1) y cuántas a la vez (32). |
| `ExplosionDamage` | Daño de la TNT y los creepers a lo que hay alrededor en Subnautica (también a ti). |
| `SwordDamageScale` | La espada actúa como el cuchillo: daño = daño de la espada en Minecraft × esto (diamante 7 × 3,5 ≈ el cuchillo, 25). |

## Compilar

Requisitos: JDK 25+ (tienes el 26), .NET SDK (instalado en `%LOCALAPPDATA%\Microsoft\dotnet`) y Subnautica con BepInEx.

```bash
cd mc && ./gradlew.bat build
```
Genera `mc/build/libs/subnauticraft-0.1.0.jar`. Cópialo a `%APPDATA%\PrismLauncher\instances\subnauticraft\minecraft\mods`.

```bash
cd subnautica && "$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build -c Release
```
Compila e instala `SubnautiCraft.dll` en `Subnautica\BepInEx\plugins\SubnautiCraft\` (`-p:Deploy=false` para no
instalar; con Subnautica abierto la DLL está bloqueada).

## Probar sin Subnautica

`mc/gradlew.bat runClient -PsncPort=25612` abre un Minecraft de desarrollo con el mod (cuenta offline, mundo en `mc/run`)
en otro puerto, así no choca con el de Prism. Después, con `tools/.venv` (tiene `websocket-client`, `numpy` y `pillow`):

```bash
tools/.venv/Scripts/python.exe tools/fake_steve.py --port 25612
```
Prueba el modo Steve. Manda un fondo marino y un muro de roca como triángulos, a -300 m, y el océano, y comprueba que:
- Steve está en el agua y se hunde.
- Aterriza sobre el fondo de triángulos.
- Anda con W y el muro lo para (y nada hacia arriba por él, como en Minecraft).
- El clic derecho pone un bloque sobre el fondo.
- E abre y cierra el inventario.
- Un salmón nada por el océano.
- El golpe de espada llega a Subnautica con su daño.

```bash
tools/.venv/Scripts/python.exe tools/fake_subnautica.py --port 25612
```
Prueba el modo Ryley y la imagen.

```bash
tools/.venv/Scripts/python.exe tools/fake_island.py --port 25612
tools/.venv/Scripts/python.exe tools/fake_survival.py --port 25612
tools/.venv/Scripts/python.exe tools/fake_sea.py --port 25612
```
`fake_survival.py` prueba daño y curación, la pausa, que el aire de Minecraft no ahogue, nadar hacia arriba esprintando
y la muerte con reaparición. `fake_sea.py` prueba cubos, fuego y hogueras bajo y sobre el mar, que las luces llegan a
Subnautica, y que los botes flotan en la superficie y se reman.
Genera una isla con colinas y un acantilado, la muestrea igual que el plugin (rayos en tres ejes convertidos en parches)
y comprueba que Steve aterriza, camina siguiendo el suelo y que el acantilado lo para. Construye bloques conocidos, coloca la cámara como el plugin (con yaw, pitch y roll),
reconstruye cada píxel en 3D con las mismas fórmulas que `McLayer.cs` y comprueba que cae en los bloques.

## Estado

Verificado (Minecraft de desarrollo + Subnautica falso):
- Todos los mixins se aplican en 26.3.
- Dentro del juego (captura del usuario): eres Steve con su mano y su hotbar, y un salmón de Minecraft se ve en el agua
  con la niebla de Subnautica.
- Modo Steve: las siete comprobaciones de `fake_steve.py` pasan. Aterriza en y = -299,875, encima de
  la capa de colisión de 1/8, y el muro lo para en z = 11,700 exacto.
- Modo Ryley: la reconstrucción cae exactamente en los bloques (pared en z = 20,000).
- Ventana de Minecraft fuera de la pantalla: sigue generando unos 80 frames por segundo.
- Plugin: compila contra el Subnautica instalado y está instalado. Su cliente WebSocket funciona contra el Minecraft
  real.

Arreglado tras las primeras partidas, sin ver todavía en el juego:
- **Terreno:** el log mostró que el plugin encontraba los colliders de terreno pero se saltaba todos. Ahora el terreno
  se sondea con rayos (ver "Cómo funciona"), y `fake_island.py` comprueba que Steve camina sobre ese tipo de datos. Los
  dos logs dicen cuánto terreno se manda y se recibe (busca `terrain:`).
- Los bloques de Minecraft como terreno para las criaturas, la espada como cuchillo y los peces que nadan.

## Ideas siguientes

- Interactuar con cosas de Subnautica siendo Steve (puertas, fabricadores, recoger objetos): el "use" se repartiría
  entre los dos juegos, gana el objetivo más cercano, como en SkyCraft.
- Colisión también con naufragios, rocas y bases (ahora solo terreno).
- Mobs de Minecraft que naden con más gracia: siguen el "agua virtual", pero su comportamiento es el de Minecraft.
- Ver a Steve nadando en tercera persona (F5) con su modelo de Minecraft.
- Mobs de Minecraft contra la fauna de Subnautica.
