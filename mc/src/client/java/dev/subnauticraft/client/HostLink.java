package dev.subnauticraft.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.subnauticraft.SubnautiCraft;
import dev.subnauticraft.WorldBridge;
import dev.subnauticraft.world.SnCollision;
import dev.subnauticraft.world.SnWater;
import java.net.InetSocketAddress;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Locale;
import net.minecraft.client.CameraType;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.player.LocalPlayer;
import org.java_websocket.WebSocket;
import org.java_websocket.handshake.ClientHandshake;
import org.java_websocket.server.WebSocketServer;

/**
 * Subnautica's connection: a WebSocket server on 127.0.0.1 (port 25611, or -Dsubnauticraft.port).
 *
 * <p>Subnautica to Minecraft (JSON, Minecraft coordinates):
 * <ul>
 * <li>{"t":"cam","f":frame,"p":[x,y,z],"r":[yaw,pitch,roll],"fov":vertical degrees}</li>
 * <li>{"t":"release"}: Subnautica paused or left the game: stop following now</li>
 * <li>{"t":"ground","c":[x,z,yBottom,yTop, ...]}: solid columns (barriers)</li>
 * <li>{"t":"clear"}: remove the barriers placed so far</li>
 * <li>{"t":"cmd","c":"time set noon"}: a server command</li>
 * <li>{"t":"blocksync","r":radius}: report every solid block near the player</li>
 * <li>{"t":"key","k":"use|attack|pick|inventory|drop|swap|escape","down":bool}</li>
 * <li>{"t":"slot","n":0-8}, {"t":"scroll","d":+-1}, {"t":"hud","hidden":bool}, {"t":"view","w":px,"h":px}</li>
 * <li>{"t":"window","hide":bool}: move Minecraft's own window off the desktop (it keeps rendering) or back</li>
 * <li>{"t":"mode","steve":bool}: Steve mode (Minecraft owns the player) or Ryley mode (Subnautica's camera drives)</li>
 * <li>{"t":"tp","p":[feet x,y,z],"r":[yaw,pitch]}: put Steve there (taking over from the diver, a respawn, a warp)</li>
 * <li>{"t":"fov","v":deg,"o":overscan}, {"t":"water","y":ocean level or null (dry)}</li>
 * <li>{"t":"in",...}: Subnautica's keyboard and mouse for Minecraft, plus "look":[yaw,pitch] (see InputBridge)</li>
 * <li>{"t":"releaseall"}: let go of every key (Subnautica's PDA or menu took the input)</li>
 * <li>{"t":"menu","kind":"chest|crafting|vehicles",...}: Steve used a locker, the fabricator or the vehicle bay: Minecraft's screen for it (SnMenus)</li>
 * <li>{"t":"creatures","c":[[id,x,y,z,size],...]}: Subnautica's creatures near the player (Minecraft's monsters hunt their stand-ins)</li>
 * <li>{"t":"mobhit","id":entity,"dmg":hp}: a Subnautica creature bit that Minecraft mob</li>
 * <li>{"t":"known","items":[...],"new":bool}: blueprints the diver knows: their recipes are unlocked (SnBlueprints)</li>
 * <li>{"t":"scanning"}: a second of scanning with the scanner in Steve's hand</li>
 * <li>{"t":"recipes","r":[{"out":{...},"n":count,"in":[...]},...]}: the rest of Subnautica's fabricator (SnFabricator)</li>
 * <li>{"t":"toolcharge","c":0..1}: the battery of the real tool Steve holds (SnBattery)</li>
 * <li>{"t":"terrainblock","p":[x,y,z],"b":block id or null}: Subnautica's terrain in the cell Steve swings at (SnDig)</li>
 * <li>{"t":"give","item":id or "tech":TechType,"name":...,"n":count,"food":f,"water":w}: Steve picked something up in Subnautica (SnPickups)</li>
 * <li>{"t":"built","tech":...,"ok":bool}: the vehicle bay started building what Steve asked for, or had no room</li>
 * <li>{"t":"save","slot":"slot0001"}: the Subnautica save being played; Minecraft plays that save's own world</li>
 * <li>{"t":"lighting","unlit":bool}: Subnautica lights the world (Minecraft draws it without its own light) or Minecraft does</li>
 * <li>{"t":"icon","n":item,"png":base64}, then {"t":"icons","names":{item:name}}: Subnautica's icons and names for its items (IconPack)</li>
 * <li>{"t":"hurt","kind":Subnautica damage type,"hp":amount}, {"t":"heal","hp":amount}: the diver's damage and healing, for Steve</li>
 * <li>{"t":"pause","on":bool}: Subnautica paused, loading, in its menu, or Steve waiting for the ground under him</li>
 * <li>{"t":"terrainevict","p":[x,y,z],"r":radius}, {"t":"terrainclear"}</li>
 * <li>binary "TERR" key(long) count(int) count x 9 floats: one terrain chunk's collision triangles (SnCollision)</li>
 * </ul>
 * Minecraft to Subnautica in Steve mode, every frame: {"t":"pov","p":camera,"r":[yaw,pitch],"f":feet,...}.
 * Minecraft to Subnautica: {"t":"hello",...} on connect, {"t":"blocks","set":[...],"clear":[...]},
 * {"t":"explosion","pos":[x,y,z],"r":radius,"src":kind}.
 */
