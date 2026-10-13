using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// SkyCraft's rule for health: in Steve mode Minecraft's is the one that counts. Whatever would hurt the diver
    /// (a bite, a hot vent, an explosion, no oxygen, hunger, thirst...) is worked out with Subnautica's own rules
    /// (suits, creative mode, shields) and goes to Steve instead; the diver's health bar then shows Steve's
    /// (<see cref="Bridge"/>), and when Steve dies the diver dies, so Subnautica's own death and respawn run.
    /// </summary>
    [HarmonyPatch(typeof(LiveMixin), nameof(LiveMixin.TakeDamage))]
    internal static class RyleyDamagePatch
    {
        private static bool Prefix(LiveMixin __instance, float originalDamage, DamageType type, GameObject dealer, ref bool __result)
        {
            if (Player.main == null || __instance != Player.main.liveMixin)
            {
                return true;
            }

            if (Bridge.SteveDriving)
            {
                // at the wheel Subnautica's health runs as usual, and Steve takes the same hit so it's still there on foot
                if (__instance.health > 0f && !__instance.invincible && !(GameModeUtils.IsInvisible() && __instance.invincibleInCreative))
                {
                    float hit = __instance.shielded ? 0f : DamageSystem.CalculateDamage(originalDamage, type, __instance.gameObject, dealer);
                    if (hit > 0f)
                    {
                        Bridge.ForwardToSteve("hurt", type.ToString(), hit);
                    }
                }

                return true;
            }

            if (!Bridge.SteveEngaged)
            {
                return true;
            }

            __result = false;
            if (__instance.health <= 0f || __instance.invincible || (GameModeUtils.IsInvisible() && __instance.invincibleInCreative))
            {
                return false;
            }

            float damage = __instance.shielded ? 0f : DamageSystem.CalculateDamage(originalDamage, type, __instance.gameObject, dealer);
            if (damage > 0f)
            {
                Bridge.ForwardToSteve("hurt", type.ToString(), damage);
            }

            return false;
        }
    }

    /// <summary>
    /// Some deaths skip damage altogether: running out of oxygen kills the diver outright (and a few other things).
    /// Steve dies of it too, in Minecraft (drowned when it's the oxygen), so both games die and respawn together.
    /// </summary>
    [HarmonyPatch(typeof(LiveMixin), nameof(LiveMixin.Kill))]
    internal static class RyleyKillPatch
    {
        private static void Prefix(LiveMixin __instance, DamageType damageType)
        {
            if (Player.main == null || __instance != Player.main.liveMixin || !__instance.IsAlive() || Bridge.KillingForSteve
                || !(Bridge.SteveEngaged || Bridge.SteveDriving))
            {
                return;
            }

            bool drowned = Player.main.GetOxygenAvailable() <= 0.01f;
            string kind = drowned ? "Drown" : damageType.ToString();
            Plugin.Log.LogInfo($"the diver was killed outright ({kind}): so is Steve");
            // far more than any health, in Subnautica points (Steve's share of it is still all his health)
            Bridge.ForwardToSteve("hurt", kind, 100000f);
        }
    }

    /// <summary>First aid kits and the like heal Steve as much as they would heal the diver.</summary>
    [HarmonyPatch(typeof(LiveMixin), nameof(LiveMixin.AddHealth))]
    internal static class RyleyHealPatch
    {
        private static void Prefix(LiveMixin __instance, float healthBack)
        {
            if (Bridge.SteveEngaged && Player.main != null && __instance == Player.main.liveMixin && healthBack > 0f)
            {
                Bridge.ForwardToSteve("heal", null, healthBack);
            }
        }
    }
}
