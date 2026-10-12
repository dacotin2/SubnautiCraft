package dev.subnauticraft.client.mixin;

import com.mojang.blaze3d.platform.InputConstants;
import com.mojang.blaze3d.platform.Window;
import dev.subnauticraft.client.HostState;
import dev.subnauticraft.client.InputBridge;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** In Steve mode the keyboard state and the mouse come from Subnautica, not from SDL (SkyCraft's InputConstantsMixin, MIT). */
@Mixin(InputConstants.class)
abstract class InputConstantsMixin {
	@Inject(method = "isKeyDown", at = @At("HEAD"), cancellable = true)
	private static void snc$isKeyDown(final int key, final CallbackInfoReturnable<Boolean> cir) {
		if (HostState.tookOver()) {
			cir.setReturnValue(InputBridge.isKeyDown(key));
		}
	}

	@Inject(method = "grabMouse", at = @At("HEAD"), cancellable = true)
	private static void snc$grabMouse(final Window window, final double xpos, final double ypos, final CallbackInfo ci) {
		if (HostState.tookOver()) {
			ci.cancel(); // Subnautica owns the real mouse
		}
	}

	@Inject(method = "releaseMouse", at = @At("HEAD"), cancellable = true)
	private static void snc$releaseMouse(final Window window, final double xpos, final double ypos, final CallbackInfo ci) {
		if (HostState.tookOver()) {
			ci.cancel();
		}
	}
}