public final class HostLink extends WebSocketServer {
	public static final int DEFAULT_PORT = 25611;
	private static HostLink instance;
	/** The Subnautica save being played ({"t":"save"}). */
	private volatile String slot;

	private HostLink(final int port) {
		super(new InetSocketAddress("127.0.0.1", port));
		this.setReuseAddr(true);
		this.setDaemon(true);
	}

	static void launch() {
		int port = Integer.getInteger("subnauticraft.port", DEFAULT_PORT);
		instance = new HostLink(port);
		instance.start();
		SubnautiCraft.events = message -> instance.broadcast(message);
	}

	@Override
	public void onStart() {
		SubnautiCraft.LOG.info("Subnautica link listening on 127.0.0.1:{}", this.getPort());
	}

	@Override
	public void onOpen(final WebSocket conn, final ClientHandshake handshake) {
		SubnautiCraft.LOG.info("Subnautica connected from {}", conn.getRemoteSocketAddress());
		conn.send(String.format(Locale.ROOT, "{\"t\":\"hello\",\"v\":1,\"shm\":\"%s\",\"pid\":%d}", FrameExporter.NAME.replace("\\", "\\\\"), ProcessHandle.current().pid()));
	}

	@Override
	public void onClose(final WebSocket conn, final int code, final String reason, final boolean remote) {
		SubnautiCraft.LOG.info("Subnautica disconnected ({} {})", code, reason);
		HostState.release();
		if (this.getConnections().stream().noneMatch(c -> c != conn && c.isOpen())) {
			// nobody to show Minecraft any more: Minecraft is a normal game again, with its window back
			SubnautiCraft.steve = false;
			SnWater.clear();
			Minecraft minecraft = Minecraft.getInstance();
			WorldBridge.withServer(server -> dev.subnauticraft.SnCreatures.clear());
			minecraft.execute(() -> {
				setUnlit(minecraft, false);
				InputBridge.releaseAll();
				hover(minecraft);
				ClientInput.hideWindow(minecraft, false);
				// Minecraft on its own again: Steve waits in the pause menu instead of sinking or falling meanwhile
				HostState.hostPaused = false;
				if (minecraft.player != null && minecraft.gui.screen() == null) {
					minecraft.gui.setScreen(new PauseScreen(true));
				}
			});
		}
	}

