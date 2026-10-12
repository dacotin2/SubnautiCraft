package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.item.FireChargeItem;
import net.minecraft.world.item.FlintAndSteelItem;
import net.minecraft.world.item.context.UseOnContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.CampfireBlock;
import net.minecraft.world.level.block.CandleBlock;
import net.minecraft.world.level.block.CandleCakeBlock;
import net.minecraft.world.level.block.LevelEvent;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Flint and steel and fire charges don't light anything under Subnautica's sea: a hiss and a puff of steam instead. */
@Mixin({FlintAndSteelItem.class, FireChargeItem.class})
abstract class FireStarterMixin {
	@Inject(method = "useOn", at = @At("HEAD"), cancellable = true)
	private void snc$wet(final UseOnContext context, final CallbackInfoReturnable<InteractionResult> cir) {
		Level level = context.getLevel();
		BlockPos pos = context.getClickedPos();
		BlockState state = level.getBlockState(pos);
		// the same choice the items make: light the campfire or candle clicked, or set fire to the space in front of the face
		boolean lightsIt = CampfireBlock.canLight(state) || CandleBlock.canLight(state) || CandleCakeBlock.canLight(state);
		BlockPos target = lightsIt ? pos : pos.relative(context.getClickedFace());
		if (SnWater.under(target)) {
			if (!level.isClientSide()) {
				level.levelEvent(LevelEvent.LAVA_FIZZ, target, 0);
			}

			cir.setReturnValue(InteractionResult.FAIL);
		}
	}
}
