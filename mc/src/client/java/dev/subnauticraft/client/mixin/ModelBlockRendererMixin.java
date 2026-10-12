package dev.subnauticraft.client.mixin;

import dev.subnauticraft.client.HostState;
import net.minecraft.client.renderer.block.ModelBlockRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/** No ambient occlusion while Subnautica lights the world: its own lighting shades the corners. */
@Mixin(ModelBlockRenderer.class)
abstract class ModelBlockRendererMixin {
	@ModifyVariable(method = "<init>", at = @At("HEAD"), argsOnly = true, ordinal = 0)
	private static boolean snc$noOcclusion(final boolean ambientOcclusion) {
		return ambientOcclusion && !HostState.unlit;
	}
}
