// Water's Edition (a GameNative.dll patch) has its own battle camera, hooked into the battle background routine: it
// eases the scroll and zoom toward their targets and writes them back each frame - over the co-op camera's. It offers a
// hand-off for co-op cameras: a block tagged "WATERCOOPCAM-v1" in its data section (no fixed address: found by the
// tag), version 1, then the RVAs of its Camera setting (byte: 0 Vanilla, 1 Water, 2 Co-op) and of a "co-op component
// active" byte. Its camera stands down while the setting is Co-op and that byte is 1; the setting's Co-op choice is
// greyed out until the byte is set.
//
// The mod sets the byte while the co-op camera is enabled, and in battles with two or more players a Water setting
// becomes Co-op (our camera wins), going back to Water once fewer than two play. Vanilla is left alone.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EterniaCoop
{
    static class WaterCompat
    {
        const string Tag = "WATERCOOPCAM-v1";
        const byte ModeWater = 1, ModeCoop = 2;
        static bool searched;
        static IntPtr mode, active;   // zero when Water's Edition isn't installed
        static bool switched;         // we changed Water's setting from Water to Co-op

        public static bool Present { get { return active != IntPtr.Zero; } }

        static void Find()
        {
            searched = true;
            try
            {
                long b = Native.Base;
                int pe = Marshal.ReadInt32(new IntPtr(b + 0x3C));
                int sections = Marshal.ReadInt16(new IntPtr(b + pe + 6));
                int optSize = Marshal.ReadInt16(new IntPtr(b + pe + 20));
                int imageSize = Marshal.ReadInt32(new IntPtr(b + pe + 24 + 56));
                long table = b + pe + 24 + optSize;
                byte[] tag = Encoding.ASCII.GetBytes(Tag + "\0");
                for (int s = 0; s < sections; s++)
                {
                    long h = table + s * 40;
                    int va = Marshal.ReadInt32(new IntPtr(h + 12)), size = Marshal.ReadInt32(new IntPtr(h + 8));
                    uint flags = (uint)Marshal.ReadInt32(new IntPtr(h + 36));
                    if ((flags & 0x40000000) == 0 || (flags & 0x20000000) != 0 || size <= 0) continue;   // readable data only
                    var data = new byte[size];
                    Marshal.Copy(new IntPtr(b + va), data, 0, size);
                    for (int i = 0; i + 32 <= size; i += 16)   // the block is 16-byte aligned
                    {
                        int k = 0;
                        while (k < tag.Length && data[i + k] == tag[k]) k++;
                        if (k < tag.Length) continue;
                        int version = BitConverter.ToInt32(data, i + 16);
                        int modeRva = BitConverter.ToInt32(data, i + 20), activeRva = BitConverter.ToInt32(data, i + 24);
                        if (version != 1 || modeRva <= 0 || modeRva >= imageSize || activeRva <= 0 || activeRva >= imageSize)
                        {
                            Plugin.Log.LogWarning("Water's Edition camera hand-off: unknown layout (version " + version + "), left alone.");
                            return;
                        }
                        mode = Native.At(modeRva);
                        active = Native.At(activeRva);
                        Plugin.Log.LogMessage("Water's Edition found: its battle camera hands over to the co-op camera.");
                        return;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Water's Edition camera hand-off: " + e.Message); }
        }

        // Every frame (main thread). coopPlayers: two or more players are playing.
        public static void Update(bool coopPlayers)
        {
            if (Native.Base == 0) return;
            if (!searched) Find();
            if (!Present) return;
            bool ours = Settings.CoopCamera.Value && Features.Camera;
            Marshal.WriteByte(active, (byte)(ours ? 1 : 0));
            byte m = Marshal.ReadByte(mode);
            if (ours && coopPlayers && m == ModeWater)
            {
                Marshal.WriteByte(mode, ModeCoop);
                switched = true;
                Plugin.Log.LogMessage("Water's Edition camera set to Co-op while two or more play.");
            }
            else if (switched && (!ours || !coopPlayers))
            {
                if (m == ModeCoop) Marshal.WriteByte(mode, ModeWater);
                switched = false;
            }
        }
    }
}
