using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SubnautiCraft
{
    /// <summary>
    /// While Steve plays, whatever Subnautica would put in the diver's inventory goes to Steve's Minecraft inventory:
    /// what he picks up (sulphur, a first aid kit, a fish, anything), what his sword cuts (kelp samples, coral) and what
    /// the game hands over (console items). The diver's own inventory is hidden, so nothing may end up there.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.Pickup))]
    internal static class StevePickupPatch
    {
        private static bool Prefix(Inventory __instance, Pickupable pickupable, ref bool __result)
        {
            if (!Bridge.SteveEngaged || SteveTools.Taking || __instance != Inventory.main || pickupable == null || !Bridge.GiveToSteve(pickupable))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Steve's inventory is Minecraft's (a full one drops at his feet): the diver's is never full for him, whatever was
    /// left in it (the lifepod's medical cabinet asks before handing out a kit).
    /// </summary>
    [HarmonyPatch]
    internal static class SteveHasRoomPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.HasRoomFor), new[] { typeof(Pickupable) });
            yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.HasRoomFor), new[] { typeof(TechType) });
            yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.HasRoomFor), new[] { typeof(int), typeof(int) });
        }

        private static void Postfix(Inventory __instance, ref bool __result)
        {
            if (Bridge.SteveEngaged && __instance == Inventory.main && Bridge.Connected)
            {
                __result = true;
            }
        }
    }

    /// <summary>Steve gets into the Seamoth and the Prawn suit at once: no climbing in (its animation bugs out with him).</summary>
    [HarmonyPatch(typeof(SeaMoth), "EnterVehicle")]
    internal static class SeamothNoEnterAnimationPatch
    {
        private static void Prefix(ref bool playEnterAnimation)
        {
            if (Bridge.SteveEngaged)
            {
                playEnterAnimation = false;
            }
        }
    }

    [HarmonyPatch(typeof(Exosuit), "EnterVehicle")]
    internal static class ExosuitNoEnterAnimationPatch
    {
        private static void Prefix(ref bool playEnterAnimation)
        {
            if (Bridge.SteveEngaged)
            {
                playEnterAnimation = false;
            }
        }
    }
}
