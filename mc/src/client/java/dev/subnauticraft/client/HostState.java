package dev.subnauticraft.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import dev.subnauticraft.SubnautiCraft;

/**
 * What Subnautica last told us. Two modes:
 * <ul>
 * <li><b>Steve mode</b> ({@link SubnautiCraft#steve}): Minecraft owns the player; Subnautica only sends input,
 * its field of view, its ocean and terrain, and follows Minecraft's camera.</li>
 * <li><b>Ryley mode</b>: Subnautica's diver plays and its camera pose (already in Minecraft coordinates) drives
 * Minecraft's camera, so Minecraft's blocks stay in the right place on screen.</li>
 * </ul>
 */
public final class HostState {
	/**
	 * @param hostFrame Subnautica's frame number for this pose (echoed in the exported frame)
	 * @param x camera position
	 * @param yaw Minecraft yaw (0 = facing +Z), pitch (positive = down) and roll, in degrees
	 * @param fov vertical field of view, degrees
	 */
	public record Pose(long hostFrame, double x, double y, double z, float yaw, float pitch, float roll, float fov, long receivedNanos) {
	}

	private static final long TIMEOUT_NANOS = 2_000_000_000L;
	private static volatile Pose latest;
	private static volatile float fov = 70.0F;
	private static volatile float overscan = 1.0F;
	private static volatile long lastMessageNanos;
	/** Steve mode: where Subnautica's camera looks (Minecraft yaw, pitch); Subnautica turns its camera with no lag and Steve follows. */
	private static volatile float lookYaw, lookPitch;
	private static volatile boolean hasLook;
	/** Subnautica is paused, loading, in its menu, or waiting for the ground under Steve: Minecraft is paused too. */
	public static volatile boolean hostPaused;
	/**
	 * Subnautica lights Minecraft's world itself (its sun, caustics, shadows, lamps): Minecraft draws only what blocks
	 * look like, with no light of its own (no shaded sides, no ambient occlusion, a white lightmap).
	 */
	public static volatile boolean unlit;
	/** The Ryley-mode pose this frame renders with, taken once per frame so every hook agrees. Render thread only. */
	private static Pose frame;

	private HostState() {
	}

	/** Any message from Subnautica: it's still there. */
	static void touch() {
		lastMessageNanos = System.nanoTime();
	}

	static void setFov(final float degrees, final float margin) {
		fov = degrees;
		overscan = Math.max(1.0F, Math.min(2.0F, margin));
	}

	static void setLook(final float yaw, final float pitch) {
		lookYaw = yaw;
		lookPitch = pitch;
		hasLook = true;
	}

	public static boolean hasLook() {
		return hasLook;
	}

	public static float lookYaw() {
		return lookYaw;
	}

	public static float lookPitch() {
		return lookPitch;
	}

	/** {"t":"cam","f":frame,"p":[x,y,z],"r":[yaw,pitch,roll],"fov":deg} (Ryley mode) */
	static void update(final JsonObject m) {
		JsonArray p = m.getAsJsonArray("p");
		JsonArray r = m.getAsJsonArray("r");
		float degrees = m.has("fov") ? m.get("fov").getAsFloat() : fov;
		fov = degrees;
		latest = new Pose(
			m.has("f") ? m.get("f").getAsLong() : 0L,
			p.get(0).getAsDouble(), p.get(1).getAsDouble(), p.get(2).getAsDouble(),
			r.get(0).getAsFloat(), r.get(1).getAsFloat(), r.size() > 2 ? r.get(2).getAsFloat() : 0.0F,
			degrees, System.nanoTime()
		);
	}

	/** Subnautica paused or left: stop following straight away instead of waiting for the timeout. */
	static void release() {
		latest = null;
	}

	/** Subnautica is connected and talking. */
	public static boolean linked() {
		return System.nanoTime() - lastMessageNanos < TIMEOUT_NANOS;
	}

	/** Steve mode and Subnautica is there: Minecraft takes Subnautica's input as its own. */
	public static boolean tookOver() {
		return SubnautiCraft.steve && linked();
	}

	/**
	 * The vertical field of view Minecraft draws its world with: Subnautica's, widened by the overscan margin (the
	 * picture lags a frame or two behind a fast turn, and the margin keeps its edges off the screen). The frame header
	 * carries it, so Subnautica rebuilds the picture with the same one; the hand keeps its own.
	 */
	public static float fov() {
		double half = Math.toRadians(fov) * 0.5;
		return (float) Math.toDegrees(2.0 * Math.atan(Math.tan(half) * overscan));
	}

	/** The latest Ryley-mode pose if Subnautica is still sending, else null. */
	static Pose live() {
		Pose p = latest;
		return p != null && System.nanoTime() - p.receivedNanos() < TIMEOUT_NANOS ? p : null;
	}

	/** The Ryley-mode pose for the frame being rendered, or null (Steve mode, or Subnautica not attached). */
	public static Pose frame() {
		return frame;
	}

	public static void beginFrame() {
		frame = SubnautiCraft.steve ? null : live();
		SubnautiCraft.active = frame != null || tookOver();
	}
}
