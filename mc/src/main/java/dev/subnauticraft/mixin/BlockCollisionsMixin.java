package dev.subnauticraft.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.subnauticraft.world.SnCollision;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockCollisions;
import net.minecraft.world.level.CollisionGetter;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.shapes.CollisionContext;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Adds Subnautica's terrain to every block-collision query: vanilla movement, step-up, landing and swimming
 * then run unchanged against the seafloor (SkyCraft's BlockCollisionsMixin, MIT).
 */
@Mixin(BlockCollisions.class)
abstract class BlockCollisionsMixin {
	@WrapOperation(
		method = "computeNext",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/world/phys/shapes/CollisionContext;getCollisionShape(Lnet/minecraft/world/level/block/state/BlockState;Lnet/minecraft/world/level/CollisionGetter;Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/phys/shapes/VoxelShape;"
		)
	)
	private VoxelShape snc$addTerrain(final CollisionContext context, final BlockState state, final CollisionGetter level, final BlockPos pos, final Operation<VoxelShape> original) {
		VoxelShape blockShape = original.call(context, state, level, pos);
		VoxelShape terrain = SnCollision.shapeAt(pos);
		if (terrain == null) {
			return blockShape;
		}

		return blockShape.isEmpty() ? terrain : Shapes.or(blockShape, terrain);
	}
}
