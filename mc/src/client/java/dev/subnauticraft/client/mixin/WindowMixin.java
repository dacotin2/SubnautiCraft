package dev.subnauticraft.client.mixin;

import com.mojang.blaze3d.platform.Window;
import dev.subnauticraft.client.HostState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Minecraft's window is off the desktop in Steve mode and Subnautica has the real focus: pretend we do (SkyCraft's WindowMixin, MIT). */
@Mixin(Window.class)
abstract class WindowMixin {
	@Inject(method = "isFocused", at = @At("HEAD"), cancellable = true)
	private void snc$focused(final CallbackInfoReturnable<Boolean> cir) {
		if (HostState.tookOver()) {
			cir.setReturnValue(true);
		}
	}

	@Inject(method = "isIconified", at = @At("HEAD"), cancellable = true)
	private void snc$notIconified(final CallbackInfoReturnable<Boolean> cir) {
		if (HostState.linked()) {
			cir.setReturnValue(false);
		}
	}
}
