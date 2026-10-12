package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.BaseFireBlock;
import net.minecraft.world.level.block.LevelEvent;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Fire that gets under Subnautica's sea anyway (spreading, an explosion, lightning) goes out at once. */
@Mixin(BaseFireBlock.class)
abstract class BaseFireBlockMixin {
	@Inject(method = "onPlace", at = @At("HEAD"), cancellable = true)
	private void snc$doused(final BlockState state, final Level level, final BlockPos pos, final BlockState oldState, final boolean movedByPiston,
		final CallbackInfo ci) {
		if (!level.isClientSide() && SnWater.under(pos)) {
			level.removeBlock(pos, false);
			level.levelEvent(LevelEvent.SOUND_EXTINGUISH_FIRE, pos, 0);
			ci.cancel();
		}
	}
}
