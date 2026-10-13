using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// A Minecraft sword swing works as Subnautica's survival knife (Knife.OnToolUseAnim): it hits what's in front of
    /// the camera, hurts creatures, and cuts samples and seeds from what the knife harvests (creepvine, coral, plants).
    /// The damage follows the sword: Minecraft's attack damage times <see cref="Plugin.SwordDamageScale"/>
    /// (a diamond sword, 7, makes about a knife's 25).
    /// </summary>
    internal static class SwordStrike
    {
        private const float Reach = 2.5f;

        public static void Swing(float minecraftDamage)
        {
            Player player = Player.main;
            if (player == null || Plugin.SwordDamageScale.Value <= 0f)
            {
                return;
            }

            Vector3 position = default;
            GameObject target = null;
            UWE.Utils.TraceFPSTargetPosition(player.gameObject, Reach, ref target, ref position);
            if (target == null)
            {
                InteractionVolumeUser volume = player.gameObject.GetComponent<InteractionVolumeUser>();
                if (volume != null && volume.GetMostRecent() != null)
                {
                    target = volume.GetMostRecent().gameObject;
                }
            }

            if (target == null)
            {
                return;
            }

            LiveMixin live = target.FindAncestor<LiveMixin>();
            if (!IsValidTarget(live))
            {
                return;
            }

            if (live != null)
            {
                bool wasAlive = live.IsAlive();
                live.TakeDamage(minecraftDamage * Plugin.SwordDamageScale.Value, position, DamageType.Normal, player.gameObject);
                GiveResourceOnDamage(target, live.IsAlive(), wasAlive);
            }

            Vector3 euler = MainCameraControl.main.transform.eulerAngles + new Vector3(300f, 90f, 0f);
            VFXSurfaceTypeManager.main.Play(target.GetComponent<VFXSurface>(), VFXEventTypes.knife, position, Quaternion.Euler(euler), player.transform);
        }

        /// <summary>The knife's rule: not welded things, not unknifeable things, not the lifepod.</summary>
        private static bool IsValidTarget(LiveMixin live)
        {
            if (live == null)
            {
                return true;
            }

            // a Minecraft mob's body: Steve's sword hits the mob itself in Minecraft
            return !live.weldable && live.knifeable && live.GetComponent<EscapePod>() == null && live.gameObject != Player.main.gameObject
                && live.GetComponent<McMobProxy>() == null;
        }

        /// <summary>The knife's harvest: creepvine seeds, coral samples and the like go into the inventory.</summary>
        private static void GiveResourceOnDamage(GameObject target, bool isAlive, bool wasAlive)
        {
            TechType techType = CraftData.GetTechType(target);
            HarvestType harvestType = TechData.GetHarvestType(techType);
            if (techType == TechType.Creepvine)
            {
                GoalManager.main.OnCustomGoalEvent("Cut_Creepvine");
            }

            if ((harvestType == HarvestType.DamageAlive && wasAlive) || (harvestType == HarvestType.DamageDead && !isAlive))
            {
                int count = 1;
                if (harvestType == HarvestType.DamageAlive && !isAlive)
                {
                    count += TechData.GetHarvestFinalCutBonus(techType);
                }

                TechType output = TechData.GetHarvestOutput(techType);
                if (output != TechType.None)
                {
                    CraftData.AddToInventory(output, count, noMessage: false, spawnIfCantAdd: false);
                }
            }
        }
    }
}
