package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.PathNavigationRegion;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.material.Fluids;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The swim path finder also asks its region for fluids directly (where a fish may breach): the ocean answers there too. */
@Mixin(PathNavigationRegion.class)
abstract class PathNavigationRegionMixin {
	@Inject(method = "getFluidState", at = @At("RETURN"), cancellable = true)
	private void snc$oceanFluid(final BlockPos pos, final CallbackInfoReturnable<FluidState> cir) {
		if (cir.getReturnValue().isEmpty() && SnWater.depthIn(pos) > 0.5F && ((PathNavigationRegion) (Object) this).getBlockState(pos).isAir()) {
			cir.setReturnValue(Fluids.WATER.getSource(false));
		}
	}
}
