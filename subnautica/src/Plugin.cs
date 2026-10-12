using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Minecraft inside Subnautica. Minecraft (with the SubnautiCraft Fabric mod) runs next to the game:
    /// this plugin sends it Subnautica's camera every frame, gets back Minecraft's picture (colour + depth) through shared
    /// memory, and rebuilds it as a depth mesh in Subnautica's world, so Subnautica's own lighting, fog and depth apply.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.subnauticraft";
        public const string Name = "SubnautiCraft";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<bool> StartAsSteve;
        internal static ConfigEntry<bool> ShowMinecraftWindow;
        internal static ConfigEntry<int> SubnauticaMaxFps;
        internal static ConfigEntry<int> MinecraftDistance;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<float> ResolutionScale;
        internal static ConfigEntry<int> GridStep;
        internal static ConfigEntry<float> EdgeTolerance;
        internal static ConfigEntry<string> ShaderName;
        internal static ConfigEntry<string> Lighting;
        internal static ConfigEntry<bool> Shadows;
        internal static ConfigEntry<float> GlowingBlocks;
        internal static ConfigEntry<bool> SmoothTexture;
        internal static ConfigEntry<int> TerrainRadius;
        internal static ConfigEntry<bool> BlockColliders;
        internal static ConfigEntry<bool> BlockLights;
        internal static ConfigEntry<float> BlockLightStrength;
        internal static ConfigEntry<int> MaxBlockLights;
        internal static ConfigEntry<float> ExplosionDamage;
        internal static ConfigEntry<float> DamageScale;
        internal static ConfigEntry<float> SwordDamageScale;
        internal static ConfigEntry<float> ArrowDamage;
        internal static ConfigEntry<string> OutcropDrops;

        private void Awake()
        {
            Log = Logger;
            Port = Config.Bind("General", "Port", 25611, "WebSocket port of the SubnautiCraft Fabric mod (must match -Dsubnauticraft.port in Minecraft).");
            StartAsSteve = Config.Bind("General", "StartAsSteve", true,
                "Play as Steve as soon as Minecraft is connected (Minecraft movement, swimming, inventory and building). Off: start as Ryley.");
            ShowMinecraftWindow = Config.Bind("General", "ShowMinecraftWindow", false,
                "Leave Minecraft's own window where it is (on a second screen, say) instead of moving it off the desktop while connected.");
            ToggleKey = Config.Bind("Keys", "Toggle", new KeyboardShortcut(KeyCode.F7),
                "Switch between Steve (Minecraft plays) and Ryley (Subnautica plays; Minecraft's blocks stay visible).");
            SubnauticaMaxFps = Config.Bind("Render", "SubnauticaMaxFps", 60,
                new ConfigDescription("While Minecraft is connected, Subnautica draws at most this many frames a second, leaving the graphics card some room for Minecraft (both games share it). 0 = no limit.",
                    new AcceptableValueRange<int>(0, 300)));
            MinecraftDistance = Config.Bind("Render", "MinecraftDistance", 6,
                new ConfigDescription("Minecraft's render and simulation distance in chunks while connected (its world is 2288 blocks tall: small is fast).",
                    new AcceptableValueRange<int>(2, 16)));
            ResolutionScale = Config.Bind("Render", "ResolutionScale", 0.5f,
                new ConfigDescription("Minecraft's picture size relative to Subnautica's screen.", new AcceptableValueRange<float>(0.25f, 1f)));
            GridStep = Config.Bind("Render", "GridStep", 4,
                new ConfigDescription("Pixels between depth-mesh vertices (smaller = sharper block edges, more CPU).", new AcceptableValueRange<int>(1, 16)));
            EdgeTolerance = Config.Bind("Render", "EdgeTolerance", 0.12f,
                new ConfigDescription("Relative depth jump that splits the mesh (a block edge in front of something further away).", new AcceptableValueRange<float>(0.01f, 1f)));
            ShaderName = Config.Bind("Render", "Shader", "MarmosetUBER",
                new ConfigDescription("Shader for Minecraft's blocks: MarmosetUBER (Subnautica's own: its lights and darkness), Standard, or Sprites/Default (flat).",
                    new AcceptableValueList<string>("MarmosetUBER", "Standard", "Sprites/Default")));
            Lighting = Config.Bind("Render", "Lighting", "Subnautica",
                new ConfigDescription("Who lights Minecraft's blocks. Subnautica: they are like the game's own models (its sun, caustics, fog, lamps and darkness; they cast and take shadows) and only light-giving blocks glow. Minecraft: they keep Minecraft's own light, as in Minecraft.",
                    new AcceptableValueList<string>("Subnautica", "Minecraft")));
            Shadows = Config.Bind("Render", "Shadows", true, "Minecraft's blocks cast shadows in Subnautica (with the game's shadows on).");
            GlowingBlocks = Config.Bind("Render", "GlowingBlocks", 1f,
                new ConfigDescription("How brightly torches, sea lanterns, lava and other light-giving blocks glow when Subnautica lights the blocks.", new AcceptableValueRange<float>(0f, 3f)));
            SmoothTexture = Config.Bind("Render", "SmoothTexture", true, "Bilinear filtering of Minecraft's picture (off = sharp pixels).");
            TerrainRadius = Config.Bind("World", "TerrainRadius", 24,
                new ConfigDescription("How far (metres) around you Subnautica's terrain is sent to Minecraft as collision: Steve stands and builds on it, mobs walk on it. 0 = off.",
                    new AcceptableValueRange<int>(0, 96)));
            BlockColliders = Config.Bind("World", "BlockColliders", true, "Minecraft blocks are solid in Subnautica (you, creatures and vehicles bump into them).");
            BlockLights = Config.Bind("World", "BlockLights", true, "Minecraft's torches, sea lanterns, glowstone, campfires... are real lights in Subnautica (they light up its seafloor, fish and bases).");
            BlockLightStrength = Config.Bind("World", "BlockLightStrength", 1f,
                new ConfigDescription("Brightness of those lights.", new AcceptableValueRange<float>(0.1f, 4f)));
            MaxBlockLights = Config.Bind("World", "MaxBlockLights", 32,
                new ConfigDescription("How many of the nearest Minecraft lights shine at once.", new AcceptableValueRange<int>(1, 128)));
            DamageScale = Config.Bind("Fun", "DamageScale", 0.2f,
                "Steve mode: what would hurt the diver hurts Steve, times this (the diver has 100 health, Steve 20 = 10 hearts).");
            ExplosionDamage = Config.Bind("Fun", "ExplosionDamage", 60f, "Damage a Minecraft explosion (TNT, creeper) does to Subnautica creatures, and to you, at its centre.");
            ArrowDamage = Config.Bind("Fun", "ArrowDamage", 20f, "Damage a Minecraft arrow does to the Subnautica creature it hits (the survival knife does 25).");
            OutcropDrops = Config.Bind("Fun", "OutcropDrops", "Both",
                new ConfigDescription("Breaking limestone, sandstone or shale as Steve drops: the Minecraft ore of what Subnautica rolled (copper -> raw copper, titanium -> raw iron, gold -> raw gold...), Subnautica's own chunk, or both.",
                    new AcceptableValueList<string>("Both", "Minecraft", "Subnautica")));
            SwordDamageScale = Config.Bind("Fun", "SwordDamageScale", 3.5f,
                "Minecraft swords work as Subnautica's knife (they also cut samples and seeds): damage = the sword's Minecraft attack damage x this (diamond 7 x 3.5 = about the knife's 25). 0 = off.");

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            var host = new GameObject("SubnautiCraft");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<SceneCleanerPreserve>(); // Subnautica's SceneCleaner destroys every other root object between menu and game
            host.AddComponent<Bridge>();
            Log.LogInfo($"{Name} {Version} loaded: start Minecraft with the SubnautiCraft mod; {ToggleKey.Value} switches between Steve and Ryley.");
        }
    }
}
