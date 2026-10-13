using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// The lifepod with Steve in it. It floats (a rigidbody the waves move), and Steve both stands on it in Minecraft
    /// and drags the invisible diver through it here: the diver pushed the pod, the pod moved Steve's floor, and round it
    /// went. While Steve is near, the pod holds still (kinematic) and the diver passes through its walls (Minecraft's
    /// collision is the one Steve feels); both come back when Steve leaves or Ryley plays.
    /// </summary>
    internal sealed class Lifepod
    {
        private const float HoldRadius = 25f;
        private EscapePod held;
        private bool wasKinematic;
        private readonly List<Collider> ignored = new List<Collider>();
        private readonly List<Collider> diver = new List<Collider>();

        public void Tick(Player player, bool steve)
        {
            EscapePod pod = EscapePod.main;
            bool hold = steve && pod != null && pod.rigidbodyComponent != null && player != null
                && (pod.transform.position - player.transform.position).sqrMagnitude < HoldRadius * HoldRadius;
            if (hold && held == null)
            {
                Hold(pod, player);
            }
            else if (!hold && held != null)
            {
                Release();
            }
        }

        private void Hold(EscapePod pod, Player player)
        {
            held = pod;
            wasKinematic = pod.rigidbodyComponent.isKinematic;
            pod.rigidbodyComponent.isKinematic = true;
            diver.Clear();
            player.GetComponentsInChildren(true, diver);
            ignored.Clear();
            foreach (Collider wall in pod.GetComponentsInChildren<Collider>(true))
            {
                if (wall.isTrigger)
                {
                    continue; // its trigger volumes still see the diver
                }

                ignored.Add(wall);
                foreach (Collider d in diver)
                {
                    if (!d.isTrigger)
                    {
                        Physics.IgnoreCollision(d, wall, true);
                    }
                }
            }

            Plugin.Log.LogInfo($"lifepod held still while Steve is near ({ignored.Count} colliders)");
        }

        public void Release()
        {
            if (held != null && held.rigidbodyComponent != null)
            {
                held.rigidbodyComponent.isKinematic = wasKinematic;
            }

            foreach (Collider wall in ignored)
            {
                foreach (Collider d in diver)
                {
                    if (wall != null && d != null && !d.isTrigger)
                    {
                        Physics.IgnoreCollision(d, wall, false);
                    }
                }
            }

            ignored.Clear();
            diver.Clear();
            held = null;
        }

        /// <summary>The lifepod's storage flap: its hand target calls EscapePod.StorageOpen (it isn't a StorageContainer itself).</summary>
        public static bool IsStorage(GameObject target)
        {
            if (target == null || target.GetComponentInParent<EscapePod>() == null)
            {
                return false;
            }

            GenericHandTarget hand = target.GetComponent<GenericHandTarget>() ?? target.GetComponentInParent<GenericHandTarget>();
            if (hand == null || hand.onHandClick == null)
            {
                return false;
            }

            for (int i = 0; i < hand.onHandClick.GetPersistentEventCount(); i++)
            {
                if (hand.onHandClick.GetPersistentMethodName(i) == nameof(EscapePod.StorageOpen))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Hatches (the lifepod's, bases', the Cyclops') in Steve mode: no climbing cinematic, which fought Steve (the diver
    /// held in an animation, Steve thrown back to him after). The game's own way without one runs instead (it moves the
    /// diver straight through and counts him in or out), and Steve is sent to where the diver now is.
    /// </summary>
    [HarmonyPatch(typeof(UseableDiveHatch), nameof(UseableDiveHatch.OnHandClick))]
    internal static class HatchTeleportPatch
    {
        private static void Prefix(UseableDiveHatch __instance, out PlayerCinematicController[] __state)
        {
            __state = null;
            if (!Bridge.SteveEngaged)
            {
                return;
            }

            __state = new[] { __instance.enterCinematicController, __instance.exitCinematicController };
            __instance.enterCinematicController = null;
            __instance.exitCinematicController = null;
        }

        private static void Postfix(UseableDiveHatch __instance, PlayerCinematicController[] __state)
        {
            if (__state == null)
            {
                return;
            }

            __instance.enterCinematicController = __state[0];
            __instance.exitCinematicController = __state[1];
            Bridge.SendSteveToDiver("hatch");
        }
    }
}
