using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// A vehicle that rams a Minecraft block hurts itself as it does ramming rock. The game's own impact rule
    /// (DealDamageOnImpact) is left to run; if it didn't hurt the vehicle (Minecraft's blocks are bare colliders, not
    /// the game's terrain), the same formula is applied here: impact speed x mass factor x 3, times the vehicle's
    /// share of the hit (less with hull reinforcements).
    /// </summary>
    [HarmonyPatch(typeof(DealDamageOnImpact), "OnCollisionEnter")]
    internal static class VehicleImpactPatch
    {
        private const float MinimumSpeed = 2.5f;
        private static readonly AccessTools.FieldRef<DealDamageOnImpact, float> LastSelfDamage = AccessTools.FieldRefAccess<DealDamageOnImpact, float>("timeLastDamagedSelf");
        private static readonly AccessTools.FieldRef<DealDamageOnImpact, Vector3> PreviousVelocity = AccessTools.FieldRefAccess<DealDamageOnImpact, Vector3>("prevVelocity");

        private static void Prefix(DealDamageOnImpact __instance, out float __state)
        {
            __state = LastSelfDamage(__instance);
        }

        private static void Postfix(DealDamageOnImpact __instance, Collision collision, float __state)
        {
            if (!__instance.enabled || collision == null || collision.contacts.Length == 0 || !BlockColliders.Owns(collision.collider))
            {
                return;
            }

            Vehicle vehicle = __instance.GetComponent<Vehicle>();
            LiveMixin live = vehicle != null ? vehicle.liveMixin : null;
            if (live == null || !live.IsAlive())
            {
                return;
            }

            float speed = Mathf.Max(0f, Vector3.Dot(-collision.contacts[0].normal, PreviousVelocity(__instance)));
            if (LastSelfDamage(__instance) != __state || speed < MinimumSpeed || Time.time < __state + 1f)
            {
                return; // the game hurt it already, too gentle a bump, or hurt a moment ago
            }

            Rigidbody body = __instance.GetComponent<Rigidbody>();
            float massFactor = Mathf.Clamp(1f + (body != null ? body.mass : 0f) * 0.001f, 0f, 10f) * 3f;
            float damage = speed * massFactor * __instance.mirroredSelfDamageFraction;
            if (__instance.capMirrorDamage != -1f)
            {
                damage = Mathf.Min(__instance.capMirrorDamage, damage);
            }

            live.TakeDamage(damage, collision.contacts[0].point, DamageType.Collide, __instance.gameObject);
            LastSelfDamage(__instance) = Time.time;
            Plugin.Log.LogInfo($"{vehicle.name} rammed a Minecraft block at {speed:F1} m/s: {damage:F0} damage");
        }
    }
}
