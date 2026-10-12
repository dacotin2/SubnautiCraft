package dev.subnauticraft.client.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.subnauticraft.client.HostState;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Linked to Subnautica, Minecraft never counts as idle: the input Subnautica passes on doesn't reset Minecraft's AFK
 * timer, so after a minute it dropped to 30 frames a second (and Steve's camera went choppy). As SkyCraft does, the
 * frame cap stays the configured one.
 */
@Mixin(FramerateLimitTracker.class)
abstract class FramerateLimitTrackerMixin {
	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void snc$neverIdleWhileLinked(final CallbackInfoReturnable<Integer> cir) {
		if (HostState.linked()) {
			cir.setReturnValue(Minecraft.getInstance().options.framerateLimit().get());
		}
	}
}
