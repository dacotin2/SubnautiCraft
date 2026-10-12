package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.EntityFluidInteraction;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Entities meet Subnautica's ocean as Minecraft water: swimming, floating, slow movement, splashes
 * (SkyCraft's EntityFluidInteractionMixin, MIT). See {@link SnWater}.
 */
@Mixin(EntityFluidInteraction.class)
abstract class EntityFluidInteractionMixin {
	@WrapOperation(
		method = "update",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/EntityFluidInteraction;hasFluidAndLoaded(Lnet/minecraft/world/level/Level;IIIIII)Z")
	)
	private static boolean snc$oceanNearby(final Level level, final int x0, final int y0, final int z0, final int x1, final int y1, final int z1, final Operation<Boolean> original) {
		return original.call(level, x0, y0, z0, x1, y1, z1) || SnWater.anyIn(x0, y0, z0, x1, y1, z1);
	}

	@WrapOperation(
		method = "update",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/BlockGetter;getFluidState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/material/FluidState;")
	)
	private FluidState snc$ocean(final BlockGetter level, final BlockPos pos, final Operation<FluidState> original) {
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
		method = "update",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/material/FluidState;getHeight(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)F")
	)
	private float snc$oceanHeight(final FluidState state, final BlockGetter level, final BlockPos pos, final Operation<Float> original) {
		float height = SnWater.active() ? SnWater.substitutedHeight(level, pos) : -1.0F;
		return height >= 0.0F ? height : original.call(state, level, pos);
	}

	@WrapOperation(
		method = "update",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/material/FluidState;getHeightForCamera(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)F")
	)
	private float snc$oceanEyeHeight(final FluidState state, final BlockGetter level, final BlockPos pos, final Operation<Float> original) {
		float height = SnWater.active() ? SnWater.substitutedHeight(level, pos) : -1.0F;
		return height >= 0.0F ? height : original.call(state, level, pos);
	}
}
