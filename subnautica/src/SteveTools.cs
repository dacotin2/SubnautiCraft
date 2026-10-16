using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Subnautica's tools in Steve's hand. When he holds one as a Minecraft item (a "Subnautica item": the propulsion
    /// cannon, the laser cutter, the habitat builder, the stasis rifle...), the hidden diver holds the real tool and
    /// Subnautica's own controls for it come through (left click uses it, right click its second action, as for the
    /// diver), so every tool does exactly what it does in Subnautica, aimed where Steve looks. Tools with their own
    /// Minecraft handling are left out: the scanner, flashlight and Seaglide (Minecraft items), the repair tool
    /// (SnWelder), the knives (swords) and what right click places (the vehicle bay, beacons, flares...).
    /// The tool only exists while held, so it is never saved in the diver's inventory.
    /// </summary>
    internal sealed class SteveTools
    {
        private static readonly HashSet<TechType> Handled = new HashSet<TechType>
        {
            TechType.Welder, TechType.Knife, TechType.HeatBlade, TechType.Scanner, TechType.Flashlight, TechType.Seaglide,
            TechType.Constructor, TechType.Beacon, TechType.Gravsphere, TechType.SmallStorage, TechType.LEDLight, TechType.Flare,
            TechType.PipeSurfaceFloater,
        };

        private static readonly System.Reflection.FieldInfo BatteryField = AccessTools.Field(typeof(EnergyMixin), "battery");
        private static Pickupable held;
        private float wantedCharge = -1f;
        private float sentCharge = -1f;
        private float nextChargeReport;
        private static float pausedUntil;
        private readonly HashSet<TechType> notTools = new HashSet<TechType>();
        private TechType heldTech = TechType.None;
        private TechType drawing = TechType.None;

        /// <summary>A tool is in the diver's hand for Steve: Subnautica's tool controls come through (InputPatch).</summary>
        public static bool Active => held != null;

        /// <summary>The diver's inventory is emptied for Steve (SnItems.EmptyDiver), except for this.</summary>
        public static bool IsHeld(Pickupable pickupable) => pickupable != null && pickupable == held;

        /// <summary>Set while the tool goes into the diver's hand, so the pickup isn't sent on to Steve (StevePickupPatch).</summary>
        public static bool Taking { get; private set; }

        /// <summary>Every frame in Steve mode: <paramref name="tool"/> is what Minecraft says is in Steve's hand ("sn:PropulsionCannon").</summary>
        public void Tick(MonoBehaviour host, string tool, bool steve, float charge, HostLink link)
        {
            Battery(charge, link);
            Use();
            TechType want = TechType.None;
            if (steve && tool != null && tool.StartsWith("sn:") && Time.unscaledTime >= pausedUntil
                && TechTypeExtensions.FromString(tool.Substring(3), out TechType tech, true) && !Handled.Contains(tech) && !notTools.Contains(tech))
            {
                want = tech;
            }

            if (held == null && heldTech != TechType.None)
            {
                heldTech = TechType.None; // gone with the scene (back to the menu)
            }

            if (want == TechType.None)
            {
                if (held != null || drawing != TechType.None)
                {
                    PutAway();
                }

                return;
            }

            if ((want == heldTech && held != null) || want == drawing)
            {
                return;
            }

            PutAway();
            Plugin.Log.LogInfo($"Steve takes out {want}: the diver gets the real tool");
            host.StartCoroutine(Draw(want, charge));
        }

        private IEnumerator Draw(TechType tech, float charge)
        {
            drawing = tech;
            var spawned = new TaskResult<GameObject>();
            yield return CraftData.InstantiateFromPrefabAsync(tech, spawned);
            GameObject go = spawned.Get();
            if (drawing != tech || go == null || Inventory.main == null || !Bridge.SteveEngaged)
            {
                Plugin.Log.LogInfo($"tool {tech}: not taken out ({(go == null ? "Subnautica made none" : drawing != tech ? "Steve changed tool" : "not playing as Steve")})");
                Abandon(go, tech);
                yield break;
            }

            Pickupable pickupable = go.GetComponent<Pickupable>();
            if (pickupable == null || go.GetComponentInChildren<PlayerTool>(true) == null)
            {
                Plugin.Log.LogInfo($"tool {tech}: not a tool you hold ({(pickupable == null ? "can't be picked up" : "no PlayerTool")})");
                notTools.Add(tech); // a Subnautica item, but nothing to hold and use
                Abandon(go, tech);
                yield break;
            }

            // its battery, with the charge Minecraft's item has (a full one for an item from before batteries), then the
            // rest of what the fabricator does to a new tool
            EnergyMixin energy = go.GetComponent<EnergyMixin>();
            if (energy != null && (IBattery)BatteryField.GetValue(energy) == null && energy.defaultBattery != TechType.None)
            {
                yield return energy.SetBatteryAsync(energy.defaultBattery, charge >= 0f ? Mathf.Clamp01(charge) : 1f, new TaskResult<InventoryItem>());
            }

            if (drawing != tech || Inventory.main == null)
            {
                Abandon(go, tech);
                yield break;
            }

            CrafterLogic.NotifyCraftEnd(go, tech);
            Taking = true;
            try
            {
                Inventory.main.Pickup(pickupable, true);
            }
            finally
            {
                Taking = false;
            }

            InventoryItem item = null;
            foreach (InventoryItem i in (IEnumerable<InventoryItem>)Inventory.main.container)
            {
                if (i != null && i.item == pickupable)
                {
                    item = i;
                    break;
                }
            }

            if (item == null)
            {
                Plugin.Log.LogWarning($"tool {tech}: the diver couldn't take it");
                Abandon(go, tech);
                yield break;
            }

            drawing = TechType.None;
            held = pickupable;
            heldTech = tech;
            sentCharge = -1f;
            Inventory.main.quickSlots.Bind(0, item);
            Inventory.main.quickSlots.SelectImmediate(0);
            // hidden with the diver (RyleyPuppet hides what he holds too, within a second): Minecraft draws Steve's hand
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
            }

            var battery = energy != null ? (IBattery)BatteryField.GetValue(energy) : null;
            Plugin.Log.LogInfo($"Steve holds {tech}: the diver holds the real tool"
                + (energy == null ? "" : battery == null ? " (no battery!)" : $" (battery {Mathf.RoundToInt(100f * battery.charge / battery.capacity)}%)"));
        }

        private void Abandon(GameObject go, TechType tech)
        {
            if (go != null)
            {
                Object.Destroy(go);
            }

            if (drawing == tech)
            {
                drawing = TechType.None;
            }
        }

        /// <summary>
        /// Right click is the tool's use, handed to it directly (press, hold, release: a stasis rifle charges while held
        /// and fires on release), so it doesn't depend on Subnautica's hand taking input while Steve plays.
        /// </summary>
        private static void Use()
        {
            PlayerTool tool = held != null ? held.GetComponent<PlayerTool>() : null;
            if (tool == null || Bridge.McScreenOpen || Player.main == null || (Player.main.GetPDA() != null && Player.main.GetPDA().isInUse))
            {
                return;
            }

            try
            {
                if (Input.GetMouseButtonDown(1))
                {
                    if (tool.OnRightHandDown())
                    {
                        tool.OnToolActionStart();
                    }
                }
                else if (Input.GetMouseButton(1))
                {
                    tool.OnRightHandHeld();
                }
                else if (Input.GetMouseButtonUp(1))
                {
                    tool.OnRightHandUp();
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"tool {tool.name}: {e.Message}");
            }
        }

        /// <summary>
        /// The tool's battery is the Minecraft item's (its durability bar): the real tool starts with that charge, what
        /// it spends goes back to Minecraft, and a battery swapped in Minecraft (SnBattery) fills the real one.
        /// </summary>
        private void Battery(float charge, HostLink link)
        {
            EnergyMixin energy = held != null ? held.GetComponent<EnergyMixin>() : null;
            var battery = energy != null ? (IBattery)BatteryField.GetValue(energy) : null;
            if (battery == null || battery.capacity <= 0f)
            {
                return; // (a fresh tool's battery comes a few frames after it)
            }

            // the charge it was taken out with, or a fresh battery put in (Minecraft's lags behind what was spent, so only a full one counts)
            if (charge >= 0f && (sentCharge < 0f || (charge >= 0.999f && sentCharge < 0.95f)))
            {
                battery.charge = battery.capacity * Mathf.Clamp01(charge);
                sentCharge = charge;
                return;
            }

            float now = battery.charge / battery.capacity;
            if (Time.unscaledTime >= nextChargeReport && Mathf.Abs(now - sentCharge) > 0.004f)
            {
                nextChargeReport = Time.unscaledTime + 0.5f;
                sentCharge = now;
                link.Send("{\"t\":\"toolcharge\",\"c\":" + now.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "}");
            }
        }

        /// <summary>A tool's charge (0..1) for Minecraft: null if it runs on nothing.</summary>
        public static string ChargeJson(GameObject tool, bool full)
        {
            EnergyMixin energy = tool != null ? tool.GetComponent<EnergyMixin>() : null;
            if (energy == null)
            {
                return "";
            }

            var battery = full ? null : (IBattery)BatteryField.GetValue(energy);
            float charge = full ? 1f : battery != null && battery.capacity > 0f ? battery.charge / battery.capacity : 0f;
            return ",\"charge\":" + charge.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A tool that came out of Steve's inventory into the world: its battery, with the charge it had.</summary>
        public static System.Collections.IEnumerator Charge(GameObject tool, TechType tech, float charge)
        {
            EnergyMixin energy = tool != null ? tool.GetComponent<EnergyMixin>() : null;
            if (energy == null)
            {
                yield break;
            }

            CrafterLogic.NotifyCraftEnd(tool, tech);
            for (int i = 0; i < 120 && tool != null; i++)
            {
                var battery = (IBattery)BatteryField.GetValue(energy);
                if (battery != null)
                {
                    battery.charge = battery.capacity * Mathf.Clamp01(charge);
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>Out of the diver's hand and gone (Steve holds something else, or the game is about to save).</summary>
        public void PutAway()
        {
            drawing = TechType.None;
            heldTech = TechType.None;
            Pickupable tool = held;
            held = null;
            if (tool == null)
            {
                return;
            }

            if (Inventory.main != null)
            {
                Inventory.main.quickSlots.DeselectImmediate();
                Inventory.main.container.RemoveItem(tool, true);
            }

            Object.Destroy(tool.gameObject);
        }

        /// <summary>The game saves: the tool goes for a few seconds, so it isn't written into the diver's inventory.</summary>
        public static void PauseForSave(SteveTools tools)
        {
            pausedUntil = Time.unscaledTime + 5f;
            tools?.PutAway();
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveToTemporaryStorageAsync), typeof(Texture2D))]
    internal static class SteveToolsSavePatch
    {
        private static void Prefix() => SteveTools.PauseForSave(Bridge.Tools);
    }
}
