package dev.subnauticraft.client.mixin;

import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.renderpearl.api.textures.GpuTexture;
import dev.subnauticraft.client.HostState;
import net.minecraft.client.renderer.Lightmap;
import net.minecraft.client.renderer.state.LightmapRenderState;
import org.joml.Vector4f;
import org.joml.Vector4fc;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * A white lightmap while Subnautica lights the world: every block at full brightness whatever its sky and block light
 * in Minecraft (dark corners, night, a torch's glow), so the picture is just what things look like.
 */
@Mixin(Lightmap.class)
abstract class LightmapMixin {
	private static final Vector4fc WHITE = new Vector4f(1.0F, 1.0F, 1.0F, 1.0F);

	@Shadow
	@Final
	private GpuTexture texture;

	private boolean snc$whitened;

	@Inject(method = "render", at = @At("HEAD"), cancellable = true)
	private void snc$white(final LightmapRenderState renderState, final CallbackInfo ci) {
		if (HostState.unlit) {
			RenderSystem.getDevice().createCommandEncoder().clearColorTexture(this.texture, WHITE);
			this.snc$whitened = true;
			ci.cancel();
		} else if (this.snc$whitened) {
			// back to Minecraft's own light: recompute it now, not whenever the light next changes
			this.snc$whitened = false;
			renderState.needsUpdate = true;
		}
	}
}
