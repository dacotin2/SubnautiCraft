package dev.subnauticraft;

import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.damagesource.DamageSources;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.Vec3;

/** Server-side half: Subnautica's seafloor as invisible barrier blocks, commands, and block/light/explosion events back to Subnautica. */
public final class WorldBridge {
	private static volatile MinecraftServer server;
	/** Barriers we placed (so a reset only removes ours, never the player's builds). */
	private static final Set<BlockPos> barriers = ConcurrentHashMap.newKeySet();
	/** Block changes this tick (server thread only): true = now solid, false = gone. Sent at the end of the tick. */
	private static final Map<BlockPos, Boolean> changes = new LinkedHashMap<>();
	/** Blocks that give light (torches, sea lanterns...) Subnautica has been told about (server thread only). */
	private static final Set<BlockPos> lights = new HashSet<>();
	/** Light changes this tick (server thread only): the block's new state, light or not. */
	private static final Map<BlockPos, BlockState> lightChanges = new LinkedHashMap<>();
	/** While placing or removing Subnautica's own ground: those changes aren't news to Subnautica (server thread only). */
	private static boolean placingGround;

	private WorldBridge() {
	}

	static void attach(final MinecraftServer s) {
		server = s;
	}

	static void detach() {
		server = null;
		barriers.clear();
	}

	public static boolean ready() {
		return server != null;
	}

