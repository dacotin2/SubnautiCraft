package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnCollision;
import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.pathfinder.PathType;
import net.minecraft.world.level.pathfinder.PathfindingContext;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Pathfinding reads block states, not the level's fluids or Subnautica's terrain, so both were invisible to it: a
 * fish would float but never find anywhere to swim, a dolphin planned straight through rock, and a drowned standing
 * on Subnautica's seafloor found no floor at all, so no path, and stood still next to its prey. For the path finders,
 * air below Subnautica's surface is water, and a cell Subnautica's terrain fills (more than half its height: rock, a
 * cliff, a cave ceiling) is solid, so they walk on it, swim around it, and go around it as around Minecraft's blocks.
 * Path types are answered on top of Minecraft's own (shared, per level) cache, which keeps the plain block's type.
 */
@Mixin(PathfindingContext.class)
abstract class PathfindingContextMixin {
	private static final BlockState ROCK = Blocks.STONE.defaultBlockState();

	/** The swim path finder asks for block states. */
	@Inject(method = "getBlockState", at = @At("RETURN"), cancellable = true)
	private void snc$oceanForPaths(final BlockPos pos, final CallbackInfoReturnable<BlockState> cir) {
		BlockState state = cir.getReturnValue();
		if (state.isAir() && isRock(pos)) {
			cir.setReturnValue(ROCK);
			return;
		}

		BlockState water = SnWater.pathState(state, pos);
		if (water != null) {
			cir.setReturnValue(water);
		}
	}

	/** The walking and amphibious path finders ask for path types. */
	@Inject(method = "getPathTypeFromState", at = @At("RETURN"), cancellable = true)
	private void snc$terrainAndOcean(final int x, final int y, final int z, final CallbackInfoReturnable<PathType> cir) {
		if (cir.getReturnValue() != PathType.OPEN) {
			return;
		}

		BlockPos pos = new BlockPos(x, y, z);
		if (isRock(pos)) {
			cir.setReturnValue(PathType.BLOCKED);
		} else if (SnWater.depthIn(pos) > 0.5F) {
			cir.setReturnValue(PathType.WATER);
		}
	}

	private static boolean isRock(final BlockPos pos) {
		VoxelShape terrain = SnCollision.shapeAt(pos);
		return terrain != null && !terrain.isEmpty() && terrain.max(Direction.Axis.Y) > 0.5;
	}
}