	@Override
	public void onMessage(final WebSocket conn, final String message) {
		try {
			JsonObject m = JsonParser.parseString(message).getAsJsonObject();
			HostState.touch();
			Minecraft minecraft = Minecraft.getInstance();
			switch (m.get("t").getAsString()) {
				case "cam" -> HostState.update(m);
				case "fov" -> HostState.setFov(m.get("v").getAsFloat(), m.has("o") ? m.get("o").getAsFloat() : 1.0F);
				case "water" -> {
					if (m.has("y") && !m.get("y").isJsonNull()) {
						SnWater.set(m.get("y").getAsDouble());
					} else {
						SnWater.clear();
					}
				}
				case "in" -> {
					if (m.has("look")) {
						JsonArray look = m.getAsJsonArray("look");
						HostState.setLook(look.get(0).getAsFloat(), look.get(1).getAsFloat());
					}

					minecraft.execute(() -> InputBridge.apply(minecraft, m));
				}
				case "releaseall" -> minecraft.execute(InputBridge::releaseAll);
				case "icon" -> IconPack.put(m.get("n").getAsString(), java.util.Base64.getDecoder().decode(m.get("png").getAsString()));
				case "icons" -> {
					java.util.Map<String, String> names = new java.util.HashMap<>();
					if (m.has("names")) {
						m.getAsJsonObject("names").entrySet().forEach(e -> names.put(e.getKey(), e.getValue().getAsString()));
					}

					minecraft.execute(() -> IconPack.finish(minecraft, names));
				}
				case "menu" -> {
					String kind = m.get("kind").getAsString();
					String title = m.has("title") ? m.get("title").getAsString() : "";
					String id = m.has("id") ? m.get("id").getAsString() : "";
					java.util.List<dev.subnauticraft.SnMenus.Option> options = new java.util.ArrayList<>();
					if (m.has("options")) {
						for (com.google.gson.JsonElement e : m.getAsJsonArray("options")) {
							JsonObject o = e.getAsJsonObject();
							java.util.List<dev.subnauticraft.SnMenus.Need> needs = new java.util.ArrayList<>();
							for (com.google.gson.JsonElement n : o.getAsJsonArray("ingredients")) {
								JsonArray a = n.getAsJsonArray();
								needs.add(new dev.subnauticraft.SnMenus.Need(a.get(0).getAsString(), a.get(1).getAsInt(), a.get(2).getAsString()));
							}

							options.add(new dev.subnauticraft.SnMenus.Option(o.get("tech").getAsString(), o.get("item").getAsString(), o.get("name").getAsString(), needs));
						}
					}

					int rows = m.has("rows") ? m.get("rows").getAsInt() : 3;
					WorldBridge.withPlayer(player -> {
						switch (kind) {
							case "chest" -> dev.subnauticraft.SnMenus.openLocker(player, id, rows, title);
							case "crafting" -> dev.subnauticraft.SnMenus.openCrafting(player, title);
							case "vehicles" -> dev.subnauticraft.SnMenus.openVehicles(player, id, title, options);
							default -> SubnautiCraft.LOG.warn("unknown menu {}", kind);
						}
					});
				}
				case "creatures" -> {
					JsonArray list = m.getAsJsonArray("c");
					double[][] creatures = new double[list.size()][];
					for (int i = 0; i < list.size(); i++) {
						JsonArray c = list.get(i).getAsJsonArray();
						creatures[i] = new double[] {c.get(0).getAsDouble(), c.get(1).getAsDouble(), c.get(2).getAsDouble(), c.get(3).getAsDouble(), c.get(4).getAsDouble()};
					}

					WorldBridge.withServer(server -> dev.subnauticraft.SnCreatures.update(server, creatures));
				}
				case "mobhit" -> {
					int mob = m.get("id").getAsInt();
					float damage = m.get("dmg").getAsFloat();
					WorldBridge.withServer(server -> dev.subnauticraft.SnCreatures.mobHit(server, mob, damage));
				}
				case "known" -> {
					java.util.List<String> items = new java.util.ArrayList<>();
					m.getAsJsonArray("items").forEach(e -> items.add(e.getAsString()));
					dev.subnauticraft.SnBlueprints.received(items);
				}
				case "scanning" -> WorldBridge.withServer(dev.subnauticraft.SnBlueprints::scanned);
				case "recipes" -> dev.subnauticraft.SnFabricator.received(m.getAsJsonArray("r"));
				case "terrainblock" -> {
					com.google.gson.JsonArray p = m.getAsJsonArray("p");
					net.minecraft.core.BlockPos cell = new net.minecraft.core.BlockPos(p.get(0).getAsInt(), p.get(1).getAsInt(), p.get(2).getAsInt());
					String block = m.has("b") && !m.get("b").isJsonNull() ? m.get("b").getAsString() : null;
					WorldBridge.withServer(server -> dev.subnauticraft.SnDig.answer(server, cell, block));
				}
				case "give" -> {
					String item = m.has("item") ? m.get("item").getAsString() : null;
					String tech = m.has("tech") ? m.get("tech").getAsString() : null;
					String name = m.has("name") ? m.get("name").getAsString() : null;
					int count = m.has("n") ? m.get("n").getAsInt() : 1;
					Float food = m.has("food") ? m.get("food").getAsFloat() : null;
					Float water = m.has("water") ? m.get("water").getAsFloat() : null;
					Float charge = m.has("charge") ? m.get("charge").getAsFloat() : null;
					WorldBridge.withPlayer(player -> dev.subnauticraft.SnPickups.give(player, item, tech, name, count, food, water, charge));
				}
				case "toolcharge" -> {
					float charge = m.get("c").getAsFloat();
					WorldBridge.withPlayer(player -> dev.subnauticraft.SnBattery.charge(player, charge));
				}
				case "built" -> {
					boolean ok = m.get("ok").getAsBoolean();
					WorldBridge.withPlayer(player -> dev.subnauticraft.SnMenus.built(player, ok));
				}
				case "save" -> {
					String slot = m.get("slot").getAsString();
					if (!slot.equals(this.slot)) {
						// another Subnautica save: what the diver knows there comes next
						this.slot = slot;
						dev.subnauticraft.SnBlueprints.forget();
					}

					minecraft.execute(() -> SubnautiCraftClient.useSave(minecraft, slot));
				}
				case "lighting" -> {
					boolean unlit = m.get("unlit").getAsBoolean();
					minecraft.execute(() -> setUnlit(minecraft, unlit));
				}
				case "distance" -> {
					// render and simulation distance in chunks (each column here is 143 sections tall: keep it small)
					int chunks = Math.max(2, Math.min(32, m.get("v").getAsInt()));
					minecraft.execute(() -> {
						if (minecraft.options.renderDistance().get() != chunks) {
							minecraft.options.renderDistance().set(chunks);
							minecraft.options.simulationDistance().set(Math.max(5, chunks));
							SubnautiCraft.LOG.info("render distance {} chunks", chunks);
						}
					});
				}
				case "hurt" -> WorldBridge.hurt(m.get("kind").getAsString(), m.get("hp").getAsFloat());
				case "heal" -> WorldBridge.heal(m.get("hp").getAsFloat());
				case "projhit" -> {
					JsonArray at = m.getAsJsonArray("pos");
					WorldBridge.projectileHit(m.get("id").getAsInt(), at.get(0).getAsDouble(), at.get(1).getAsDouble(), at.get(2).getAsDouble(),
						m.has("stick") && m.get("stick").getAsBoolean());
				}
				case "pause" -> {
					boolean on = m.get("on").getAsBoolean();
					minecraft.execute(() -> pause(minecraft, on));
				}
				case "mode" -> {
					boolean steve = m.get("steve").getAsBoolean();
					minecraft.execute(() -> setMode(minecraft, steve));
				}
				case "tp" -> {
					JsonArray p = m.getAsJsonArray("p");
					JsonArray r = m.getAsJsonArray("r");
					double x = p.get(0).getAsDouble(), y = p.get(1).getAsDouble(), z = p.get(2).getAsDouble();
					float yaw = r.get(0).getAsFloat(), pitch = r.get(1).getAsFloat();
					HostState.setLook(yaw, pitch);
					minecraft.execute(() -> teleport(minecraft, x, y, z, yaw, pitch));
					WorldBridge.command(String.format(Locale.ROOT, "tp @a %.4f %.4f %.4f %.2f %.2f", x, y, z, yaw, pitch));
				}
				case "terrainevict" -> {
					JsonArray p = m.getAsJsonArray("p");
					SnCollision.evictBeyond(p.get(0).getAsDouble(), p.get(1).getAsDouble(), p.get(2).getAsDouble(), m.get("r").getAsDouble());
				}
				case "terrainclear" -> SnCollision.clear();
				case "release" -> HostState.release();
				case "ground" -> WorldBridge.solid(ints(m.getAsJsonArray("c")));
				case "clear" -> WorldBridge.clearSolid();
				case "cmd" -> WorldBridge.command(m.get("c").getAsString());
				case "blocksync" -> WorldBridge.sync(m.has("r") ? m.get("r").getAsInt() : 32);
				default -> minecraft.execute(() -> ClientInput.handle(minecraft, m));
			}
		} catch (RuntimeException e) {
			SubnautiCraft.LOG.warn("bad Subnautica message {}: {}", message.length() > 200 ? message.substring(0, 200) : message, e.toString());
		}
	}

