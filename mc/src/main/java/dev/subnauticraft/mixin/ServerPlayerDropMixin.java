package dev.subnauticraft.mixin;

import dev.subnauticraft.SnPickups;
import dev.subnauticraft.SubnautiCraft;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.Prediction;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Steve throws one of Subnautica's things out of his inventory (Q, or out of a screen) in Steve mode: it comes out in
 * Subnautica's world as the real thing ({"t":"drop"}), not as a Minecraft item. Not when he dies (Minecraft keeps
 * those) nor what's left over when a screen closes.
 */
@Mixin(ServerPlayer.class)
abstract class ServerPlayerDropMixin {
	@Inject(method = "drop(Lnet/minecraft/world/item/ItemStack;ZLnet/minecraft/util/Prediction;)Lnet/minecraft/world/entity/item/ItemEntity;",
		at = @At("HEAD"), cancellable = true)
	private void snc$dropInSubnautica(final ItemStack stack, final boolean thrownFromHand, final Prediction prediction, final CallbackInfoReturnable<ItemEntity> cir) {
		ServerPlayer player = (ServerPlayer) (Object) this;
		if (!SubnautiCraft.steve || !thrownFromHand || player.isDeadOrDying()) {
			return;
		}

		String event = SnPickups.dropEvent(stack, false);
		if (event != null) {
			SubnautiCraft.events.accept(event);
			SubnautiCraft.LOG.info("drop: {} x{} into Subnautica", stack.getHoverName().getString(), stack.getCount());
			cir.setReturnValue(null);
		}
	}
}
