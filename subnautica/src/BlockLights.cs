using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Minecraft's light-giving blocks (torches, sea lanterns, glowstone, campfires...) as real lights in Subnautica:
    /// point lights at each block, in the block's colour and reaching as far as its Minecraft light level, so they
    /// light up the seafloor, fish, bases and the diver, not just Minecraft's own picture. Only the nearest
    /// <see cref="Plugin.MaxBlockLights"/> are lit at once.
    /// </summary>
    internal sealed class BlockLights
    {
        private struct Source
        {
            public Vector3Int Block;
            public Vector3 Position;
            public Color Color;
            public int Level;
            public bool Flickers;
            /// <summary>The whole block glows (a torch, a sea lantern; not a furnace, whose fire is only its front).</summary>
            public bool Glows;
            public float Seed;
        }

        private readonly Dictionary<long, Source> sources = new Dictionary<long, Source>();
        private readonly List<Light> pool = new List<Light>();
        private readonly List<Source> lit = new List<Source>();
        private readonly List<KeyValuePair<float, long>> byDistance = new List<KeyValuePair<float, long>>();
        private GameObject parent;
        private float nextPick;
        private bool dirty = true;

        private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        /// <summary>{"t":"lights","reset":bool,"set":[[x,y,z,level,"torch"],...],"clear":[x,y,z,...]}</summary>
        public void Apply(JObject m)
        {
            if (m["reset"] != null && (bool)m["reset"])
            {
                sources.Clear();
            }

            int[] clear = m["clear"]?.ToObject<int[]>();
            for (int i = 0; clear != null && i + 2 < clear.Length; i += 3)
            {
                sources.Remove(Key(clear[i], clear[i + 1], clear[i + 2]));
            }

            if (m["set"] is JArray set)
            {
                foreach (JToken t in set)
                {
                    if (!(t is JArray a) || a.Count < 5)
                    {
                        continue;
                    }

                    int x = (int)a[0], y = (int)a[1], z = (int)a[2];
                    string block = (string)a[4];
                    sources[Key(x, y, z)] = new Source
                    {
                        Block = new Vector3Int(x, y, z),
                        Glows = GlowsWhole(block),
                        Position = Coords.BlockCenter(x, y, z),
                        Color = ColorOf(block, out bool flickers),
                        Level = Mathf.Clamp((int)a[3], 1, 15),
                        Flickers = flickers,
                        Seed = (x * 73856093 ^ y * 19349663 ^ z * 83492791) % 1000 * 0.1f,
                    };
                }
            }

            dirty = true;
        }

        /// <summary>The light-giving blocks within <paramref name="radius"/> that glow as a whole, for Minecraft's picture.</summary>
        public void Emitters(Vector3 near, float radius, List<Vector3Int> into)
        {
            into.Clear();
            foreach (Source s in sources.Values)
            {
                if (s.Glows && (s.Position - near).sqrMagnitude < radius * radius)
                {
                    into.Add(s.Block);
                }
            }
        }

        private static bool GlowsWhole(string block)
        {
            block = block ?? "";
            return !(block.Contains("furnace") || block.Contains("smoker") || block.Contains("enchanting_table") || block.Contains("ender_chest")
                || block.Contains("brewing_stand") || block.Contains("end_portal_frame") || block.Contains("respawn_anchor") || block.Contains("sculk")
                || block.Contains("dragon_egg") || block.Contains("creaking_heart") || block.Contains("vault") || block.Contains("trial_spawner"));
        }

        public void Clear()
        {
            sources.Clear();
            dirty = true;
            Update(Vector3.zero, false);
        }

        /// <summary>Every frame: the nearest sources get the pooled lights (re-picked a few times a second), torches flicker.</summary>
        public void Update(Vector3 camera, bool on)
        {
            on &= Plugin.BlockLights.Value && sources.Count > 0;
            if (!on)
            {
                if (lit.Count > 0)
                {
                    lit.Clear();
                    Show();
                }

                return;
            }

            if (dirty || Time.unscaledTime >= nextPick)
            {
                dirty = false;
                nextPick = Time.unscaledTime + 0.3f;
                Pick(camera);
                Show();
            }

            float strength = Plugin.BlockLightStrength.Value;
            for (int i = 0; i < lit.Count && i < pool.Count; i++)
            {
                Source s = lit[i];
                float intensity = strength * (0.6f + 0.9f * s.Level / 15f);
                if (s.Flickers)
                {
                    intensity *= 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 7f, s.Seed);
                }

                pool[i].intensity = intensity;
            }
        }

        private void Pick(Vector3 camera)
        {
            byDistance.Clear();
            foreach (KeyValuePair<long, Source> s in sources)
            {
                float d = (s.Value.Position - camera).sqrMagnitude;
                if (d < 96f * 96f)
                {
                    byDistance.Add(new KeyValuePair<float, long>(d, s.Key));
                }
            }

            byDistance.Sort((a, b) => a.Key.CompareTo(b.Key));
            lit.Clear();
            int max = Plugin.MaxBlockLights.Value;
            for (int i = 0; i < byDistance.Count && lit.Count < max; i++)
            {
                lit.Add(sources[byDistance[i].Value]);
            }
        }

        private void Show()
        {
            if (lit.Count > 0 && parent == null)
            {
                // first use, or Subnautica's SceneCleaner took the old lights with it (leaving a game to the menu)
                pool.Clear();
                parent = new GameObject("SubnautiCraft lights");
                Object.DontDestroyOnLoad(parent);
                parent.AddComponent<SceneCleanerPreserve>();
            }

            while (pool.Count < lit.Count)
            {
                var go = new GameObject("light");
                go.transform.SetParent(parent.transform, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                pool.Add(l);
            }

            for (int i = 0; i < pool.Count; i++)
            {
                Light l = pool[i];
                if (l == null)
                {
                    continue;
                }

                bool use = i < lit.Count;
                if (use)
                {
                    Source s = lit[i];
                    l.transform.position = s.Position;
                    l.color = s.Color;
                    // Minecraft's light fades one level per block: a torch (14) reaches about 14 blocks
                    l.range = s.Level + 1f;
                }

                if (l.gameObject.activeSelf != use)
                {
                    l.gameObject.SetActive(use);
                }
            }
        }

        /// <summary>The colour of a block's light (Minecraft's light has none of its own, so these are what the blocks look like).</summary>
        private static Color ColorOf(string block, out bool flickers)
        {
            flickers = false;
            block = block ?? "";
            if (block.Contains("soul"))
            {
                flickers = block.Contains("torch") || block.Contains("fire") || block.Contains("lantern");
                return new Color(0.35f, 0.85f, 1f);
            }

            if (block.Contains("sea_lantern") || block.Contains("conduit") || block.Contains("beacon"))
            {
                return new Color(0.7f, 0.95f, 1f);
            }

            if (block.Contains("sea_pickle") || block.Contains("glow_lichen") || block.Contains("verdant_froglight"))
            {
                return new Color(0.55f, 1f, 0.7f);
            }

            if (block.Contains("redstone_lamp") || block.Contains("copper_bulb"))
            {
                return new Color(1f, 0.78f, 0.5f);
            }

            if (block.Contains("redstone"))
            {
                return new Color(1f, 0.25f, 0.15f);
            }

            if (block.Contains("amethyst") || block.Contains("crying_obsidian") || block.Contains("portal") || block.Contains("end_gateway")
                || block.Contains("respawn_anchor") || block.Contains("pearlescent_froglight"))
            {
                return new Color(0.75f, 0.45f, 1f);
            }

            if (block.Contains("end_rod"))
            {
                return new Color(1f, 0.95f, 1f);
            }

            if (block.Contains("magma") || block.Contains("lava"))
            {
                return new Color(1f, 0.45f, 0.15f);
            }

            if (block.Contains("sculk"))
            {
                return new Color(0.3f, 0.8f, 0.9f);
            }

            if (block.Contains("torch") || block.Contains("fire") || block.Contains("lantern") || block.Contains("candle") || block.Contains("campfire"))
            {
                flickers = true;
                return new Color(1f, 0.72f, 0.4f);
            }

            if (block.Contains("glowstone") || block.Contains("shroomlight") || block.Contains("ochre_froglight"))
            {
                return new Color(1f, 0.82f, 0.5f);
            }

            return new Color(1f, 0.85f, 0.65f);
        }
    }
}
