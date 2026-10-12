package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.BoatItem;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** A boat goes down on the sea where you look at it, as on Minecraft water (Minecraft's own look only sees its blocks). */
@Mixin(BoatItem.class)
abstract class BoatItemMixin {
	@WrapOperation(
		method = "use",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/item/BoatItem;getPlayerPOVHitResult(Lnet/minecraft/world/level/Level;Lnet/minecraft/world/entity/player/Player;Lnet/minecraft/world/level/ClipContext$Fluid;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private BlockHitResult snc$onTheSea(final Level level, final Player player, final ClipContext.Fluid fluid, final Operation<BlockHitResult> original) {
		BlockHitResult hit = original.call(level, player, fluid);
		Vec3 eye = player.getEyePosition();
		BlockHitResult sea = SnWater.surfaceHit(eye, Player.calculateViewVector(player.getXRot(), player.getYRot()), player.blockInteractionRange());
		if (sea != null && (hit.getType() == HitResult.Type.MISS || sea.getLocation().distanceToSqr(eye) < hit.getLocation().distanceToSqr(eye))) {
			return sea;
		}

		return hit;
	}
}
