using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Runs the link every frame, in one of two modes (F7 switches):
    /// <list type="bullet">
    /// <item><b>Steve</b> (default, SkyCraft's design): Minecraft owns the player. Your keyboard and mouse go to
    /// Minecraft; Steve swims, walks, builds and opens his inventory with Minecraft's own code, against
    /// Subnautica's terrain and ocean; Subnautica's camera sits in Steve's eyes and the diver follows him, invisible.</item>
    /// <item><b>Ryley</b>: Subnautica plays as usual and Minecraft's camera follows the diver, so Minecraft's
    /// blocks stay in place on screen.</item>
    /// </list>
    /// Either way Minecraft's newest picture comes back as the depth mesh (world) and a screen overlay (hand,
    /// hotbar, screens), and Minecraft's events (blocks, explosions, sword hits) act on Subnautica.
    /// </summary>
    internal sealed class Bridge : MonoBehaviour
    {
        /// <summary>Steve plays: Subnautica's own movement is held back (<see cref="InputPatch"/>).</summary>
        public static bool SteveEngaged;
        /// <summary>Steve plays and no Subnautica screen (PDA, pause menu) is open: Subnautica's buttons are held back too.</summary>
        public static bool BlockGameInput;
        /// <summary>Steve at the wheel of a vehicle: Subnautica's controls and health rules, Steve's hand and hotbar.</summary>
        public static bool SteveDriving;
        /// <summary>A Minecraft screen (inventory, chat, pause) is open: the cursor is Minecraft's.</summary>
        public static bool McScreenOpen;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const float StaleSeconds = 3f;
        private const float SteveEyeHeight = 1.62f;

        private HostLink link;
        private FrameReader reader;
        private McLayer layer;
        private BlockColliders blocks;
        private BlockLights lights;
        private TerrainExporter terrain;
        private InputForwarder input;
        private RyleyPuppet puppet;
        private readonly ArrowTracker arrows = new ArrowTracker();
        private readonly McFrame frame = new McFrame();
        private Texture2D overlay;
        private bool overlayValid;
        private bool steveWanted;
        private bool active;
        private int seenConnections;
        private int sentW, sentH;
        private int sentHud = -1;
        private int sentUnlit = -1;
        private readonly List<Vector3Int> emitters = new List<Vector3Int>();
        private int sentMode = -1;
        private float sentFov = -1f;
        private bool inputReleased = true;
        private bool cursorFreed;
        private float nextWater;
        private long hostFrame;
        private int lastPreCullFrame = -1;
        private float lastFrameTime = -100f;
        private Vector3 lastBlockSync = new Vector3(float.NaN, 0f, 0f);
        private float nextWarn;

        // Minecraft's latest "pov" (Steve mode), in Subnautica's space
        private bool hasPov;
        private string lastPovJson;
        private double povArrival;
        private Vector3 povCamera;
        private Vector3 povFeet;
        private Vector3 povVelocity;
        private bool povThirdPerson;
        private float povHealth, povMaxHealth;
        private bool povDead;
        private int povPlayerId = -1;
        private int povTerrainBacklog;
        /// <summary>The Minecraft player object Steve mode is following (a new one after a respawn waits at the world spawn).</summary>
        private int followedPlayerId = -1;
        private bool killedRyley;

        /// <summary>
        /// Steve was sent to the diver (taking over, a respawn, a warp): until Minecraft reports him there and the
        /// ground around him has reached Minecraft, both games hold still (Minecraft is paused) so he can't fall or drown.
        /// </summary>
        private bool teleportPending;
        private Vector3 teleportTarget;
        private float teleportSince;
        private int sentPause = -1;
        private float notSteveSince = -1f;
        private float nextStateReport;
        private bool cappedFps;
        private bool bodyHiddenForVehicle;

        private static Bridge instance;

        private static Camera MainCam => SNCameraRoot.main != null ? SNCameraRoot.main.mainCam : null;

        private void Awake()
        {
            instance = this;
            steveWanted = Plugin.StartAsSteve.Value;
            link = new HostLink(Plugin.Port.Value);
            reader = new FrameReader();
            layer = new McLayer();
            blocks = new BlockColliders();
            lights = new BlockLights();
            terrain = new TerrainExporter();
            input = new InputForwarder { TrySubnauticaUse = TrySubnauticaUse };
            puppet = new RyleyPuppet();
            Camera.onPreCull += OnPreCullCamera;
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnPreCullCamera;
            link?.Dispose();
            reader?.Dispose();
        }

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Warn("update", e);
            }
        }

        /// <summary>One error a few seconds is enough: a failure every frame would bury the log (and the game).</summary>
        private void Warn(string where, Exception e)
        {
            if (Time.unscaledTime >= nextWarn)
            {
                nextWarn = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"{where}: {e}");
            }
        }

        private void Tick()
        {
            if (Plugin.ToggleKey.Value.IsDown())
            {
                steveWanted = !steveWanted;
                ErrorMessage.AddMessage(steveWanted ? "SubnautiCraft: juegas como Steve (Minecraft)" : "SubnautiCraft: juegas como Ryley (Subnautica)");
            }

            while (link.Inbox.TryDequeue(out string message))
            {
                Handle(message);
            }

            ReadPov();
            if (link.Connected && link.Connections != seenConnections)
            {
                // a fresh Minecraft (or a reconnect): it knows nothing about this session yet
                seenConnections = link.Connections;
                sentW = sentH = 0;
                sentHud = sentMode = sentPause = sentUnlit = -1;
                sentFov = -1f;
                terrain.Reset();
                blocks.Clear();
                lights.Clear();
                lastBlockSync = new Vector3(float.NaN, 0f, 0f);
                hasPov = false;
                Disengage();
                // Minecraft's own window goes off the desktop (it keeps rendering there); it comes back when Subnautica quits
                link.Send(Plugin.ShowMinecraftWindow.Value ? "{\"t\":\"window\",\"hide\":false}" : "{\"t\":\"window\",\"hide\":true}");
                link.Send("{\"t\":\"distance\",\"v\":" + Plugin.MinecraftDistance.Value.ToString(Inv) + "}");
                ErrorMessage.AddMessage("SubnautiCraft: conectado a Minecraft (F7 = Steve / Ryley)");
            }

            // both games share the graphics card: Subnautica unbounded leaves Minecraft a handful of frames a second
            int fpsCap = link.Connected && Plugin.SubnauticaMaxFps.Value > 0 ? Plugin.SubnauticaMaxFps.Value : -1;
            if (Application.targetFrameRate != fpsCap && (fpsCap > 0 || cappedFps))
            {
                Application.targetFrameRate = fpsCap;
                cappedFps = fpsCap > 0;
            }

            Camera cam = MainCam;
            Player player = Player.main;
            bool inGame = player != null && cam != null && player.playerController != null;
            // only Subnautica's loading screen: LargeWorldStreamer.IsWorldSettled() is false whenever the world streams
            // in around you (all the time while moving), which switched Steve mode off and on in a loop
            bool loading = WaitScreen.IsWaiting;
            if (link.Connected)
            {
                // Minecraft holds still whenever Subnautica isn't playing: its menu, a loading screen, its pause menu,
                // the diver's death, and while Steve waits for the ground under him
                // (never while Steve is dead in Minecraft: he has to be able to respawn there)
                string why = !inGame ? "not in a game" : loading ? "loading" : Time.timeScale <= 0f ? "Subnautica paused"
                    : povDead ? null : SteveEngaged && teleportPending ? "placing Steve" : steveWanted && !player.IsAlive() ? "diver dead" : null;
                int pauseState = why != null ? 1 : 0;
                if (pauseState != sentPause)
                {
                    sentPause = pauseState;
                    Plugin.Log.LogInfo(why != null ? "Minecraft paused: " + why : "Minecraft running");
                    link.Send(why != null ? "{\"t\":\"pause\",\"on\":true}" : "{\"t\":\"pause\",\"on\":false}");
                }
            }

            active = link.Connected && inGame;
            if (!active)
            {
                Disengage();
                layer.Visible = false;
                overlayValid = false;
                lights.Update(Vector3.zero, false);
                return;
            }

            bool canSteve = steveWanted && !loading && player.IsAlive() && player.GetMode() == Player.Mode.Normal && !player.cinematicModeActive
                && player.currentMountedVehicle == null && !player.isPiloting;
            // leaving Steve mode needs the reason to last a moment (a one-frame flicker of a game state must not throw
            // Steve back to the diver); death and vehicles count at once
            if (canSteve)
            {
                notSteveSince = -1f;
            }
            else if (notSteveSince < 0f)
            {
                notSteveSince = Time.unscaledTime;
            }

            bool leave = !canSteve && (Time.unscaledTime - notSteveSince > 0.75f || !player.IsAlive() || player.currentMountedVehicle != null || !steveWanted);
            if (canSteve && !SteveEngaged)
            {
                Engage(cam);
            }
            else if (leave && SteveEngaged)
            {
                Plugin.Log.LogInfo("leaving Steve mode: " + (!steveWanted ? "F7" : !player.IsAlive() ? "diver dead" : loading ? "loading"
                    : player.GetMode() != Player.Mode.Normal ? "player mode " + player.GetMode() : player.cinematicModeActive ? "cinematic"
                    : player.currentMountedVehicle != null || player.isPiloting ? "vehicle" : "?"));
                if (!player.IsAlive() && !killedRyley)
                {
                    // the diver died some other way than through damage (a cinematic, a console kill): Steve goes too
                    ForwardToSteve("hurt", "Normal", 100000f);
                }

                Disengage();
            }

            int mode = SteveEngaged ? 1 : 0;
            if (mode != sentMode)
            {
                sentMode = mode;
                link.Send(SteveEngaged ? "{\"t\":\"mode\",\"steve\":true}" : "{\"t\":\"mode\",\"steve\":false}");
            }

            SendView();
            int unlit = McLayer.LitBySubnautica ? 1 : 0;
            if (unlit != sentUnlit)
            {
                // Subnautica lights the blocks: Minecraft draws them without its own light
                sentUnlit = unlit;
                link.Send(unlit == 1 ? "{\"t\":\"lighting\",\"unlit\":true}" : "{\"t\":\"lighting\",\"unlit\":false}");
            }
            if (Mathf.Abs(cam.fieldOfView - sentFov) > 0.01f)
            {
                sentFov = cam.fieldOfView;
                link.Send("{\"t\":\"fov\",\"v\":" + sentFov.ToString("F3", Inv) + "}");
            }

            if (Time.unscaledTime >= nextWater)
            {
                // the ocean, except where Subnautica itself says you're dry (bases, the lifepod, precursor rooms)
                nextWater = Time.unscaledTime + 0.25f;
                bool dry = player.IsInsideWalkable() || player.precursorOutOfWater;
                link.Send(dry ? "{\"t\":\"water\",\"y\":null}" : "{\"t\":\"water\",\"y\":" + Ocean.GetOceanLevel().ToString("F3", Inv) + "}");
            }

            bool subnauticaUi = Time.timeScale <= 0f || (player.GetPDA() != null && player.GetPDA().isInUse) || uGUI_BuilderMenu.IsOpen() || DevConsoleOpen()
                || (FPSInputModule.current != null && FPSInputModule.current.lastGroup != null); // a fabricator, a locker...

            // Steve at the wheel (Seamoth, Prawn suit, Cyclops): Subnautica drives with its own controls and camera,
            // Minecraft's camera follows (Ryley mode underneath), but the diver stays hidden and Steve's hand shows
            bool steveDriving = steveWanted && !SteveEngaged && player.IsAlive() && (player.currentMountedVehicle != null || player.isPiloting);
            SteveDriving = steveDriving;
            if (steveDriving)
            {
                puppet.HideBody();
                bodyHiddenForVehicle = true;
            }
            else if (bodyHiddenForVehicle && !SteveEngaged)
            {
                puppet.ShowBody();
                bodyHiddenForVehicle = false;
            }

            int hud = (SteveEngaged && !teleportPending && !povDead || steveDriving) && !subnauticaUi ? 0 : 1;
            if (hud != sentHud)
            {
                sentHud = hud;
                link.Send(hud == 1 ? "{\"t\":\"hud\",\"hidden\":true}" : "{\"t\":\"hud\",\"hidden\":false}");
            }

            if (SteveEngaged)
            {
                PlaySteve(cam, subnauticaUi);
                if (Time.unscaledTime >= nextStateReport)
                {
                    nextStateReport = Time.unscaledTime + 5f;
                    Plugin.Log.LogInfo($"state: placing {teleportPending}, paused {sentPause == 1}, hud {(sentHud == 0 ? "on" : "off")}, Subnautica UI {subnauticaUi}, " +
                        $"Minecraft screen {McScreenOpen}, pov age {HostLink.Now - povArrival:F2} s, frame age {Time.unscaledTime - lastFrameTime:F2} s, " +
                        $"Steve {povFeet} hp {povHealth:F1}, diver {player.transform.position}, outbox {link.Pending}");
                }
            }

            Vector3 at = cam.transform.position;
            terrain.Tick(link, at);
            lights.Update(at, true);
            if (float.IsNaN(lastBlockSync.x) || (at - lastBlockSync).sqrMagnitude > 48f * 48f)
            {
                // Minecraft's blocks around here become solid in Subnautica; later changes arrive by themselves
                lastBlockSync = at;
                link.Send("{\"t\":\"blocksync\",\"r\":48}");
            }

            if (Time.unscaledTime - lastFrameTime > StaleSeconds)
            {
                layer.Visible = false;
                overlayValid = false;
            }
        }

        /// <summary>Steve takes over at the diver's position and look.</summary>
        private void Engage(Camera cam)
        {
            SteveEngaged = true;
            puppet.TakeOver(cam);
            input.Reset();
            inputReleased = true;
            followedPlayerId = -1;
            killedRyley = false;
            SendTeleport(cam.transform.position - new Vector3(0f, SteveEyeHeight, 0f), cam.transform.rotation);
            Plugin.Log.LogInfo("Steve mode: Minecraft plays");
        }

        private void Disengage()
        {
            if (!SteveEngaged)
            {
                return;
            }

            SteveEngaged = false;
            BlockGameInput = false;
            McScreenOpen = false;
            FreeCursor(false);
            puppet.Release();
            link.Send("{\"t\":\"releaseall\"}");
            input.Reset();
            inputReleased = true;
            Plugin.Log.LogInfo("Ryley mode: Subnautica plays");
        }

        private void SendTeleport(Vector3 feet, Quaternion look)
        {
            if (hasPov && !povDead && (povFeet - feet).sqrMagnitude < 2.5f * 2.5f)
            {
                return; // Steve is already there: no need to stop the game to place him
            }

            Plugin.Log.LogInfo("placing Steve at the diver (" + feet + ")");
            double[] p = Coords.ToMc(feet);
            Coords.Angles(look, out float yaw, out float pitch, out _);
            link.Send(string.Format(Inv, "{{\"t\":\"tp\",\"p\":[{0:F4},{1:F4},{2:F4}],\"r\":[{3:F3},{4:F3}]}}", p[0], p[1], p[2], yaw, pitch));
            teleportPending = true;
            teleportTarget = feet;
            teleportSince = Time.unscaledTime;
            puppet.Forget();
        }

        private void PlaySteve(Camera cam, bool subnauticaUi)
        {
            BlockGameInput = !subnauticaUi;
            if (subnauticaUi)
            {
                // Subnautica's PDA or menu has the input: Minecraft lets go of everything
                if (!inputReleased)
                {
                    inputReleased = true;
                    link.Send("{\"t\":\"releaseall\"}");
                    input.Reset();
                }

                FreeCursor(false);
            }
            else if (!teleportPending)
            {
                inputReleased = false;
                FreeCursor(McScreenOpen);
                link.Send(input.Build(cam.transform, McScreenOpen, Math.Max(sentW, 1), Math.Max(sentH, 1)));
            }

            if (!hasPov)
            {
                return;
            }

            if (povDead)
            {
                // Minecraft's health is the one that counts: the Steve we follow died, so the diver dies, and
                // Subnautica's own death runs (respawn at the lifepod or a base). Steve then joins him there. A Steve
                // already dead before this (died while Subnautica was away) just respawns in Minecraft.
                if (povPlayerId == followedPlayerId && !killedRyley && Player.main.liveMixin != null && Player.main.liveMixin.IsAlive())
                {
                    killedRyley = true;
                    Plugin.Log.LogInfo("Steve died: so does the diver");
                    Player.main.liveMixin.Kill();
                }

                return;
            }

            if (povPlayerId != followedPlayerId)
            {
                // a new Minecraft player object (Steve respawned or rejoined) starts at the world spawn: never follow
                // him there, bring him here
                followedPlayerId = povPlayerId;
                Plugin.Log.LogInfo("following Minecraft player " + povPlayerId + ": sending him to the diver");
                SendTeleport(cam.transform.position - new Vector3(0f, SteveEyeHeight, 0f), cam.transform.rotation);
            }

            if (teleportPending)
            {
                float waited = Time.unscaledTime - teleportSince;
                bool arrived = (povFeet - teleportTarget).sqrMagnitude < 2f * 2f;
                bool groundReady = terrain.CoveredAround(teleportTarget, 12f, teleportSince) && povTerrainBacklog == 0;
                if (waited < 4f && !(arrived && groundReady && waited > 0.25f))
                {
                    return; // the diver waits where he is, Minecraft is paused: nobody falls through unsent ground
                }

                Plugin.Log.LogInfo($"Steve placed after {waited:F1} s (arrived {arrived}, ground ready {groundReady}, Minecraft terrain backlog {povTerrainBacklog})");
                teleportPending = false;
            }

            // the diver's health bar shows Steve's (Subnautica's damage reaches Steve through RyleyDamagePatch)
            LiveMixin live = Player.main.liveMixin;
            if (live != null && povMaxHealth > 0f)
            {
                live.health = Mathf.Max(0.5f, povHealth / povMaxHealth * live.maxHealth);
            }

            Vector3 eye = povThirdPerson ? povFeet + new Vector3(0f, SteveEyeHeight, 0f) : povCamera;
            if (!puppet.Follow(eye))
            {
                // Subnautica moved the diver itself (respawn, warp, ladder): Steve goes there
                SendTeleport(cam.transform.position - new Vector3(0f, SteveEyeHeight, 0f), cam.transform.rotation);
            }
        }

        /// <summary>Minecraft's screens need the real cursor; gameplay keeps it locked like Subnautica does.</summary>
        private void FreeCursor(bool free)
        {
            if (free)
            {
                UWE.Utils.lockCursor = false;
                cursorFreed = true;
            }
            else if (cursorFreed)
            {
                cursorFreed = false;
                UWE.Utils.lockCursor = true;
            }
        }

        /// <summary>Just before Subnautica's main camera renders: Steve mode puts it at Minecraft's camera; Ryley mode sends it to Minecraft. Then the newest picture is shown.</summary>
        private void OnPreCullCamera(Camera cam)
        {
            if (!active || cam != MainCam || Time.frameCount == lastPreCullFrame)
            {
                return;
            }

            lastPreCullFrame = Time.frameCount;
            try
            {
                Transform t = cam.transform;
                if (SteveEngaged)
                {
                    if (hasPov && !teleportPending && !povDead)
                    {
                        // Minecraft's camera, carried forward by Steve's velocity to this frame (the two games' frames
                        // don't line up); the rotation is Subnautica's own (it's where Steve looks)
                        float ahead = Mathf.Clamp((float)(HostLink.Now - povArrival), 0f, 0.1f);
                        t.position = povCamera + povVelocity * ahead;
                    }
                }
                else
                {
                    double[] p = Coords.ToMc(t.position);
                    Coords.Angles(t.rotation, out float yaw, out float pitch, out float roll);
                    // no "{n:F3}}}" here: .NET reads the closing braces as part of the format ("F3}") and prints "F3"
                    link.SendCamera("{\"t\":\"cam\",\"f\":" + (++hostFrame).ToString(Inv) +
                        ",\"p\":[" + p[0].ToString("F4", Inv) + "," + p[1].ToString("F4", Inv) + "," + p[2].ToString("F4", Inv) +
                        "],\"r\":[" + yaw.ToString("F3", Inv) + "," + pitch.ToString("F3", Inv) + "," + roll.ToString("F3", Inv) +
                        "],\"fov\":" + cam.fieldOfView.ToString("F3", Inv) + "}");
                }

                if (reader.TryRead(frame, Plugin.GridStep.Value))
                {
                    lights.Emitters(frame.Position, 64f, emitters);
                    layer.Show(frame, emitters);
                    ShowOverlay(frame);
                    layer.Visible = true;
                    lastFrameTime = Time.unscaledTime;
                }
            }
            catch (Exception e)
            {
                Warn("frame", e);
                lastFrameTime = -100f;
            }
        }

        private void ShowOverlay(McFrame f)
        {
            if (overlay == null || overlay.width != f.Width || overlay.height != f.Height)
            {
                if (overlay != null)
                {
                    Destroy(overlay);
                }

                overlay = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false, false)
                {
                    name = "SubnautiCraft overlay", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset
                };
            }

            overlay.filterMode = Plugin.SmoothTexture.Value ? FilterMode.Bilinear : FilterMode.Point;
            overlay.LoadRawTextureData(f.Overlay);
            overlay.Apply(false);
            overlayValid = true;
        }

        private void OnGUI()
        {
            if (!active || !overlayValid || overlay == null || Event.current.type != EventType.Repaint || sentHud != 0)
            {
                return;
            }

            GUI.depth = -100;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), overlay, ScaleMode.StretchToFill, true);
        }

        /// <summary>Minecraft's window matches Subnautica's picture shape, at the configured scale.</summary>
        private void SendView()
        {
            int w = Mathf.Max(320, Mathf.RoundToInt(Screen.width * Plugin.ResolutionScale.Value));
            int h = Mathf.Max(180, Mathf.RoundToInt(w * (float)Screen.height / Screen.width));
            if (w == sentW && h == sentH)
            {
                return;
            }

            sentW = w;
            sentH = h;
            link.Send(string.Format(Inv, "{{\"t\":\"view\",\"w\":{0},\"h\":{1}}}", w, h));
        }

        private static readonly System.Reflection.FieldInfo ConsoleInstance = HarmonyLib.AccessTools.Field(typeof(DevConsole), "instance");
        private static readonly System.Reflection.FieldInfo ConsoleState = HarmonyLib.AccessTools.Field(typeof(DevConsole), "state");

        /// <summary>A Minecraft item appears in Minecraft's world at this Subnautica position (an ore from a broken outcrop).</summary>
        public static void DropMinecraftItem(Vector3 at, string item)
        {
            Bridge b = instance;
            if (b == null || !b.link.Connected)
            {
                return;
            }

            double[] p = Coords.ToMc(at);
            string command = "summon minecraft:item " + p[0].ToString("F2", Inv) + " " + p[1].ToString("F2", Inv) + " " + p[2].ToString("F2", Inv)
                + " {Item:{id:\"" + item + "\",count:1},PickupDelay:10}";
            b.link.Send("{\"t\":\"cmd\",\"c\":\"" + command.Replace("\"", "\\\"") + "\"}");
        }

        /// <summary>Right click on something Subnautica's hand can use: Subnautica uses it (the same as its own click).</summary>
        private static bool TrySubnauticaUse()
        {
            Player player = Player.main;
            GUIHand hand = player != null ? player.guiHand : null;
            GameObject target = hand != null ? hand.GetActiveTarget() : null;
            if (target == null || !player.IsFreeToInteract())
            {
                return false;
            }

            GUIHand.Send(target, HandTargetEventType.Click, hand);
            return true;
        }

        /// <summary>Subnautica's command console (Shift+Enter) is open: what you type is for it, not for Steve.</summary>
        private static bool DevConsoleOpen()
        {
            object console = ConsoleInstance?.GetValue(null);
            return console != null && ConsoleState != null && (bool)ConsoleState.GetValue(console);
        }

        /// <summary>Minecraft's newest "pov": Steve's camera, feet, velocity, health and state.</summary>
        private void ReadPov()
        {
            string json = link.LatestPov;
            if (json == null || ReferenceEquals(json, lastPovJson))
            {
                return;
            }

            lastPovJson = json;
            JObject m;
            try
            {
                m = JObject.Parse(json);
            }
            catch (Exception)
            {
                return;
            }

            float[] p = m["p"].ToObject<float[]>();
            float[] feet = m["f"].ToObject<float[]>();
            float[] v = m["v"]?.ToObject<float[]>() ?? new float[3];
            povCamera = Coords.FromMc(p[0], p[1], p[2]);
            povFeet = Coords.FromMc(feet[0], feet[1], feet[2]);
            povVelocity = new Vector3(-v[0], v[1], v[2]);
            povArrival = link.LatestPovTime;
            povThirdPerson = (bool)m["tp"];
            McScreenOpen = (bool)m["scr"];
            povHealth = m["hp"] != null ? (float)m["hp"] : 20f;
            povMaxHealth = m["hpMax"] != null ? (float)m["hpMax"] : 20f;
            povDead = m["dead"] != null && (bool)m["dead"];
            povPlayerId = m["pid"] != null ? (int)m["pid"] : 0;
            povTerrainBacklog = m["tq"] != null ? (int)m["tq"] : 0;
            hasPov = true;
        }

        /// <summary>The diver's damage or healing, for Steve (Minecraft health points: Subnautica's times DamageScale).</summary>
        public static void ForwardToSteve(string what, string kind, float subnauticaAmount)
        {
            Bridge b = instance;
            if (b == null || !b.link.Connected)
            {
                return;
            }

            float hp = subnauticaAmount * Plugin.DamageScale.Value;
            b.link.Send(what == "hurt"
                ? "{\"t\":\"hurt\",\"kind\":\"" + kind + "\",\"hp\":" + hp.ToString("F3", Inv) + "}"
                : "{\"t\":\"heal\",\"hp\":" + hp.ToString("F3", Inv) + "}");
        }

        private void Handle(string message)
        {
            JObject m;
            try
            {
                m = JObject.Parse(message);
            }
            catch (Exception)
            {
                Plugin.Log.LogWarning($"bad message from Minecraft: {message}");
                return;
            }

            switch ((string)m["t"])
            {
                case "hello":
                    Plugin.Log.LogInfo($"Minecraft says hello: {message}");
                    reader.UseName((string)m["shm"]);
                    break;
                case "blocks":
                    if (Plugin.BlockColliders.Value)
                    {
                        blocks.Apply(m["set"]?.ToObject<int[]>(), m["clear"]?.ToObject<int[]>());
                    }

                    break;
                case "lights":
                    lights.Apply(m);
                    break;
                case "explosion":
                    Explode(m);
                    break;
                case "proj":
                    arrows.Update(link, m["p"].ToObject<float[][]>());
                    break;
                case "died":
                    // Steve died in Minecraft (it respawns him at once, so this event is all Subnautica sees)
                    if (SteveEngaged && !killedRyley && Player.main != null && Player.main.liveMixin != null && Player.main.liveMixin.IsAlive())
                    {
                        killedRyley = true;
                        Plugin.Log.LogInfo("Steve died: so does the diver");
                        Player.main.liveMixin.Kill();
                    }

                    break;
                case "melee":
                    SwordStrike.Swing(m["dmg"] != null ? (float)m["dmg"] : 7f);
                    break;
            }
        }

        /// <summary>TNT and creepers hurt what's around them in Subnautica too (including you).</summary>
        private void Explode(JObject m)
        {
            float[] pos = m["pos"]?.ToObject<float[]>();
            if (pos == null || pos.Length < 3 || Plugin.ExplosionDamage.Value <= 0f)
            {
                return;
            }

            float radius = m["r"] != null ? (float)m["r"] : 4f;
            Vector3 at = Coords.FromMc(pos[0], pos[1], pos[2]);
            DamageSystem.RadiusDamage(Plugin.ExplosionDamage.Value, at, radius * 1.5f, DamageType.Explosive, null);
        }
    }
}
