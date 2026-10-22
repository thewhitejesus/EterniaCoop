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
        // Zoom: 0x200 = the game's closest. FUN_D6420's clamp shows the relation: at zoom z the view is
        // 320 + 2 * (z - 0x200) / 3 PS1 pixels wide, and the field is 0x160 + 320 = 672 wide (the camera centre ranges
        // over 0..0x160 at zoom 0x200). The game's own limit (manual zoom) is 0x29C, a 424-pixel view; 0x410 shows
        // the whole field.
        const short ZoomMin = 0x200, ZoomGameMax = 0x29C, ZoomFullField = 0x410;
        const float FieldWidth = 672f;
        const float Edge = 40f;            // room kept beyond the outermost players, PS1 pixels
        const float DeadZone = 6f;         // ignore spread changes smaller than this (no twitching), PS1 pixels

        static float ViewWidth(float z) { return 320f + 2f * (z - ZoomMin) / 3f; }
        static float ZoomFor(float width) { return ZoomMin + 1.5f * (width - 320f); }

        static IntPtr stub, active, centre;   // stub data: active (byte), centre x (ushort, world units = 1/16 px)
        static bool installed;
        static long lastActors;
        static float zoom, x, baseZoom, zoomVel, xVel, lastSpread, zoomInWait, lastMid, leadVel;
        static bool tracking, resume;
        const float LeadMax = 20f;         // look-ahead cap, PS1 pixels

        // Critically damped spring (no overshoot, no snap): moves value toward target, settling in about smoothTime
        // seconds. One step per battle frame (60 fps).
        static float Spring(float value, float target, ref float velocity, float smoothTime)
        {
            const float dt = 1f / 60f;
            float omega = 2f / smoothTime, k = omega * dt;
            float exp = 1f / (1f + k + 0.48f * k * k + 0.235f * k * k * k);
            float change = value - target;
            float temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            return target + (change + temp) * exp;
        }

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
            int min = int.MaxValue, max = int.MinValue, humans = 0, leaderX = 0;
            if (enabled)
                for (int i = 0; i < Rva.ActorCount; i++)
                {
                    IntPtr a = Battle.ActorAt(actors, i);
                    int slot = Battle.PartySlot(a);
                    if (slot < 0 || !humanSlots[slot]) continue;
                    int ax = Marshal.ReadInt16(a, Actor.X);
                    if (ax < min) min = ax;
                    if (ax > max) max = ax;
                    if (slot == 0) leaderX = ax;
                    humans++;
                }

            if (humans < 2)
            {
                // One player (or a scripted / paused camera): the game's camera. Ease a zoom we changed back first.
                // When we take over again, start from wherever the camera is by then (no jump back after a spell).
                Marshal.WriteByte(active, 0);
                resume = true;
                if (tracking && enabled)
                {
                    zoom = Spring(zoom, baseZoom, ref zoomVel, 0.5f);
                    if (Math.Abs(zoom - baseZoom) < 0.5f) { zoom = baseZoom; tracking = false; }
                    Marshal.WriteInt16(Native.At(Rva.CamZoom), (short)Math.Round(zoom));
                }
                return;
            }

            float rawMid = (min + max) / 2f;
            if (!tracking || resume)
            {
                // Pick up the camera where it actually is (the game's scroll: -(x >> 4)), so taking over - at the
                // start, after a spell's camera, when a second player joins - glides instead of jumping.
                x = -Marshal.ReadInt16(Native.At(Rva.CamScroll)) * 16f + 8f;
                xVel = 0; lastMid = rawMid; leadVel = 0;
                if (!tracking) { zoom = Marshal.ReadInt16(Native.At(Rva.CamZoom)); zoomVel = 0; lastSpread = (max - min) / 16f; }
                tracking = true; resume = false;
            }

            // Look-ahead: when the players move together, lead a little in that direction (capped, smoothed).
            leadVel += ((rawMid - lastMid) * 60f - leadVel) * 0.1f;   // world units per second
            lastMid = rawMid;
            float lead = Math.Max(-LeadMax * 16f, Math.Min(LeadMax * 16f, leadVel * 0.25f));

            // Zoom to fit everyone (plus Edge on both sides), within the allowed range; small spread changes ignored.
            float spread = (max - min) / 16f;
            if (Math.Abs(spread - lastSpread) > DeadZone) lastSpread = spread;
            float limit = Math.Max(ZoomMin, Math.Min(ZoomFullField, Settings.CoopCameraMaxZoom.Value));
            float wantZoom = Math.Max(baseZoom, Math.Min(limit, ZoomFor(lastSpread + 2 * Edge)));

            // Centre between them. If they can't all fit even at the limit, keep player 1's character in view
            // instead of centring on empty ground between characters who are both off-screen.
            float mid = rawMid + lead;
            float halfView = ViewWidth(Math.Min(limit, Math.Max(zoom, wantZoom))) / 2f * 16f;   // world units
            float room = halfView - Edge * 16f;
            // Never let the lead (or a too-wide spread) push anyone out: keep everyone in reach, player 1 above all.
            mid = Math.Max(max - room, Math.Min(min + room, mid));
            if (Math.Abs(leaderX - mid) > room) mid = leaderX + (mid > leaderX ? room : -room);

            // Zoom out promptly (nobody slips off screen), zoom back in slowly and only after the players have stayed
            // together for a moment - so the camera doesn't breathe in and out with every step.
            x = Spring(x, mid, ref xVel, 0.35f);
            if (wantZoom >= zoom - 1f) { zoomInWait = 0.5f; zoom = Spring(zoom, wantZoom, ref zoomVel, 0.4f); }
            else if (zoomInWait > 0f) { zoomInWait -= 1f / 60f; zoom = Spring(zoom, zoom, ref zoomVel, 0.4f); }
            else zoom = Spring(zoom, wantZoom, ref zoomVel, 0.9f);
            Marshal.WriteInt16(centre, (short)Math.Round(x));
            Marshal.WriteByte(active, 1);
            Marshal.WriteInt16(Native.At(Rva.CamZoom), (short)Math.Round(zoom));
        }
    }
}
