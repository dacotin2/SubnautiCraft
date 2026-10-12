package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Vanilla sprint-swimming steers up and down with the look, but only while there's water above the head: Minecraft
 * asks the block there, which is air in Subnautica's ocean. With the ocean answered, Steve swims up like in Minecraft.
 */
@Mixin(Player.class)
abstract class PlayerSwimMixin {
	@WrapOperation(
		method = "travel",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;getFluidState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/material/FluidState;")
	)
	private FluidState snc$oceanAboveHead(final Level level, final BlockPos pos, final Operation<FluidState> original) {
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
