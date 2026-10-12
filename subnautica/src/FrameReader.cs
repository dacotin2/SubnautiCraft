using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>One Minecraft frame, copied out of shared memory and checked for tearing.</summary>
    internal sealed class McFrame
    {
        public int Width;
        public int Height;
        public float Near;
        public float Far;
        public float Fov;
        public int Flags;
        /// <summary>Where Minecraft's camera was for this picture, in Subnautica's world.</summary>
        public Vector3 Position;
        public Quaternion Rotation;
        public long HostFrame;
        public byte[] Color = new byte[0];
        public byte[] Overlay = new byte[0];
        /// <summary>Raw depth of every pixel (Width x Height), rows bottom-up like the picture.</summary>
        public float[] Depth = new float[0];
        /// <summary>Raw depth at every <see cref="Step"/>-th pixel: (GridW x GridH), rows bottom-up like the picture.</summary>
        public float[] GridDepth = new float[0];
        public int Step;
        public int GridW;
        public int GridH;

        public bool ReversedZ => (Flags & 4) != 0;

        /// <summary>Distance along the view axis in metres, or -1 where Minecraft drew nothing.</summary>
        public float Linear(float d)
        {
            float n = Near, f = Far;
            if (ReversedZ)
            {
                return d <= 0f ? -1f : n * f / (n + d * (f - n));
            }

            return d >= 1f ? -1f : n * f / (f - d * (f - n));
        }
    }

    /// <summary>
    /// Reads the frames the Fabric mod publishes in the named shared memory "Local\SubnautiCraftFrame".
    /// Layout (little-endian): header 4096 bytes, then slots of world colour RGBA8, world depth float32 and overlay RGBA8.
    /// <code>
    ///   0 int magic "SNCF"  4 int version  8 int header bytes  12 int slot count  16 long slot stride
    ///   24 int max width  28 int max height  32 long publish counter  40 int latest slot  44 int Minecraft pid
    ///   256 + 128 * i: slot i: +0 long seq (odd while written)  +8 long Minecraft frame  +16 long host frame
    ///     +24 int width  +28 int height  +32 float near  +36 float far  +40 float vertical fov
    ///     +44 int flags (1 depth 0..1, 2 rows bottom-up, 4 reversed Z)  +48 double x, y, z  +72 float yaw, pitch, roll
    /// </code>
    /// </summary>
    internal sealed unsafe class FrameReader : IDisposable
    {
        private string name = "Local\\SubnautiCraftFrame";
        private const int Magic = 0x46434E53;
        private const uint FileMapRead = 0x0004;

        private IntPtr mapping;
        private byte* view;
        private long lastPublish = -1;
        private float retryAt;

        public bool Open => view != null;

        /// <summary>The shared memory Minecraft announced in its hello (one per port).</summary>
        public void UseName(string announced)
        {
            if (!string.IsNullOrEmpty(announced) && announced != name)
            {
                Dispose();
                name = announced;
                retryAt = 0f;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        private static extern bool UnmapViewOfFile(IntPtr address);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private bool Ensure()
        {
            if (view != null)
            {
                return true;
            }

            if (Time.unscaledTime < retryAt)
            {
                return false;
            }

            retryAt = Time.unscaledTime + 1f;
            mapping = OpenFileMappingW(FileMapRead, false, name);
            if (mapping == IntPtr.Zero)
            {
                return false; // Minecraft hasn't exported a frame yet
            }

            view = (byte*)MapViewOfFile(mapping, FileMapRead, 0, 0, UIntPtr.Zero);
            if (view == null || *(int*)view != Magic)
            {
                Plugin.Log.LogWarning("shared memory found but it isn't SubnautiCraft's frame export");
                Dispose();
                return false;
            }

            Plugin.Log.LogInfo("reading Minecraft's frames from shared memory");
            return true;
        }

        /// <summary>Copies the newest published frame into <paramref name="frame"/>; false if there is none or it tore.</summary>
        public bool TryRead(McFrame frame, int step)
        {
            if (!Ensure())
            {
                return false;
            }

            long publish = *(long*)(view + 32);
            int slot = *(int*)(view + 40);
            if (publish == lastPublish || slot < 0 || slot >= *(int*)(view + 12))
            {
                return false;
            }

            byte* desc = view + 256 + 128 * slot;
            long seq = *(long*)desc;
            if ((seq & 1) != 0)
            {
                return false;
            }

            int w = *(int*)(desc + 24), h = *(int*)(desc + 28);
            if (w <= 0 || h <= 0 || w > *(int*)(view + 24) || h > *(int*)(view + 28))
            {
                return false;
            }

            int header = *(int*)(view + 8);
            long stride = *(long*)(view + 16);
            byte* data = view + header + stride * slot;
            int layer = w * h * 4;

            frame.Width = w;
            frame.Height = h;
            frame.HostFrame = *(long*)(desc + 16);
            frame.Near = *(float*)(desc + 32);
            frame.Far = *(float*)(desc + 36);
            frame.Fov = *(float*)(desc + 40);
            frame.Flags = *(int*)(desc + 44);
            frame.Position = Coords.FromMc(*(double*)(desc + 48), *(double*)(desc + 56), *(double*)(desc + 64));
            frame.Rotation = Coords.Rotation(*(float*)(desc + 72), *(float*)(desc + 76), *(float*)(desc + 80));

            if (frame.Color.Length != layer)
            {
                frame.Color = new byte[layer];
                frame.Overlay = new byte[layer];
            }

            Marshal.Copy((IntPtr)data, frame.Color, 0, layer);
            Marshal.Copy((IntPtr)(data + 2L * layer), frame.Overlay, 0, layer);
            if (frame.Depth.Length != w * h)
            {
                frame.Depth = new float[w * h];
            }

            // all of it: the mesh finds exactly where an edge falls between its grid points
            Marshal.Copy((IntPtr)(data + layer), frame.Depth, 0, w * h);

            // the depth mesh only needs every step-th pixel (always including the last row and column)
            step = Math.Max(1, step);
            int gw = (w - 1 + step - 1) / step + 1, gh = (h - 1 + step - 1) / step + 1;
            if (frame.GridDepth.Length != gw * gh)
            {
                frame.GridDepth = new float[gw * gh];
            }

            float[] depth = frame.Depth;
            for (int gy = 0; gy < gh; gy++)
            {
                int row = Math.Min(gy * step, h - 1) * w;
                for (int gx = 0; gx < gw; gx++)
                {
                    frame.GridDepth[gy * gw + gx] = depth[row + Math.Min(gx * step, w - 1)];
                }
            }

            frame.Step = step;
            frame.GridW = gw;
            frame.GridH = gh;

            // Minecraft started rewriting this slot while we copied: drop the frame (the next one is moments away)
            if (*(long*)desc != seq)
            {
                return false;
            }

            lastPublish = publish;
            return true;
        }

        public void Dispose()
        {
            if (view != null)
            {
                UnmapViewOfFile((IntPtr)view);
                view = null;
            }

            if (mapping != IntPtr.Zero)
            {
                CloseHandle(mapping);
                mapping = IntPtr.Zero;
            }
        }
    }
}
