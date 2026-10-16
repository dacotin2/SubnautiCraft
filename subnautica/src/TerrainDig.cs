using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using UnityEngine;
using WorldStreaming;

namespace SubnautiCraft
{
    /// <summary>
    /// Steve digs Subnautica's terrain. When he swings at the seafloor or a rock face, Minecraft asks here what is there
    /// ({"t":"dig"}): if it's terrain, a block of it (sand, gravel, stone...) appears in that spot for him to mine. When
    /// he has mined it ({"t":"carve"}), the terrain loses that cube for real. That is the game's own voxel edit (its
    /// LargeWorldStreamer.PerformBoxEdit, which today's terrain streamer no longer reads), done on the terrain data the
    /// streamer has loaded, which is then rebuilt around the hole (look and collision). What was dug is kept in the save
    /// (a file in its folder) and cut again whenever that terrain loads.
    /// </summary>
    internal static class TerrainDig
    {
        private const float Cube = 1.05f; // a hair over a block, so the hole is clean
        private const string FileName = "SubnautiCraft-dug.txt";
        private static readonly FieldInfo OctreeData = AccessTools.Field(typeof(Octree), "data");
        private static readonly FieldInfo BatchAllocator = AccessTools.Field(typeof(BatchOctrees), "allocator");
        private static readonly FieldInfo OctreeSize = AccessTools.Field(typeof(BatchOctreesStreamer), "octreeSize");
        private static readonly FieldInfo OctreesPerBatch = AccessTools.Field(typeof(BatchOctreesStreamer), "numOctreesPerBatch");
        private static readonly FieldInfo CollisionLevel = AccessTools.Field(typeof(ClipmapStreamer), "collisionLevel");
        private static readonly FieldInfo CellState = AccessTools.Field(typeof(ClipmapCell), "state");
        private static readonly FieldInfo CellChunk = AccessTools.Field(typeof(ClipmapCell), "chunk");
        private static readonly MethodInfo CellActivate = AccessTools.Method(typeof(ClipmapCell), "ActivateChunkAndCollider");
        private static readonly MethodInfo CellReturnChunk = AccessTools.Method(typeof(ClipmapCell), "ReturnChunkToPool");
        private static readonly MethodInfo LevelCellRange = AccessTools.Method(typeof(ClipmapLevel), "GetCellRange");
        private static readonly UWE.Task.Function FinishTask = FinishOnMainThread;
        private static readonly UWE.Task.Function SwapTask = SwapOnStreamingThread;
        private static readonly UWE.Task.Function ShowTask = ShowOnMainThread;
        private static readonly UWE.Task.Function CarveTask = CarveOnStreamingThread;
        private static readonly object Gate = new object();
        /// <summary>What was cut: x, y, z and, for a blast, its radius (0: one Minecraft block).</summary>
        private static readonly List<Vector4> Dug = new List<Vector4>();
        /// <summary>Terrain data replaced by a cut: given back to its pool once no meshing thread can still be reading it.</summary>
        private static readonly List<KeyValuePair<long, KeyValuePair<UWE.SplitNativeArrayPool<byte>, NativeArray<byte>>>> Retired =
            new List<KeyValuePair<long, KeyValuePair<UWE.SplitNativeArrayPool<byte>, NativeArray<byte>>>>();

        /// <summary>The terrain's frame (the land's transform, read on the main thread for the streaming thread).</summary>
        private static Matrix4x4 landToWorld = Matrix4x4.identity, worldToLand = Matrix4x4.identity;
        private static bool haveLand;
        private static string loadedFor;

