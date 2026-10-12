package dev.subnauticraft.mixin;

import dev.subnauticraft.SubnautiCraft;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import org.objectweb.asm.Opcodes;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Ryley mode (Subnautica moves the camera and Steve just follows it): Steve is a ghost, so neither side collides
 * him with anything. In Steve mode Minecraft's own physics run (against Subnautica's terrain and water).
 */
@Mixin(Player.class)
abstract class PlayerMixin {
	@Inject(method = "tick", at = @At(value = "FIELD", target = "Lnet/minecraft/world/entity/player/Player;noPhysics:Z", opcode = Opcodes.PUTFIELD, shift = At.Shift.AFTER))
	private void snc$ghost(final CallbackInfo ci) {
		if (SubnautiCraft.active && !SubnautiCraft.steve) {
			((Entity) (Object) this).noPhysics = true;
		}
	}
}
