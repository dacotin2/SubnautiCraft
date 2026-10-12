using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Steve mode: Subnautica's own controls are swallowed (SkyCraft's InputBridge does the same to Skyrim's), so the
    /// keys only move Steve. Kept for Subnautica: the PDA (Tab), and its pause menu (Escape) unless a Minecraft
    /// screen is open, where Escape closes that instead. The look stays Subnautica's: it turns the camera with no
    /// lag and Steve follows it, except while a Minecraft screen has the cursor.
    /// </summary>
    [HarmonyPatch(typeof(GameInput), "HasFlag")]
    internal static class InputPatch
    {
        private static bool Prefix(GameInput.Button action, ref bool __result)
        {
            if (!Bridge.BlockGameInput)
            {
                return true;
            }

            switch (action)
            {
                case GameInput.Button.PDA:
                    return true;
                case GameInput.Button.UIMenu:
                    if (!Bridge.McScreenOpen)
                    {
                        return true;
                    }

                    __result = false;
                    return false;
                default:
                    __result = false;
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetMoveDirection))]
    internal static class MovePatch
    {
        private static void Postfix(ref Vector3 __result)
        {
            if (Bridge.SteveEngaged)
            {
                __result = Vector3.zero;
            }
        }
    }

    [HarmonyPatch(typeof(GameInput), nameof(GameInput.GetLookDelta))]
    internal static class LookPatch
    {
        private static void Postfix(ref Vector2 __result)
        {
            if (Bridge.SteveEngaged && Bridge.McScreenOpen)
            {
                __result = Vector2.zero;
            }
        }
    }
}
