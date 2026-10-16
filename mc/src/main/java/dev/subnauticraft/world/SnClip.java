package dev.subnauticraft.world;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

/**
 * The crosshair meets Subnautica's terrain: a hit on the seafloor or a rock face points at the cell a block placed
 * against it would occupy, so right-click builds on Subnautica's ground. Vanilla still clips against real
 * Minecraft blocks; whichever is nearer wins. Adapted from SkyCraft's SkyClip (MIT).
 */
public final class SnClip {
	private static final ThreadLocal<List<SnTri>> SCRATCH = ThreadLocal.withInitial(ArrayList::new);
	/** The last hit that was Subnautica's terrain rather than a block (swinging at it digs it: SnDig). */
	private static volatile BlockHitResult lastTerrain;

	private SnClip() {
	}

	public static BlockHitResult refine(final Vec3 from, final Vec3 to, final BlockHitResult vanilla) {
		if (!SnCollision.active()) {
			return vanilla;
		}

		List<SnTri> tris = SCRATCH.get();
		tris.clear();
		SnCollision.trianglesNear(new AABB(from, to).inflate(0.01), tris);
		if (tris.isEmpty()) {
			return vanilla;
		}

		SnRay.Hit hit = SnRay.cast(tris, from.x, from.y, from.z, to.x, to.y, to.z);
		tris.clear();
		if (hit == null) {
			return vanilla;
		}

		Vec3 location = new Vec3(hit.x(), hit.y(), hit.z());
		if (vanilla.getType() != HitResult.Type.MISS && from.distanceToSqr(vanilla.getLocation()) <= from.distanceToSqr(location)) {
			return vanilla;
		}

		Direction face = Direction.values()[SnRay.dominantFace(hit.nx(), hit.ny(), hit.nz())];
		int[] cell = SnRay.placementCell(hit);
		return new BlockHitResult(location, face, new BlockPos(cell[0], cell[1], cell[2]), false);
	}

	/**
	 * The crosshair: as {@link #refine}, but a block sunk in the terrain (one Steve is digging, SnDig) is what it aims
	 * at when the terrain's surface hides it, and a hit on bare terrain is remembered (swinging at it digs it).
	 */
	public static BlockHitResult pick(final BlockGetter level, final Vec3 from, final Vec3 to, final BlockHitResult vanilla) {
		BlockHitResult hit = refine(from, to, vanilla);
		if (hit == vanilla) {
			return hit;
		}

		BlockPos behind = hit.getBlockPos().relative(hit.getDirection().getOpposite());
		if (!level.getBlockState(behind).isAir()) {
			return new BlockHitResult(hit.getLocation(), hit.getDirection(), behind, false);
		}

		lastTerrain = hit;
		return hit;
	}

	/** This hit (the crosshair's) is on Subnautica's terrain. */
	public static boolean isTerrain(final BlockHitResult hit) {
		return hit != null && hit == lastTerrain;
	}
}
