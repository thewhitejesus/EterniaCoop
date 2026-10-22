// Co-op camera: in battles with two or more human players, the battle camera centres between their characters and
// zooms out as they spread apart (back in as they come together), easing smoothly - so nobody fights off-screen.
//
// The game's normal battle camera (FUN_D2C80) loads the followed actor's x (Sites "CamTargetX": movzx ecx, word
// [actor+0x10]) and passes it to the scroll function FUN_D6420, which clamps to the field using the current zoom;
// then it copies the zoom setting (Rva.CamZoom, 0x200 closest .. 0x29C farthest) to the zoom target when the camera
// mode (Rva.CamMode) is 0. That one 5-byte load becomes a call to a small stub: the original load, then - while the
// mod has a centre for this frame - our x instead. The manual pan path (hold Square in the battle menu) and scripted
// cameras (spells, mystic artes: mode >= 2) never reach it. Everything else - clamping, rendering - stays the game's.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class CoopCamera
    {
        const short ZoomMin = 0x200, ZoomMax = 0x29C;   // the game's own range (manual zoom in the battle menu)
        const float ViewWidth = 320f;                    // battle view width in PS1 pixels at ZoomMin
        const float Edge = 48f;                          // room kept around the outermost players, PS1 pixels

        static IntPtr stub, active, centre;   // stub data: active (byte), centre x (ushort, world units = 1/16 px)
        static bool installed;
        static long lastActors;
        static float zoom, x, baseZoom;
        static bool tracking;

        // Battle thread (from the battle input hook, once GameNative's camera sites are resolved).
        public static void Install()
        {
            if (installed || !Features.Camera) return;
            int site = Sites.Rva("CamTargetX", 0);
            if (!Native.Matches(site, new byte[] { 0x0F, 0xB7, 0x4C, 0x1A, 0x10 }))
            { Plugin.Log.LogError("Co-op camera: unexpected code, not installed"); return; }
            stub = Native.AllocNear(64);
            if (stub == IntPtr.Zero) { Plugin.Log.LogError("Co-op camera: no memory near the game module"); return; }
            active = new IntPtr(stub.ToInt64() + 32);
            centre = new IntPtr(stub.ToInt64() + 34);

            // stub: movzx ecx, word [rdx+rbx+0x10]   ; the original load
            //       cmp byte [rip+active], 0
            //       je done
            //       movzx ecx, word [rip+centre]
            // done: ret
            var code = new byte[]
            {
                0x0F, 0xB7, 0x4C, 0x1A, 0x10,
                0x80, 0x3D, 0, 0, 0, 0, 0x00,
                0x74, 0x07,
                0x0F, 0xB7, 0x0D, 0, 0, 0, 0,
                0xC3,
            };
            BitConverter.GetBytes((int)(active.ToInt64() - (stub.ToInt64() + 12))).CopyTo(code, 7);   // rip after cmp = +12
            BitConverter.GetBytes((int)(centre.ToInt64() - (stub.ToInt64() + 21))).CopyTo(code, 17);   // rip after movzx = +21
            Marshal.Copy(code, 0, stub, code.Length);
            Marshal.WriteByte(active, 0);

            long rel = stub.ToInt64() - (Native.Base + site + 5);
            if (rel < int.MinValue || rel > int.MaxValue) { Plugin.Log.LogError("Co-op camera: stub out of reach"); return; }
            var call = new byte[5];
            call[0] = 0xE8;
            BitConverter.GetBytes((int)rel).CopyTo(call, 1);
            Native.Write(site, call);
            installed = true;
            Plugin.Log.LogMessage("Co-op camera installed.");
        }

        // Battle thread, every battle frame, before the game's camera code runs.
        public static void Update(bool[] humanSlots)
        {
            if (!installed) return;
            IntPtr actors = Battle.Actors();
            if (actors == IntPtr.Zero) { Marshal.WriteByte(active, 0); return; }

            // New battle: start from the game's own camera.
            if (actors.ToInt64() != lastActors)
            {
                lastActors = actors.ToInt64();
                tracking = false;
                baseZoom = zoom = Marshal.ReadInt16(Native.At(Rva.CamZoom));
            }

            bool enabled = Settings.CoopCamera.Value && Marshal.ReadByte(Native.At(Rva.CamMode)) == 0 &&
                (Marshal.ReadByte(Native.At(Rva.BattleFlags)) & 1) == 0;
            int min = int.MaxValue, max = int.MinValue, humans = 0;
            if (enabled)
                for (int i = 0; i < Rva.ActorCount; i++)
                {
                    IntPtr a = Battle.ActorAt(actors, i);
                    int slot = Battle.PartySlot(a);
                    if (slot < 0 || !humanSlots[slot]) continue;
                    int ax = Marshal.ReadInt16(a, Actor.X);
                    if (ax < min) min = ax;
                    if (ax > max) max = ax;
                    humans++;
                }

            if (humans < 2)
            {
                // One player (or a scripted / paused camera): the game's camera. Ease a zoom we changed back first.
                Marshal.WriteByte(active, 0);
                if (tracking && enabled)
                {
                    zoom += (baseZoom - zoom) * 0.08f;
                    if (Math.Abs(zoom - baseZoom) < 1f) { zoom = baseZoom; tracking = false; }
                    Marshal.WriteInt16(Native.At(Rva.CamZoom), (short)zoom);
                }
                return;
            }

            float mid = (min + max) / 2f, spread = (max - min) / 16f;
            if (!tracking) { x = mid; zoom = Marshal.ReadInt16(Native.At(Rva.CamZoom)); tracking = true; }
            float limit = Math.Min(ZoomMax, Math.Max(ZoomMin, Settings.CoopCameraMaxZoom.Value));
            float want = Math.Max(baseZoom, Math.Min(limit, ZoomMin * (spread + 2 * Edge) / ViewWidth));
            x += (mid - x) * 0.15f;
            zoom += (want - zoom) * 0.06f;
            Marshal.WriteInt16(centre, (short)Math.Round(x));
            Marshal.WriteByte(active, 1);
            Marshal.WriteInt16(Native.At(Rva.CamZoom), (short)Math.Round(zoom));
        }
    }
}
