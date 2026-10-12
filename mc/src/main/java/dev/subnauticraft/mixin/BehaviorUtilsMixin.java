package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.ai.behavior.BehaviorUtils;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Fish pick their next swim target among "swimmable" spots: below Subnautica's surface, open water counts. */
@Mixin(BehaviorUtils.class)
abstract class BehaviorUtilsMixin {
	@WrapOperation(
		method = "getRandomSwimmablePos",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;getBlockState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/block/state/BlockState;")
	)
	private static BlockState snc$swimmableOcean(final Level level, final BlockPos pos, final Operation<BlockState> original) {
		BlockState state = original.call(level, pos);
		BlockState water = SnWater.pathState(state, pos);
		return water != null ? water : state;
	}
}
