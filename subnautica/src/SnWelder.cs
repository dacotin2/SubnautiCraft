using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// The repair tool in Steve's hand (a "Subnautica item" in Minecraft): holding right click on something damaged that
    /// the tool can fix (the Seamoth, the Prawn suit, the Cyclops's hull breaches, base pieces) welds it, aimed and
    /// chosen as Subnautica's own Welder does.
    /// </summary>
    internal sealed class SnWelder
    {
        private const float Range = 3f;
        private const float HealthPerSecond = 25f;

        /// <summary>Every frame in Steve mode.</summary>
        public void Tick(string tool, bool screenOpen)
        {
            if (tool != "welder" || screenOpen || Player.main == null || !Input.GetMouseButton(1))
            {
                return;
            }

            LiveMixin target = Target();
            if (target == null || target.GetHealthFraction() >= 1f)
            {
                return;
            }

            target.AddHealth(HealthPerSecond * Time.deltaTime);
            HandReticle.main.SetProgress(target.GetHealthFraction());
            HandReticle.main.SetText(HandReticle.TextType.Hand, "Weld", true);
            HandReticle.main.SetIcon(HandReticle.IconType.Progress, 1.5f);
        }

        /// <summary>As Welder.UpdateTarget: what's in front of the eyes, if it (or its weld point) can be welded.</summary>
        private static LiveMixin Target()
        {
            GameObject hit = null;
            Vector3 position = default;
            UWE.Utils.TraceFPSTargetPosition(Player.main.gameObject, Range, ref hit, ref position);
            if (hit == null)
            {
                InteractionVolumeUser volumes = Player.main.GetComponent<InteractionVolumeUser>();
                if (volumes != null && volumes.GetMostRecent() != null)
                {
                    hit = volumes.GetMostRecent().gameObject;
                }
            }

            LiveMixin live = hit != null ? hit.FindAncestor<LiveMixin>() : null;
            if (live == null)
            {
                return null;
            }

            if (live.IsWeldable())
            {
                return live;
            }

            WeldablePoint point = hit.FindAncestor<WeldablePoint>();
            return point != null && point.transform.IsChildOf(live.transform) ? live : null;
        }
    }
}
