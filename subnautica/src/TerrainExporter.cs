using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Sends Subnautica's solid world around Steve to Minecraft, like SkyCraft's "stage A" (ray casts on a grid):
    /// every 8 m region near the player is probed with lines of rays 0.5 m apart along all three axes (back faces
    /// included, so cave ceilings, overhangs and cliff walls are found too), and every hit becomes a small patch
    /// lying on the surface. Minecraft turns the patches into collision (Steve walks on the island, bumps into
    /// rock) and crosshair targets (blocks go on the ground). It doesn't depend on reading Subnautica's meshes
    /// (its pooled terrain meshes couldn't be read), and it covers what the diver himself bumps into: terrain, wrecks,
    /// big rocks, bases, Precursor buildings. Creatures, items, vehicles, plants and Minecraft's own blocks are left out.
    /// The lifepod and the Cyclops float and move: the regions around them are probed again twice a second.
    /// </summary>
    internal sealed class TerrainExporter
    {
        private const int Magic = 0x52524554; // "TERR"
        private const float Region = 8f;
        private const float Spacing = 0.5f;
        private const float PatchHalf = 0.45f;
        private const int MaxHitsPerLine = 24;
        private const int RegionsPerFrame = 2;
        private const float EvictMove = 64f;
        private const float EvictRadius = 160f;

        private sealed class RegionState
        {
            public float NextScan;
            public float LastProbe;
            public int Hits = -1;
            /// <summary>Something that moves (the lifepod, a Cyclops) is in this region: probe it often, send it when it changed.</summary>
            public bool Moving;
            /// <summary>What was last sent (the patches, to the centimetre).</summary>
            public int Hash;
        }

        private const float MovingInterval = 0.5f;
        private readonly HashSet<long> moving = new HashSet<long>();
        private readonly List<Collider> bodyColliders = new List<Collider>();
        private SubRoot[] subs = new SubRoot[0];
        private float nextMovingScan, nextSubSearch;

        /// <summary>Every region within <paramref name="radius"/> of <paramref name="center"/> is known to Minecraft (empty ones probed again since <paramref name="since"/>).</summary>
        public bool CoveredAround(Vector3 center, float radius, float since)
        {
            int r = Mathf.CeilToInt(radius / Region);
            int cx = Mathf.FloorToInt(center.x / Region), cy = Mathf.FloorToInt(center.y / Region), cz = Mathf.FloorToInt(center.z / Region);
            for (int x = cx - r; x <= cx + r; x++)
            {
                for (int y = cy - r; y <= cy + r; y++)
                {
                    for (int z = cz - r; z <= cz + r; z++)
                    {
                        long key = Key(x, y, z);
                        if ((RegionCenter(key) - center).sqrMagnitude > (radius + Region) * (radius + Region))
                        {
                            continue;
                        }

                        // terrain doesn't move: a region already sent is good; one found empty before may just not have
                        // been built yet, so it must be probed again (empty ones are, every 2 s)
                        if (!regions.TryGetValue(key, out RegionState state) || (state.Hits <= 0 && state.LastProbe < since))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private readonly Dictionary<long, RegionState> regions = new Dictionary<long, RegionState>();
        private readonly List<long> candidates = new List<long>();
        private readonly List<Vector3> patches = new List<Vector3>(); // point, normal, point, normal...
        private Vector3 lastEvict = new Vector3(float.NaN, 0f, 0f);
        private float nextReport;
        private int mask;

        public int SentRegions;
        public long SentTriangles;

        public void Reset()
        {
            regions.Clear();
            lastEvict = new Vector3(float.NaN, 0f, 0f);
        }

        public void Tick(HostLink link, Vector3 center)
        {
            int radius = Plugin.TerrainRadius.Value;
            if (radius <= 0 || link.Pending > 6)
            {
                return;
            }

            if (mask == 0)
            {
                // whatever the diver collides with (structures sit on several layers), but not the diver himself
                mask = (LayerID.GetAllCollisionsMask(LayerID.Player) | (1 << LayerID.TerrainCollider) | (1 << LayerID.Default))
                    & ~(1 << LayerID.Player) & ~(1 << LayerID.Trigger) & ~(1 << LayerID.UI);
            }

            if (Time.unscaledTime >= nextMovingScan)
            {
                nextMovingScan = Time.unscaledTime + MovingInterval;
                TrackMoving(center, radius);
            }

            if (float.IsNaN(lastEvict.x) || (center - lastEvict).sqrMagnitude > EvictMove * EvictMove)
            {
                // Minecraft forgets what's far behind; we forget it too, so it is probed again on the way back
                lastEvict = center;
                double[] p = Coords.ToMc(center);
                link.Send(string.Format(CultureInfo.InvariantCulture, "{{\"t\":\"terrainevict\",\"p\":[{0:F1},{1:F1},{2:F1}],\"r\":{3}}}",
                    p[0], p[1], p[2], EvictRadius.ToString(CultureInfo.InvariantCulture)));
                var far = new List<long>();
                foreach (long key in regions.Keys)
                {
                    if ((RegionCenter(key) - center).sqrMagnitude > (EvictRadius - 16f) * (EvictRadius - 16f))
                    {
                        far.Add(key);
                    }
                }

                foreach (long key in far)
                {
                    regions.Remove(key);
                }
            }

            // regions near the player, nearest first: the ground under Steve comes before the ground ahead
            candidates.Clear();
            int r = Mathf.CeilToInt(radius / Region);
            int cx = Mathf.FloorToInt(center.x / Region), cy = Mathf.FloorToInt(center.y / Region), cz = Mathf.FloorToInt(center.z / Region);
            for (int x = cx - r; x <= cx + r; x++)
            {
                for (int y = cy - r; y <= cy + r; y++)
                {
                    for (int z = cz - r; z <= cz + r; z++)
                    {
                        long key = Key(x, y, z);
                        Vector3 c = RegionCenter(key);
                        if ((c - center).sqrMagnitude > (radius + Region) * (radius + Region))
                        {
                            continue;
                        }

                        if (!regions.TryGetValue(key, out RegionState state) || Time.unscaledTime >= state.NextScan)
                        {
                            candidates.Add(key);
                        }
                    }
                }
            }

            candidates.Sort((a, b) => (RegionCenter(a) - center).sqrMagnitude.CompareTo((RegionCenter(b) - center).sqrMagnitude));
            for (int i = 0; i < candidates.Count && i < RegionsPerFrame; i++)
            {
                Probe(link, candidates[i], center);
            }

            if (Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + 10f;
                Plugin.Log.LogInfo($"terrain: {regions.Count} regions probed, {SentRegions} sent so far ({SentTriangles} triangles)");
            }
        }

        private void Probe(HostLink link, long key, Vector3 player)
        {
            if (!regions.TryGetValue(key, out RegionState state))
            {
                state = new RegionState();
                regions[key] = state;
            }

            Vector3 min = RegionCenter(key) - Vector3.one * (Region * 0.5f);
            patches.Clear();
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                int n = Mathf.RoundToInt(Region / Spacing);
                for (int axis = 0; axis < 3; axis++)
                {
                    Vector3 dir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    for (int i = 0; i < n; i++)
                    {
                        for (int j = 0; j < n; j++)
                        {
                            Vector3 origin = min;
                            float u = (i + 0.5f) * Spacing, v = (j + 0.5f) * Spacing;
                            switch (axis)
                            {
                                case 0: origin += new Vector3(0f, u, v); break;
                                case 1: origin += new Vector3(u, 0f, v); break;
                                default: origin += new Vector3(u, v, 0f); break;
                            }

                            Line(origin, dir);
                        }
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
            }

            int hits = patches.Count / 2;
            state.LastProbe = Time.unscaledTime;
            float distance = (RegionCenter(key) - player).magnitude;
            // empty regions are probed again soon (Subnautica builds colliders only near the player); full ones now and then
            state.NextScan = Time.unscaledTime + (state.Moving ? MovingInterval : hits == 0 ? 2f : distance < 16f ? 6f : 20f);
            int hash = 17;
            foreach (Vector3 v in patches)
            {
                hash = hash * 31 + Mathf.RoundToInt(v.x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(v.y * 100f);
                hash = hash * 31 + Mathf.RoundToInt(v.z * 100f);
            }

            if (hits == state.Hits && (!state.Moving || hash == state.Hash))
            {
                return; // nothing new: Minecraft has it already
            }

            state.Hits = hits;
            state.Hash = hash;
            link.SendBinary(Encode(key));
            SentRegions++;
            SentTriangles += hits * 2;
        }

        /// <summary>
        /// The regions the lifepod and any Cyclops near the player overlap now are probed every half second; a region
        /// one of them has just left is probed once more, so the collision doesn't stay where it was.
        /// </summary>
        private void TrackMoving(Vector3 center, int radius)
        {
            if (Time.unscaledTime >= nextSubSearch)
            {
                nextSubSearch = Time.unscaledTime + 3f;
                subs = Object.FindObjectsOfType<SubRoot>();
            }

            var now = new HashSet<long>();
            if (EscapePod.main != null)
            {
                AddMoving(EscapePod.main.gameObject, center, radius, now);
            }

            foreach (SubRoot sub in subs)
            {
                if (sub != null && !sub.isBase)
                {
                    AddMoving(sub.gameObject, center, radius, now);
                }
            }

            foreach (long key in moving)
            {
                if (!now.Contains(key) && regions.TryGetValue(key, out RegionState left))
                {
                    left.Moving = false;
                    left.Hits = -1;
                    left.NextScan = 0f;
                }
            }

            moving.Clear();
            moving.UnionWith(now);
        }

        private void AddMoving(GameObject body, Vector3 center, int radius, HashSet<long> into)
        {
            bodyColliders.Clear();
            body.GetComponentsInChildren(false, bodyColliders);
            bool any = false;
            Bounds bounds = default;
            foreach (Collider c in bodyColliders)
            {
                if (c.enabled && !c.isTrigger)
                {
                    if (!any)
                    {
                        bounds = c.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(c.bounds);
                    }
                }
            }

            if (!any || (bounds.center - center).sqrMagnitude > (radius + 32f) * (radius + 32f) || bounds.size.magnitude > 200f)
            {
                return;
            }

            bounds.Expand(1f);
            for (int x = Mathf.FloorToInt(bounds.min.x / Region); x <= Mathf.FloorToInt(bounds.max.x / Region); x++)
            {
                for (int y = Mathf.FloorToInt(bounds.min.y / Region); y <= Mathf.FloorToInt(bounds.max.y / Region); y++)
                {
                    for (int z = Mathf.FloorToInt(bounds.min.z / Region); z <= Mathf.FloorToInt(bounds.max.z / Region); z++)
                    {
                        long key = Key(x, y, z);
                        into.Add(key);
                        if (!regions.TryGetValue(key, out RegionState state))
                        {
                            state = new RegionState();
                            regions[key] = state;
                        }

                        if (!state.Moving)
                        {
                            state.Moving = true;
                            state.NextScan = 0f;
                        }
                    }
                }
            }
        }

        /// <summary>Every surface the line crosses inside the region (front and back faces), as patches.</summary>
        private void Line(Vector3 origin, Vector3 dir)
        {
            float remaining = Region;
            for (int k = 0; k < MaxHitsPerLine && remaining > 0f; k++)
            {
                if (!Physics.Raycast(origin, dir, out RaycastHit hit, remaining, mask, QueryTriggerInteraction.Ignore))
                {
                    return;
                }

                if (Solid(hit.collider))
                {
                    patches.Add(hit.point);
                    patches.Add(hit.normal);
                }

                float step = hit.distance + 0.01f;
                origin += dir * step;
                remaining -= step;
            }
        }

        /// <summary>The world, not things in it: no moving bodies (creatures, items, vehicles, the diver), nothing alive (plants), not Minecraft's blocks.</summary>
        private bool Solid(Collider collider)
        {
            int id = collider.GetInstanceID();
            if (!solidCache.TryGetValue(id, out bool solid))
            {
                solid = IsWorld(collider);
                if (solidCache.Count > 20000)
                {
                    solidCache.Clear();
                }

                solidCache[id] = solid;
            }

            return solid;
        }

        private readonly Dictionary<int, bool> solidCache = new Dictionary<int, bool>();

        private static bool IsWorld(Collider collider)
        {
            if (BlockColliders.Owns(collider))
            {
                return false;
            }

            // the lifepod and the Cyclops are solid even though they float (and have health)
            if (collider.GetComponentInParent<EscapePod>() != null)
            {
                return true;
            }

            SubRoot sub = collider.GetComponentInParent<SubRoot>();
            if (sub != null && !sub.isBase)
            {
                return collider.GetComponentInParent<Vehicle>() == null;
            }

            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
            {
                return false;
            }

            if (collider.gameObject.layer == LayerID.TerrainCollider)
            {
                return true;
            }

            return collider.GetComponentInParent<LiveMixin>() == null && collider.GetComponentInParent<Player>() == null
                && collider.GetComponentInParent<Pickupable>() == null && collider.GetComponentInParent<Vehicle>() == null;
        }

        /// <summary>"TERR", key, count, then each triangle's three corners in Minecraft space (x mirrored): two per patch.</summary>
        private byte[] Encode(long key)
        {
            int count = patches.Count / 2 * 2;
            var stream = new MemoryStream(16 + count * 36);
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic);
                w.Write(key);
                w.Write(count);
                for (int i = 0; i + 1 < patches.Count; i += 2)
                {
                    Vector3 p = patches[i], normal = patches[i + 1];
                    Vector3 t = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized * PatchHalf;
                    Vector3 b = Vector3.Cross(normal, t).normalized * PatchHalf;
                    Vector3 a = p - t - b, c = p + t - b, d = p + t + b, e = p - t + b;
                    Corner(w, a);
                    Corner(w, c);
                    Corner(w, d);
                    Corner(w, a);
                    Corner(w, d);
                    Corner(w, e);
                }
            }

            return stream.ToArray();
        }

        private static void Corner(BinaryWriter w, Vector3 p)
        {
            w.Write(-p.x);
            w.Write(p.y);
            w.Write(p.z);
        }

        private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        private static Vector3 RegionCenter(long key)
        {
            int x = Unpack(key >> 42), y = Unpack(key >> 21), z = Unpack(key);
            return new Vector3((x + 0.5f) * Region, (y + 0.5f) * Region, (z + 0.5f) * Region);
        }

        private static int Unpack(long bits)
        {
            int v = (int)(bits & 0x1FFFFF);
            return v >= 0x100000 ? v - 0x200000 : v;
        }
    }
}
