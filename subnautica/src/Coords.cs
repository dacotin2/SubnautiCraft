using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Subnautica (Unity) is left-handed: x right, y up, z forward. Minecraft is right-handed: x east, y up, z south.
    /// Mirroring x maps one onto the other with 1 metre = 1 block and no offset (the Fabric mod stretches the
    /// overworld to y -2032..256, deep enough for the whole ocean). Under that mirror Unity's Euler angles carry over
    /// unchanged: Minecraft yaw = Unity yaw, pitch = pitch (positive = down), roll = roll.
    /// </summary>
    internal static class Coords
    {
        public static double[] ToMc(Vector3 u) => new double[] { -u.x, u.y, u.z };

        public static Vector3 FromMc(double x, double y, double z) => new Vector3((float)-x, (float)y, (float)z);

        /// <summary>Centre of Minecraft block (bx, by, bz) in Subnautica's world.</summary>
        public static Vector3 BlockCenter(int bx, int by, int bz) => new Vector3(-(bx + 0.5f), by + 0.5f, bz + 0.5f);

        /// <summary>The Minecraft block column containing Subnautica position (x, z).</summary>
        public static int BlockX(float ux) => Mathf.FloorToInt(-ux);

        public static int BlockZ(float uz) => Mathf.FloorToInt(uz);

        /// <summary>Minecraft yaw, pitch and roll (degrees) of a Subnautica rotation.</summary>
        public static void Angles(Quaternion q, out float yaw, out float pitch, out float roll)
        {
            Vector3 e = q.eulerAngles;
            yaw = Wrap(e.y);
            pitch = Wrap(e.x);
            roll = Wrap(e.z);
        }

        /// <summary>The Subnautica rotation for Minecraft yaw, pitch and roll (the inverse of <see cref="Angles"/>).</summary>
        public static Quaternion Rotation(float yaw, float pitch, float roll) => Quaternion.Euler(pitch, yaw, roll);

        private static float Wrap(float a)
        {
            a %= 360f;
            if (a >= 180f)
            {
                a -= 360f;
            }
            else if (a < -180f)
            {
                a += 360f;
            }

            return a;
        }
    }
}
