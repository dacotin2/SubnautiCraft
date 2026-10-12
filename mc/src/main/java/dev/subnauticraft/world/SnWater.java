package dev.subnauticraft.world;

import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.material.Fluids;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;
import org.jspecify.annotations.Nullable;

/**
 * Subnautica's ocean as Minecraft water, the way SkyCraft does Skyrim's lakes (SkyWater, MIT): wherever Minecraft
 * has air below Subnautica's water surface, entities treat it as water, so Steve (and fish, drowned, items) swim,
 * float and sink there as in Minecraft water. Only entity physics sees it; no blocks change and nothing is drawn.
 * Subnautica sends the surface height (the ocean level), or none inside bases, the lifepod and dry precursor rooms.
 */
public final class SnWater {
	private static volatile double surface = Double.NaN;

	private SnWater() {
	}

	public static void set(final double level) {
		surface = level;
	}

	public static void clear() {
		surface = Double.NaN;
	}

	public static boolean active() {
		return !Double.isNaN(surface);
	}

	/** How much of this block (0..1) is under water; 0 above the surface. */
	public static float depthIn(final BlockPos pos) {
		double s = surface;
		if (Double.isNaN(s)) {
			return 0.0F;
		}

		double h = s - pos.getY();
		return h < 0.02 ? 0.0F : (float) Math.min(1.0, h);
	}

	/** Any of this block is under Subnautica's sea: no fire there, and no pouring water into water. */
	public static boolean under(final BlockPos pos) {
		return depthIn(pos) > 0.0F;
	}

	/**
	 * Where a look from above meets the sea's surface, within {@code range}, as a hit on top of the water (a boat goes
	 * there); null when the eye is under water or the look doesn't reach down to it.
	 */
	public static @Nullable BlockHitResult surfaceHit(final Vec3 eye, final Vec3 look, final double range) {
		double s = surface;
		if (Double.isNaN(s) || eye.y <= s || look.y >= -1.0E-6) {
			return null;
		}

		double t = (s - eye.y) / look.y;
		if (t > range) {
			return null;
		}

		Vec3 at = eye.add(look.scale(t));
		return new BlockHitResult(at, Direction.UP, BlockPos.containing(at.x, s - 1.0E-4, at.z), false);
	}

	/** True if the water reaches up into the box of block cells (inclusive). */
	public static boolean anyIn(final int x0, final int y0, final int z0, final int x1, final int y1, final int z1) {
		double s = surface;
		return !Double.isNaN(s) && s > y0;
	}

	/** Water in an otherwise empty (air) Minecraft cell, as a Minecraft fluid; null if none. */
	public static @Nullable FluidState fluidAt(final BlockGetter level, final BlockPos pos) {
		if (depthIn(pos) <= 0.0F || !level.getBlockState(pos).isAir()) {
			return null;
		}

		return Fluids.WATER.getSource(false);
	}

	/** For path finders: air that is mostly under Subnautica's surface reads as a water block; null otherwise. */
	public static @Nullable BlockState pathState(final BlockState state, final BlockPos pos) {
		return state.isAir() && depthIn(pos) > 0.5F ? Blocks.WATER.defaultBlockState() : null;
	}

	/** The exact water height in a cell only Subnautica fills (so floating matches its surface); -1 otherwise. */
	public static float substitutedHeight(final BlockGetter level, final BlockPos pos) {
		float depth = depthIn(pos);
		if (depth <= 0.0F || !level.getFluidState(pos).isEmpty() || !level.getBlockState(pos).isAir()) {
			return -1.0F;
		}

		return depth;
	}
}
