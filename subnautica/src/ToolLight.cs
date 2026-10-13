using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// The light of the Subnautica tool in Steve's hand: a spotlight from his eyes where he looks, like the diver's
    /// own flashlight (Minecraft says what he holds and whether its battery has charge left).
    /// </summary>
    internal sealed class ToolLight
    {
        private Light light;
        private string shown;

        public void Update(string tool, Vector3 eye, Quaternion look)
        {
            if (string.IsNullOrEmpty(tool) || (tool != "flashlight" && tool != "seaglide"))
            {
                if (light != null && light.enabled)
                {
                    light.enabled = false;
                }

                shown = null;
                return;
            }

            if (light == null)
            {
                // made again whenever Subnautica's SceneCleaner took it (leaving a game to the menu)
                var go = new GameObject("SubnautiCraft tool light");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<SceneCleanerPreserve>();
                light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                shown = null;
            }

            if (shown != tool)
            {
                shown = tool;
                bool flashlight = tool == "flashlight";
                light.range = flashlight ? 45f : 30f;
                light.spotAngle = flashlight ? 70f : 85f;
                light.intensity = flashlight ? 1.6f : 1.1f;
                light.color = flashlight ? new Color(1f, 0.96f, 0.88f) : new Color(0.88f, 0.96f, 1f);
            }

            light.enabled = true;
            // a hand's width in front of the eyes, so the beam starts where the tool is
            light.transform.SetPositionAndRotation(eye + look * new Vector3(0.15f, -0.15f, 0.2f), look);
        }
    }
}
