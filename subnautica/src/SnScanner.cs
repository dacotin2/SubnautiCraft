using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// The scanner in Steve's hand (a Minecraft item): holding right click on something scans it with Subnautica's own
    /// scanner logic (PDAScanner: fragments, creatures, plants, the PDA's entries), so blueprints unlock as they do for
    /// the diver. Its battery is the Minecraft item's: a second of scanning costs a second of charge.
    /// </summary>
    internal sealed class SnScanner
    {
        private const float Range = 8f;
        private float drain;
        private PDAScanner.Result shown = PDAScanner.Result.None;

        /// <summary>Every frame in Steve mode.</summary>
        public void Tick(string tool, bool screenOpen, HostLink link)
        {
            bool held = tool == "scanner" && !screenOpen && Input.GetMouseButton(1);
            if (!held)
            {
                shown = PDAScanner.Result.None;
                return;
            }

            PDAScanner.UpdateTarget(Range, false);
            PDAScanner.ScanTarget target = PDAScanner.scanTarget;
            if (!target.isValid)
            {
                return;
            }

            PDAScanner.Result result = PDAScanner.Scan();
            switch (result)
            {
                case PDAScanner.Result.Scan:
                    HandReticle.main.SetIcon(HandReticle.IconType.Progress, 1.5f);
                    HandReticle.main.SetProgress(PDAScanner.scanTarget.progress);
                    drain += Time.deltaTime;
                    if (drain >= 1f)
                    {
                        drain -= 1f;
                        link.Send("{\"t\":\"scanning\"}");
                    }

                    break;
                case PDAScanner.Result.Known when shown != result:
                    ErrorMessage.AddDebug(Language.main.GetFormat("ScannerEntityKnown", Language.main.Get(target.techType.AsString())));
                    break;
                case PDAScanner.Result.Processed when shown != result:
                    ErrorMessage.AddDebug(Language.main.Get("ScannerInstanceKnown"));
                    break;
            }

            shown = result;
        }
    }

    /// <summary>
    /// Blueprints are Minecraft recipes: what the diver knows (from the start, scanning, data boxes) is unlocked in
    /// Minecraft's recipe book and in its "Subnautica" advancements tab; the rest can't be crafted there either.
    /// </summary>
    internal static class Blueprints
    {
        private static HostLink link;
        private static bool listening;

        /// <summary>Everything the diver knows now, then each new blueprint as it comes.</summary>
        public static void SendAll(HostLink to)
        {
            link = to;
            if (!listening)
            {
                listening = true;
                KnownTech.onAdd += OnAdd;
            }

            var ids = new List<string>();
            foreach (TechType tech in KnownTech.GetTech())
            {
                ids.Add(McName(tech));
            }

            Send(ids, false);
            Plugin.Log.LogInfo($"blueprints: {ids.Count} known, sent to Minecraft");
        }

        private static void OnAdd(TechType tech, bool verbose)
        {
            string id = McName(tech);
            Send(new List<string> { id }, true);
            Plugin.Log.LogInfo($"blueprint {tech}: subnauticraft:{id} unlocked in Minecraft");
        }

        /// <summary>Its Minecraft item, or for the rest the name of its fabricator recipe in Minecraft (SnRecipes): "tech_constructor".</summary>
        private static string McName(TechType tech)
        {
            return SnItemTable.Items.TryGetValue(tech, out string id) ? id : "tech_" + tech.AsString().ToLowerInvariant();
        }

        private static void Send(List<string> ids, bool fresh)
        {
            if (link == null || !link.Connected || ids.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder("{\"t\":\"known\",\"new\":").Append(fresh ? "true" : "false").Append(",\"items\":[");
            for (int i = 0; i < ids.Count; i++)
            {
                sb.Append(i == 0 ? "\"" : ",\"").Append(ids[i]).Append('"');
            }

            link.Send(sb.Append("]}").ToString());
        }
    }

    /// <summary>
    /// Cinematics in Steve mode (the lifepod's ladder and hatch, base hatches, ladders, chairs): no animation, which
    /// fought Steve (the diver held in it, Steve mode dropped, Steve thrown back after). Subnautica's own way of skipping
    /// one (as in VR) runs instead: the diver goes straight to where the cinematic ends, the game counts it as done (in
    /// or out of the lifepod, sitting down...), and Steve is sent there. It runs a frame later, once whatever started
    /// the cinematic has hooked itself up to be told it ended.
    /// </summary>
    internal static class Cinematics
    {
        private static readonly List<KeyValuePair<PlayerCinematicController, Player>> queued = new List<KeyValuePair<PlayerCinematicController, Player>>();
        private static int queuedFrame;

        public static void Queue(PlayerCinematicController cinematic, Player player)
        {
            queued.Add(new KeyValuePair<PlayerCinematicController, Player>(cinematic, player));
            queuedFrame = Time.frameCount;
        }

        /// <summary>Every frame: finish the cinematics started before this frame.</summary>
        public static void Tick()
        {
            if (queued.Count == 0 || Time.frameCount == queuedFrame)
            {
                return;
            }

            var done = new List<KeyValuePair<PlayerCinematicController, Player>>(queued);
            queued.Clear();
            foreach (KeyValuePair<PlayerCinematicController, Player> c in done)
            {
                PlayerCinematicController cinematic = c.Key;
                Player player = c.Value;
                if (cinematic == null || player == null)
                {
                    continue;
                }

                if (cinematic.endTransform != null)
                {
                    player.SetPosition(cinematic.endTransform.position, cinematic.endTransform.rotation);
                }

                if (cinematic.informGameObject != null)
                {
                    cinematic.informGameObject.SendMessage("OnPlayerCinematicModeEnd", cinematic, SendMessageOptions.DontRequireReceiver);
                }

                Bridge.SendSteveToDiver("cinematic " + cinematic.name);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerCinematicController), nameof(PlayerCinematicController.StartCinematicMode))]
    internal static class SkipCinematicsPatch
    {
        private static bool Prefix(PlayerCinematicController __instance, Player setplayer)
        {
            if (!Bridge.SteveEngaged || setplayer == null || setplayer != Player.main)
            {
                return true;
            }

            Cinematics.Queue(__instance, setplayer);
            return false;
        }
    }
}
