package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnWater;
import net.minecraft.world.item.context.BlockPlaceContext;
import net.minecraft.world.level.block.CampfireBlock;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** A campfire put down under Subnautica's sea comes out unlit, as one put in Minecraft water does. */
@Mixin(CampfireBlock.class)
abstract class CampfireBlockMixin {
	@Inject(method = "getStateForPlacement", at = @At("RETURN"), cancellable = true)
	private void snc$unlitInTheSea(final BlockPlaceContext context, final CallbackInfoReturnable<BlockState> cir) {
		BlockState state = cir.getReturnValue();
		if (state != null && SnWater.under(context.getClickedPos())) {
			cir.setReturnValue(state.setValue(CampfireBlock.LIT, false));
		}
	}
}
