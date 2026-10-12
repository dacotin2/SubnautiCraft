using System.Collections.Generic;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>Every solid Minecraft block near the player gets an invisible 1 m box in Subnautica, as terrain: you, fish and the Seamoth bump into your builds.</summary>
    internal sealed class BlockColliders
    {
        private const int Max = 20000;
        private readonly Dictionary<long, GameObject> live = new Dictionary<long, GameObject>();
        private static GameObject parent;

        /// <summary>One of Minecraft's blocks (not Subnautica's world): the terrain sampler leaves these out.</summary>
        public static bool Owns(Collider collider) => parent != null && collider.transform.parent == parent.transform;

        private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        public void Apply(int[] set, int[] clear)
        {
            if (parent == null)
            {
                // first use, or Subnautica's SceneCleaner took the old boxes with it (leaving game to the menu)
                live.Clear();
                parent = new GameObject("SubnautiCraft blocks");
                Object.DontDestroyOnLoad(parent);
                parent.AddComponent<SceneCleanerPreserve>();
            }

            for (int i = 0; clear != null && i + 2 < clear.Length; i += 3)
            {
                long key = Key(clear[i], clear[i + 1], clear[i + 2]);
                if (live.TryGetValue(key, out GameObject box))
                {
                    Object.Destroy(box);
                    live.Remove(key);
                }
            }

            for (int i = 0; set != null && i + 2 < set.Length && live.Count < Max; i += 3)
            {
                long key = Key(set[i], set[i + 1], set[i + 2]);
                if (live.ContainsKey(key))
                {
                    continue;
                }

                var box = new GameObject("block");
                box.transform.SetParent(parent.transform, false);
                box.transform.position = Coords.BlockCenter(set[i], set[i + 1], set[i + 2]);
                box.AddComponent<BoxCollider>().size = Vector3.one;
                // on the terrain layer: creatures steer around it and can't swim through it, vehicles bump into it,
                // and Subnautica's builder treats it as ground (bases can stand on Minecraft blocks)
                box.layer = LayerID.TerrainCollider;
                live.Add(key, box);
            }
        }

        public void Clear()
        {
            foreach (GameObject box in live.Values)
            {
                if (box != null)
                {
                    Object.Destroy(box);
                }
            }

            live.Clear();
        }
    }
}
