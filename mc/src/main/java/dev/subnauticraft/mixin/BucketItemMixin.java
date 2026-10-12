package dev.subnauticraft.mixin;

import dev.subnauticraft.world.SnWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.BucketItem;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LevelEvent;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.Fluids;
import net.minecraft.world.phys.BlockHitResult;
import org.jspecify.annotations.Nullable;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Under Subnautica's sea a bucket meets real water: water has nowhere to go (it stays in the bucket, instead of a
 * Minecraft water block drawn inside the ocean), and lava sets into obsidian with a hiss, as lava does in water.
 */
@Mixin(BucketItem.class)
abstract class BucketItemMixin {
	@Shadow
	@Final
	protected Fluid content;

	@Inject(method = "emptyContents", at = @At("HEAD"), cancellable = true)
	private void snc$intoTheSea(final @Nullable LivingEntity user, final Level level, final BlockPos pos, final @Nullable BlockHitResult hitResult,
		final CallbackInfoReturnable<Boolean> cir) {
		if (!SnWater.under(pos)) {
			return;
		}

		if (this.content.isSame(Fluids.WATER)) {
			cir.setReturnValue(false);
		} else if (this.content.isSame(Fluids.LAVA) && level.getBlockState(pos).isAir()) {
			level.setBlock(pos, Blocks.OBSIDIAN.defaultBlockState(), Block.UPDATE_ALL_IMMEDIATE);
			if (!level.isClientSide()) {
				level.levelEvent(LevelEvent.LAVA_FIZZ, pos, 0);
			}

			cir.setReturnValue(true);
		}
	}
}
