package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Sprint-swimming starts in Subnautica's ocean too (SkyCraft's EntitySwimMixin, MIT). */
@Mixin(Entity.class)
abstract class EntitySwimMixin {
	@WrapOperation(
		method = "updateSwimming",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;getFluidState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/material/FluidState;")
	)
	private FluidState snc$swimInOcean(final Level level, final BlockPos pos, final Operation<FluidState> original) {
		FluidState state = original.call(level, pos);
		if (state.isEmpty() && SnWater.active()) {
			FluidState water = SnWater.fluidAt(level, pos);
			if (water != null) {
				return water;
			}
		}

		return state;
	}
}
