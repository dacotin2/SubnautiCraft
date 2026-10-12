package dev.subnauticraft.client.mixin;

import dev.subnauticraft.client.HostState;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Subnautica keeps running while a Minecraft screen (inventory, chat) is open, so Minecraft's world must too
 * (SkyCraft's ScreenMixin, MIT). The exception is Subnautica asking Minecraft to pause (its own pause, loading,
 * the menu): then the pause screen it opened really pauses.
 */
@Mixin(Screen.class)
abstract class ScreenMixin {
	@Inject(method = "isPauseScreen", at = @At("HEAD"), cancellable = true)
	private void snc$neverPauseWhileLinked(final CallbackInfoReturnable<Boolean> cir) {
		if (HostState.linked() && !HostState.hostPaused) {
			cir.setReturnValue(false);
		}
	}
}
