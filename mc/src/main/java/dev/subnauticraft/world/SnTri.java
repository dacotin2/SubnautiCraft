package dev.subnauticraft.world;

/**
 * One Subnautica terrain collision triangle in Minecraft space, with its plane and bounds.
 * Adapted from SkyCraft's SkyTri (MIT, https://github.com/chasmlol/SkyCraft).
 */
public final class SnTri {
	public final double ax, ay, az, bx, by, bz, cx, cy, cz;
	/** Unit normal (winding is not trusted: Subnautica's is mirrored into Minecraft's space). */
	public final double nx, ny, nz;
	public final double minX, minY, minZ, maxX, maxY, maxZ;

	public SnTri(final float[] v, final int o) {
		this.ax = v[o];
		this.ay = v[o + 1];
		this.az = v[o + 2];
		this.bx = v[o + 3];
		this.by = v[o + 4];
		this.bz = v[o + 5];
		this.cx = v[o + 6];
		this.cy = v[o + 7];
		this.cz = v[o + 8];
		double ux = this.bx - this.ax, uy = this.by - this.ay, uz = this.bz - this.az;
		double wx = this.cx - this.ax, wy = this.cy - this.ay, wz = this.cz - this.az;
		double qx = uy * wz - uz * wy, qy = uz * wx - ux * wz, qz = ux * wy - uy * wx;
		double len = Math.sqrt(qx * qx + qy * qy + qz * qz);
		if (len < 1e-12) {
			this.nx = 0;
			this.ny = 1;
			this.nz = 0;
		} else {
			this.nx = qx / len;
			this.ny = qy / len;
			this.nz = qz / len;
		}

		this.minX = Math.min(this.ax, Math.min(this.bx, this.cx));
		this.minY = Math.min(this.ay, Math.min(this.by, this.cy));
		this.minZ = Math.min(this.az, Math.min(this.bz, this.cz));
		this.maxX = Math.max(this.ax, Math.max(this.bx, this.cx));
		this.maxY = Math.max(this.ay, Math.max(this.by, this.cy));
		this.maxZ = Math.max(this.az, Math.max(this.bz, this.cz));
	}

	public boolean degenerate() {
		double ex = this.maxX - this.minX, ey = this.maxY - this.minY, ez = this.maxZ - this.minZ;
		return ex + ey + ez < 1e-6;
	}

	public double longestEdge() {
		double ab = Math.sqrt(sq(this.bx - this.ax) + sq(this.by - this.ay) + sq(this.bz - this.az));
		double bc = Math.sqrt(sq(this.cx - this.bx) + sq(this.cy - this.by) + sq(this.cz - this.bz));
		double ca = Math.sqrt(sq(this.ax - this.cx) + sq(this.ay - this.cy) + sq(this.az - this.cz));
		return Math.max(ab, Math.max(bc, ca));
	}

	private static double sq(final double v) {
		return v * v;
	}
}
