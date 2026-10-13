using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Subnautica's hunters (stalkers, bonesharks, Reapers...) go for prey that moves: they read its rigidbody's speed.
    /// In Steve mode the diver is a puppet put in place by hand, so his rigidbody's speed is always zero: to them Steve
    /// was standing still, and they ignored him. They read Steve's speed instead.
    /// </summary>
    [HarmonyPatch(typeof(AggressiveWhenSeeTarget), "IsTargetValid", new[] { typeof(GameObject) })]
    internal static class HunterSeesSteveMovePatch
    {
        private static bool Prefix(AggressiveWhenSeeTarget __instance, GameObject target, ref bool __result, out float __state)
        {
            __state = -1f;
            if (!Bridge.SteveEngaged || Player.main == null || target != Player.main.gameObject || Mathf.Approximately(__instance.minimumVelocity, 0f))
            {
                return true;
            }

            if (Bridge.SteveSpeed <= __instance.minimumVelocity)
            {
                __result = false;
                return false;
            }

            // fast enough: the game's own check (on the puppet's zero speed) is skipped for this call
            __state = __instance.minimumVelocity;
            __instance.minimumVelocity = 0f;
            return true;
        }

        private static void Postfix(AggressiveWhenSeeTarget __instance, float __state)
        {
            if (__state >= 0f)
            {
                __instance.minimumVelocity = __state;
            }
        }
    }

    /// <summary>What the state report says about Steve as prey: can he be attacked, and what the nearest hunter makes of him.</summary>
    internal static class PreyReport
    {
        private static readonly System.Reflection.MethodInfo IsTargetValid = AccessTools.Method(typeof(AggressiveWhenSeeTarget), "IsTargetValid", new[] { typeof(GameObject) });

        public static string Describe(Player player)
        {
            var sb = new StringBuilder(256);
            EcoTarget eco = player.GetComponent<EcoTarget>();
            GameModeUtils.GetGameMode(out GameModeOption mode, out GameModeOption cheats);
            sb.Append("game mode ").Append(mode).Append(cheats != GameModeOption.None ? ", cheats " + cheats : "")
                .Append("; attackable ").Append(player.CanBeAttacked())
                .Append(" (pod ").Append(player.escapePod.value)
                .Append(", sub ").Append(player.currentSub != null ? player.currentSub.name : "-")
                .Append(", just spawned ").Append(player.justSpawned)
                .Append(", invisible ").Append(GameModeUtils.IsInvisible())
                .Append("), eco target ").Append(eco != null ? (eco.enabled ? eco.type.ToString() : "off") : "none")
                .Append(", Steve speed ").Append(Bridge.SteveSpeed.ToString("F1"));

            AggressiveWhenSeeTarget nearest = null;
            float best = 80f;
            foreach (AggressiveWhenSeeTarget hunter in Object.FindObjectsOfType<AggressiveWhenSeeTarget>())
            {
                float d = Vector3.Distance(hunter.transform.position, player.transform.position);
                if (hunter.isActiveAndEnabled && d < best)
                {
                    best = d;
                    nearest = hunter;
                }
            }

            if (nearest == null)
            {
                return sb.Append("; no hunter within 80 m").ToString();
            }

            Creature creature = nearest.GetComponent<Creature>();
            bool valid = IsTargetValid != null && (bool)IsTargetValid.Invoke(nearest, new object[] { player.gameObject });
            sb.Append("; nearest hunter ").Append(nearest.name).Append(' ').Append(best.ToString("F0")).Append(" m (range ")
                .Append(nearest.maxRangeScalar.ToString("F0")).Append(", wants speed ").Append(nearest.minimumVelocity.ToString("F1"))
                .Append(", hunts ").Append(nearest.targetType).Append("): Steve valid ").Append(valid);
            if (creature != null)
            {
                sb.Append(", sees ").Append(creature.GetCanSeeObject(player.gameObject))
                    .Append(", aggression ").Append(creature.Aggression.Value.ToString("F2"))
                    .Append(", hunger ").Append(creature.Hunger.Value.ToString("F2"));
            }

            GameObject target = nearest.lastTarget != null ? nearest.lastTarget.target : null;
            return sb.Append(", target ").Append(target != null ? target.name : "-").ToString();
        }
    }
}
