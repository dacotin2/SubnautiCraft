package dev.subnauticraft;

import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Steve digs Subnautica's terrain. Swinging at it (the crosshair on Subnautica's ground, not on a block) asks
 * Subnautica what's there ({"t":"dig"}); if it's terrain, an invisible block standing for it (sand, gravel, stone...:
 * SnTerrainBlocks) goes in that cell ({"t":"terrainblock"}) and Steve mines it as that block. Once mined, Subnautica cuts that cube out of its terrain
 * ({"t":"carve"}). A terrain block left alone goes away after half a minute, and the terrain stays whole.
 */
public final class SnDig {
	private static final int LEFT_FOR = 600;
	/** Cells Subnautica was asked about, and when. */
	private static final Map<BlockPos, Long> ASKED = new ConcurrentHashMap<>();
	/** Terrain blocks waiting to be mined, until when. */
	private static final Map<BlockPos, Long> PLACED = new ConcurrentHashMap<>();
	/** Terrain blocks just mined, and who by: their drops go to him on the next tick (they're dropped after the event). */
	private static final Map<BlockPos, Player> MINED = new ConcurrentHashMap<>();

	private SnDig() {
	}

	/** Client: Steve swings at Subnautica's terrain at this cell (behind the face he aims at). */
	public static void swing(final BlockPos cell, final Direction face) {
		BlockPos at = cell.immutable();
		WorldBridge.withPlayer(player -> ask(player, at, face));
	}

	private static void ask(final ServerPlayer player, final BlockPos cell, final Direction face) {
		ServerLevel level = player.level();
		if (!SubnautiCraft.active || ASKED.containsKey(cell) || !level.getBlockState(cell).isAir()
			|| player.distanceToSqr(Vec3.atCenterOf(cell)) > 49.0) {
			return;
		}

		ASKED.put(cell, level.getGameTime());
		String side = face == Direction.UP ? "up" : face == Direction.DOWN ? "down" : "side";
		SubnautiCraft.events.accept(String.format(Locale.ROOT, "{\"t\":\"dig\",\"p\":[%d,%d,%d],\"f\":\"%s\"}", cell.getX(), cell.getY(), cell.getZ(), side));
	}

	/** Server thread: Subnautica's answer, the block standing for its terrain there (null: no terrain). */
	public static void answer(final MinecraftServer server, final BlockPos cell, final String block) {
		ASKED.remove(cell);
		if (block == null) {
			return;
		}

		Block found = SnTerrainBlocks.standingFor(block);
		ServerLevel level = server.overworld();
		if (!level.getBlockState(cell).isAir()) {
			return;
		}

		PLACED.put(cell, level.getGameTime() + LEFT_FOR);
		level.setBlockAndUpdate(cell, found.defaultBlockState());
		SubnautiCraft.LOG.info("dig: {} stands for Subnautica's terrain at {}", block, cell.toShortString());
	}

	/**
	 * Steve mined a block: if it stood for Subnautica's terrain, the terrain is cut there, and what it dropped comes to
	 * Steve (it fell inside the terrain, where nothing holds it up yet).
	 */
	public static void broken(final Level level, final Player player, final BlockPos pos) {
		if (PLACED.remove(pos) == null) {
			return;
		}

		SubnautiCraft.LOG.info("dig: mined at {}, Subnautica cuts its terrain there", pos.toShortString());
		SubnautiCraft.events.accept(String.format(Locale.ROOT, "{\"t\":\"carve\",\"p\":[%d,%d,%d]}", pos.getX(), pos.getY(), pos.getZ()));
		MINED.put(pos.immutable(), player);
	}

	/** Every server tick: forget unanswered questions, take away terrain blocks nobody mined. */
	static void tick(final MinecraftServer server) {
		ServerLevel level = server.overworld();
		MINED.entrySet().removeIf(e -> {
			Player player = e.getValue();
			for (ItemEntity item : level.getEntitiesOfClass(ItemEntity.class, new AABB(e.getKey()).inflate(0.5))) {
				item.setPos(player.getX(), player.getY() + 0.3, player.getZ());
				item.setDeltaMovement(Vec3.ZERO);
				item.setNoPickUpDelay();
			}

			return true;
		});
		long now = level.getGameTime();
		if (now % 20 != 0) {
			return;
		}

		ASKED.values().removeIf(asked -> now - asked > 100);
		PLACED.entrySet().removeIf(e -> {
			if (now < e.getValue()) {
				return false;
			}

			level.removeBlock(e.getKey(), false);
			return true;
		});
	}
}
