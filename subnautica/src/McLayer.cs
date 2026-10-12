using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SubnautiCraft
{
    /// <summary>
    /// Minecraft's world picture rebuilt as geometry in Subnautica's world: a grid mesh whose vertices are pushed out
    /// along each pixel's view ray to the depth Minecraft measured. Drawn with Subnautica's own shader, it is hidden by
    /// (and hides) Subnautica's geometry through the ordinary depth buffer, and stays put in the world while the camera
    /// moves on (no swimming when Minecraft lags a frame). A mod can't compile shaders without the Unity editor, so this
    /// uses only shaders the game already ships.
    /// <para>
    /// Lit by Subnautica (Lighting = Subnautica), Minecraft draws its blocks with no light of its own and this mesh
    /// carries each face's real direction, so the game's sun, caustics, fog, lamps and shadows fall on them as on any
    /// of its own models, and they cast shadows. Only blocks that give light glow, with their own picture.
    /// </para>
    /// <para>
    /// Where an edge passes between grid points (a block in front of something further away, or one face of a block
    /// meeting the next), the full-resolution depth says exactly where, and the cell is cut there (marching squares):
    /// straight block edges and crisp corners instead of a staircase of cells and rounded shading.
    /// </para>
    /// </summary>
    internal sealed class McLayer
    {
        /// <summary>Faces meeting at more than this angle (cos ~25 degrees) are different faces: the edge between them is cut.</summary>
        private const float CreaseCos = 0.9f;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly int[] polygon = new int[8];
        private readonly int[] corner = new int[4];
        private readonly int[] side = new int[4];
        private readonly bool[] joined = new bool[4];
        private GameObject root;
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private float[] linear = new float[0];
        /// <summary>Each grid point's surface direction, in the view of the camera Minecraft rendered from.</summary>
        private Vector3[] facing = new Vector3[0];
        /// <summary>Per grid edge (horizontal ones, then vertical ones), the vertex that ends the surface on either side of an edge crossing it; -2 = not looked for yet.</summary>
        private int[] crossing = new int[0];
        /// <summary>Per grid edge: the two surfaces meet there without a depth jump (two faces of one thing).</summary>
        private bool[] crease = new bool[0];
        private Texture2D color;
        private Texture2D glow;
        private byte[] glowPixels = new byte[0];
        private readonly List<RectInt> glowRects = new List<RectInt>();
        private Material material;
        private string materialKey;

        // the frame being built
        private McFrame frame;
        private Quaternion rotation;
        private float tanHalf, aspect, invW, invH, maxDepth, tolerance;
        private int gw, horizontalEdges;

        /// <summary>
        /// Made in the game itself, not at startup: Subnautica clears the scene on its way into a game (SceneCleaner
        /// destroys every root object without SceneCleanerPreserve), so the objects are rebuilt whenever they're gone.
        /// </summary>
        private void EnsureObjects()
        {
            if (root != null)
            {
                return;
            }

            root = new GameObject("SubnautiCraft world");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);
            root.AddComponent<SceneCleanerPreserve>();
            if (mesh == null)
            {
                mesh = new Mesh { name = "SubnautiCraft depth mesh", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontUnloadUnusedAsset };
                mesh.MarkDynamic();
            }

            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = true;
            // Subnautica's materials take their ambient light and reflections from the biome's sky: SkyApplier does that
            var sky = root.AddComponent<SkyApplier>();
            sky.renderers = new Renderer[] { meshRenderer };
            sky.anchorSky = Skies.Auto;
            sky.dynamic = true;
            materialKey = null;
        }

        public bool Visible
        {
            get => root != null && root.activeSelf;
            set
            {
                if (root == null)
                {
                    if (!value)
                    {
                        return;
                    }

                    EnsureObjects();
                }

                if (root.activeSelf != value)
                {
                    root.SetActive(value);
                }
            }
        }

        /// <summary>Subnautica lights Minecraft's blocks (Minecraft draws them unlit); otherwise Minecraft's own light shows.</summary>
        public static bool LitBySubnautica => Plugin.Lighting.Value != "Minecraft";

        /// <summary>Rebuild the mesh and texture from a new frame; <paramref name="emitters"/> are the light-giving blocks that glow.</summary>
        public void Show(McFrame f, List<Vector3Int> emitters)
        {
            EnsureObjects();
            EnsureTexture(f.Width, f.Height);
            color.LoadRawTextureData(f.Color);
            color.Apply(false);
            ShadowCastingMode shadows = Plugin.Shadows.Value ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            if (meshRenderer.shadowCastingMode != shadows)
            {
                meshRenderer.shadowCastingMode = shadows;
            }

            Begin(f);
            if (LitBySubnautica)
            {
                Glow(f, emitters);
            }

            EnsureMaterial();
            Build(f);
            frame = null;
        }

        private void EnsureTexture(int w, int h)
        {
            if (color == null || color.width != w || color.height != h)
            {
                if (color != null)
                {
                    Object.Destroy(color);
                }

                // Minecraft writes display (sRGB) colours into an ordinary RGBA8 target
                color = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    name = "SubnautiCraft world colour", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                materialKey = null;
            }

            color.filterMode = Plugin.SmoothTexture.Value ? FilterMode.Bilinear : FilterMode.Point;
        }

        private void EnsureMaterial()
        {
            string wanted = Plugin.ShaderName.Value;
            bool bySubnautica = LitBySubnautica;
            float strength = Plugin.GlowingBlocks.Value;
            string key = wanted + "|" + bySubnautica + "|" + strength;
            if (material != null && materialKey == key)
            {
                if (bySubnautica && material.shader.name == "MarmosetUBER")
                {
                    material.SetTexture("_Illum", glow != null ? (Texture)glow : Texture2D.blackTexture);
                }

                return;
            }

            Shader shader = Shader.Find(wanted) ?? Shader.Find("MarmosetUBER") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            if (material == null || material.shader != shader)
            {
                if (material != null)
                {
                    Object.Destroy(material);
                }

                material = new Material(shader) { name = "SubnautiCraft blocks", hideFlags = HideFlags.DontUnloadUnusedAsset };
                Plugin.Log.LogInfo($"drawing Minecraft with shader {shader.name}");
            }

            // Lit by Subnautica: Minecraft's unlit picture is the blocks' colour, and only light-giving blocks glow (their
            // pixels in the glow picture). Lit by Minecraft: its picture already holds its light, so most of it glows as is
            // and a little is left as colour for Subnautica's lights; all of it as colour would be lit twice (near black).
            Color diffuse = bySubnautica ? Color.white : new Color(0.2f, 0.2f, 0.2f, 1f);
            Texture glowMap = bySubnautica ? (glow != null ? (Texture)glow : Texture2D.blackTexture) : color;
            float glowStrength = bySubnautica ? strength : 0.8f;
            material.mainTexture = color;
            material.SetColor("_Color", Color.white);
            switch (shader.name)
            {
                case "MarmosetUBER":
                    // the game only compiled its glow together with a specular map (MARMO_EMISSION alone falls back to
                    // no glow at all): a black one, so no shine (the picture's alpha would otherwise read as gloss)
                    material.SetFloat("_Mode", 0f);
                    material.SetFloat("_ZWrite", 1f);
                    material.SetColor("_Color", diffuse);
                    material.EnableKeyword("MARMO_SPECMAP");
                    material.SetTexture("_SpecTex", Texture2D.blackTexture);
                    material.SetColor("_SpecColor", Color.black);
                    material.SetFloat("_SpecInt", 0f);
                    material.SetFloat("_Fresnel", 0f);
                    material.SetFloat("_Shininess", 1f);
                    material.EnableKeyword("MARMO_EMISSION");
                    material.SetTexture("_Illum", glowMap);
                    material.SetColor("_GlowColor", Color.white);
                    material.SetFloat("_GlowStrength", glowStrength);
                    material.SetFloat("_GlowStrengthNight", glowStrength);
                    material.SetFloat("_EmissionLM", 0f);
                    material.SetFloat("_EmissionLMNight", 0f);
                    break;
                case "Standard":
                    material.SetColor("_Color", diffuse);
                    material.SetFloat("_Glossiness", 0f);
                    material.SetFloat("_Metallic", 0f);
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    material.SetFloat("_SpecularHighlights", 0f);
                    material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
                    material.SetFloat("_GlossyReflections", 0f);
                    material.EnableKeyword("_EMISSION");
                    material.SetTexture("_EmissionMap", glowMap);
                    material.SetColor("_EmissionColor", Color.white * glowStrength);
                    break;
            }

            meshRenderer.sharedMaterial = material;
            materialKey = key;
        }

        private void Begin(McFrame f)
        {
            frame = f;
            tanHalf = Mathf.Tan(f.Fov * 0.5f * Mathf.Deg2Rad);
            aspect = (float)f.Width / f.Height;
            invW = 1f / f.Width;
            invH = 1f / f.Height;
            rotation = f.Rotation;
            maxDepth = f.Far * 0.98f;
            tolerance = Plugin.EdgeTolerance.Value;
        }

        /// <summary>
        /// The glow picture: Minecraft's own pixels where they show a light-giving block (torch, sea lantern, lava...),
        /// black elsewhere. Each such block is projected into the picture, and the pixels in that box whose surface
        /// lies inside the block are its.
        /// </summary>
        private void Glow(McFrame f, List<Vector3Int> emitters)
        {
            int w = f.Width, h = f.Height;
            bool changed = glowRects.Count > 0;
            if (glow == null || glow.width != w || glow.height != h)
            {
                if (glow != null)
                {
                    Object.Destroy(glow);
                }

                glow = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    name = "SubnautiCraft glow", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                glowPixels = new byte[w * h * 4];
                glowRects.Clear();
                materialKey = null;
                // a new texture is Unity's light grey until it gets pixels: all black first, or every block glows white
                changed = true;
            }

            glow.filterMode = color.filterMode;
            foreach (RectInt r in glowRects)
            {
                for (int y = r.yMin; y < r.yMax; y++)
                {
                    System.Array.Clear(glowPixels, (y * w + r.xMin) * 4, r.width * 4);
                }
            }

            glowRects.Clear();
            Quaternion toView = Quaternion.Inverse(rotation);
            Vector3 ex = toView * Vector3.right, ey = toView * Vector3.up, ez = toView * Vector3.forward;
            byte[] src = f.Color;
            float[] depth = f.Depth;
            for (int e = 0; emitters != null && e < emitters.Count; e++)
            {
                Vector3Int b = emitters[e];
                Vector3 centre = toView * (Coords.BlockCenter(b.x, b.y, b.z) - f.Position);
                if (!ScreenBox(centre, ex, ey, ez, w, h, out RectInt r))
                {
                    continue;
                }

                for (int y = r.yMin; y < r.yMax; y++)
                {
                    float ry = ((y + 0.5f) * invH * 2f - 1f) * tanHalf;
                    for (int x = r.xMin; x < r.xMax; x++)
                    {
                        int i = y * w + x;
                        float z = Linear(depth[i]);
                        if (z <= 0f)
                        {
                            continue;
                        }

                        // a little past the surface along the view: inside the block the surface belongs to
                        z += 0.02f;
                        Vector3 d = new Vector3(((x + 0.5f) * invW * 2f - 1f) * tanHalf * aspect * z, ry * z, z) - centre;
                        if (Mathf.Abs(Vector3.Dot(d, ex)) <= 0.5f && Mathf.Abs(Vector3.Dot(d, ey)) <= 0.5f && Mathf.Abs(Vector3.Dot(d, ez)) <= 0.5f)
                        {
                            int p = i * 4;
                            glowPixels[p] = src[p];
                            glowPixels[p + 1] = src[p + 1];
                            glowPixels[p + 2] = src[p + 2];
                            glowPixels[p + 3] = 255;
                        }
                    }
                }

                glowRects.Add(r);
                changed = true;
            }

            if (changed)
            {
                glow.LoadRawTextureData(glowPixels);
                glow.Apply(false);
            }
        }

        /// <summary>Where a block (centre and axes in view space) falls in the picture; false if not in front of the camera.</summary>
        private bool ScreenBox(Vector3 centre, Vector3 ex, Vector3 ey, Vector3 ez, int w, int h, out RectInt r)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int behind = 0;
            for (int c = 0; c < 8; c++)
            {
                Vector3 v = centre + ex * ((c & 1) - 0.5f) + ey * (((c >> 1) & 1) - 0.5f) + ez * (((c >> 2) & 1) - 0.5f);
                if (v.z < frame.Near)
                {
                    behind++;
                    continue;
                }

                float sx = (v.x / (v.z * tanHalf * aspect) + 1f) * 0.5f * w, sy = (v.y / (v.z * tanHalf) + 1f) * 0.5f * h;
                minX = Mathf.Min(minX, sx);
                maxX = Mathf.Max(maxX, sx);
                minY = Mathf.Min(minY, sy);
                maxY = Mathf.Max(maxY, sy);
            }

            if (behind == 8)
            {
                r = default;
                return false;
            }

            if (behind > 0)
            {
                // the camera is at the block: any of the picture can be it
                minX = minY = 0f;
                maxX = w;
                maxY = h;
            }

            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX) - 1, 0, w), x1 = Mathf.Clamp(Mathf.CeilToInt(maxX) + 1, 0, w);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY) - 1, 0, h), y1 = Mathf.Clamp(Mathf.CeilToInt(maxY) + 1, 0, h);
            r = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return r.width > 0 && r.height > 0;
        }

        private void Build(McFrame f)
        {
            gw = f.GridW;
            int gh = f.GridH, n = gw * gh;
            if (linear.Length != n)
            {
                linear = new float[n];
                facing = new Vector3[n];
            }

            horizontalEdges = (gw - 1) * gh;
            int edges = horizontalEdges + gw * (gh - 1);
            if (crossing.Length != edges * 2)
            {
                crossing = new int[edges * 2];
                crease = new bool[edges];
            }

            for (int i = 0; i < crossing.Length; i++)
            {
                crossing[i] = -2;
            }

            // vertices are relative to the camera Minecraft rendered from, so precision stays good and SkyApplier picks the biome there
            root.transform.SetPositionAndRotation(f.Position, Quaternion.identity);
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            triangles.Clear();

            // one vertex per grid point (vertex i = grid point i), at the centre of its pixel
            for (int gy = 0; gy < gh; gy++)
            {
                int y = Mathf.Min(gy * f.Step, f.Height - 1);
                for (int gx = 0; gx < gw; gx++)
                {
                    int i = gy * gw + gx;
                    int x = Mathf.Min(gx * f.Step, f.Width - 1);
                    float z = Linear(f.GridDepth[i]);
                    linear[i] = z;
                    facing[i] = z > 0f ? Facing(x, y, z) : Vector3.back;
                    AddVertex(x + 0.5f, y + 0.5f, z, x + 0.5f, y + 0.5f, facing[i]);
                }
            }

            for (int gy = 0; gy + 1 < gh; gy++)
            {
                for (int gx = 0; gx + 1 < gw; gx++)
                {
                    Cell(gx, gy);
                }
            }

            mesh.Clear(true);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
        }

        /// <summary>Distance along the view axis, or -1 where Minecraft drew nothing (or only its far plane).</summary>
        private float Linear(float raw)
        {
            float z = frame.Linear(raw);
            return z > maxDepth ? -1f : z;
        }

        /// <summary>Pixel (x, y) at view depth z, in the view of the camera Minecraft rendered from.</summary>
        private Vector3 ViewPoint(int x, int y, float z)
        {
            return new Vector3(((x + 0.5f) * invW * 2f - 1f) * tanHalf * aspect * z, ((y + 0.5f) * invH * 2f - 1f) * tanHalf * z, z);
        }

        /// <summary>
        /// Which way the surface at a pixel faces (view space, towards the camera), from its neighbours' depths: on each
        /// axis the neighbour on the same face (the smaller step), so a pixel at a block's edge takes one face's
        /// direction, not a blend of both.
        /// </summary>
        private bool PixelFacing(int x, int y, float z, out Vector3 n)
        {
            McFrame f = frame;
            int w = f.Width, h = f.Height, i = y * w + x;
            Vector3 p = ViewPoint(x, y, z);
            float left = x > 0 ? Linear(f.Depth[i - 1]) : -1f, right = x + 1 < w ? Linear(f.Depth[i + 1]) : -1f;
            float down = y > 0 ? Linear(f.Depth[i - w]) : -1f, up = y + 1 < h ? Linear(f.Depth[i + w]) : -1f;
            bool l = Near(z, left), r = Near(z, right), d = Near(z, down), u = Near(z, up);
            Vector3 across, along;
            if (r && (!l || Mathf.Abs(right - z) <= Mathf.Abs(z - left)))
            {
                across = ViewPoint(x + 1, y, right) - p;
            }
            else if (l)
            {
                across = p - ViewPoint(x - 1, y, left);
            }
            else
            {
                n = Vector3.back;
                return false;
            }

            if (u && (!d || Mathf.Abs(up - z) <= Mathf.Abs(z - down)))
            {
                along = ViewPoint(x, y + 1, up) - p;
            }
            else if (d)
            {
                along = p - ViewPoint(x, y - 1, down);
            }
            else
            {
                n = Vector3.back;
                return false;
            }

            n = Vector3.Cross(along, across);
            float length = n.magnitude;
            if (length < 1e-9f)
            {
                n = Vector3.back;
                return false;
            }

            n /= length;
            if (Vector3.Dot(n, p) > 0f)
            {
                n = -n;
            }

            return true;
        }

        /// <summary>A grid point's facing; facing the camera where it can't be told (a lone pixel).</summary>
        private Vector3 Facing(int x, int y, float z)
        {
            return PixelFacing(x, y, z, out Vector3 n) ? n : -ViewPoint(x, y, 1f).normalized;
        }

        /// <summary>A vertex at picture position (x, y) in pixels (pixel i spans i..i+1), z metres out, textured from (u, v) in pixels, facing n (view space).</summary>
        private int AddVertex(float x, float y, float z, float u, float v, Vector3 n)
        {
            Vector3 ray = new Vector3((x * invW * 2f - 1f) * tanHalf * aspect, (y * invH * 2f - 1f) * tanHalf, 1f);
            // rows are bottom-up in Minecraft's picture and in Unity textures alike, so the picture maps 1:1
            vertices.Add(z > 0f ? rotation * (ray * z) : Vector3.zero);
            normals.Add(rotation * n);
            uvs.Add(new Vector2(u * invW, v * invH));
            return vertices.Count - 1;
        }

        /// <summary>Same object: both drawn and no depth jump between them (a jump is an edge between objects).</summary>
        private bool Near(float a, float b)
        {
            return a > 0f && b > 0f && Mathf.Abs(a - b) <= tolerance * Mathf.Min(a, b) + 0.05f;
        }

        /// <summary>Same face: the same object, facing the same way.</summary>
        private bool Same(int i, int j)
        {
            return Near(linear[i], linear[j]) && Vector3.Dot(facing[i], facing[j]) > CreaseCos;
        }

        private void Cell(int gx, int gy)
        {
            int a = gy * gw + gx; // bottom left
            int b = a + gw;       // top left
            int c = b + 1;        // top right
            int d = a + 1;        // bottom right
            // the corners clockwise as the camera sees them (Unity's front faces), and the side from each to the next
            corner[0] = a;
            corner[1] = b;
            corner[2] = c;
            corner[3] = d;
            side[0] = horizontalEdges + a;                // a-b, the left side
            side[1] = (gy + 1) * (gw - 1) + gx;           // b-c, the top
            side[2] = horizontalEdges + d;                // d-c, the right side
            side[3] = gy * (gw - 1) + gx;                 // a-d, the bottom
            int cuts = 0;
            for (int k = 0; k < 4; k++)
            {
                joined[k] = Same(corner[k], corner[(k + 1) & 3]);
                cuts += joined[k] ? 0 : 1;
            }

            if (cuts < 2)
            {
                Whole(a, b, c, d);
                return;
            }

            // each run of corners joined along the cell's sides is one face: those corners, and the points where an
            // edge cuts the sides at either end of the run
            for (int start = 0; start < 4; start++)
            {
                int before = (start + 3) & 3;
                if (joined[before] || linear[corner[start]] <= 0f)
                {
                    continue; // not the first corner of a run, or nothing drawn there
                }

                int count = 0;
                polygon[count++] = Crossing(side[before], corner[start]);
                int k = start;
                while (true)
                {
                    polygon[count++] = corner[k];
                    if (!joined[k])
                    {
                        break;
                    }

                    k = (k + 1) & 3;
                }

                polygon[count++] = Crossing(side[k], corner[k]);
                Fan(count);
            }

            // three or four faces of one thing meeting inside the cell (a block's corner): the runs leave the middle
            // between their cuts open, so close it
            if (cuts >= 3)
            {
                int count = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (!joined[k])
                    {
                        if (!crease[side[k]] || linear[corner[k]] <= 0f)
                        {
                            return; // an edge in front of something further away: the gap is real
                        }

                        polygon[count++] = Crossing(side[k], corner[k]);
                    }
                }

                Fan(count);
            }
        }

        private void Fan(int count)
        {
            for (int t = 1; t + 1 < count; t++)
            {
                Add(polygon[0], polygon[t], polygon[t + 1]);
            }
        }

        /// <summary>A cell with no edge through it (or a muddle): two triangles, or one where only three corners agree.</summary>
        private void Whole(int a, int b, int c, int d)
        {
            if (Joined(a, b, c))
            {
                Add(a, b, c);
                if (Joined(a, c, d))
                {
                    Add(a, c, d);
                }
            }
            else if (Joined(a, c, d))
            {
                Add(a, c, d);
            }
            else if (Joined(a, b, d))
            {
                Add(a, b, d);
            }
            else if (Joined(b, c, d))
            {
                Add(b, c, d);
            }
        }

        /// <summary>
        /// Where the face at grid point <paramref name="from"/> ends along grid edge <paramref name="edge"/>: walks the
        /// edge's pixels from either end while they stay on the same face. The vertex sits on the boundary between the
        /// last pixel of the face and the next, textured from the centre of that last pixel (so the smoothed picture
        /// doesn't smear the other side, or the empty black around a block, onto it).
        /// </summary>
        private int Crossing(int edge, int from)
        {
            if (crossing[edge * 2] == -2)
            {
                Walk(edge);
            }

            int g0 = edge < horizontalEdges ? edge / (gw - 1) * gw + edge % (gw - 1) : edge - horizontalEdges;
            return crossing[edge * 2 + (from == g0 ? 0 : 1)];
        }

        private void Walk(int edge)
        {
            McFrame f = frame;
            bool vertical = edge >= horizontalEdges;
            int g0 = vertical ? edge - horizontalEdges : edge / (gw - 1) * gw + edge % (gw - 1);
            int g1 = vertical ? g0 + gw : g0 + 1;
            int x0 = Mathf.Min(g0 % gw * f.Step, f.Width - 1), y0 = Mathf.Min(g0 / gw * f.Step, f.Height - 1);
            int x1 = Mathf.Min(g1 % gw * f.Step, f.Width - 1), y1 = Mathf.Min(g1 / gw * f.Step, f.Height - 1);
            int length = vertical ? y1 - y0 : x1 - x0;
            int stride = vertical ? f.Width : 1;
            int first = y0 * f.Width + x0;

            // from the first end: k0 = the last pixel on its face
            int k0 = 0;
            float z0 = linear[g0];
            if (z0 > 0f)
            {
                for (int k = 1; k < length; k++)
                {
                    float z = Linear(f.Depth[first + k * stride]);
                    if (!Near(z0, z) || !SameFacing(vertical ? x0 : x0 + k, vertical ? y0 + k : y0, z, facing[g0]))
                    {
                        break;
                    }

                    z0 = z;
                    k0 = k;
                }
            }

            // from the other end: k1 = the last pixel on its face
            int k1 = length;
            float z1 = linear[g1];
            if (z1 > 0f)
            {
                for (int k = length - 1; k > 0; k--)
                {
                    float z = Linear(f.Depth[first + k * stride]);
                    if (!Near(z1, z) || !SameFacing(vertical ? x0 : x0 + k, vertical ? y0 + k : y0, z, facing[g1]))
                    {
                        break;
                    }

                    z1 = z;
                    k1 = k;
                }
            }

            // boundaries in pixels from the first end. Two faces of one thing meet: one boundary (a pixel right on the
            // fold belongs to neither, and leaving it out would open a crack). An object in front of another: a gap
            // between them is a sliver of a third thing, too thin to draw
            float end0 = k0 + 1f, end1 = k1;
            bool fold = linear[g0] > 0f && linear[g1] > 0f && Near(z0, z1);
            crease[edge] = fold;
            if (fold || end1 < end0)
            {
                end0 = end1 = (end0 + end1) * 0.5f;
            }

            if (fold)
            {
                z0 = z1 = (z0 + z1) * 0.5f; // one point for both faces: no crack along the fold
            }

            crossing[edge * 2] = linear[g0] > 0f ? EdgeVertex(vertical, x0, y0, end0, Mathf.Min(k0 + 0.5f, end0), z0, facing[g0]) : -1;
            crossing[edge * 2 + 1] = linear[g1] > 0f ? EdgeVertex(vertical, x0, y0, end1, Mathf.Max(k1 + 0.5f, end1), z1, facing[g1]) : -1;
        }

        /// <summary>The pixel's surface faces about the same way as <paramref name="n"/> (or its facing can't be told).</summary>
        private bool SameFacing(int x, int y, float z, Vector3 n)
        {
            return !PixelFacing(x, y, z, out Vector3 here) || Vector3.Dot(here, n) > CreaseCos;
        }

        private int EdgeVertex(bool vertical, int x0, int y0, float at, float textureAt, float z, Vector3 n)
        {
            return vertical
                ? AddVertex(x0 + 0.5f, y0 + 0.5f + at - 0.5f, z, x0 + 0.5f, y0 + 0.5f + textureAt - 0.5f, n)
                : AddVertex(x0 + 0.5f + at - 0.5f, y0 + 0.5f, z, x0 + 0.5f + textureAt - 0.5f, y0 + 0.5f, n);
        }

        private bool Joined(int i, int j, int k)
        {
            float a = linear[i], b = linear[j], c = linear[k];
            if (a <= 0f || b <= 0f || c <= 0f)
            {
                return false;
            }

            float min = Mathf.Min(a, Mathf.Min(b, c)), max = Mathf.Max(a, Mathf.Max(b, c));
            return max - min <= tolerance * min + 0.05f;
        }

        private void Add(int i, int j, int k)
        {
            triangles.Add(i);
            triangles.Add(j);
            triangles.Add(k);
        }
    }
}
