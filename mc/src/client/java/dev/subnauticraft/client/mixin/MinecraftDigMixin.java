package dev.subnauticraft.client.mixin;

import dev.subnauticraft.SnDig;
import dev.subnauticraft.world.SnClip;
import net.minecraft.client.Minecraft;
import net.minecraft.core.BlockPos;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Swinging at Subnautica's terrain (the crosshair on its ground, with no block there) digs it: the cell behind the
 * face aimed at becomes a block of that terrain to mine (SnDig). Vanilla would count it as a swing at the air.
 */
@Mixin(Minecraft.class)
abstract class MinecraftDigMixin {
	@Inject(method = "startAttack", at = @At("HEAD"), cancellable = true)
	private void snc$digStart(final CallbackInfoReturnable<Boolean> cir) {
		Minecraft minecraft = (Minecraft) (Object) this;
		if (snc$dig(minecraft)) {
			minecraft.player.swing(InteractionHand.MAIN_HAND, minecraft.player.getMainHandItem().getAttackAnimation(), false);
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "continueAttack", at = @At("HEAD"))
	private void snc$digHeld(final boolean down, final CallbackInfo ci) {
		if (down) {
			snc$dig((Minecraft) (Object) this);
		}
	}

	private static boolean snc$dig(final Minecraft minecraft) {
		if (minecraft.player == null || minecraft.level == null || !(minecraft.hitResult instanceof BlockHitResult hit) || !SnClip.isTerrain(hit)
			|| minecraft.player.isSpectator() || !minecraft.level.getBlockState(hit.getBlockPos()).isAir()) {
			return false;
		}

		BlockPos cell = hit.getBlockPos().relative(hit.getDirection().getOpposite());
		if (!minecraft.level.getBlockState(cell).isAir()) {
			return false;
		}

		SnDig.swing(cell, hit.getDirection());
		return true;
	}
}
