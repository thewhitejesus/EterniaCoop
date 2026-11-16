// Water's Edition (a GameNative.dll patch) adds battle controls of its own: Overlimit on L3 (or F6 on the keyboard)
// and the Artesphere's arte pages on L2/R2. Each of its checks first asks whether the character is player 1's
// (`cmp byte [actor+0x56], 0` - command block 0) and then reads the one PSP pad, player 1's - so players 2-4 had neither
// (reported by zackFlame2, Water's Edition 0.14.180). These checks run in the characters' own battle code, after the
// command builder, where the PSP pad is player 1's again.
//
// The fix keeps Water's code and only changes what it reads: each check jumps to a small stub that, for command block 0,
// reads the PSP pad as before, and for a block a player 2-4 is playing this frame, that player's held buttons (filled by
// CoopBattleInput from the pad it builds for them); other blocks (AI) are skipped, as Water skips them. The stub leaves
// the buttons in a scratch word, and Water's pad reads after the check are pointed at it. F6 (Water reads it with
// GetAsyncKeyState into a byte) counts for the character whose player has the keyboard.
//
// No fixed addresses: the checks are found by their code in Water's own sections, and each read must reach the PSP pad.
// A Water's Edition whose code differs is left alone (logged).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class WaterInput
    {
        const int Blocks = 6;
        static IntPtr held, active, kbBlock;   // per command block: held buttons (dword), playing this frame (byte); keyboard's block
        static bool installed;

        public static bool Installed { get { return held != IntPtr.Zero; } }

        // Battle hook, start of each frame: nobody but block 0 has buttons until RunPlayer fills them.
        public static void BeginFrame(int keyboardBlock)
        {
            if (held == IntPtr.Zero) return;
            for (int b = 1; b < Blocks; b++) { Marshal.WriteInt32(held, b * 4, 0); Marshal.WriteByte(active, b, 0); }
            Marshal.WriteByte(kbBlock, (byte)(keyboardBlock < 0 || keyboardBlock >= Blocks ? 0xFF : keyboardBlock));
        }

        // Battle hook: player buttons for command block `block` (1-5) this frame, from the PSP pad built for them.
        public static void SetHeld(int block, IntPtr pad)
        {
            if (held == IntPtr.Zero || block < 1 || block >= Blocks) return;
            Marshal.WriteInt32(held, block * 4, Marshal.ReadInt32(pad));
            Marshal.WriteByte(active, block, 1);
            seenBits[block] |= Marshal.ReadInt32(pad);
        }

        static IntPtr counters;
        static string[] siteNames;
        static readonly int[] seenBits = new int[Blocks];
        static float nextReport;

        // Debug build: once every 2 s in battle, how often each check ran per command block and which buttons were seen.
        public static void Report(float now)
        {
            if (counters == IntPtr.Zero || now < nextReport) return;
            nextReport = now + 2f;
            var sb = new System.Text.StringBuilder("Water input:");
            bool any = false;
            for (int i = 0; i < siteNames.Length; i++)
            {
                sb.Append(" ").Append(siteNames[i]).Append(" [");
                for (int b = 0; b < Blocks; b++)
                {
                    int n = Marshal.ReadInt32(counters, i * 0x20 + b * 4);
                    Marshal.WriteInt32(counters, i * 0x20 + b * 4, 0);
                    any |= n != 0;
                    sb.Append(n).Append(b < Blocks - 1 ? "," : "]");
                }
            }
            sb.Append(" held seen");
            for (int b = 0; b < Blocks; b++) { sb.Append(" ").Append(seenBits[b].ToString("X")); seenBits[b] = 0; }
            sb.Append(" pad0 ").Append(Marshal.ReadInt32(Native.At(Rva.Pad)).ToString("X")).Append(" kb ").Append(Marshal.ReadByte(kbBlock));
            if (any) Plugin.Log.LogInfo(sb.ToString());
        }

        // A check: `cmp byte [reg+0x56], 0 ; jne fail` followed by reads of the PSP pad.
        class Site
        {
            public string Name;
            public int At, Length, Back, Fail;   // the cmp+jne (patched), where it continues, where jne went
            public byte[] Movzx;                 // movzx ecx, byte [reg+0x56]
            public List<int> Reads = new List<int>();   // RVAs of disp32 fields reading the pad
            public int Key;                      // RVA of the disp32 of `or al, [F6 byte]`, 0 if none
            public int KeyByte;                  // RVA of the F6 byte
        }

        static byte[] image;
        static int imageBase;   // RVA of image[0]

        static int ReadDisp(int rva) { return BitConverter.ToInt32(image, rva - imageBase); }
        static int Target(int dispRva, int insEnd) { return insEnd + ReadDisp(dispRva); }

        // Pattern with -1 as a wildcard; all matches' RVAs.
        static List<int> Find(int[] pat)
        {
            var hits = new List<int>();
            for (int i = 0; i + pat.Length <= image.Length; i++)
            {
                int k = 0;
                while (k < pat.Length && (pat[k] < 0 || image[i + k] == pat[k])) k++;
                if (k == pat.Length) hits.Add(imageBase + i);
            }
            return hits;
        }

        static int Unique(string name, int[] pat)
        {
            var hits = Find(pat);
            if (hits.Count != 1) { Plugin.Log.LogInfo("Water's Edition " + name + ": " + hits.Count + " matches, left alone."); return 0; }
            return hits[0];
        }

        // Next `test dword [rip+d], 0x80000` (F7 05 d32 00 00 08 00) within `range` bytes: its disp32's RVA, or 0.
        static int NextPage(int from, int range)
        {
            for (int a = from; a < from + range; a++)
            {
                int i = a - imageBase;
                if (i + 10 > image.Length) break;
                if (image[i] == 0xF7 && image[i + 1] == 0x05 && image[i + 6] == 0 && image[i + 7] == 0 && image[i + 8] == 8 && image[i + 9] == 0)
                    return a + 2;
            }
            return 0;
        }

        static bool ReadsPad(Site s)
        {
            foreach (int d in s.Reads)
            {
                if (d == 0) return false;
                int i = d - imageBase;
                int end = d + 4 + (image[i - 2] == 0xF7 ? 4 : 0);   // test dword [rip+d], imm32 / mov eax, [rip+d]
                if (Target(d, end) != Rva.Pad) return false;
            }
            return true;
        }

        static List<Site> FindSites()
        {
            var sites = new List<Site>();
            // Overlimit: xor edx,edx ; cmp byte [rbx+0x56],0 ; jne ; mov eax,[pad] ; and eax,0x100000 ; or al,[F6]
            int a = Unique("Overlimit", new[] { 0x31, 0xD2, 0x80, 0x7B, 0x56, 0x00, 0x75, -1, 0x8B, 0x05, -1, -1, -1, -1,
                0x25, 0x00, 0x00, 0x10, 0x00, 0x0A, 0x05, -1, -1, -1, -1 });
            if (a != 0)
            {
                var s = new Site { Name = "Overlimit", At = a + 2, Length = 6, Back = a + 8, Movzx = new byte[] { 0x0F, 0xB6, 0x4B, 0x56 } };
                s.Fail = s.Back + (sbyte)image[a + 7 - imageBase];
                s.Reads.Add(a + 10);
                s.Key = a + 21;
                s.KeyByte = Target(a + 21, a + 25);
                sites.Add(s);
            }
            // Artesphere pages: cmp byte [rbp+0x56],0 ; jne ; test dword [pad],0xC0000 ; je  (then 0x80000 picks the page)
            int b = Unique("Artesphere (1)", new[] { 0x80, 0x7D, 0x56, 0x00, 0x75, -1, 0xF7, 0x05, -1, -1, -1, -1, 0x00, 0x00, 0x0C, 0x00, 0x74 });
            if (b != 0)
            {
                var s = new Site { Name = "Artesphere (1)", At = b, Length = 6, Back = b + 6, Movzx = new byte[] { 0x0F, 0xB6, 0x4D, 0x56 } };
                s.Fail = s.Back + (sbyte)image[b + 5 - imageBase];
                s.Reads.Add(b + 8);
                s.Reads.Add(NextPage(b + 16, 0x60));
                sites.Add(s);
            }
            // Same with r14
            int c = Unique("Artesphere (2)", new[] { 0x41, 0x80, 0x7E, 0x56, 0x00, 0x75, -1, 0xF7, 0x05, -1, -1, -1, -1, 0x00, 0x00, 0x0C, 0x00, 0x74 });
            if (c != 0)
            {
                var s = new Site { Name = "Artesphere (2)", At = c, Length = 7, Back = c + 7, Movzx = new byte[] { 0x41, 0x0F, 0xB6, 0x4E, 0x56 } };
                s.Fail = s.Back + (sbyte)image[c + 6 - imageBase];
                s.Reads.Add(c + 9);
                s.Reads.Add(NextPage(c + 17, 0x40));
                sites.Add(s);
            }
            sites.RemoveAll(s =>
            {
                if (ReadsPad(s)) return false;
                Plugin.Log.LogInfo("Water's Edition " + s.Name + ": doesn't read the pad as expected, left alone.");
                return true;
            });
            return sites;
        }

        // Water's code lives in sections it added: every executable section after the first.
        static bool LoadWaterCode()
        {
            long m = Native.Base;
            int pe = Marshal.ReadInt32(new IntPtr(m + 0x3C));
            int count = Marshal.ReadInt16(new IntPtr(m + pe + 6));
            int optSize = Marshal.ReadInt16(new IntPtr(m + pe + 20));
            long table = m + pe + 24 + optSize;
            int lo = int.MaxValue, hi = 0, exec = 0;
            for (int s = 0; s < count; s++)
            {
                long h = table + s * 40;
                int va = Marshal.ReadInt32(new IntPtr(h + 12)), size = Marshal.ReadInt32(new IntPtr(h + 8));
                uint flags = (uint)Marshal.ReadInt32(new IntPtr(h + 36));
                if ((flags & 0x20000000) == 0 || size <= 0) continue;
                if (exec++ == 0) continue;   // the game's own .text
                lo = Math.Min(lo, va); hi = Math.Max(hi, va + size);
            }
            if (hi <= lo) return false;
            imageBase = lo;
            image = new byte[hi - lo];
            Marshal.Copy(new IntPtr(m + lo), image, 0, image.Length);   // gaps between sections are mapped (image is contiguous)
            return true;
        }

        public static void Install()
        {
            if (installed || Native.Base == 0) return;
            WaterCompat.EnsureFound();
            if (!WaterCompat.Present) return;
            installed = true;
            try
            {
                if (!LoadWaterCode()) return;
                var sites = FindSites();
                image = null;
                if (sites.Count == 0) return;

                IntPtr region = Native.AllocNear(0x2000);
                if (region == IntPtr.Zero) { Plugin.Log.LogError("Water's Edition controls for players 2-4: no memory near the game module"); return; }
                long S = region.ToInt64(), D = S + 0x1000;   // data on its own page: written every frame
                long heldA = D, activeA = D + 0x20, kbA = D + 0x28, curHeld = D + 0x30, curKey = D + 0x34;
                long pad = Native.Base + Rva.Pad;

                var c = new List<byte>();
                Func<long, int, byte[]> rel = (target, end) => BitConverter.GetBytes((int)(target - (S + end)));
                var entries = new List<long>();
                foreach (var s in sites)
                {
                    entries.Add(S + c.Count);
                    c.AddRange(new byte[] { 0x50, 0x51 });                                   // push rax ; push rcx
                    c.AddRange(s.Movzx);                                                     // movzx ecx, byte [reg+0x56]
                    c.AddRange(new byte[] { 0x83, 0xF9, (byte)Blocks });                     // cmp ecx, Blocks
                    int jae = c.Count; c.AddRange(new byte[] { 0x0F, 0x83, 0, 0, 0, 0 });    // jae fail
#if COOP_DEBUG
                    // lea rax, [counters + site*0x20] ; inc dword [rax+rcx*4]   (calls per block, logged by Report)
                    c.AddRange(new byte[] { 0x48, 0x8D, 0x05 }); c.AddRange(rel(D + 0x40 + entries.Count * 0x20 - 0x20, c.Count + 4));
                    c.AddRange(new byte[] { 0xFF, 0x04, 0x88 });
#endif
                    c.AddRange(new byte[] { 0x85, 0xC9 });                                   // test ecx, ecx
                    int jnz = c.Count; c.AddRange(new byte[] { 0x0F, 0x85, 0, 0, 0, 0 });    // jnz other
                    c.AddRange(new byte[] { 0x8B, 0x05 }); c.AddRange(rel(pad, c.Count + 4)); // mov eax, [pad]  (player 1's, as before)
                    int jmp = c.Count; c.AddRange(new byte[] { 0xE9, 0, 0, 0, 0 });          // jmp store
                    int other = c.Count;
                    c.AddRange(new byte[] { 0x48, 0x8D, 0x05 }); c.AddRange(rel(activeA, c.Count + 4));   // lea rax, [active]
                    c.AddRange(new byte[] { 0x80, 0x3C, 0x08, 0x00 });                       // cmp byte [rax+rcx], 0
                    int je = c.Count; c.AddRange(new byte[] { 0x0F, 0x84, 0, 0, 0, 0 });     // je fail (AI: skipped like before)
                    c.AddRange(new byte[] { 0x48, 0x8D, 0x05 }); c.AddRange(rel(heldA, c.Count + 4));     // lea rax, [held]
                    c.AddRange(new byte[] { 0x8B, 0x04, 0x88 });                             // mov eax, [rax+rcx*4]
                    int store = c.Count;
                    c.AddRange(new byte[] { 0x89, 0x05 }); c.AddRange(rel(curHeld, c.Count + 4));        // mov [curHeld], eax
                    if (s.Key != 0)
                    {
                        c.AddRange(new byte[] { 0x31, 0xC0 });                               // xor eax, eax
                        c.AddRange(new byte[] { 0x3A, 0x0D }); c.AddRange(rel(kbA, c.Count + 4));        // cmp cl, [kbBlock]
                        c.AddRange(new byte[] { 0x75, 0x06 });                               // jne +6
                        c.AddRange(new byte[] { 0x8A, 0x05 }); c.AddRange(rel(Native.Base + s.KeyByte, c.Count + 4));   // mov al, [F6]
                        c.AddRange(new byte[] { 0x88, 0x05 }); c.AddRange(rel(curKey, c.Count + 4));     // mov [curKey], al
                    }
                    c.AddRange(new byte[] { 0x59, 0x58, 0xE9 }); c.AddRange(rel(Native.Base + s.Back, c.Count + 4));   // pop rcx ; pop rax ; jmp back
                    int fail = c.Count;
                    c.AddRange(new byte[] { 0x59, 0x58, 0xE9 }); c.AddRange(rel(Native.Base + s.Fail, c.Count + 4));   // pop rcx ; pop rax ; jmp fail
                    Action<int, int, int> fix = (at, size, to) => { var r = BitConverter.GetBytes(to - (at + size)); for (int k = 0; k < 4; k++) c[at + size - 4 + k] = r[k]; };
                    fix(jae, 6, fail); fix(jnz, 6, other); fix(jmp, 5, store); fix(je, 6, fail);
                }
                var code = c.ToArray();
                Marshal.Copy(code, 0, region, code.Length);
                held = new IntPtr(heldA); active = new IntPtr(activeA); kbBlock = new IntPtr(kbA);
                counters = new IntPtr(D + 0x40); siteNames = sites.ConvertAll(x => x.Name).ToArray();
                BeginFrame(0);

                var names = new List<string>();
                for (int i = 0; i < sites.Count; i++)
                {
                    var s = sites[i];
                    // Water's reads after the check: the stub's scratch word (byte for the F6 key) instead of the pad.
                    bool ok = true;
                    foreach (int d in s.Reads) ok &= Redirect(d, Native.Base + Rva.Pad, curHeld, false);
                    if (s.Key != 0) ok &= Redirect(s.Key, Native.Base + s.KeyByte, curKey, false);
                    if (!ok) { Plugin.Log.LogInfo("Water's Edition " + s.Name + ": unexpected reads, left alone."); continue; }
                    foreach (int d in s.Reads) Redirect(d, Native.Base + Rva.Pad, curHeld, true);
                    if (s.Key != 0) Redirect(s.Key, Native.Base + s.KeyByte, curKey, true);
                    var jump = new byte[s.Length];
                    jump[0] = 0xE9;
                    BitConverter.GetBytes((int)(entries[i] - (Native.Base + s.At + 5))).CopyTo(jump, 1);
                    for (int k = 5; k < jump.Length; k++) jump[k] = 0x90;
                    Native.Write(s.At, jump);
                    names.Add(s.Name);
                }
                Plugin.Log.LogMessage("Water's Edition controls for players 2-4: " + string.Join(", ", names.ToArray()) + ".");
            }
            catch (Exception e) { Plugin.Log.LogError("Water's Edition controls for players 2-4: " + e.Message); }
        }

        // Points the RIP-relative disp32 at `rva` (reading `from`) at `to`. The instruction's end is where `from` is reached from.
        static bool Redirect(int rva, long from, long to, bool write)
        {
            int disp = Marshal.ReadInt32(Native.At(rva));
            long end = from - disp;   // address after the instruction
            long now = Native.Base + rva + 4;
            if (end < now || end > now + 4) return false;   // disp32 is followed by at most an imm32
            if (write) Native.Write(rva, BitConverter.GetBytes((int)(to - end)));
            return true;
        }
    }
}
