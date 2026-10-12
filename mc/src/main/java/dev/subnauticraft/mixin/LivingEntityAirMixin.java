package dev.subnauticraft.mixin;

import dev.subnauticraft.SubnautiCraft;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * In Steve mode Subnautica's oxygen decides when Steve runs out of air (tanks, rebreathers and air pumps keep
 * working); when it's empty, Subnautica's drowning damage reaches him. Minecraft's own 15-second air bar would
 * drown him long before any tank runs dry, so it stays full.
 */
@Mixin(LivingEntity.class)
abstract class LivingEntityAirMixin {
	@Inject(method = "decreaseAirSupply", at = @At("HEAD"), cancellable = true)
	private void snc$subnauticaBreathes(final int currentSupply, final CallbackInfoReturnable<Integer> cir) {
		if (SubnautiCraft.steve && (Object) this instanceof Player) {
			cir.setReturnValue(currentSupply);
		}
	}
}