        /// <summary>Every frame: keep the terrain's frame, and read what was dug in this save once it's loaded.</summary>
        public static void Tick()
        {
            LargeWorldStreamer streamer = LargeWorldStreamer.main;
            if (streamer == null || streamer.land == null)
            {
                return;
            }

            Transform land = streamer.land.transform;
            lock (Gate)
            {
                landToWorld = land.localToWorldMatrix;
                worldToLand = land.worldToLocalMatrix;
                haveLand = true;
            }

            string path = SavePath();
            if (path == null || path == loadedFor)
            {
                return;
            }

            loadedFor = path;
            var read = new List<Vector4>();
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length >= 3)
                        {
                            read.Add(new Vector4(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture),
                                float.Parse(parts[2], CultureInfo.InvariantCulture), parts.Length > 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 0f));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"dig: couldn't read {path}: {e.Message}");
            }

            lock (Gate)
            {
                Dug.Clear();
                Dug.AddRange(read);
            }

            if (read.Count > 0)
            {
                Plugin.Log.LogInfo($"dig: {read.Count} holes dug or blasted in this save, cut again as the terrain loads");
                foreach (Vector4 cut in read)
                {
                    Enqueue(cut);
                }
            }
        }

        /// <summary>{"t":"dig","p":[block x,y,z],"f":"up|down|side"}: is that Minecraft cell Subnautica's terrain, and of what?</summary>
        public static void Ask(JObject m, HostLink link)
        {
            int[] p = m["p"]?.ToObject<int[]>();
            if (p == null || p.Length < 3)
            {
                return;
            }

            Vector3 centre = Centre(p);
            string block = null;
            if (Physics.CheckBox(centre, Vector3.one * 0.5f, Quaternion.identity, 1 << LayerID.TerrainCollider, QueryTriggerInteraction.Ignore))
            {
                block = Material(centre, (string)m["f"]);
            }

            link.Send("{\"t\":\"terrainblock\",\"p\":[" + p[0] + "," + p[1] + "," + p[2] + "],\"b\":" + (block == null ? "null" : "\"" + block + "\"") + "}");
            Plugin.Log.LogInfo($"dig: Minecraft asks about {centre}: " + (block ?? "no terrain there"));
        }

        /// <summary>{"t":"carve","p":[block x,y,z]}: Steve mined the block that stood for the terrain there: the terrain goes too.</summary>
        public static void Carve(JObject m, TerrainExporter terrain)
        {
            int[] p = m["p"]?.ToObject<int[]>();
            if (p == null || p.Length < 3)
            {
                return;
            }

            Vector3 centre = Centre(p);
            Add(new Vector4(centre.x, centre.y, centre.z, 0f));
            // the hole is rebuilt over the next frames: Minecraft's copy of the terrain there is probed again meanwhile
            terrain.Refresh(centre, 5f, 1.5f);
            Plugin.Log.LogInfo($"dig: terrain cut at {centre}");
        }

        /// <summary>A Minecraft explosion (TNT, a creeper) blasts a round hole of that radius in the terrain.</summary>
        public static void Blast(Vector3 centre, float radius, TerrainExporter terrain)
        {
            Add(new Vector4(centre.x, centre.y, centre.z, radius));
            terrain.Refresh(centre, 6f, radius + 1.5f);
            Plugin.Log.LogInfo($"dig: terrain blasted at {centre}, radius {radius:F1}");
        }

        private static void Add(Vector4 cut)
        {
            lock (Gate)
            {
                Dug.Add(cut);
            }

            Save();
            Enqueue(cut);
        }

        /// <summary>What the terrain there looks like in Minecraft: sand or gravel underfoot, stone in walls and ceilings.</summary>
        private static string Material(Vector3 centre, string face)
        {
            string biome = (LargeWorld.main != null ? LargeWorld.main.GetBiome(centre) : null) ?? "";
            biome = biome.ToLowerInvariant();
            if (biome.Contains("lava") || biome.Contains("ilz"))
            {
                return "minecraft:blackstone";
            }

            if (face == "up")
            {
                return biome.Contains("lostriver") || biome.Contains("jelly") || biome.Contains("mushroom") ? "minecraft:gravel" : "minecraft:sand";
            }

            return centre.y < -500f ? "minecraft:deepslate" : "minecraft:stone";
        }

        /// <summary>The centre of Minecraft's cell (x, y, z) in Subnautica (Minecraft's x is Unity's -x).</summary>
        private static Vector3 Centre(int[] p) => new Vector3(-(p[0] + 0.5f), p[1] + 0.5f, p[2] + 0.5f);

        private static string SavePath()
        {
            string folder = SaveLoadManager.GetTemporarySavePath();
            return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : Path.Combine(folder, FileName);
        }

        /// <summary>The list goes in the save's working folder: the game copies it into the save when it saves.</summary>
        private static void Save()
        {
            string path = SavePath();
            if (path == null)
            {
                return;
            }

            var lines = new List<string>();
            lock (Gate)
            {
                foreach (Vector4 c in Dug)
                {
                    lines.Add(c.x.ToString("F2", CultureInfo.InvariantCulture) + " " + c.y.ToString("F2", CultureInfo.InvariantCulture) + " "
                        + c.z.ToString("F2", CultureInfo.InvariantCulture) + (c.w > 0f ? " " + c.w.ToString("F2", CultureInfo.InvariantCulture) : ""));
                }
            }

            try
            {
                File.WriteAllLines(path, lines);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"dig: couldn't write {path}: {e.Message}");
            }
        }

        /// <summary>The cut is made on the terrain streamer's own thread, between its loads and unloads.</summary>
        private static void Enqueue(Vector4 cut)
        {
            WorldStreamer v2 = LargeWorldStreamer.main != null ? LargeWorldStreamer.main.streamerV2 : null;
            if (v2 == null || v2.octreesStreamer == null)
            {
                return;
            }

            v2.octreesStreamer.streamingThread.Enqueue(CarveTask, v2, cut);
        }

        private static void CarveOnStreamingThread(object owner, object state)
        {
            try
            {
                Cut((WorldStreamer)owner, (Vector4)state);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"dig: the cut failed: {e}");
            }
        }

        /// <summary>A terrain batch (re)loaded: the cubes dug in it are cut again (cutting twice changes nothing).</summary>
        internal static void OnBatchLoaded(BatchOctreesStreamer octrees, Int3 batchId)
        {
            List<Vector4> inside = null;
            lock (Gate)
            {
                if (!haveLand || Dug.Count == 0 || LargeWorldStreamer.main == null || LargeWorldStreamer.main.streamerV2 == null
                    || octrees != LargeWorldStreamer.main.streamerV2.octreesStreamer)
                {
                    return; // (the far, low-detail terrain has its own copy: a cube there doesn't show)
                }

                int size = octrees.batchSize;
                foreach (Vector4 c in Dug)
                {
                    // any part of it in this batch
                    Bounds bounds = Shape(c);
                    Int3 a = Int3.Floor(worldToLand.MultiplyPoint3x4(bounds.min)), b = Int3.Floor(worldToLand.MultiplyPoint3x4(bounds.max));
                    Int3 lo = Int3.FloorDiv(new Int3(Mathf.Min(a.x, b.x) - 1, Mathf.Min(a.y, b.y) - 1, Mathf.Min(a.z, b.z) - 1), size);
                    Int3 hi = Int3.FloorDiv(new Int3(Mathf.Max(a.x, b.x) + 1, Mathf.Max(a.y, b.y) + 1, Mathf.Max(a.z, b.z) + 1), size);
                    if (batchId.x >= lo.x && batchId.x <= hi.x && batchId.y >= lo.y && batchId.y <= hi.y && batchId.z >= lo.z && batchId.z <= hi.z)
                    {
                        (inside ?? (inside = new List<Vector4>())).Add(c);
                    }
                }
            }

            if (inside == null)
            {
                return;
            }

            WorldStreamer v2 = LargeWorldStreamer.main.streamerV2;
            foreach (Vector4 c in inside)
            {
                try
                {
                    Cut(v2, c);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"dig: cutting {c} again failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// As LargeWorldStreamer.PerformVoxelEdit with a box, subtracting: each octree the cube touches is unpacked, the
        /// cube's signed distance blended out of every voxel around it, packed again and swapped in; then the terrain
        /// meshes over it are rebuilt.
        /// </summary>
        /// <summary>The space a cut takes: a block's cube, or a blast's sphere.</summary>
        private static Bounds Shape(Vector4 cut)
        {
            var centre = new Vector3(cut.x, cut.y, cut.z);
            return new Bounds(centre, Vector3.one * (cut.w > 0f ? 2f * cut.w : Cube));
        }

        private static void Cut(WorldStreamer v2, Vector4 shape)
        {
            if (v2 == null || v2.octreesStreamer == null || v2.clipmapStreamer == null)
            {
                return;
            }

            Matrix4x4 toWorld, toLand;
            lock (Gate)
            {
                if (!haveLand)
                {
                    return;
                }

                toWorld = landToWorld;
                toLand = worldToLand;
            }

            BatchOctreesStreamer octrees = v2.octreesStreamer;
            int size = (int)OctreeSize.GetValue(octrees);
            int perBatch = (int)OctreesPerBatch.GetValue(octrees);
            var centre = new Vector3(shape.x, shape.y, shape.z);
            var cube = Shape(shape);
            Int3 a = Int3.Floor(toLand.MultiplyPoint3x4(cube.min)), b = Int3.Floor(toLand.MultiplyPoint3x4(cube.max));
            Int3 mins = new Int3(Mathf.Min(a.x, b.x) - 1, Mathf.Min(a.y, b.y) - 1, Mathf.Min(a.z, b.z) - 1);
            Int3 maxs = new Int3(Mathf.Max(a.x, b.x) + 1, Mathf.Max(a.y, b.y) + 1, Mathf.Max(a.z, b.z) + 1);
            var args = new VoxelandData.OctNode.BlendArgs(VoxelandData.OctNode.BlendOp.Subtraction, false, 0);
            bool any = false;
            foreach (Int3 treeId in Int3.Range(Int3.FloorDiv(mins, size), Int3.FloorDiv(maxs, size)))
            {
                Octree tree = octrees.GetOctree(treeId);
                if (tree == null || tree.IsEmpty())
                {
                    continue; // not loaded, or nothing but water there
                }

                var data = (NativeArray<byte>)OctreeData.GetValue(tree);
                if (!data.IsCreated || data.Length < 4)
                {
                    continue;
                }

                var compact = new CompactOctree();
                using (var stream = new MemoryStream())
                {
                    var writer = new BinaryWriter(stream);
                    byte[] bytes = data.ToArray();
                    int nodes = 1 + MaxNode(bytes, 0);
                    writer.Write((ushort)nodes);
                    writer.Write(bytes, 0, nodes * 4);
                    writer.Flush();
                    stream.Position = 0;
                    compact.Read(new BinaryReader(stream), 0, Int3.zero, treeId);
                }

                VoxelandData.OctNode root = compact.ToVLOctree();
                compact.NotifyUnload();
                Int3 origin = new Int3(treeId.x * size, treeId.y * size, treeId.z * size);
                bool changed = false;
                foreach (Int3 block in Int3.Range(Int3.Max(mins, origin), Int3.Min(maxs, new Int3(origin.x + size - 1, origin.y + size - 1, origin.z + size - 1))))
                {
                    Vector3 ws = toWorld.MultiplyPoint3x4(new Vector3(block.x + 0.5f, block.y + 0.5f, block.z + 0.5f));
                    float distance = shape.w > 0f ? shape.w - Vector3.Distance(ws, centre) : VoxelandMisc.SignedDistToBox(cube, ws);
                    var cut = new VoxelandData.OctNode((byte)(distance >= 0f ? 1 : 0), VoxelandData.OctNode.EncodeDensity(distance));
                    int x = block.x - origin.x, y = block.y - origin.y, z = block.z - origin.z;
                    VoxelandData.OctNode before = root.GetNode(x, y, z, size / 2);
                    VoxelandData.OctNode after = VoxelandData.OctNode.Blend(before, cut, args);
                    if (after.type != before.type || after.density != before.density)
                    {
                        root.SetNode(x, y, z, size / 2, after.type, after.density);
                        changed = true;
                    }
                }

                if (changed)
                {
                    root.Collapse();
                    CompactOctree edited = CompactOctree.CreateFrom(root);
                    BatchOctrees batch = octrees.GetBatch(Int3.FloorDiv(treeId, perBatch));
                    var pool = batch != null ? (UWE.SplitNativeArrayPool<byte>)BatchAllocator.GetValue(batch) : null;
                    if (pool != null)
                    {
                        NativeArray<byte> fresh = pool.Get(edited.dataCount);
                        NativeArray<byte>.Copy(edited.data.Array, edited.data.Offset, fresh, 0, edited.dataCount);
                        OctreeData.SetValue(tree, fresh);
                        Retire(pool, data);
                        any = true;
                    }

                    edited.NotifyUnload();
                }

                root.Clear();
            }

            if (any)
            {
                // terrain still waiting to be built picks the cut up as it loads; what's built is rebuilt in place
                v2.clipmapStreamer.OnRangeLoaded(octrees, new Int3.Bounds(mins, maxs), octrees.minLod, octrees.maxLod);
                Rebuild(v2, new Int3.Bounds(mins, maxs));
            }
        }

        /// <summary>
        /// Streaming thread: the terrain pieces already built over the cut are meshed again from the cut data, as when
        /// they load (MeshBuilder), for the nearest level of detail and the one with collision; each new piece replaces
        /// the old one once it's ready, so the ground never has a gap meanwhile.
        /// </summary>
        private static void Rebuild(WorldStreamer v2, Int3.Bounds blocks)
        {
            ClipmapStreamer clip = v2.clipmapStreamer;
            var lods = new HashSet<int> { 0, (int)CollisionLevel.GetValue(clip) };
            foreach (int lod in lods)
            {
                ClipmapLevel level = clip.GetLevel(lod);
                if (level == null)
                {
                    continue;
                }

                var cells = (Int3.Bounds)LevelCellRange.Invoke(level, new object[] { blocks });
                foreach (Int3 cellId in Int3.Range(cells.mins, cells.maxs))
                {
                    ClipmapCell cell = level.GetCell(cellId);
                    if (cell == null || cell.id != cellId || !Built(cell))
                    {
                        continue;
                    }

                    MeshBuilder builder = clip.meshBuilderPool.Get();
                    builder.Reset(level.id, cell.id, level.cellSize, level.settings, v2.blockTypes);
                    builder.DoThreadablePart(v2.GetOctreesStreamer(level.id), clip.settings.collision);
                    clip.buildLayersThread.Enqueue(FinishTask, cell, builder);
                }
            }
        }

        /// <summary>Built and not in the middle of loading or unloading.</summary>
        private static bool Built(ClipmapCell cell)
        {
            string state = CellState.GetValue(cell).ToString();
            return state == "Loaded" || state == "Visible" || state == "HiddenByParent" || state == "HiddenByChildren";
        }

        /// <summary>Main thread: the new piece is made and its collision baked, as the game does.</summary>
        private static void FinishOnMainThread(object owner, object state)
        {
            UWE.CoroutineHost.StartCoroutine(Finish((ClipmapCell)owner, (MeshBuilder)state));
        }

        private static System.Collections.IEnumerator Finish(ClipmapCell cell, MeshBuilder builder)
        {
            ClipmapStreamer clip = cell.level.streamer;
            ClipmapChunk fresh = builder.DoFinalizePart(clip.host.chunkRoot, clip.host.terrainPoolManager);
            clip.meshBuilderPool.Return(builder);
            if (fresh != null)
            {
                yield return (System.Collections.IEnumerator)CellActivate.Invoke(cell, new object[] { fresh });
            }

            clip.streamingThread.Enqueue(SwapTask, cell, fresh);
        }

        /// <summary>Streaming thread (where the cell's own changes happen): the new piece takes the old one's place.</summary>
        private static void SwapOnStreamingThread(object owner, object state)
        {
            var cell = (ClipmapCell)owner;
            var fresh = (ClipmapChunk)state;
            bool built = Built(cell);
            bool visible = CellState.GetValue(cell).ToString() == "Visible";
            ClipmapChunk old = fresh;
            if (built)
            {
                old = (ClipmapChunk)CellChunk.GetValue(cell);
                CellChunk.SetValue(cell, fresh);
            }

            cell.level.streamer.toggleChunksThread.Enqueue(ShowTask, cell, new object[] { built ? fresh : null, old, visible });
        }

        /// <summary>Main thread: the new piece shows (if the cell is shown), the old one goes back to the pool.</summary>
        private static void ShowOnMainThread(object owner, object state)
        {
            var cell = (ClipmapCell)owner;
            var parts = (object[])state;
            var fresh = (ClipmapChunk)parts[0];
            var old = (ClipmapChunk)parts[1];
            if (fresh != null)
            {
                if ((bool)parts[2])
                {
                    fresh.Show();
                }
                else
                {
                    fresh.Hide(cell.level.settings.keepGrassVisible);
                }
            }

            if (old != null)
            {
                if (!cell.level.streamer.host.terrainPoolManager.meshPoolingEnabled)
                {
                    MeshBuilder.DestroyMeshes(old);
                }

                CellReturnChunk.Invoke(cell, new object[] { old });
            }

            if (fresh != null)
            {
                Plugin.Log.LogInfo($"dig: terrain piece {cell.id} (detail {cell.level.id}) rebuilt with the cut");
            }
        }

        /// <summary>The highest node index a packed octree uses (its array may be longer: pools hand out bigger ones).</summary>
        private static int MaxNode(byte[] bytes, int node)
        {
            int first = bytes[node * 4 + 2] | (bytes[node * 4 + 3] << 8);
            if (first == 0)
            {
                return node;
            }

            int max = node;
            for (int i = 0; i < 8; i++)
            {
                max = Math.Max(max, MaxNode(bytes, first + i));
            }

            return max;
        }

        private static void Retire(UWE.SplitNativeArrayPool<byte> pool, NativeArray<byte> old)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (Gate)
            {
                Retired.Add(new KeyValuePair<long, KeyValuePair<UWE.SplitNativeArrayPool<byte>, NativeArray<byte>>>(now, new KeyValuePair<UWE.SplitNativeArrayPool<byte>, NativeArray<byte>>(pool, old)));
                for (int i = Retired.Count - 1; i >= 0; i--)
                {
                    if (now - Retired[i].Key > TimeSpan.TicksPerSecond * 30)
                    {
                        Retired[i].Value.Key.Return(Retired[i].Value.Value);
                        Retired.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>Before the terrain over a freshly loaded batch is built from it: the cuts go in first.</summary>
    [HarmonyPatch(typeof(BatchOctreesStreamer), nameof(BatchOctreesStreamer.OnBatchLoaded))]
    internal static class TerrainDigReloadPatch
    {
        private static void Prefix(BatchOctreesStreamer __instance, Int3 batchId) => TerrainDig.OnBatchLoaded(__instance, batchId);
    }
}
