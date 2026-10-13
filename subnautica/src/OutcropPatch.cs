using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace SubnautiCraft
{
    /// <summary>
    /// With OutcropMinecraftOres on, breaking an outcrop (limestone, sandstone, shale) also drops the vanilla Minecraft
    /// ore of what Subnautica rolled for it: limestone gives copper or titanium, so raw copper or raw iron; sandstone
    /// silver, gold or lead; shale diamond, gold or lithium. It falls in Minecraft's world at the outcrop. Off by
    /// default: Subnautica's own chunk is already a Minecraft item when Steve picks it up, so this is a second one.
    /// </summary>
    internal static class OutcropDrops
    {
        private static readonly Dictionary<TechType, string> Ores = new Dictionary<TechType, string>
        {
            { TechType.Copper, "minecraft:raw_copper" },
            { TechType.Titanium, "minecraft:raw_iron" },
            { TechType.Gold, "minecraft:raw_gold" },
            { TechType.Silver, "minecraft:raw_iron" },
            { TechType.Lead, "minecraft:raw_iron" },
            { TechType.Diamond, "minecraft:diamond" },
            { TechType.Lithium, "minecraft:amethyst_shard" },
            { TechType.Quartz, "minecraft:quartz" },
            { TechType.Magnetite, "minecraft:raw_iron" },
            { TechType.Nickel, "minecraft:raw_iron" },
            { TechType.AluminumOxide, "minecraft:redstone" },
            { TechType.Kyanite, "minecraft:lapis_lazuli" },
            { TechType.UraniniteCrystal, "minecraft:emerald" },
            { TechType.Sulphur, "minecraft:gunpowder" },
        };

        /// <summary>What the outcrop being broken rolled (ChooseRandomResource runs once per chance inside BreakIntoResources).</summary>
        internal static List<TechType> Rolled;

        internal static bool Active => Bridge.SteveEngaged || Bridge.SteveDriving;

        internal static void Drop(BreakableResource outcrop, List<TechType> rolled)
        {
            if (rolled.Count == 0)
            {
                rolled.Add(outcrop.defaultPrefabTechType);
            }

            Vector3 at = outcrop.transform.position + outcrop.transform.up * 0.4f;
            foreach (TechType t in rolled)
            {
                if (Ores.TryGetValue(t, out string item))
                {
                    Bridge.DropMinecraftItem(at, item);
                }
            }
        }
    }

    [HarmonyPatch(typeof(BreakableResource), nameof(BreakableResource.BreakIntoResources))]
    internal static class OutcropBreakPatch
    {
        private static readonly System.Reflection.FieldInfo Broken = AccessTools.Field(typeof(BreakableResource), "broken");

        private static void Prefix(BreakableResource __instance)
        {
            OutcropDrops.Rolled = OutcropDrops.Active && Plugin.OutcropMinecraftOres.Value && !(bool)Broken.GetValue(__instance) ? new List<TechType>() : null;
        }

        private static void Postfix(BreakableResource __instance)
        {
            List<TechType> rolled = OutcropDrops.Rolled;
            OutcropDrops.Rolled = null;
            if (rolled != null)
            {
                OutcropDrops.Drop(__instance, rolled);
            }
        }
    }

    [HarmonyPatch(typeof(BreakableResource), "ChooseRandomResource")]
    internal static class OutcropRollPatch
    {
        private static void Postfix(BreakableResource __instance, AssetReferenceGameObject __result)
        {
            if (OutcropDrops.Rolled == null || __result == null)
            {
                return;
            }

            foreach (BreakableResource.RandomPrefab option in __instance.prefabList)
            {
                if (ReferenceEquals(option.prefabReference, __result))
                {
                    OutcropDrops.Rolled.Add(option.prefabTechType);
                    return;
                }
            }
        }
    }
}
