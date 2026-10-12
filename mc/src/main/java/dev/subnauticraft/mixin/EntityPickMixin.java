package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnClip;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** The crosshair targets Subnautica's terrain, so blocks can be placed on the seafloor and rock walls (SkyCraft's EntityPickMixin, MIT). */
@Mixin(Entity.class)
abstract class EntityPickMixin {
	@WrapOperation(
		method = "pick",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/Level;clip(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;")
	)
	private BlockHitResult snc$pickTerrain(final Level level, final ClipContext context, final Operation<BlockHitResult> original) {
		return SnClip.refine(context.getFrom(), context.getTo(), original.call(level, context));
	}
}
