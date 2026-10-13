using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Subnautica's machines open Minecraft's screens for Steve: a locker is a Minecraft chest (its own, kept in that
    /// save's Minecraft world), the fabricator is a crafting table (Subnautica's recipes are Minecraft recipes), and the
    /// mobile vehicle bay lists the vehicles with what they cost in Minecraft items; Minecraft takes the items, and the
    /// bay builds the vehicle here as it always does.
    /// </summary>
    internal static class SnMenus
    {
        private static readonly TechType[] Vehicles = { TechType.Seamoth, TechType.Exosuit, TechType.Cyclops, TechType.RocketBase };
        private static readonly Dictionary<string, ConstructorInput> bays = new Dictionary<string, ConstructorInput>();
        private static readonly MethodInfo Craft = AccessTools.Method(typeof(ConstructorInput), "Craft", new[] { typeof(TechType), typeof(float) });

        /// <summary>While the bay builds for Steve: Minecraft already took the materials, so Subnautica takes none.</summary>
        internal static bool BuildingForSteve;
        internal static bool BuildStarted;

        /// <summary>Right click on one of the machines: true if Minecraft's screen opens instead of Subnautica's.</summary>
        public static bool TryOpen(GameObject target, HostLink link)
        {
            if (target == null)
            {
                return false;
            }

            ConstructorInput bay = target.GetComponentInParent<ConstructorInput>();
            if (bay != null)
            {
                OpenBay(bay, link);
                return true;
            }

            Fabricator fabricator = target.GetComponentInParent<Fabricator>();
            if (fabricator != null)
            {
                link.Send("{\"t\":\"menu\",\"kind\":\"crafting\",\"title\":\"" + Escape(Name(TechType.Fabricator)) + "\"}");
                return true;
            }

            if (Lifepod.IsStorage(target))
            {
                StorageContainer pod = EscapePod.main != null ? EscapePod.main.storageContainer : null;
                int podRows = pod != null ? Mathf.Clamp(Mathf.CeilToInt(pod.width * pod.height / 9f), 1, 6) : 3;
                link.Send("{\"t\":\"menu\",\"kind\":\"chest\",\"id\":\"lifepod\",\"rows\":" + podRows + ",\"title\":\""
                    + Escape(Language.main.Get("EscapePod")) + "\"}");
                return true;
            }

            StorageContainer storage = target.GetComponentInParent<StorageContainer>();
            if (storage != null && IsLocker(storage, out string id, out TechType type))
            {
                int rows = Mathf.Clamp(Mathf.CeilToInt(storage.width * storage.height / 9f), 1, 6);
                string title = type != TechType.None ? Name(type) : Language.main.Get("Locker");
                link.Send("{\"t\":\"menu\",\"kind\":\"chest\",\"id\":\"" + Escape(id) + "\",\"rows\":" + rows + ",\"title\":\"" + Escape(title) + "\"}");
                return true;
            }

            return false;
        }

        /// <summary>Lockers and the lifepod's storage; not machines that store things for a purpose (bioreactor, aquarium, planters).</summary>
        private static bool IsLocker(StorageContainer storage, out string id, out TechType type)
        {
            id = null;
            type = TechType.None;
            if (storage.GetComponentInParent<EscapePod>() != null)
            {
                id = "lifepod";
                return true;
            }

            GameObject root = storage.prefabRoot != null ? storage.prefabRoot : storage.gameObject;
            type = CraftData.GetTechType(root);
            if (type != TechType.Locker && type != TechType.SmallLocker && type != TechType.SmallStorage)
            {
                return false;
            }

            PrefabIdentifier identifier = root.GetComponentInParent<PrefabIdentifier>();
            id = identifier != null ? identifier.Id : root.name + "@" + root.transform.position.ToString("F0");
            return true;
        }

        private static void OpenBay(ConstructorInput bay, HostLink link)
        {
            string id = bay.GetInstanceID().ToString();
            bays[id] = bay;
            var sb = new StringBuilder("{\"t\":\"menu\",\"kind\":\"vehicles\",\"id\":\"").Append(id).Append("\",\"title\":\"")
                .Append(Escape(Name(TechType.Constructor))).Append("\",\"options\":[");
            bool first = true;
            foreach (TechType vehicle in Vehicles)
            {
                if (!KnownTech.Contains(vehicle) && !GameModeUtils.IsOptionActive(GameModeOption.NoBlueprints))
                {
                    continue; // its blueprint isn't known yet
                }

                SnItemTable.Items.TryGetValue(vehicle, out string item);
                sb.Append(first ? "" : ",").Append("{\"tech\":\"").Append(vehicle).Append("\",\"item\":\"").Append(item ?? "").Append("\",\"name\":\"")
                    .Append(Escape(Name(vehicle))).Append("\",\"ingredients\":[");
                first = false;
                bool firstIngredient = true;
                var ingredients = TechData.GetIngredients(vehicle);
                if (ingredients != null)
                {
                    foreach (Ingredient ingredient in ingredients)
                    {
                        SnItemTable.Items.TryGetValue(ingredient.techType, out string needed);
                        sb.Append(firstIngredient ? "" : ",").Append("[\"").Append(needed ?? "?" + ingredient.techType).Append("\",").Append(ingredient.amount)
                            .Append(",\"").Append(Escape(Name(ingredient.techType))).Append("\"]");
                        firstIngredient = false;
                    }
                }

                sb.Append("]}");
            }

            link.Send(sb.Append("]}").ToString());
        }

        /// <summary>{"t":"build","id":bay,"tech":"Seamoth"}: Minecraft has the materials; the bay builds it (or says why not).</summary>
        public static void Build(string id, string tech, HostLink link)
        {
            bool ok = false;
            if (bays.TryGetValue(id ?? "", out ConstructorInput bay) && bay != null && Craft != null
                && System.Enum.TryParse(tech, out TechType vehicle))
            {
                BuildingForSteve = true;
                BuildStarted = false;
                try
                {
                    Craft.Invoke(bay, new object[] { vehicle, 10f });
                    ok = BuildStarted;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"vehicle bay: {e.Message}");
                }
                finally
                {
                    BuildingForSteve = false;
                }
            }

            Plugin.Log.LogInfo($"vehicle bay: {tech} {(ok ? "building" : "can't be built here")}");
            link.Send("{\"t\":\"built\",\"tech\":\"" + Escape(tech) + "\",\"ok\":" + (ok ? "true" : "false") + "}");
        }

        private static string Name(TechType type)
        {
            return Language.main != null ? Language.main.Get(type.AsString()) : type.ToString();
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /// <summary>The bay building for Steve takes nothing from the diver's inventory (Minecraft took the materials).</summary>
    [HarmonyPatch(typeof(CrafterLogic), nameof(CrafterLogic.ConsumeResources))]
    internal static class SteveBuildsFreePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!SnMenus.BuildingForSteve)
            {
                return true;
            }

            SnMenus.BuildStarted = true; // the bay found room for it: it's being built
            __result = true;
            return false;
        }
    }
}