	/** Columns of solid ground from Subnautica: {x, z, yBottom, yTop, ...} in block coordinates (inclusive). Only air is replaced. */
	public static void solid(final int[] columns) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			BlockState barrier = Blocks.BARRIER.defaultBlockState();
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			placingGround = true;
			for (int i = 0; i + 3 < columns.length; i += 4) {
				for (int y = columns[i + 2]; y <= columns[i + 3]; y++) {
					pos.set(columns[i], y, columns[i + 1]);
					if (level.isInWorldBounds(pos) && level.getBlockState(pos).isAir()) {
						level.setBlock(pos, barrier, Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
						barriers.add(pos.immutable());
					}
				}
			}

			placingGround = false;
		});
	}

	/** Remove every barrier we placed (e.g. when the player teleports somewhere else). */
	public static void clearSolid() {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			placingGround = true;
			for (BlockPos pos : barriers) {
				if (level.getBlockState(pos).is(Blocks.BARRIER)) {
					level.setBlock(pos, Blocks.AIR.defaultBlockState(), Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
				}
			}

			placingGround = false;
			barriers.clear();
		});
	}

	/** Run a command as the server (op). Results go to the log, not to chat (send_command_feedback is off). */
	public static void command(final String command) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			SubnautiCraft.LOG.info("command: {}", command);
			s.getCommands().performPrefixedCommand(s.createCommandSourceStack(), command);
		});
	}

	private static boolean solidForHost(final ServerLevel level, final BlockPos pos, final BlockState state) {
		return !state.isAir() && !state.is(Blocks.BARRIER) && !state.getCollisionShape(level, pos).isEmpty();
	}

	/** Every server tick: block changes go to Subnautica (it gives them collision), and Steve's arrows in flight. */
	static void tick(final MinecraftServer s) {
		flush(s);
		if (SubnautiCraft.steve) {
			reportProjectiles(s.overworld());
		}
	}

	/** Arrows (and tridents) Steve shot that already hit something in Subnautica: they stay where they hit. */
	private static final String HIT_TAG = "snc_hit";

	/**
	 * Steve's arrows in flight, {"t":"proj","p":[[id,x,y,z],...]} (the GTA example's way): Subnautica traces each one's
	 * path through its own world, so arrows hit fish and leviathans, and stick in rock.
	 */
	private static void reportProjectiles(final ServerLevel level) {
		StringBuilder b = null;
		for (Entity e : level.getAllEntities()) {
			if (!(e instanceof AbstractArrow arrow) || !(arrow.getOwner() instanceof Player) || arrow.entityTags().contains(HIT_TAG)
				|| arrow.getDeltaMovement().lengthSqr() < 1.0E-4) {
				continue;
			}

			b = b == null ? new StringBuilder("{\"t\":\"proj\",\"p\":[") : b.append(',');
			b.append(String.format(Locale.ROOT, "[%d,%.3f,%.3f,%.3f]", e.getId(), e.getX(), e.getY(), e.getZ()));
		}

		if (b != null) {
			SubnautiCraft.events.accept(b.append("]}").toString());
		}
	}

	/** Subnautica traced an arrow into something: a creature (the arrow is spent) or its rock (the arrow sticks there). */
	public static void projectileHit(final int id, final double x, final double y, final double z, final boolean stick) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			Entity e = s.overworld().getEntity(id);
			if (!(e instanceof Projectile)) {
				return;
			}

			if (stick) {
				e.setPos(x, y, z);
				e.setDeltaMovement(Vec3.ZERO);
				e.setNoGravity(true);
				e.addTag(HIT_TAG);
			} else {
				e.discard();
			}
		});
	}

	/** Server thread, from Level.setBlock: remember the change; flushed once per tick. */
	public static void onBlockChanged(final ServerLevel level, final BlockPos pos, final BlockState state) {
		if (!SubnautiCraft.active || level != level.getServer().overworld()) {
			return;
		}

		if (!placingGround) {
			BlockPos at = pos.immutable();
			changes.put(at, solidForHost(level, pos, state));
			if (state.getLightEmission() > 0 || lights.contains(at)) {
				lightChanges.put(at, state);
			}
		}
	}

	/** End of each server tick: {"t":"blocks","set":[x,y,z,...],"clear":[x,y,z,...]}, and the lights that changed. */
	static void flush(final MinecraftServer s) {
		flushLights(false);
		if (changes.isEmpty()) {
			return;
		}

		StringBuilder set = new StringBuilder();
		StringBuilder clear = new StringBuilder();
		for (Map.Entry<BlockPos, Boolean> e : changes.entrySet()) {
			StringBuilder b = e.getValue() ? set : clear;
			BlockPos p = e.getKey();
			b.append(b.isEmpty() ? "" : ",").append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ());
		}

		changes.clear();
		SubnautiCraft.events.accept("{\"t\":\"blocks\",\"set\":[" + set + "],\"clear\":[" + clear + "]}");
	}

	/**
	 * {"t":"lights","reset":bool,"set":[[x,y,z,level,"torch"],...],"clear":[x,y,z,...]}: Minecraft's light-giving blocks
	 * become real lights in Subnautica (its own lighting, so they light up its rock, fish and the diver). With reset,
	 * the set replaces every light Subnautica had.
	 */
	private static void flushLights(final boolean reset) {
		if (lightChanges.isEmpty() && !reset) {
			return;
		}

		if (reset) {
			lights.clear();
		}

		StringBuilder set = new StringBuilder();
		StringBuilder clear = new StringBuilder();
		for (Map.Entry<BlockPos, BlockState> e : lightChanges.entrySet()) {
			BlockPos p = e.getKey();
			BlockState state = e.getValue();
			int level = state.getLightEmission();
			if (level > 0) {
				lights.add(p);
				set.append(set.isEmpty() ? "" : ",").append('[').append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ())
					.append(',').append(level).append(",\"").append(BuiltInRegistries.BLOCK.getKey(state.getBlock()).getPath()).append("\"]");
			} else if (lights.remove(p)) {
				clear.append(clear.isEmpty() ? "" : ",").append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ());
			}
		}

		lightChanges.clear();
		SubnautiCraft.events.accept("{\"t\":\"lights\",\"reset\":" + reset + ",\"set\":[" + set + "],\"clear\":[" + clear + "]}");
	}

	/** Every solid block within `radius` of the player, as one "blocks" message (Subnautica's colliders start from this), and every light. */
	public static void sync(final int radius) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			ServerPlayer player = s.getPlayerList().getPlayers().isEmpty() ? null : s.getPlayerList().getPlayers().get(0);
			if (player == null) {
				return;
			}

			BlockPos c = player.blockPosition();
			BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
			int found = 0;
			lightChanges.clear();
			for (int x = -radius; x <= radius; x++) {
				for (int z = -radius; z <= radius; z++) {
					for (int y = -radius; y <= radius; y++) {
						p.set(c.getX() + x, c.getY() + y, c.getZ() + z);
						if (!level.isInWorldBounds(p) || !level.isLoaded(p)) {
							continue;
						}

						BlockState state = level.getBlockState(p);
						if (found < 3000 && solidForHost(level, p, state)) {
							changes.put(p.immutable(), true);
							found++;
						}

						if (state.getLightEmission() > 0) {
							lightChanges.put(p.immutable(), state);
						}
					}
				}
			}

			flushLights(true);
			flush(s);
		});
	}

	/**
	 * Subnautica hurt the diver (a bite, a hot vent, no oxygen, hunger...): in Steve mode Minecraft's health is the
	 * one that counts, so Steve takes it, with armour, totems and death all Minecraft's. {@code kind} is Subnautica's
	 * damage type; {@code amount} is already in Minecraft health points.
	 */
	public static void hurt(final String kind, final float amount) {
		MinecraftServer s = server;
		if (s == null || amount <= 0.0F) {
			return;
		}

		s.execute(() -> {
			for (ServerPlayer player : s.getPlayerList().getPlayers()) {
				DamageSources sources = player.damageSources();
				DamageSource source = switch (kind) {
					case "Heat" -> sources.hotFloor();
					case "Fire" -> sources.inFire();
					case "Starve" -> sources.starve();
					case "Explosive" -> sources.explosion(null, null);
					case "Electrical" -> sources.lightningBolt();
					case "Cold" -> sources.freeze();
					case "Poison", "Acid", "Radiation", "Smoke" -> sources.magic();
					case "Drown" -> sources.drown();
					default -> sources.generic();
				};
				player.hurtServer(player.level(), source, amount);
			}
		});
	}

	/** Subnautica healed the diver (first aid kit, ...): Steve heals as much. {@code amount} in Minecraft health points. */
	public static void heal(final float amount) {
		MinecraftServer s = server;
		if (s == null || amount <= 0.0F) {
			return;
		}

		s.execute(() -> s.getPlayerList().getPlayers().forEach(player -> player.heal(amount)));
	}

	/** `source`: what exploded or was blown up, e.g. "tnt", "creeper", "fireball" (a ghast's). */
	public static void onExplosion(final Vec3 center, final float radius, final String source) {
		if (SubnautiCraft.active) {
			SubnautiCraft.events.accept(String.format(Locale.ROOT, "{\"t\":\"explosion\",\"pos\":[%.3f,%.3f,%.3f],\"r\":%.2f,\"src\":\"%s\"}",
				center.x, center.y, center.z, radius, source));
		}
	}
}
