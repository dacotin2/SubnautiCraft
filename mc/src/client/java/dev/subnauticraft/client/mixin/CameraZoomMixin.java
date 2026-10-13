package dev.subnauticraft.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnClip;
import net.minecraft.client.Camera;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** The third-person camera (F5) stops at Subnautica's rock and seafloor as it does at blocks, instead of going through them. */
@Mixin(Camera.class)
abstract class CameraZoomMixin {
	@WrapOperation(
		method = "getMaxZoom",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clip(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private BlockHitResult snc$stopAtTerrain(final Level level, final ClipContext context, final Operation<BlockHitResult> original) {
		return SnClip.refine(context.getFrom(), context.getTo(), original.call(level, context));
	}
}
