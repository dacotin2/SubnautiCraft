using HarmonyLib;

namespace SubnautiCraft
{
    /// <summary>
    /// A new game's intro is skipped (it fought Steve mode: a locked camera, the diver held in a cinematic): the
    /// launch cutscene stops at once, the way holding the skip key does, and the lifepod's fire puts itself out, so the
    /// game goes on exactly as after the intro (lights back, the PDA's first entry, the story goals).
    /// </summary>
    [HarmonyPatch(typeof(uGUI_SceneIntro), nameof(uGUI_SceneIntro.Play))]
    internal static class IntroCutscenePatch
    {
        private static void Postfix(uGUI_SceneIntro __instance)
        {
            if (Plugin.SkipIntro.Value && __instance.showing)
            {
                Plugin.Log.LogInfo("skipping the intro cutscene");
                __instance.Stop(true);
            }
        }
    }

    [HarmonyPatch(typeof(IntroLifepodDirector), nameof(IntroLifepodDirector.EnableIntroSequence))]
    internal static class IntroFirePatch
    {
        private static void Postfix(IntroLifepodDirector __instance)
        {
            if (!Plugin.SkipIntro.Value || __instance.escapePod == null)
            {
                return;
            }

            foreach (Fire fire in __instance.escapePod.GetComponentsInChildren<Fire>(true))
            {
                if (fire.introFire && !fire.IsExtinguished())
                {
                    fire.Douse(100000f);
                }
            }

            Plugin.Log.LogInfo("skipping the lifepod intro: its fire is out");
        }
    }
}
