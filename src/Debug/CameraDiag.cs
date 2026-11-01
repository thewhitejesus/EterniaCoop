// Debug build only: once a second in battle, how the co-op camera spent that second (frames per camera mode, frames we
// steered), the zoom, whether the ground extension allows full zoom, and where the party is. For reports of players
// going off screen.
#if COOP_DEBUG
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EterniaCoop
{
    static class CameraDiag
    {
        static readonly int[] modes = new int[256];
        static int frames, steered;
        static long last;

        // Battle thread, from CoopCamera.Update: every unpaused battle frame.
        public static void Frame(IntPtr actors, bool steering)
        {
            frames++;
            if (steering) steered++;
            modes[Marshal.ReadByte(Native.At(Rva.CamMode))]++;
            long now = Stopwatch.GetTimestamp();
            if (now - last < Stopwatch.Frequency) return;
            last = now;
            var s = new StringBuilder("Camera: " + frames + " frames, steered " + steered + ", modes");
            for (int m = 0; m < modes.Length; m++)
                if (modes[m] > 0) { s.Append(' ').Append(m).Append('=').Append(modes[m]); modes[m] = 0; }
            s.Append(", zoom now ").Append(Marshal.ReadInt16(Native.At(Rva.ZoomNow)));
            s.Append(" set ").Append(Marshal.ReadInt16(Native.At(Rva.CamZoom)));
            s.Append(", ground usable ").Append(BattleGround.Usable);
            s.Append(", players ").Append(CoopCamera.HumanPlayers);
            s.Append(", scroll ").Append(Marshal.ReadInt16(Native.At(Rva.CamScrollNow)));
            s.Append(" -> ").Append(Marshal.ReadInt16(Native.At(Rva.CamScroll)));
            s.Append(", party x");
            for (int i = 0; i < Rva.ActorCount; i++)
            {
                IntPtr a = Battle.ActorAt(actors, i);
                if (Marshal.ReadByte(a, Actor.Side) != 0 || Marshal.ReadIntPtr(a, Actor.CmdBlock) == IntPtr.Zero) continue;
                short kind = Marshal.ReadInt16(a, Actor.Kind);
                if (kind != 1 && kind != 2) continue;
                s.Append(' ').Append(Marshal.ReadByte(a, Actor.Block)).Append(':').Append(Marshal.ReadInt16(a, Actor.X) / 16);
                s.Append(kind == 2 ? "(down)" : "").Append(Battle.PartySlot(a) >= 0 ? "" : "(ai slot)");
            }
            Plugin.Log.LogInfo(s.ToString());
            frames = steered = 0;
        }
    }
}
#endif
