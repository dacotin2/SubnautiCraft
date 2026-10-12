package dev.subnauticraft.client.mixin;

import dev.subnauticraft.client.HostState;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.world.level.CardinalLighting;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Every side of a block equally bright while Subnautica lights the world (its sun decides which side is in shade). */
@Mixin(ClientLevel.class)
abstract class ClientLevelMixin {
	private static final CardinalLighting FLAT = new CardinalLighting(1.0F, 1.0F, 1.0F, 1.0F, 1.0F, 1.0F);

	@Inject(method = "cardinalLighting", at = @At("HEAD"), cancellable = true)
	private void snc$flat(final CallbackInfoReturnable<CardinalLighting> cir) {
		if (HostState.unlit) {
			cir.setReturnValue(FLAT);
		}
	}
}
