using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Subnautica's materials as Minecraft items (<see cref="SnItemTable"/>): Steve picks one up with right click and
    /// it goes to his Minecraft inventory, where Subnautica's own recipes craft it (two copper ore, a copper wire).
    /// Their icons are Subnautica's: drawn here from the game's own sprites and sent to Minecraft, which keeps them in
    /// a resource pack on this computer.
    /// </summary>
    internal static class SnItems
    {
        private const int IconSize = 32;
        private const int RenderSize = 128;
        private static Material spriteMaterial;
        private static readonly HashSet<TechType> iconSent = new HashSet<TechType>();

        /// <summary>What each food or drink gives the diver (food, water), as Minecraft was told when it got one.</summary>
        private static readonly Dictionary<TechType, Vector2> eats = new Dictionary<TechType, Vector2>();
        private static Dictionary<string, TechType> byId;
        private static readonly System.Reflection.MethodInfo DeployConstructor = HarmonyLib.AccessTools.Method(typeof(Constructor), "Deploy");

        /// <summary>
        /// The kind as Minecraft knows it: "item":"quartz" for a material with a Minecraft item of its own, otherwise
        /// "tech":"CrashPowder","name":"Sedimento sulfúrico" (Minecraft's "Subnautica item", named as this game names it).
        /// </summary>
        public static string McItem(TechType tech)
        {
            if (SnItemTable.Items.TryGetValue(tech, out string item))
            {
                return "\"item\":\"" + item + "\"";
            }

            string name = Language.main != null ? Language.main.Get(tech.AsString()) : null;
            return "\"tech\":\"" + tech.AsString() + "\",\"name\":\"" + Escape(string.IsNullOrEmpty(name) ? tech.AsString() : name) + "\"";
        }

        /// <summary>Food and drink: what it gives the diver, whole numbers (the same every time, so Minecraft's stacks match).</summary>
        public static string EatJson(TechType tech, Eatable eatable)
        {
            if (eatable == null || SnItemTable.Items.ContainsKey(tech))
            {
                return "";
            }

            var values = new Vector2(Mathf.Round(eatable.foodValue), Mathf.Round(eatable.waterValue));
            eats[tech] = values;
            return ",\"food\":" + ((int)values.x).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"water\":" + ((int)values.y).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>What a Subnautica food or drink Steve ate gives the diver (loaded from the game if not seen yet).</summary>
        public static IEnumerator EatValues(TechType tech, Action<Vector2> then)
        {
            if (!eats.TryGetValue(tech, out Vector2 values))
            {
                CoroutineTask<GameObject> task = CraftData.GetPrefabForTechTypeAsync(tech, false);
                yield return task;
                GameObject prefab = task.GetResult();
                Eatable eatable = prefab != null ? prefab.GetComponent<Eatable>() : null;
                values = eatable != null ? new Vector2(Mathf.Round(eatable.foodValue), Mathf.Round(eatable.waterValue)) : Vector2.zero;
                eats[tech] = values;
            }

            then(values);
        }

        /// <summary>
        /// Steve picked this up, cut it or was handed it: it goes to his Minecraft inventory and leaves Subnautica's world
        /// (the game still counts the pickup: its sound, goals and PDA entries). A material with a Minecraft item of its
        /// own comes as that; anything else as Minecraft's "Subnautica item" with its name, its icon and, for food and
        /// drink, what it gives the diver.
        /// </summary>
        public static bool Give(Pickupable pickupable, HostLink link, bool quiet = false)
        {
            TechType tech = pickupable.GetTechType();
            if (tech == TechType.None)
            {
                return false;
            }

            if (!SnItemTable.Items.ContainsKey(tech) && SendIcon(tech, link))
            {
                link.Send("{\"t\":\"icons\"}");
            }

            string message = "{\"t\":\"give\"," + McItem(tech) + ",\"n\":1" + EatJson(tech, pickupable.GetComponent<Eatable>())
                + (SnItemTable.Items.ContainsKey(tech) ? "" : SteveTools.ChargeJson(pickupable.gameObject, false)) + "}";
            if (!quiet)
            {
                pickupable.Pickup();
                KnownTech.Analyze(tech, verbose: false);
            }

            UnityEngine.Object.Destroy(pickupable.gameObject);
            link.Send(message);
            Plugin.Log.LogInfo($"Steve picked up {tech}" + (SnItemTable.Items.TryGetValue(tech, out string item) ? $": subnauticraft:{item}" : ": a Subnautica item"));
            return true;
        }

        /// <summary>
        /// What the diver still carries (picked up before Steve took over, or put there some other way) goes to Steve's
        /// Minecraft inventory, a few things a frame. Not what he wears (tanks, fins): that's equipment, not inventory.
        /// </summary>
        public static void EmptyDiver(HostLink link)
        {
            ItemsContainer container = Inventory.main != null ? Inventory.main.container : null;
            if (container == null || container.count == 0 || !link.Connected)
            {
                return;
            }

            var moving = new List<Pickupable>();
            foreach (InventoryItem item in (IEnumerable<InventoryItem>)container)
            {
                if (item != null && item.item != null && !SteveTools.IsHeld(item.item))
                {
                    moving.Add(item.item);
                }

                if (moving.Count >= 4)
                {
                    break;
                }
            }

            foreach (Pickupable pickupable in moving)
            {
                if (container.RemoveItem(pickupable, forced: true))
                {
                    Plugin.Log.LogInfo($"the diver was carrying {pickupable.GetTechType()}: to Steve");
                    Give(pickupable, link, quiet: true);
                }
            }
        }

        /// <summary>
        /// Steve dropped (Q) or placed (right click) one of Subnautica's things in Minecraft: it comes out in Subnautica's
        /// world as the real thing, in front of him. Placing the mobile vehicle bay deploys it (it floats up to the
        /// surface, as when the diver throws it); a beacon, a waterproof locker or a fish just come out.
        /// </summary>
        public static IEnumerator DropFromSteve(string item, string techName, int count, bool place, float charge = -1f)
        {
            TechType tech = TechType.None;
            if (item != null)
            {
                if (byId == null)
                {
                    byId = new Dictionary<string, TechType>();
                    foreach (KeyValuePair<TechType, string> pair in SnItemTable.Items)
                    {
                        byId[pair.Value] = pair.Key;
                    }
                }

                byId.TryGetValue(item, out tech);
            }
            else if (techName != null)
            {
                TechTypeExtensions.FromString(techName, out tech, true);
            }

            if (tech == TechType.None || MainCamera.camera == null)
            {
                Plugin.Log.LogWarning($"Steve dropped {item ?? techName}: no such thing in Subnautica");
                yield break;
            }

            Transform eye = MainCamera.camera.transform;
            for (int i = 0; i < Mathf.Clamp(count, 1, 64); i++)
            {
                var spawned = new TaskResult<GameObject>();
                yield return CraftData.InstantiateFromPrefabAsync(tech, spawned);
                GameObject go = spawned.Get();
                if (go == null)
                {
                    Plugin.Log.LogWarning($"Steve dropped {tech}: Subnautica has no prefab for it");
                    yield break;
                }

                Vector3 spread = i == 0 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * 0.2f;
                Vector3 at = eye.position + eye.forward * 0.9f + Vector3.down * 0.2f + spread;
                go.transform.position = at;
                go.transform.rotation = Quaternion.LookRotation(eye.forward);
                Pickupable pickupable = go.GetComponent<Pickupable>();
                if (pickupable != null)
                {
                    pickupable.Drop(at, eye.forward * (place ? 3f : 2f));
                }
                else
                {
                    LargeWorldEntity.Register(go);
                }

                if (charge >= 0f)
                {
                    UWE.CoroutineHost.StartCoroutine(SteveTools.Charge(go, tech, charge));
                }

                Constructor bay = go.GetComponent<Constructor>();
                if (place && bay != null)
                {
                    // as Constructor.OnRightHandDown: thrown forward, deployed, part of the world
                    Rigidbody body = go.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.AddForce(eye.forward * 6.5f, ForceMode.VelocityChange);
                    }

                    DeployConstructor.Invoke(bay, new object[] { true });
                    bay.OnDeployAnimationStart();
                    LargeWorldEntity.Register(go);
                    GoalManager.main.OnCustomGoalEvent("Release_Constructor");
                }

                Plugin.Log.LogInfo($"Steve {(place ? "placed" : "dropped")} {tech} in Subnautica");
            }
        }

        /// <summary>A kind without a Minecraft item of its own: its icon, once a session (Minecraft keeps it after that).</summary>
        public static bool SendIcon(TechType tech, HostLink link)
        {
            if (!iconSent.Add(tech))
            {
                return false;
            }

            try
            {
                Sprite sprite = SpriteManager.Get(tech);
                byte[] png = sprite != null ? Icon(sprite) : null;
                if (png != null)
                {
                    link.Send("{\"t\":\"icon\",\"n\":\"tech_" + tech.AsString().ToLowerInvariant() + "\",\"png\":\"" + Convert.ToBase64String(png) + "\"}");
                    return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"icon for {tech}: {e.Message}");
            }

            return false;
        }

        /// <summary>Every item's icon, then their names in Subnautica's language; a few icons a frame.</summary>
        public static IEnumerator SendIcons(HostLink link)
        {
            var names = new StringBuilder("{\"t\":\"icons\",\"names\":{");
            int sent = 0;
            bool first = true;
            foreach (KeyValuePair<TechType, string> pair in SnItemTable.Items)
            {
                if (!link.Connected)
                {
                    yield break;
                }

                byte[] png = null;
                try
                {
                    Sprite sprite = SpriteManager.Get(pair.Key);
                    png = sprite != null ? Icon(sprite) : null;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"icon for {pair.Key}: {e.Message}");
                }

                if (png != null)
                {
                    link.Send("{\"t\":\"icon\",\"n\":\"" + pair.Value + "\",\"png\":\"" + Convert.ToBase64String(png) + "\"}");
                    sent++;
                }

                string name = Language.main != null ? Language.main.Get(pair.Key.AsString()) : null;
                if (!string.IsNullOrEmpty(name))
                {
                    names.Append(first ? "" : ",").Append('"').Append(pair.Value).Append("\":\"").Append(Escape(name)).Append('"');
                    first = false;
                }

                if (sent % 4 == 0)
                {
                    yield return null;
                }
            }

            link.Send(names.Append("}}").ToString());
            Plugin.Log.LogInfo($"sent {sent} item icons to Minecraft");
            CheckRecipes();
        }

        /// <summary>
        /// The sprite as a 32 x 32 PNG. Sprites live packed in atlases (tightly, so not as rectangles): the sprite's
        /// own mesh is drawn into a transparent render target, then scaled down with straight alpha.
        /// </summary>
        private static byte[] Icon(Sprite sprite)
        {
            if (spriteMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    return null;
                }

                spriteMaterial = new Material(shader) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                spriteMaterial.SetColor("_Color", Color.white);
                spriteMaterial.SetColor("_RendererColor", Color.white);
                spriteMaterial.SetVector("_Flip", Vector4.one);
            }

            Vector2[] vertices = sprite.vertices;
            Vector2[] uv = sprite.uv;
            ushort[] tris = sprite.triangles;

            // the sprite's full area (its bounds, not just the opaque part the mesh covers), fitted into the square
            Bounds b = sprite.bounds;
            float extent = Mathf.Max(b.size.x, b.size.y);
            if (extent <= 0f)
            {
                return null;
            }

            RenderTexture rt = RenderTexture.GetTemporary(RenderSize, RenderSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var read = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false, false);
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                spriteMaterial.mainTexture = sprite.texture;
                spriteMaterial.SetPass(0);
                GL.PushMatrix();
                GL.LoadOrtho(); // (0, 0) bottom left to (1, 1) top right, like the sprite's own y-up space
                GL.Begin(GL.TRIANGLES);
                GL.Color(Color.white);
                for (int i = 0; i < tris.Length; i++)
                {
                    int k = tris[i];
                    Vector2 p = (vertices[k] - (Vector2)b.center) / extent + new Vector2(0.5f, 0.5f);
                    GL.TexCoord2(uv[k].x, uv[k].y);
                    GL.Vertex3(p.x, p.y, 0f);
                }

                GL.End();
                GL.PopMatrix();
                read.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0, false);
                read.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            // Sprites/Default writes premultiplied colour: average 4 x 4 blocks as they are, then undo the premultiply
            Color32[] big = read.GetPixels32();
            UnityEngine.Object.Destroy(read);
            int step = RenderSize / IconSize;
            var small = new Color32[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    int r = 0, g = 0, bl = 0, a = 0;
                    for (int dy = 0; dy < step; dy++)
                    {
                        for (int dx = 0; dx < step; dx++)
                        {
                            Color32 c = big[(y * step + dy) * RenderSize + x * step + dx];
                            r += c.r;
                            g += c.g;
                            bl += c.b;
                            a += c.a;
                        }
                    }

                    small[y * IconSize + x] = a == 0
                        ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)Mathf.Min(255, r * 255 / a), (byte)Mathf.Min(255, g * 255 / a), (byte)Mathf.Min(255, bl * 255 / a), (byte)(a / (step * step)));
                }
            }

            var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false, false);
            icon.SetPixels32(small);
            icon.Apply(false);
            byte[] png = icon.EncodeToPNG();
            UnityEngine.Object.Destroy(icon);
            return png;
        }

        /// <summary>The Minecraft recipes were written from Subnautica's: say in the log if the game now says otherwise.</summary>
        private static void CheckRecipes()
        {
            foreach (SnItemTable.Recipe recipe in SnItemTable.Recipes)
            {
                var game = new Dictionary<TechType, int>();
                var ours = new Dictionary<TechType, int>();
                var ingredients = TechData.GetIngredients(recipe.Result);
                if (ingredients == null)
                {
                    continue; // made another way (titanium from metal salvage)
                }

                foreach (Ingredient i in ingredients)
                {
                    game[i.techType] = (game.TryGetValue(i.techType, out int n) ? n : 0) + i.amount;
                }

                foreach (TechType t in recipe.Ingredients)
                {
                    if (t != TechType.None)
                    {
                        ours[t] = (ours.TryGetValue(t, out int n) ? n : 0) + 1;
                    }
                }

                var differences = new List<string>();
                foreach (KeyValuePair<TechType, int> g in game)
                {
                    int mine = ours.TryGetValue(g.Key, out int n) ? n : 0;
                    if (mine != g.Value)
                    {
                        differences.Add($"{g.Key} {g.Value} (Minecraft {mine})");
                    }
                }

                foreach (KeyValuePair<TechType, int> o in ours)
                {
                    if (!game.ContainsKey(o.Key))
                    {
                        differences.Add($"{o.Key} 0 (Minecraft {o.Value})");
                    }
                }

                if (differences.Count > 0)
                {
                    Plugin.Log.LogInfo($"recipe {recipe.Result}: Subnautica's differs: {string.Join(", ", differences)}");
                }
            }
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
