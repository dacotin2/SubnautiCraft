using System.Collections.Generic;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Every creature that bites goes for Steve, and for Minecraft's mobs (their invisible bodies, see
    /// <see cref="MobProxies"/>), without a rule per species: Subnautica's own attack (MeleeAttack, which every biter
    /// has) does the bite, and this makes sure it gets the chance. A biter that sees prey near it gets angry and takes the
    /// nearest as its target (its own AI then swims at it), and when its mouth reaches it, it bites: Steve with its own
    /// bite (its damage, sound and effects, reaching his hearts through the diver), a mob with the same damage (which
    /// goes to the mob in Minecraft). The game's own rules still hold for Steve: no attacks indoors, while just
    /// respawned, or with creative's no-aggression.
    /// </summary>
    internal sealed class HostileCreatures
    {
        private const float Interval = 0.25f;
        private const float SearchInterval = 2f;
        private const float SearchRadius = 80f;
        private readonly List<MeleeAttack> nearby = new List<MeleeAttack>();
        private readonly Dictionary<int, float> lastMobBite = new Dictionary<int, float>();
        private float nextTick, nextSearch;

        public void Tick(Player player, MobProxies mobs, bool steveMode)
        {
            if (Time.time < nextTick || player == null)
            {
                return;
            }

            nextTick = Time.time + Interval;
            bool steveIsPrey = steveMode && player.CanBeAttacked() && player.liveMixin != null && player.liveMixin.IsAlive();
            Vector3 at = player.transform.position;
            if (Time.time >= nextSearch)
            {
                nextSearch = Time.time + SearchInterval;
                nearby.Clear();
                foreach (MeleeAttack melee in Object.FindObjectsOfType<MeleeAttack>())
                {
                    if ((melee.canBitePlayer || melee.canBiteCreature) && (melee.transform.position - at).sqrMagnitude < SearchRadius * SearchRadius)
                    {
                        nearby.Add(melee);
                    }
                }
            }

            Collider steve = player.playerController != null && player.playerController.underWaterController != null
                ? player.playerController.underWaterController.GetCollider() : null;
            foreach (MeleeAttack melee in nearby)
            {
                if (melee == null || !melee.isActiveAndEnabled)
                {
                    continue;
                }

                Creature creature = melee.GetComponent<Creature>();
                LiveMixin live = melee.GetComponent<LiveMixin>();
                if (creature == null || (live != null && !live.IsAlive()))
                {
                    continue;
                }

                // the nearest prey it notices: Steve, or a Minecraft mob
                Vector3 from = creature.transform.position;
                float sight = SightRange(melee);
                GameObject prey = null;
                Collider preyBody = null;
                float best = sight;
                if (steveIsPrey && melee.canBitePlayer && steve != null)
                {
                    float d = Vector3.Distance(from, at);
                    if (d < best && (d < 6f || creature.GetCanSeeObject(player.gameObject)))
                    {
                        best = d;
                        prey = player.gameObject;
                        preyBody = steve;
                    }
                }

                if (melee.canBiteCreature && mobs != null)
                {
                    foreach (McMobProxy mob in mobs.Bodies)
                    {
                        if (mob == null)
                        {
                            continue;
                        }

                        float d = Vector3.Distance(from, mob.transform.position);
                        if (d < best && (d < 6f || creature.GetCanSeeObject(mob.gameObject)))
                        {
                            best = d;
                            prey = mob.gameObject;
                            preyBody = mob.GetComponent<Collider>();
                        }
                    }
                }

                if (prey == null)
                {
                    continue;
                }

                creature.Aggression.Add(Interval * 1.5f);
                if (melee.lastTarget != null && melee.lastTarget.target != prey)
                {
                    melee.lastTarget.SetTarget(prey);
                }

                if (preyBody == null || melee.mouth == null)
                {
                    continue;
                }

                Vector3 mouth = melee.mouth.transform.position;
                if ((preyBody.ClosestPoint(mouth) - mouth).sqrMagnitude > Reach(creature) * Reach(creature))
                {
                    continue;
                }

                if (prey == player.gameObject)
                {
                    melee.OnTouch(preyBody); // its own bite (it checks its anger and how often it may bite)
                }
                else
                {
                    Bite(melee, creature, prey, mouth);
                }
            }
        }

        /// <summary>A bite on a Minecraft mob's body: the game's bite only takes players, creatures and vehicles, so it's done here, the same way.</summary>
        private void Bite(MeleeAttack melee, Creature creature, GameObject prey, Vector3 mouth)
        {
            int id = melee.GetInstanceID();
            if (creature.Aggression.Value < melee.biteAggressionThreshold
                || (lastMobBite.TryGetValue(id, out float last) && Time.time < last + melee.biteInterval))
            {
                return;
            }

            lastMobBite[id] = Time.time;
            LiveMixin target = prey.GetComponent<LiveMixin>();
            if (target != null)
            {
                target.TakeDamage(melee.biteDamage, mouth, DamageType.Normal, melee.gameObject);
            }

            if (melee.damageFX != null)
            {
                Object.Instantiate(melee.damageFX, mouth, melee.damageFX.transform.rotation);
            }

            if (melee.attackSound != null)
            {
                Utils.PlayEnvSound(melee.attackSound, mouth);
            }

            creature.Aggression.Add(-melee.biteAggressionDecrement);
            melee.gameObject.SendMessage("OnMeleeAttack", prey, SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>How far it notices prey: its own aggression range when it has one (a Reaper sees far).</summary>
        private static float SightRange(MeleeAttack melee)
        {
            float range = 15f;
            foreach (AggressiveWhenSeeTarget aggressive in melee.GetComponents<AggressiveWhenSeeTarget>())
            {
                range = Mathf.Max(range, aggressive.maxRangeScalar);
            }

            return Mathf.Min(range, SearchRadius);
        }

        /// <summary>How close the mouth must be: bigger creatures have bigger mouths.</summary>
        private static float Reach(Creature creature)
        {
            return Mathf.Clamp(creature.transform.lossyScale.magnitude * 0.6f, 1.2f, 6f);
        }
    }
}
