using System.Globalization;
using System.Text;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Steve mode: Subnautica's keyboard and mouse go to Minecraft, which replays them as its own (SkyCraft's
    /// InputBridge). Keys travel as SDL scancodes (USB HID usage ids). The look direction is Subnautica's camera, which
    /// Subnautica turns itself with no lag. While a Minecraft screen (inventory, chat, pause) is open, the real cursor
    /// is free and its position goes to Minecraft.
    /// </summary>
    internal sealed class InputForwarder
    {
        private static readonly KeyCode[] Codes;
        private static readonly int[] Scancodes;

        static InputForwarder()
        {
            var codes = new System.Collections.Generic.List<KeyCode>();
            var scans = new System.Collections.Generic.List<int>();
            void Map(KeyCode k, int sc)
            {
                codes.Add(k);
                scans.Add(sc);
            }

            for (int i = 0; i < 26; i++)
            {
                Map(KeyCode.A + i, 4 + i);
            }

            for (int i = 0; i < 9; i++)
            {
                Map(KeyCode.Alpha1 + i, 30 + i);
            }

            Map(KeyCode.Alpha0, 39);
            Map(KeyCode.Return, 40);
            Map(KeyCode.Backspace, 42);
            Map(KeyCode.Space, 44);
            Map(KeyCode.Minus, 45);
            Map(KeyCode.Equals, 46);
            Map(KeyCode.LeftBracket, 47);
            Map(KeyCode.RightBracket, 48);
            Map(KeyCode.Backslash, 49);
            Map(KeyCode.Semicolon, 51);
            Map(KeyCode.Quote, 52);
            Map(KeyCode.BackQuote, 53);
            Map(KeyCode.Comma, 54);
            Map(KeyCode.Period, 55);
            Map(KeyCode.Slash, 56);
            for (int i = 0; i < 12; i++)
            {
                if (KeyCode.F1 + i != KeyCode.F7)
                {
                    Map(KeyCode.F1 + i, 58 + i); // F7 is SubnautiCraft's own toggle
                }
            }

            Map(KeyCode.Insert, 73);
            Map(KeyCode.Home, 74);
            Map(KeyCode.PageUp, 75);
            Map(KeyCode.Delete, 76);
            Map(KeyCode.End, 77);
            Map(KeyCode.PageDown, 78);
            Map(KeyCode.RightArrow, 79);
            Map(KeyCode.LeftArrow, 80);
            Map(KeyCode.DownArrow, 81);
            Map(KeyCode.UpArrow, 82);
            Map(KeyCode.LeftControl, 224);
            Map(KeyCode.LeftShift, 225);
            Map(KeyCode.LeftAlt, 226);
            Map(KeyCode.RightControl, 228);
            Map(KeyCode.RightShift, 229);
            Map(KeyCode.RightAlt, 230);
            Codes = codes.ToArray();
            Scancodes = scans.ToArray();
        }

        private const int EscapeScancode = 41;
        private readonly bool[] down = new bool[Codes.Length];
        private readonly bool[] buttons = new bool[3];
        private bool escapeDown;
        /// <summary>The right click now held went to Subnautica's hand, so its release stays there too.</summary>
        private bool useWentToSubnautica;

        /// <summary>
        /// SkyCraft's "use" rule: a right click on something Subnautica's hand can use (a limestone outcrop, an item to
        /// pick up, a hatch, a door) goes to Subnautica; on anything else it's Minecraft's (place a block). Returns true
        /// when Subnautica took it.
        /// </summary>
        public System.Func<bool> TrySubnauticaUse;
        private readonly StringBuilder sb = new StringBuilder(256);

        /// <summary>Forget what we think is held (Minecraft was told to let go of everything).</summary>
        public void Reset()
        {
            System.Array.Clear(down, 0, down.Length);
            System.Array.Clear(buttons, 0, buttons.Length);
            escapeDown = false;
            useWentToSubnautica = false;
        }

        /// <summary>This frame's input for Minecraft as one "in" message.</summary>
        public string Build(Transform camera, bool mcScreenOpen, int mcWidth, int mcHeight)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            Coords.Angles(camera.rotation, out float yaw, out float pitch, out _);
            sb.Clear();
            sb.Append("{\"t\":\"in\",\"look\":[").Append(yaw.ToString("F3", inv)).Append(',').Append(pitch.ToString("F3", inv)).Append(']');

            int keys = 0;
            for (int i = 0; i < Codes.Length; i++)
            {
                bool now = Input.GetKey(Codes[i]);
                if (now != down[i])
                {
                    down[i] = now;
                    AppendPair(ref keys, "k", Scancodes[i], now);
                }
            }

            // Escape closes Minecraft's screens; with none open it stays Subnautica's (its pause menu)
            bool escape = mcScreenOpen && Input.GetKey(KeyCode.Escape);
            if (escape != escapeDown)
            {
                escapeDown = escape;
                AppendPair(ref keys, "k", EscapeScancode, escape);
            }

            if (keys > 0)
            {
                sb.Append(']');
            }

            int clicks = 0;
            int[] sdlButton = { 1, 3, 2 }; // Unity left, right, middle -> SDL left, right, middle
            for (int b = 0; b < 3; b++)
            {
                bool now = Input.GetMouseButton(b);
                if (now != buttons[b])
                {
                    buttons[b] = now;
                    if (b == 1 && now && !mcScreenOpen && TrySubnauticaUse != null && TrySubnauticaUse())
                    {
                        useWentToSubnautica = true;
                        continue;
                    }

                    if (b == 1 && !now && useWentToSubnautica)
                    {
                        useWentToSubnautica = false;
                        continue;
                    }

                    AppendPair(ref clicks, "b", sdlButton[b], now);
                }
            }

            if (clicks > 0)
            {
                sb.Append(']');
            }

            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                sb.Append(",\"w\":").Append(wheel.ToString("F2", inv));
            }

            if (mcScreenOpen)
            {
                Vector3 m = Input.mousePosition;
                float x = Mathf.Clamp01(m.x / Screen.width) * mcWidth, y = Mathf.Clamp01(1f - m.y / Screen.height) * mcHeight;
                sb.Append(",\"c\":[").Append(x.ToString("F1", inv)).Append(',').Append(y.ToString("F1", inv)).Append(']');
                string typed = Input.inputString;
                if (!string.IsNullOrEmpty(typed))
                {
                    var text = new StringBuilder();
                    foreach (char ch in typed)
                    {
                        if (ch < 32)
                        {
                            continue; // Enter and Backspace go as keys
                        }

                        if (ch == '"' || ch == '\\')
                        {
                            text.Append('\\');
                        }

                        text.Append(ch);
                    }

                    if (text.Length > 0)
                    {
                        sb.Append(",\"txt\":\"").Append(text).Append('"');
                    }
                }
            }

            return sb.Append('}').ToString();
        }

        private void AppendPair(ref int count, string name, int code, bool isDown)
        {
            sb.Append(count++ == 0 ? ",\"" + name + "\":[" : ",");
            sb.Append('[').Append(code).Append(',').Append(isDown ? "true" : "false").Append(']');
        }
    }
}