	/** "TERR", key (long), count (int), count x 9 floats: one terrain chunk's triangles, already in Minecraft space. */
	@Override
	public void onMessage(final WebSocket conn, final ByteBuffer message) {
		HostState.touch();
		ByteBuffer b = message.order(ByteOrder.LITTLE_ENDIAN);
		if (b.remaining() < 16 || b.getInt() != TERRAIN_MAGIC) {
			SubnautiCraft.LOG.warn("unknown binary message from Subnautica ({} bytes)", message.limit());
			return;
		}

		long key = b.getLong();
		int count = b.getInt();
		if (count < 0 || (long) count * 36 > b.remaining()) {
			SubnautiCraft.LOG.warn("bad terrain message: {} triangles in {} bytes", count, b.remaining());
			return;
		}

		float[] v = new float[count * 9];
		b.asFloatBuffer().get(v);
		SnCollision.submit(key, v);
	}

	private static final int TERRAIN_MAGIC = 0x52524554; // "TERR"

	/** Client thread. Steve mode: Steve stops being a ghost and swims; the mouse is "grabbed" (Subnautica holds the real one). */
	private static void setMode(final Minecraft minecraft, final boolean steve) {
		SubnautiCraft.steve = steve;
		LocalPlayer player = minecraft.player;
		if (steve) {
			HostState.release();
			if (player != null) {
				player.noPhysics = false;
				player.getAbilities().flying = false;
				player.onUpdateAbilities();
			}

			minecraft.options.setCameraType(CameraType.FIRST_PERSON);
			minecraft.mouseHandler.grabMouse();
		} else {
			InputBridge.releaseAll();
			hover(minecraft);
		}

		SubnautiCraft.LOG.info(steve ? "Steve mode: Minecraft plays, Subnautica follows" : "Ryley mode: Subnautica plays, Minecraft follows");
	}

