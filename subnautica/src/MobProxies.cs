using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>Marks the invisible body of a Minecraft mob (its entity id).</summary>
    internal sealed class McMobProxy : MonoBehaviour
    {
        public int EntityId;
        public float LastSeen;
    }

    /// <summary>
    /// Minecraft's mobs and Subnautica's creatures fight each other. Every Minecraft mob near the player gets an
    /// invisible body here, with health and a place in the food chain (small fish, bigger fish, or a shark-sized threat),
    /// so Subnautica's creatures notice and bite it; a bite goes back to the mob in Minecraft. The other way, the
    /// creatures near the player are reported to Minecraft, which gives each an invisible stand-in that its monsters
    /// hunt; their hits come back here.
    /// </summary>
    internal sealed class MobProxies
    {
        private const float ReportRadius = 40f;
        private const float StaleAfter = 1f;
        private readonly Dictionary<int, McMobProxy> bodies = new Dictionary<int, McMobProxy>();
        private readonly Dictionary<int, Creature> creatures = new Dictionary<int, Creature>();
        private readonly List<int> stale = new List<int>();
        private readonly StringBuilder sb = new StringBuilder(1024);
        private GameObject parent;
        private LiveMixinData health;
        private Creature[] all = new Creature[0];
        private float nextReport, nextSearch;

        public IEnumerable<McMobProxy> Bodies => bodies.Values;

        /// <summary>{"t":"mobs","m":[[id,x,y,z,width,height,hostile],...]}: Minecraft's mobs (feet, Minecraft coordinates).</summary>
        public void Apply(float[][] mobs)
        {
            if (parent == null)
            {
                bodies.Clear();
                parent = new GameObject("SubnautiCraft mobs");
                Object.DontDestroyOnLoad(parent);
                parent.AddComponent<SceneCleanerPreserve>();
            }

            float now = Time.time;
            foreach (float[] m in mobs)
            {
                if (m.Length < 7)
                {
                    continue;
                }

                int id = (int)m[0];
                float width = Mathf.Clamp(m[4], 0.2f, 6f), height = Mathf.Clamp(m[5], 0.2f, 6f);
                if (!bodies.TryGetValue(id, out McMobProxy body) || body == null)
                {
                    body = Make(id, width, height, m[6] > 0.5f);
                    bodies[id] = body;
                }

                body.LastSeen = now;
                body.transform.position = Coords.FromMc(m[1], m[2], m[3]);
            }

            stale.Clear();
            foreach (KeyValuePair<int, McMobProxy> b in bodies)
            {
                if (b.Value == null || now - b.Value.LastSeen > StaleAfter)
                {
                    stale.Add(b.Key);
                }
            }

            foreach (int id in stale)
            {
                if (bodies[id] != null)
                {
                    Object.Destroy(bodies[id].gameObject);
                }

                bodies.Remove(id);
            }
        }

        private McMobProxy Make(int id, float width, float height, bool hostile)
        {
            if (health == null)
            {
                health = ScriptableObject.CreateInstance<LiveMixinData>();
                health.maxHealth = 100f;
                health.destroyOnDeath = false;
                health.knifeable = false;
                health.weldable = false;
            }

            var go = new GameObject("minecraft mob " + id);
            go.SetActive(false);
            go.transform.SetParent(parent.transform, false);
            go.layer = LayerID.Default;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = width * 0.5f;
            capsule.height = Mathf.Max(height, width);
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var live = go.AddComponent<LiveMixin>();
            live.data = health;
            live.health = health.maxHealth;
            // where it sits in Subnautica's food chain: what hunts it
            var eco = go.AddComponent<EcoTarget>();
            eco.type = hostile || height > 1.5f ? EcoTargetType.Shark : width > 0.8f ? EcoTargetType.MediumFish : EcoTargetType.SmallFish;
            var proxy = go.AddComponent<McMobProxy>();
            proxy.EntityId = id;
            go.SetActive(true);
            return proxy;
        }

        public void Clear()
        {
            foreach (McMobProxy b in bodies.Values)
            {
                if (b != null)
                {
                    Object.Destroy(b.gameObject);
                }
            }

            bodies.Clear();
            creatures.Clear();
        }

        /// <summary>Four times a second: the creatures near the player, for Minecraft's stand-ins.</summary>
        public void Report(HostLink link, Vector3 at)
        {
            if (Time.time < nextReport)
            {
                return;
            }

            nextReport = Time.time + 0.25f;
            if (Time.time >= nextSearch)
            {
                nextSearch = Time.time + 1f;
                all = Object.FindObjectsOfType<Creature>();
            }

            creatures.Clear();
            sb.Clear().Append("{\"t\":\"creatures\",\"c\":[");
            bool first = true;
            foreach (Creature c in all)
            {
                if (c == null || !c.isActiveAndEnabled || (c.transform.position - at).sqrMagnitude > ReportRadius * ReportRadius)
                {
                    continue;
                }

                LiveMixin live = c.GetComponent<LiveMixin>();
                if (live == null || !live.IsAlive())
                {
                    continue;
                }

                int id = c.GetInstanceID();
                creatures[id] = c;
                Vector3 centre = c.transform.position;
                float size = 1f;
                Collider col = c.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    centre = col.bounds.center;
                    size = Mathf.Max(col.bounds.size.x, Mathf.Max(col.bounds.size.y, col.bounds.size.z));
                }

                double[] p = Coords.ToMc(centre);
                sb.Append(first ? "" : ",").Append('[').Append(id).Append(',')
                    .Append(p[0].ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(p[1].ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(p[2].ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(size.ToString("F2", CultureInfo.InvariantCulture)).Append(']');
                first = false;
            }

            link.Send(sb.Append("]}").ToString());
        }

        /// <summary>{"t":"creaturehit","id":creature,"dmg":hp}: a Minecraft mob hit that creature (Minecraft health points).</summary>
        public void CreatureHit(int id, float damage)
        {
            if (creatures.TryGetValue(id, out Creature c) && c != null)
            {
                LiveMixin live = c.GetComponent<LiveMixin>();
                if (live != null && live.IsAlive())
                {
                    live.TakeDamage(damage / Mathf.Max(0.01f, Plugin.DamageScale.Value), c.transform.position, DamageType.Normal, null);
                }
            }
        }
    }

    /// <summary>A bite (or anything else) on a Minecraft mob's invisible body goes to the mob in Minecraft; the body itself never wears down.</summary>
    [HarmonyPatch(typeof(LiveMixin), nameof(LiveMixin.TakeDamage))]
    internal static class MobProxyDamagePatch
    {
        private static bool Prefix(LiveMixin __instance, float originalDamage, ref bool __result)
        {
            McMobProxy proxy = __instance.GetComponent<McMobProxy>();
            if (proxy == null)
            {
                return true;
            }

            __result = false;
            if (originalDamage > 0f)
            {
                Bridge.ForwardMobHit(proxy.EntityId, originalDamage * Plugin.DamageScale.Value);
            }

            return false;
        }
    }
}
