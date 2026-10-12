package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.vehicle.boat.AbstractBoat;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Boats float on Subnautica's sea: a boat finds its water with its own block checks (not the entity fluid update),
 * so those see the sea too, at its exact surface. A boat under it rises as from under Minecraft water.
 */
@Mixin(AbstractBoat.class)
abstract class AbstractBoatMixin {
	@WrapOperation(
		method = {"getWaterLevelAbove", "checkInWater", "isUnderwater", "checkFallDamage"},
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;getFluidState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/material/FluidState;")
	)
	private FluidState snc$sea(final Level level, final BlockPos pos, final Operation<FluidState> original) {
		FluidState state = original.call(level, pos);
		if (state.isEmpty() && SnWater.active()) {
			FluidState water = SnWater.fluidAt(level, pos);
			if (water != null) {
				return water;
			}
		}

		return state;
	}

	@WrapOperation(
		method = {"getWaterLevelAbove", "checkInWater", "isUnderwater"},
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/material/FluidState;getHeight(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)F")
	)
	private float snc$seaHeight(final FluidState state, final BlockGetter level, final BlockPos pos, final Operation<Float> original) {
		float height = SnWater.active() ? SnWater.substitutedHeight(level, pos) : -1.0F;
		return height >= 0.0F ? height : original.call(state, level, pos);
	}
}
