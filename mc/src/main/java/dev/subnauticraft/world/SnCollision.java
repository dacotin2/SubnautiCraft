package dev.subnauticraft.world;

import dev.subnauticraft.SubnautiCraft;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import net.minecraft.core.BlockPos;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.BitSetDiscreteVoxelShape;
import net.minecraft.world.phys.shapes.CubeVoxelShape;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.jspecify.annotations.Nullable;

/**
 * Subnautica's terrain as Minecraft sees it, in the SkyCraft way (https://github.com/chasmlol/SkyCraft, MIT):
 * the plugin streams the triangles of each terrain chunk's collider, and from them come
 * <ul>
 * <li>an 8x8x8 sub-voxel collision shape per block position, merged into block-collision queries
 * (BlockCollisionsMixin), so vanilla movement, step-up and landing work against the seafloor. These are not
 * blocks: Minecraft blocks can be placed in any of these cells;</li>
 * <li>the exact triangles, for the crosshair (SnClip): blocks are placed against the real surface.</li>
 * </ul>
 * Each terrain chunk is one "collider" with a stable key; a chunk sent again replaces its old version.
 */
public final class SnCollision {
	/** Triangles are bucketed in cubes of this many blocks. */
	public static final int REGION = 8;
	/** Sample spacing on a triangle when voxelizing (half a sub-voxel, so the surface has no holes). */
	private static final double SAMPLES_PER_BLOCK = 16.0;

	private record Collider(Map<Long, List<SnTri>> regionTris, Map<Long, long[]> cellBits, double cx, double cy, double cz) {
	}

	private static final Map<Long, Collider> COLLIDERS = new HashMap<>(); // worker thread only
	private static final Map<Long, Set<Long>> CELL_OWNERS = new HashMap<>(); // worker thread only
	private static final Map<Long, Set<Long>> REGION_OWNERS = new HashMap<>(); // worker thread only
	private static final ConcurrentHashMap<Long, VoxelShape> SHAPES = new ConcurrentHashMap<>();
	private static final ConcurrentHashMap<Long, SnTri[]> TRIS = new ConcurrentHashMap<>();
	private static final ExecutorService WORKER = Executors.newSingleThreadExecutor(r -> {
		Thread t = new Thread(r, "SubnautiCraft terrain");
		t.setDaemon(true);
		return t;
	});

	private SnCollision() {
	}

	public static boolean active() {
		return !TRIS.isEmpty();
	}

	public static @Nullable VoxelShape shapeAt(final BlockPos pos) {
		return SHAPES.isEmpty() ? null : SHAPES.get(pos.asLong());
	}

	/** Adds every terrain triangle whose bounds overlap {@code box}. */
	public static void trianglesNear(final AABB box, final List<SnTri> out) {
		if (TRIS.isEmpty()) {
			return;
		}

		int rx0 = Math.floorDiv((int) Math.floor(box.minX), REGION), rx1 = Math.floorDiv((int) Math.floor(box.maxX), REGION);
		int ry0 = Math.floorDiv((int) Math.floor(box.minY), REGION), ry1 = Math.floorDiv((int) Math.floor(box.maxY), REGION);
		int rz0 = Math.floorDiv((int) Math.floor(box.minZ), REGION), rz1 = Math.floorDiv((int) Math.floor(box.maxZ), REGION);
		for (int rx = rx0; rx <= rx1; rx++) {
			for (int ry = ry0; ry <= ry1; ry++) {
				for (int rz = rz0; rz <= rz1; rz++) {
					SnTri[] tris = TRIS.get(BlockPos.asLong(rx, ry, rz));
					if (tris == null) {
						continue;
					}

					for (SnTri t : tris) {
						if (t.maxX >= box.minX && t.minX <= box.maxX && t.maxY >= box.minY && t.minY <= box.maxY && t.maxZ >= box.minZ && t.minZ <= box.maxZ) {
							out.add(t);
						}
					}
				}
			}
		}
	}

