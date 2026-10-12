package dev.subnauticraft.world;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
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
}
