package dev.subnauticraft.world;

import java.util.List;

/** Ray casts against Subnautica's terrain triangles (pure math). Adapted from SkyCraft's SkyRay (MIT). */
public final class SnRay {
	private SnRay() {
	}

	/** Nearest hit along a segment: {@code t} in [0, 1], the surface normal facing the ray origin. */
	public record Hit(double t, double x, double y, double z, double nx, double ny, double nz) {
	}

	/** First triangle hit on the segment from (fx, fy, fz) to (tx, ty, tz), or null. */
	public static Hit cast(final List<SnTri> tris, final double fx, final double fy, final double fz, final double tx, final double ty, final double tz) {
		double dx = tx - fx, dy = ty - fy, dz = tz - fz;
		double best = Double.POSITIVE_INFINITY;
		SnTri hitTri = null;
		for (SnTri tri : tris) {
			double t = intersect(tri, fx, fy, fz, dx, dy, dz);
			if (t >= 0.0 && t <= 1.0 && t < best) {
				best = t;
				hitTri = tri;
			}
		}

		if (hitTri == null) {
			return null;
		}

		double nx = hitTri.nx, ny = hitTri.ny, nz = hitTri.nz;
		if (nx * dx + ny * dy + nz * dz > 0.0) {
			nx = -nx;
			ny = -ny;
			nz = -nz;
		}

		return new Hit(best, fx + dx * best, fy + dy * best, fz + dz * best, nx, ny, nz);
	}

	/** Moller-Trumbore, two-sided. Returns the segment parameter or -1. */
	static double intersect(final SnTri tri, final double ox, final double oy, final double oz, final double dx, final double dy, final double dz) {
		double e1x = tri.bx - tri.ax, e1y = tri.by - tri.ay, e1z = tri.bz - tri.az;
		double e2x = tri.cx - tri.ax, e2y = tri.cy - tri.ay, e2z = tri.cz - tri.az;
		double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
		double det = e1x * px + e1y * py + e1z * pz;
		if (Math.abs(det) < 1e-12) {
			return -1.0;
		}

		double inv = 1.0 / det;
		double sx = ox - tri.ax, sy = oy - tri.ay, sz = oz - tri.az;
		double u = (sx * px + sy * py + sz * pz) * inv;
		if (u < -1e-9 || u > 1.0 + 1e-9) {
			return -1.0;
		}

		double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
		double v = (dx * qx + dy * qy + dz * qz) * inv;
		if (v < -1e-9 || u + v > 1.0 + 1e-9) {
			return -1.0;
		}

		return (e2x * qx + e2y * qy + e2z * qz) * inv;
	}

	/**
	 * Where a block placed against a Subnautica surface goes: the cell a little way out from the hit along the
	 * surface normal, so blocks sit on uneven ground slightly sunk in instead of floating or vanishing into it.
	 */
	public static int[] placementCell(final Hit hit) {
		double out = 0.4;
		return new int[] {
			(int) Math.floor(hit.x + hit.nx * out), (int) Math.floor(hit.y + hit.ny * out), (int) Math.floor(hit.z + hit.nz * out)
		};
	}

	/** Index into Direction.values() {DOWN, UP, NORTH, SOUTH, WEST, EAST} for the dominant normal axis and sign. */
	public static int dominantFace(final double nx, final double ny, final double nz) {
		double ax = Math.abs(nx), ay = Math.abs(ny), az = Math.abs(nz);
		if (ay >= ax && ay >= az) {
			return ny >= 0 ? 1 : 0;
		}

		if (ax >= az) {
			return nx >= 0 ? 5 : 4;
		}

		return nz >= 0 ? 3 : 2;
	}
}
