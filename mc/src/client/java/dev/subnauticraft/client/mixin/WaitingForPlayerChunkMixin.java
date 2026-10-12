package dev.subnauticraft.client.mixin;

import dev.subnauticraft.client.HostState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Placing Steve far away makes Minecraft show "Loading terrain" until the section he's in is compiled for rendering,
 * which in this 2288-block-tall world took over 12 seconds: no picture, no camera, Subnautica stuck. Subnautica keeps
 * him paused until the ground under him is there anyway, so don't wait (SkyCraft's WaitingForPlayerChunkMixin, MIT).
 */
@Mixin(targets = "net.minecraft.client.multiplayer.LevelLoadTracker$WaitingForPlayerChunk")
abstract class WaitingForPlayerChunkMixin {
	@Inject(method = "isReady", at = @At("HEAD"), cancellable = true)
	private void snc$ready(final CallbackInfoReturnable<Boolean> cir) {
		if (HostState.linked()) {
			cir.setReturnValue(true);
		}
	}
}