	/** A terrain chunk's triangles (x, y, z of three corners each, Minecraft space), replacing any earlier version. */
	public static void submit(final long key, final float[] vertices) {
		PENDING.incrementAndGet();
		WORKER.execute(() -> {
			try {
				replace(key, vertices);
			} catch (RuntimeException e) {
				SubnautiCraft.LOG.error("terrain chunk {} failed", key, e);
			} finally {
				PENDING.decrementAndGet();
			}
		});
	}

	private static final java.util.concurrent.atomic.AtomicInteger PENDING = new java.util.concurrent.atomic.AtomicInteger();

	/** Terrain received but not turned into collision yet (Subnautica waits for 0 before letting Steve go). */
	public static int pending() {
		return PENDING.get();
	}

	/** Forget terrain further than {@code radius} blocks from (x, y, z) (Subnautica sends it again when it's near). */
	public static void evictBeyond(final double x, final double y, final double z, final double radius) {
		WORKER.execute(() -> {
			List<Long> far = new ArrayList<>();
			for (Map.Entry<Long, Collider> e : COLLIDERS.entrySet()) {
				Collider c = e.getValue();
				double dx = c.cx - x, dy = c.cy - y, dz = c.cz - z;
				if (dx * dx + dy * dy + dz * dz > radius * radius) {
					far.add(e.getKey());
				}
			}

			far.forEach(SnCollision::remove);
		});
	}

	public static void clear() {
		WORKER.execute(() -> {
			COLLIDERS.clear();
			CELL_OWNERS.clear();
			REGION_OWNERS.clear();
			SHAPES.clear();
			TRIS.clear();
		});
	}

	private static long nextReport;
	private static int received;

	private static void replace(final long key, final float[] v) {
		received++;
		long now = System.currentTimeMillis();
		if (now >= nextReport) {
			nextReport = now + 10_000L;
			SubnautiCraft.LOG.info("terrain: {} chunks received, {} held, {} collision cells, {} triangle regions", received, COLLIDERS.size(), SHAPES.size(), TRIS.size());
		}

		remove(key);
		Map<Long, List<SnTri>> regionTris = new HashMap<>();
		Map<Long, long[]> cellBits = new HashMap<>();
		double sx = 0, sy = 0, sz = 0;
		int n = 0;
		for (int o = 0; o + 8 < v.length; o += 9) {
			SnTri t = new SnTri(v, o);
			if (t.degenerate()) {
				continue;
			}

			n++;
			sx += t.ax;
			sy += t.ay;
			sz += t.az;
			for (int rx = Math.floorDiv((int) Math.floor(t.minX), REGION); rx <= Math.floorDiv((int) Math.floor(t.maxX), REGION); rx++) {
				for (int ry = Math.floorDiv((int) Math.floor(t.minY), REGION); ry <= Math.floorDiv((int) Math.floor(t.maxY), REGION); ry++) {
					for (int rz = Math.floorDiv((int) Math.floor(t.minZ), REGION); rz <= Math.floorDiv((int) Math.floor(t.maxZ), REGION); rz++) {
						regionTris.computeIfAbsent(BlockPos.asLong(rx, ry, rz), k -> new ArrayList<>()).add(t);
					}
				}
			}

			voxelize(t, cellBits);
		}

		if (n == 0) {
			return;
		}

		COLLIDERS.put(key, new Collider(regionTris, cellBits, sx / n, sy / n, sz / n));
		for (Long cell : cellBits.keySet()) {
			CELL_OWNERS.computeIfAbsent(cell, k -> new java.util.HashSet<>()).add(key);
			rebuildCell(cell);
		}

		for (Long region : regionTris.keySet()) {
			REGION_OWNERS.computeIfAbsent(region, k -> new java.util.HashSet<>()).add(key);
			rebuildRegion(region);
		}
	}