	/**
	 * Subnautica paused, loading, in its menu, or Steve waiting for the ground under him: Minecraft pauses too (its
	 * integrated server stops: no sinking, falling, drowning or mobs moving), behind a pause screen nobody sees.
	 */
	/** {"t":"lighting","unlit":bool}: who lights the world (see {@link HostState#unlit}); the chunks are rebuilt to match. */
	private static void setUnlit(final Minecraft minecraft, final boolean unlit) {
		if (HostState.unlit != unlit) {
			HostState.unlit = unlit;
			SubnautiCraft.LOG.info("lighting: {}", unlit ? "Subnautica's (Minecraft draws unlit)" : "Minecraft's own");
			minecraft.levelExtractor.allChanged();
		}
	}

	private static void pause(final Minecraft minecraft, final boolean on) {
		HostState.hostPaused = on;
		if (on) {
			if (minecraft.player != null && minecraft.gui.screen() == null) {
				minecraft.gui.setScreen(new PauseScreen(false));
			}
		} else if (minecraft.gui.screen() instanceof PauseScreen) {
			minecraft.gui.setScreen(null);
			if (SubnautiCraft.steve) {
				minecraft.mouseHandler.grabMouse();
			}
		}
	}

	/** Subnautica let go of Steve: he flies where he is instead of dropping out of the (otherwise empty) world. */
	private static void hover(final Minecraft minecraft) {
		LocalPlayer player = minecraft.player;
		if (player != null && player.getAbilities().mayfly && !player.getAbilities().flying) {
			player.getAbilities().flying = true;
			player.onUpdateAbilities();
			player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
		}
	}

	private static void teleport(final Minecraft minecraft, final double x, final double y, final double z, final float yaw, final float pitch) {
		LocalPlayer player = minecraft.player;
		if (player == null) {
			return;
		}

		player.setPos(x, y, z);
		player.xo = player.xOld = x;
		player.yo = player.yOld = y;
		player.zo = player.zOld = z;
		player.setYRot(yaw);
		player.setXRot(pitch);
		player.yRotO = yaw;
		player.xRotO = pitch;
		player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
	}

	@Override
	public void onError(final WebSocket conn, final Exception e) {
		SubnautiCraft.LOG.warn("Subnautica link error", e);
	}

	private static int[] ints(final JsonArray a) {
		int[] out = new int[a.size()];
		for (int i = 0; i < out.length; i++) {
			out[i] = a.get(i).getAsInt();
		}

		return out;
	}
}
