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
        private static bool Prefix(GameInput.Button action, GameInput.InputStateFlags flag, ref bool __result)
        {
            if (!Bridge.BlockGameInput)
            {
                return true;
            }

            switch (action)
            {
                case GameInput.Button.PDA:
                    return true;
                case GameInput.Button.RightHand:
                case GameInput.Button.LeftHand:
                    // a Subnautica tool in Steve's hand (SteveTools), on Minecraft's buttons: right click uses it (left
                    // click is Minecraft's attack), the middle button is its second action
                    __result = action == GameInput.Button.LeftHand && SteveTools.Active && !Bridge.McScreenOpen && Mouse(2, flag);
                    return false;
                case GameInput.Button.AltTool:
                    if (SteveTools.Active && !Bridge.McScreenOpen)
                    {
                        return true;
                    }

                    __result = false;
                    return false;
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

        /// <summary>A mouse button's state this frame, as Subnautica's input reports a button's.</summary>
        private static bool Mouse(int button, GameInput.InputStateFlags flag)
        {
            GameInput.InputStateFlags state = GameInput.InputStateFlags.None;
            if (Input.GetMouseButtonDown(button))
            {
                state |= GameInput.InputStateFlags.Down;
            }

            if (Input.GetMouseButton(button))
            {
                state |= GameInput.InputStateFlags.Held;
            }

            if (Input.GetMouseButtonUp(button))
            {
                state |= GameInput.InputStateFlags.Up;
            }

            return (state & flag) != 0;
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
