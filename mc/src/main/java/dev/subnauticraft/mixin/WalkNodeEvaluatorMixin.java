package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnWater;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.level.pathfinder.NodeEvaluator;
import net.minecraft.world.level.pathfinder.WalkNodeEvaluator;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

/**
 * A mob that floats in water (zombies, drowned) starts its path at the top of the water it's in. In Subnautica's sea
 * that's the surface, often hundreds of blocks up: every path started there and none reached anything, so underwater
 * such mobs stood still, even next to their prey. Deep in the sea, the path starts where the mob is.
 */
@Mixin(WalkNodeEvaluator.class)
abstract class WalkNodeEvaluatorMixin extends NodeEvaluator {
	@WrapOperation(method = "getStart", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/level/pathfinder/WalkNodeEvaluator;canFloat()Z"))
	private boolean snc$notFromTheSurface(final WalkNodeEvaluator self, final Operation<Boolean> original) {
		return original.call(self) && SnWater.depthAbove(this.mob.getY()) < 2.0;
	}

	/** ...and not from above it either (on Subnautica's seafloor a mob isn't always "on ground"): from its own feet. */
	@WrapOperation(method = "getStart", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/Mob;onGround()Z"))
	private boolean snc$fromItsFeet(final Mob mob, final Operation<Boolean> original) {
		return original.call(mob) || SnWater.depthAbove(mob.getY()) >= 2.0;
	}
}
