package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.pathfinder.PathfindingContext;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Pathfinding reads block states, not the level's fluids, so the entity-only ocean (SnWater) is invisible to it:
 * a fish would float but never find anywhere to swim. Below Subnautica's surface, air looks like water to the
 * path finders (fish, dolphins, drowned and anything else that paths through water).
 */
@Mixin(PathfindingContext.class)
abstract class PathfindingContextMixin {
	@Inject(method = "getBlockState", at = @At("RETURN"), cancellable = true)
	private void snc$oceanForPaths(final BlockPos pos, final CallbackInfoReturnable<BlockState> cir) {
		BlockState water = SnWater.pathState(cir.getReturnValue(), pos);
		if (water != null) {
			cir.setReturnValue(water);
		}
	}
}
