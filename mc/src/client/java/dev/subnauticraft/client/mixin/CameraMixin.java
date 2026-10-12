package dev.subnauticraft.client.mixin;

import dev.subnauticraft.SubnautiCraft;
import dev.subnauticraft.client.FrameExporter;
import dev.subnauticraft.client.HostState;
import dev.subnauticraft.client.PlayerSync;
import java.util.Locale;
import net.minecraft.client.Camera;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.phys.Vec3;
import org.joml.Quaternionf;
import org.joml.Vector3f;
import org.joml.Vector3fc;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Ryley mode: Subnautica's camera replaces the player's (position, rotation with roll, field of view).
 * Steve mode: Minecraft's own camera (Steve's eyes, or F5), looking where Subnautica's camera looks, at
 * Subnautica's field of view; after the update its pose goes back to Subnautica, which puts its camera there.
 */
@Mixin(Camera.class)
abstract class CameraMixin {
	private static final float DEG = (float) (Math.PI / 180.0);
	@Shadow @Final private static Vector3fc FORWARDS;
	@Shadow @Final private static Vector3fc UP;
	@Shadow @Final private static Vector3fc LEFT;
	@Shadow @Final private Vector3f forwards;
	@Shadow @Final private Vector3f up;
	@Shadow @Final private Vector3f left;
	@Shadow @Final private Quaternionf rotation;
	@Shadow private float xRot;
	@Shadow private float yRot;
	@Shadow private boolean detached;
	@Shadow private int matrixPropertiesDirty;
	@Shadow private float depthFar;

	@Shadow
	protected abstract void setPosition(double x, double y, double z);

	@Shadow
	public abstract Vec3 position();

	@Inject(method = "update", at = @At("HEAD"))
	private void snc$beginFrame(final DeltaTracker deltaTracker, final CallbackInfo ci) {
		HostState.beginFrame();
		PlayerSync.frame(deltaTracker.getGameTimeDeltaPartialTick(true));
		Minecraft minecraft = Minecraft.getInstance();
		LocalPlayer player = minecraft.player;
		if (HostState.tookOver() && HostState.hasLook() && player != null && minecraft.gui.screen() == null) {
			// SkyCraft's rule: the host turns its camera with no lag; Minecraft takes that look for everything else
			float yaw = HostState.lookYaw(), pitch = HostState.lookPitch();
			player.setYRot(yaw);
			player.setXRot(pitch);
			player.yRotO = yaw;
			player.xRotO = pitch;
		}
	}

	@Inject(method = "alignWithEntity", at = @At("TAIL"))
	private void snc$hostCamera(final float partialTicks, final CallbackInfo ci) {
		HostState.Pose p = HostState.frame();
		if (p == null) {
			return;
		}

		this.xRot = p.pitch();
		this.yRot = p.yaw();
		this.rotation.rotationYXZ((float) Math.PI - p.yaw() * DEG, -p.pitch() * DEG, p.roll() * DEG);
		FORWARDS.rotate(this.rotation, this.forwards);
		UP.rotate(this.rotation, this.up);
		LEFT.rotate(this.rotation, this.left);
		this.matrixPropertiesDirty |= 3;
		this.setPosition(p.x(), p.y(), p.z());
		this.detached = false;
	}

	@Inject(method = "calculateFov", at = @At("HEAD"), cancellable = true)
	private void snc$hostFov(final float partialTicks, final CallbackInfoReturnable<Float> cir) {
		if (SubnautiCraft.active) {
			cir.setReturnValue(HostState.fov());
		}
	}

	@Inject(method = "update", at = @At("TAIL"))
	private void snc$afterUpdate(final DeltaTracker deltaTracker, final CallbackInfo ci) {
		HostState.Pose p = HostState.frame();
		Vec3 at = this.position();
		FrameExporter.setCamera(at.x, at.y, at.z, this.yRot, this.xRot, p != null ? p.roll() : 0.0F, HostState.fov(), p != null ? p.hostFrame() : 0L, this.depthFar);
		if (!HostState.tookOver()) {
			return;
		}

		Minecraft minecraft = Minecraft.getInstance();
		LocalPlayer player = minecraft.player;
		if (player == null) {
			return;
		}

		// where Subnautica puts its camera (Steve's eyes, or Minecraft's F5 camera) and its diver (Steve's feet); how
		// fast he moves (Subnautica carries the camera forward to its own frame time); his health (the diver shows it);
		// which player object this is (a respawn makes a new one, with the same entity id, at the world spawn until
		// Subnautica places it)
		Vec3 feet = player.getPosition(deltaTracker.getGameTimeDeltaPartialTick(true));
		Vec3 v = player.getDeltaMovement().scale(20.0);
		boolean screen = minecraft.gui.screen() != null && !HostState.hostPaused;
		SubnautiCraft.events.accept(String.format(Locale.ROOT,
			"{\"t\":\"pov\",\"p\":[%.4f,%.4f,%.4f],\"r\":[%.3f,%.3f],\"f\":[%.4f,%.4f,%.4f],\"v\":[%.3f,%.3f,%.3f],\"scr\":%b,\"tp\":%b,\"sw\":%b,\"fly\":%b,\"hp\":%.2f,\"hpMax\":%.2f,\"dead\":%b,\"pid\":%d,\"tq\":%d}",
			at.x, at.y, at.z, this.yRot, this.xRot, feet.x, feet.y, feet.z, v.x, v.y, v.z, screen, this.detached,
			player.isInWater(), player.getAbilities().flying, player.getHealth(), player.getMaxHealth(), player.isDeadOrDying(), System.identityHashCode(player),
			dev.subnauticraft.world.SnCollision.pending()));
	}
}