	private static void remove(final long key) {
		Collider old = COLLIDERS.remove(key);
		if (old == null) {
			return;
		}

		for (Long cell : old.cellBits.keySet()) {
			Set<Long> owners = CELL_OWNERS.get(cell);
			if (owners != null) {
				owners.remove(key);
				if (owners.isEmpty()) {
					CELL_OWNERS.remove(cell);
				}
			}

			rebuildCell(cell);
		}

		for (Long region : old.regionTris.keySet()) {
			Set<Long> owners = REGION_OWNERS.get(region);
			if (owners != null) {
				owners.remove(key);
				if (owners.isEmpty()) {
					REGION_OWNERS.remove(region);
				}
			}

			rebuildRegion(region);
		}
	}

	/** Mark every sub-voxel the triangle passes through (points sampled at half a sub-voxel apart). */
	private static void voxelize(final SnTri t, final Map<Long, long[]> cellBits) {
		int n = Math.max(1, (int) Math.ceil(t.longestEdge() * SAMPLES_PER_BLOCK));
		double ux = t.bx - t.ax, uy = t.by - t.ay, uz = t.bz - t.az;
		double wx = t.cx - t.ax, wy = t.cy - t.ay, wz = t.cz - t.az;
		for (int i = 0; i <= n; i++) {
			double a = (double) i / n;
			for (int j = 0; j <= n - i; j++) {
				double b = (double) j / n;
				double px = t.ax + ux * a + wx * b, py = t.ay + uy * a + wy * b, pz = t.az + uz * a + wz * b;
				int bx = (int) Math.floor(px), by = (int) Math.floor(py), bz = (int) Math.floor(pz);
				int vx = Math.min(7, (int) ((px - bx) * 8.0)), vy = Math.min(7, (int) ((py - by) * 8.0)), vz = Math.min(7, (int) ((pz - bz) * 8.0));
				long[] layers = cellBits.computeIfAbsent(BlockPos.asLong(bx, by, bz), k -> new long[8]);
				layers[vy] |= 1L << (vz * 8 + vx);
			}
		}
	}

	private static void rebuildCell(final long cell) {
		Set<Long> owners = CELL_OWNERS.get(cell);
		if (owners == null || owners.isEmpty()) {
			SHAPES.remove(cell);
			return;
		}

		long[] merged = new long[8];
		for (Long owner : owners) {
			long[] bits = COLLIDERS.get(owner).cellBits.get(cell);
			for (int y = 0; y < 8; y++) {
				merged[y] |= bits[y];
			}
		}

		VoxelShape shape = buildShape(merged);
		if (shape == null) {
			SHAPES.remove(cell);
		} else {
			SHAPES.put(cell, shape);
		}
	}

	private static void rebuildRegion(final long region) {
		Set<Long> owners = REGION_OWNERS.get(region);
		if (owners == null || owners.isEmpty()) {
			TRIS.remove(region);
			return;
		}

		List<SnTri> all = new ArrayList<>();
		for (Long owner : owners) {
			all.addAll(COLLIDERS.get(owner).regionTris.get(region));
		}

		TRIS.put(region, all.toArray(new SnTri[0]));
	}

	/** Layer y, bit z * 8 + x (SkyCraft's layout). */
	private static @Nullable VoxelShape buildShape(final long[] layers) {
		boolean any = false;
		boolean full = true;
		for (long layer : layers) {
			any |= layer != 0;
			full &= layer == -1L;
		}

		if (!any) {
			return null;
		}

		if (full) {
			return Shapes.block();
		}

		BitSetDiscreteVoxelShape discrete = new BitSetDiscreteVoxelShape(8, 8, 8);
		for (int y = 0; y < 8; y++) {
			long layer = layers[y];
			while (layer != 0) {
				int bit = Long.numberOfTrailingZeros(layer);
				layer &= layer - 1;
				discrete.fill(bit & 7, y, bit >>> 3);
			}
		}

		return new CubeVoxelShape(discrete);
	}
}
