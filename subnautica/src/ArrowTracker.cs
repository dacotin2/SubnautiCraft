using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Steve's arrows in Subnautica's world (the GTA example's projectile tracing): Minecraft reports every arrow in
    /// flight each tick, and the stretch it flew since the last report is traced here. A creature in the way takes
    /// the hit (and the arrow is spent); rock, wrecks or bases stop it and it sticks there.
    /// </summary>
    internal sealed class ArrowTracker
    {
        private readonly Dictionary<int, Vector3> last = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float> seen = new Dictionary<int, float>();
        private readonly List<int> stale = new List<int>();

        /// <summary>{"t":"proj","p":[[id,x,y,z],...]}; hits go back as {"t":"projhit",...}.</summary>
        public void Update(HostLink link, float[][] arrows)
        {
            float now = Time.unscaledTime;
            foreach (float[] a in arrows)
            {
                int id = (int)a[0];
                Vector3 at = Coords.FromMc(a[1], a[2], a[3]);
                seen[id] = now;
                if (last.TryGetValue(id, out Vector3 from) && Trace(link, id, from, at))
                {
                    last.Remove(id);
                    continue;
                }

                last[id] = at;
            }

            stale.Clear();
            foreach (KeyValuePair<int, float> s in seen)
            {
                if (now - s.Value > 2f)
                {
                    stale.Add(s.Key);
                }
            }

            foreach (int id in stale)
            {
                seen.Remove(id);
                last.Remove(id);
            }
        }

        private static bool Trace(HostLink link, int id, Vector3 from, Vector3 to)
        {
            Vector3 path = to - from;
            float length = path.magnitude;
            if (length < 0.01f)
            {
                return false;
            }

            RaycastHit[] hits = Physics.RaycastAll(from, path / length, length, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (RaycastHit hit in hits)
            {
                Collider c = hit.collider;
                if (c.GetComponentInParent<Player>() != null || BlockColliders.Owns(c))
                {
                    continue; // the diver himself; Minecraft's own blocks already stop arrows in Minecraft
                }

                LiveMixin live = c.GetComponentInParent<LiveMixin>();
                if (live != null && live.IsAlive())
                {
                    live.TakeDamage(Plugin.ArrowDamage.Value, hit.point, DamageType.Normal, Player.main != null ? Player.main.gameObject : null);
                    Send(link, id, hit.point, false);
                    return true;
                }

                if (c.attachedRigidbody == null || c.attachedRigidbody.isKinematic)
                {
                    Send(link, id, hit.point - path / length * 0.05f, true); // into rock: it sticks
                    return true;
                }
            }

            return false;
        }

        private static void Send(HostLink link, int id, Vector3 at, bool stick)
        {
            double[] p = Coords.ToMc(at);
            link.Send(string.Format(CultureInfo.InvariantCulture, "{{\"t\":\"projhit\",\"id\":{0},\"pos\":[{1:F3},{2:F3},{3:F3}],\"stick\":{4}}}",
                id, p[0], p[1], p[2], stick ? "true" : "false"));
        }
    }
}
