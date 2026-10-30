// Battle ground extension for the co-op camera. The battle camera's zoom is a real camera distance: each battlefield's
// background routine (one per stage, dispatched by FUN_10D500) projects a grid of ground points and draws it as rows
// of textured quads (FUN_11E6F0) - but only as far toward the camera as the PS1's screen needed. Zoomed out beyond
// the game's own limit (the co-op camera does, to fit everyone), the bottom row ends above the screen's bottom edge
// and the stage's clear colour shows below it.
//
// While FUN_10D500 runs, a small native stub on FUN_11E6F0 copies every quad drawn into a buffer, and keeps the global
// draw state (texture binding) as of the last one: some stages reset it when they finish, and quads drawn after that
// never reach the screen. Afterwards, if the camera is zoomed out past the game's limit, the ground's grid is fitted
// with a perspective map (a ground plane seen through a camera is a homography) and continued downward and sideways,
// row by row, mirroring the ground's texture strips so every seam joins matching texels. The game's own drawing is
// untouched; at the game's zoom levels nothing is added. Many stages also blank the strip below their ground with a
// black fill (FUN_120F70); while recording, fills that start in the lower part of the screen are skipped.
//
// A stage whose ground isn't a regular grid is left alone, and the co-op camera then stays within the zoom that never
// shows the band (Usable). ScissorPatch lets the picture reach the bottom of the screen.
using System;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class BattleGround
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DrawBgFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void QuadFn(IntPtr prim);

        static readonly byte[] DrawBgEntry = { 0x48, 0x89, 0x5C, 0x24, 0x08 };          // mov [rsp+8], rbx
        static readonly byte[] QuadEntry = { 0x48, 0x89, 0x5C, 0x24, 0x10 };            // mov [rsp+0x10], rbx
        static readonly byte[] FillEntry = { 0x40, 0x53, 0x48, 0x83, 0xEC, 0x20 };      // push rbx ; sub rsp, 0x20
        const short LowFill = 0x60;   // fills starting this far down (PS1 y) are the under-ground blanking

        // Primitive (the game's work struct): +0x20 four corners (x, y shorts: top-left, top-right, bottom-left,
        // bottom-right), +0x64 depth, +0x66 texture page, +0x6A u, v, width, height.
        const int PrimSize = 0xC0, P0 = 0x20, StateSize = 0x20;
        const int Region = 0x10000, BufOffset = 0x200, MaxQuads = (Region - BufOffset) / PrimSize;
        public const short ZoomGameMax = 0x29C;
        // PS1 screen y the extension reaches: zoomed far out the view reaches well below the PS1's 0xE0. Rows near the
        // camera are spaced truly (homography), so it takes many of them to get there.
        const int TargetBottom = 0x1C0, MaxRows = 40;
        const int SideColumns = 12;      // how far beyond the drawn columns the new rows may reach
        const int ScreenMargin = 260;    // PS1 pixels beyond the 320-wide screen that can be visible zoomed out

        static IntPtr rec, count, buf, extra, snapA, snapB;
        static readonly byte[] liveA = new byte[StateSize], liveB = new byte[StateSize], kept = new byte[StateSize];
        static MonoMod.RuntimeDetour.NativeDetour detour;
        static DrawBgFn original, hook;
        static QuadFn drawQuad;
        static bool installed;

        // False while this battle's ground couldn't be continued: the co-op camera then keeps out of the band.
        public static bool Usable { get { return installed && !failed; } }
        static bool failed;

        // Battle thread, from the battle input hook (once). Features.Ground has checked every site.
        public static void Install()
        {
            if (installed || !Features.Ground) return;
            if (!Native.Matches(Rva.DrawQuad, QuadEntry) || !Native.Matches(Rva.FillRect, FillEntry) || !Native.Matches(Rva.DrawBg, DrawBgEntry))
            { Plugin.Log.LogWarning("Battle ground extension: unexpected code (another mod?), not installed."); Features.Ground = false; return; }
            IntPtr region = Native.AllocNear(Region);
            if (region == IntPtr.Zero) { Plugin.Log.LogWarning("Battle ground extension: no memory near the game module."); Features.Ground = false; return; }
            long S = region.ToInt64();
            // 0x00 quad stub, 0x80 fill stub, 0x100 data (recording flag, quad count, draw state copies), 0x200 quads
            rec = new IntPtr(S + 0x100);
            count = new IntPtr(S + 0x104);
            snapA = new IntPtr(S + 0x120);
            snapB = new IntPtr(S + 0x140);
            buf = new IntPtr(S + BufOffset);
            extra = Marshal.AllocHGlobal(PrimSize);

            // Quad stub, on FUN_11E6F0's entry: while recording (and the buffer has room), copy the primitive and the
            // draw state, then run the replaced instruction and continue into FUN_11E6F0.
            var c = new System.Collections.Generic.List<byte>();
            Action<byte[]> emit = b => c.AddRange(b);
            Func<long, int, byte[]> rel = (target, end) => BitConverter.GetBytes((int)(target - (S + end)));
            emit(new byte[] { 0x80, 0x3D }); emit(rel(rec.ToInt64(), c.Count + 5)); emit(new byte[] { 0x00 });     // cmp byte [rec], 0
            int je1 = c.Count; emit(new byte[] { 0x74, 0 });                                                          // je tail
            emit(new byte[] { 0x8B, 0x05 }); emit(rel(count.ToInt64(), c.Count + 4));                                // mov eax, [count]
            emit(new byte[] { 0x3D }); emit(BitConverter.GetBytes(MaxQuads));                                         // cmp eax, MaxQuads
            int jae = c.Count; emit(new byte[] { 0x73, 0 });                                                          // jae tail
            emit(new byte[] { 0xFF, 0x05 }); emit(rel(count.ToInt64(), c.Count + 4));                                // inc dword [count]
            emit(new byte[] { 0x56, 0x57, 0x51, 0x69, 0xC0 }); emit(BitConverter.GetBytes(PrimSize));                 // push rsi/rdi/rcx ; imul eax, PrimSize
            emit(new byte[] { 0x48, 0x8D, 0x3D }); emit(rel(buf.ToInt64(), c.Count + 4));                            // lea rdi, [buf]
            emit(new byte[] { 0x48, 0x01, 0xC7, 0x48, 0x89, 0xCE, 0xB9 }); emit(BitConverter.GetBytes(PrimSize));     // add rdi, rax ; mov rsi, rcx ; mov ecx, PrimSize
            emit(new byte[] { 0xF3, 0xA4 });                                                                          // rep movsb
            foreach (var pair in new[] { new[] { (long)Rva.DrawStateA, snapA.ToInt64() }, new[] { (long)Rva.DrawStateB, snapB.ToInt64() } })
            {
                emit(new byte[] { 0x48, 0x8D, 0x35 }); emit(rel(Native.Base + pair[0], c.Count + 4));                // lea rsi, [state]
                emit(new byte[] { 0x48, 0x8D, 0x3D }); emit(rel(pair[1], c.Count + 4));                              // lea rdi, [copy]
                emit(new byte[] { 0xB9, StateSize, 0, 0, 0, 0xF3, 0xA4 });                                           // mov ecx, 0x20 ; rep movsb
            }
            emit(new byte[] { 0x59, 0x5F, 0x5E });                                                                    // pop rcx/rdi/rsi
            int tail = c.Count;
            emit(QuadEntry);                                                                                          // tail: replaced instruction
            emit(new byte[] { 0xE9 }); emit(rel(Native.Base + Rva.DrawQuad + QuadEntry.Length, c.Count + 4));        // jmp FUN_11E6F0+5
            var code = c.ToArray();
            code[je1 + 1] = (byte)(tail - (je1 + 2));
            code[jae + 1] = (byte)(tail - (jae + 2));
            if (code.Length > 0x80) { Plugin.Log.LogWarning("Battle ground extension: stub too long."); Features.Ground = false; return; }
            Marshal.Copy(code, 0, region, code.Length);

            // Fill stub, on FUN_120F70's entry: while recording, a fill starting low on the screen returns at once.
            long S2 = S + 0x80;
            var f = new System.Collections.Generic.List<byte>();
            Func<long, int, byte[]> rel2 = (target, end) => BitConverter.GetBytes((int)(target - (S2 + end)));
            f.AddRange(new byte[] { 0x80, 0x3D }); f.AddRange(rel2(rec.ToInt64(), f.Count + 5)); f.Add(0x00);       // cmp byte [rec], 0
            int je2 = f.Count; f.AddRange(new byte[] { 0x74, 0 });                                                     // je tail
            f.AddRange(new byte[] { 0x66, 0x81, 0x3D }); f.AddRange(rel2(Native.Base + Rva.FillY, f.Count + 6));      // cmp word [fill y], LowFill
            f.AddRange(BitConverter.GetBytes(LowFill));
            int jl = f.Count; f.AddRange(new byte[] { 0x7C, 0 });                                                      // jl tail
            f.Add(0xC3);                                                                                               // ret
            int tail2 = f.Count;
            f.AddRange(FillEntry);                                                                                     // tail: replaced instructions
            f.Add(0xE9); f.AddRange(rel2(Native.Base + Rva.FillRect + FillEntry.Length, f.Count + 4));                // jmp FUN_120F70+6
            var fill = f.ToArray();
            fill[je2 + 1] = (byte)(tail2 - (je2 + 2));
            fill[jl + 1] = (byte)(tail2 - (jl + 2));
            Marshal.Copy(fill, 0, new IntPtr(S2), fill.Length);

            drawQuad = (QuadFn)Marshal.GetDelegateForFunctionPointer(Native.At(Rva.DrawQuad), typeof(QuadFn));
            hook = Hook;
            detour = new MonoMod.RuntimeDetour.NativeDetour(Native.At(Rva.DrawBg), Marshal.GetFunctionPointerForDelegate(hook));
            original = detour.GenerateTrampoline<DrawBgFn>();

            var patch = new byte[] { 0xE9, 0, 0, 0, 0 };
            BitConverter.GetBytes((int)(S - (Native.Base + Rva.DrawQuad + 5))).CopyTo(patch, 1);
            Native.Write(Rva.DrawQuad, patch);
            var fillPatch = new byte[] { 0xE9, 0, 0, 0, 0, 0x90 };   // jmp stub ; nop (6 bytes replaced)
            BitConverter.GetBytes((int)(S2 - (Native.Base + Rva.FillRect + 5))).CopyTo(fillPatch, 1);
            Native.Write(Rva.FillRect, fillPatch);
            installed = true;
            Plugin.Log.LogMessage("Battle ground extension installed.");
        }

        // FUN_10D500 (battle thread), every battle frame.
        static void Hook()
        {
            bool extend = Settings.CoopCamera.Value && Marshal.ReadInt16(Native.At(Rva.ZoomNow)) > ZoomGameMax;
            if (!extend) { original(); return; }
            Marshal.WriteInt32(count, 0);
            Marshal.WriteByte(rec, 1);
            try { original(); }
            finally { Marshal.WriteByte(rec, 0); }
            try { failed = !Extend(); }
            catch (Exception e) { failed = true; Plugin.Log.LogError("Battle ground extension: " + e.Message); }
        }

        static IntPtr Quad(int i) { return new IntPtr(buf.ToInt64() + i * PrimSize); }

        // Corners of every recorded quad, read from the buffer once per frame.
        static readonly short[] corners = new short[MaxQuads * 8];
        static readonly short[] q = new short[8], nb = new short[8];
        static int quads;

        static void Read(int i, short[] to) { Array.Copy(corners, i * 8, to, 0, 8); }
        static int Bottom(short[] v) { return Math.Max(v[5], v[7]); }
        static bool IsGround(short[] v) { return v[5] > v[1] && v[7] > v[3]; }
        static bool Same(short[] a, int ia, short[] b, int ib) { return a[ia] == b[ib] && a[ia + 1] == b[ib + 1]; }

        // The ground grid: neighbouring quads share corners exactly (they come from the same projected points), so
        // the quads connected to the lowest one get grid coordinates (column, row) by walking those shared edges.
        const int GridSize = 64, GridOff = 32;
        static readonly int[] gcol = new int[MaxQuads], grow = new int[MaxQuads], queue = new int[MaxQuads], members = new int[MaxQuads];
        static readonly int[,] cell = new int[GridSize, GridSize];
        static int bottomRow;

        static int BuildGrid(int start)
        {
            for (int c = 0; c < GridSize; c++) for (int r = 0; r < GridSize; r++) cell[c, r] = -1;
            for (int i = 0; i < quads; i++) gcol[i] = int.MinValue;
            int head = 0, tail = 0, found = 0;
            gcol[start] = GridOff; grow[start] = GridOff; cell[GridOff, GridOff] = start; queue[tail++] = start;
            while (head < tail)
            {
                int i = queue[head++];
                found++;
                Read(i, q);
                for (int j = 0; j < quads; j++)
                {
                    if (gcol[j] != int.MinValue) continue;
                    Read(j, nb);
                    if (!IsGround(nb)) continue;
                    int dc, dr;
                    // corners: 0 top-left, 2 top-right, 4 bottom-left, 6 bottom-right (x, y pairs)
                    if (Same(nb, 0, q, 2) && Same(nb, 4, q, 6)) { dc = 1; dr = 0; }
                    else if (Same(nb, 2, q, 0) && Same(nb, 6, q, 4)) { dc = -1; dr = 0; }
                    else if (Same(nb, 0, q, 4) && Same(nb, 2, q, 6)) { dc = 0; dr = 1; }
                    else if (Same(nb, 4, q, 0) && Same(nb, 6, q, 2)) { dc = 0; dr = -1; }
                    else continue;
                    int c = gcol[i] + dc, r = grow[i] + dr;
                    if (c < 0 || r < 0 || c >= GridSize || r >= GridSize || cell[c, r] >= 0) continue;
                    gcol[j] = c; grow[j] = r; cell[c, r] = j; queue[tail++] = j;
                }
            }
            return found;
        }

        // Perspective map from grid coordinates (X = column, Y = row, corners at whole numbers) to the screen:
        // u = (aX + bY + c) / (gX + hY + 1), likewise v. Fitted by least squares to every corner of the grid, so
        // rounding in single points averages out.
        static readonly double[] H = new double[8];
        static readonly double[,] M = new double[8, 9];
        static readonly double[] ru = new double[8], rv = new double[8];

        static void AddRow(double[] row, double rhs)
        {
            for (int a = 0; a < 8; a++)
            {
                if (row[a] == 0) continue;
                for (int b = 0; b < 8; b++) M[a, b] += row[a] * row[b];
                M[a, 8] += row[a] * rhs;
            }
        }

        static bool FitGrid(int n, out double maxError)
        {
            maxError = double.MaxValue;
            Array.Clear(M, 0, M.Length);
            int points = 0;
            for (int m = 0; m < n; m++)
            {
                int i = members[m];
                Read(i, q);
                for (int k = 0; k < 4; k++)
                {
                    double X = gcol[i] - GridOff + (k & 1), Y = grow[i] - GridOff + (k >> 1), u = q[k * 2], v = q[k * 2 + 1];
                    ru[0] = X; ru[1] = Y; ru[2] = 1; ru[3] = 0; ru[4] = 0; ru[5] = 0; ru[6] = -X * u; ru[7] = -Y * u;
                    rv[0] = 0; rv[1] = 0; rv[2] = 0; rv[3] = X; rv[4] = Y; rv[5] = 1; rv[6] = -X * v; rv[7] = -Y * v;
                    AddRow(ru, u); AddRow(rv, v);
                    points++;
                }
            }
            if (points < 8) return false;

            // Gaussian elimination with partial pivoting.
            for (int col = 0; col < 8; col++)
            {
                int piv = col;
                for (int r = col + 1; r < 8; r++) if (Math.Abs(M[r, col]) > Math.Abs(M[piv, col])) piv = r;
                if (Math.Abs(M[piv, col]) < 1e-9) return false;
                if (piv != col) for (int c = 0; c < 9; c++) { double t = M[col, c]; M[col, c] = M[piv, c]; M[piv, c] = t; }
                for (int r = 0; r < 8; r++)
                {
                    if (r == col) continue;
                    double f = M[r, col] / M[col, col];
                    if (f == 0) continue;
                    for (int c = col; c < 9; c++) M[r, c] -= f * M[col, c];
                }
            }
            for (int a = 0; a < 8; a++) H[a] = M[a, 8] / M[a, a];

            maxError = 0;
            for (int m = 0; m < n; m++)
            {
                int i = members[m];
                Read(i, q);
                for (int k = 0; k < 4; k++)
                {
                    double u, v;
                    if (!Project(gcol[i] - GridOff + (k & 1), grow[i] - GridOff + (k >> 1), out u, out v)) return false;
                    maxError = Math.Max(maxError, Math.Max(Math.Abs(u - q[k * 2]), Math.Abs(v - q[k * 2 + 1])));
                }
            }
            return true;
        }

        static bool Project(double X, double Y, out double u, out double v)
        {
            double w = H[6] * X + H[7] * Y + 1;
            u = v = 0;
            if (w < 0.02) return false;   // at or behind the camera
            u = (H[0] * X + H[1] * Y + H[2]) / w;
            v = (H[3] * X + H[4] * Y + H[5]) / w;
            return true;
        }

        // False when this frame's ground can't be continued (no regular grid); true when drawn or not needed.
        static bool Extend()
        {
            int n = quads = Math.Min(Marshal.ReadInt32(count), MaxQuads);
            if (n == 0) return false;
            for (int i = 0; i < n; i++) Marshal.Copy(new IntPtr(Quad(i).ToInt64() + P0), corners, i * 8, 8);

            // Start from the lowest ground quad drawn.
            int lowest = int.MinValue, start = -1;
            for (int i = 0; i < n; i++)
            {
                Read(i, q);
                if (IsGround(q) && Bottom(q) > lowest) { lowest = Bottom(q); start = i; }
            }
            if (start < 0) return false;
            if (lowest >= TargetBottom) return true;   // the ground already reaches the bottom

            int found = BuildGrid(start);
            int inGrid = 0, maxRow = int.MinValue, minRow = int.MaxValue;
            for (int i = 0; i < n; i++)
                if (gcol[i] != int.MinValue)
                {
                    members[inGrid++] = i;
                    if (grow[i] > maxRow) maxRow = grow[i];
                    if (grow[i] < minRow) minRow = grow[i];
                }
            if (found < 4 || maxRow - minRow < 1) return false;   // need at least two rows to know the perspective

            double error;
            if (!FitGrid(inGrid, out error) || error > 2.5) return false;   // not a regular grid: leave it alone

            int b0 = int.MaxValue, b1 = int.MinValue;
            for (int c = 0; c < GridSize; c++) if (cell[c, maxRow] >= 0) { b0 = Math.Min(b0, c); b1 = Math.Max(b1, c); }
            if (b0 > b1) return false;
            bottomRow = maxRow;

            // Draw with the state the stage's own ground was drawn with, then put the current one back.
            Marshal.Copy(Native.At(Rva.DrawStateA), liveA, 0, StateSize);
            Marshal.Copy(Native.At(Rva.DrawStateB), liveB, 0, StateSize);
            Marshal.Copy(snapA, kept, 0, StateSize); Marshal.Copy(kept, 0, Native.At(Rva.DrawStateA), StateSize);
            Marshal.Copy(snapB, kept, 0, StateSize); Marshal.Copy(kept, 0, Native.At(Rva.DrawStateB), StateSize);
            try { DrawRows(maxRow, b0, b1); }
            finally
            {
                Marshal.Copy(liveA, 0, Native.At(Rva.DrawStateA), StateSize);
                Marshal.Copy(liveB, 0, Native.At(Rva.DrawStateB), StateSize);
            }
            return true;
        }

        // The new rows span the whole screen width, every corner from the one perspective fit (so neighbours share
        // corners exactly), except the top edge of the first new row, which is the real bottom edge.
        static void DrawRows(int maxRow, int b0, int b1)
        {
            int u0 = Marshal.ReadInt16(Quad(cell[b0, maxRow]), 0x6A), uw = Marshal.ReadInt16(Quad(cell[b0, maxRow]), 0x6E);
            for (int c = b0 - SideColumns; c <= b1 + SideColumns; c++)
            {
                int sc = SourceColumn(c, b0, b1, u0, uw);
                int rows = 0;
                while (maxRow - rows >= 0 && cell[sc, maxRow - rows] >= 0) rows++;

                double X = c - GridOff, Ytop = maxRow - GridOff + 1, tlx, tly, trx, tr_y;
                if (!Corner(c, maxRow, X, Ytop, out tlx, out tly) || !Corner(c + 1, maxRow, X + 1, Ytop, out trx, out tr_y)) continue;
                for (int j = 1; j <= MaxRows && Math.Min(tly, tr_y) < TargetBottom; j++)
                {
                    double Y = Ytop + j, blx, bly, brx, bry;
                    if (!Project(X, Y, out blx, out bly) || !Project(X + 1, Y, out brx, out bry)) break;
                    if (Math.Abs(blx) > 4000 || Math.Abs(brx) > 4000 || bly > 4000 || bry > 4000) break;
                    if (Math.Max(Math.Max(tlx, blx), Math.Max(trx, brx)) < -ScreenMargin ||
                        Math.Min(Math.Min(tlx, blx), Math.Min(trx, brx)) > 320 + ScreenMargin) break;   // off screen

                    Texture(sc, rows, j);
                    Marshal.WriteInt16(extra, P0 + 0, (short)Math.Round(tlx));
                    Marshal.WriteInt16(extra, P0 + 2, (short)Math.Round(tly));
                    Marshal.WriteInt16(extra, P0 + 4, (short)Math.Round(trx));
                    Marshal.WriteInt16(extra, P0 + 6, (short)Math.Round(tr_y));
                    Marshal.WriteInt16(extra, P0 + 8, (short)Math.Round(blx));
                    Marshal.WriteInt16(extra, P0 + 10, (short)Math.Round(bly));
                    Marshal.WriteInt16(extra, P0 + 12, (short)Math.Round(brx));
                    Marshal.WriteInt16(extra, P0 + 14, (short)Math.Round(bry));
                    drawQuad(extra);
                    tlx = blx; tly = bly; trx = brx; tr_y = bry;
                }
            }
        }

        // A corner of the real bottom edge (column line cx) if a bottom quad has it, else the projected one.
        static bool Corner(int cx, int row, double X, double Y, out double x, out double y)
        {
            if (cx >= 0 && cx < GridSize && cell[cx, row] >= 0) { Read(cell[cx, row], nb); x = nb[4]; y = nb[5]; return true; }
            if (cx - 1 >= 0 && cx - 1 < GridSize && cell[cx - 1, row] >= 0) { Read(cell[cx - 1, row], nb); x = nb[6]; y = nb[7]; return true; }
            return Project(X, Y, out x, out y);
        }

        // Which drawn column a new column takes its texture from: inside the drawn range, itself; outside, the drawn
        // column whose texture slice continues the pattern (the ground's columns step through the texture by their
        // width, wrapping at 256), so the extension carries on sideways without a seam.
        static int SourceColumn(int c, int b0, int b1, int u0, int uw)
        {
            if (FindRow(c) >= 0) return c;
            int want = (u0 + (c - b0) * uw) & 0xFF;
            for (int s = b0; s <= b1; s++)
            {
                int r = FindRow(s);
                if (r >= 0 && (Marshal.ReadInt16(Quad(cell[s, r]), 0x6A) & 0xFF) == want) return s;
            }
            return c < b0 ? b0 : b1;   // no matching slice: the nearest edge column
        }

        static int FindRow(int c) { return c >= 0 && c < GridSize && cell[c, bottomRow] >= 0 ? bottomRow : -1; }

        // Sets extra to the source quad's look for new row j: the ground mirrored below its bottom edge (the bottom
        // strip flipped, then the one above it, ... and back again), so every seam joins matching texture. One texel
        // is left off at the strip's far edge: sampling exactly on it reads the next tile of the texture sheet (a seam).
        static void Texture(int sc, int rows, int j)
        {
            int period = 2 * rows, s = (j - 1) % period;
            bool flip = s < rows;
            int src = cell[sc, bottomRow - (flip ? s : period - 1 - s)];
            Native.Copy(Quad(src), extra, PrimSize);
            short v = Marshal.ReadInt16(extra, 0x6C), h = Marshal.ReadInt16(extra, 0x70);
            if (flip) { Marshal.WriteInt16(extra, 0x6C, (short)(v + h - 1)); Marshal.WriteInt16(extra, 0x70, (short)-(h - 1)); }
            else Marshal.WriteInt16(extra, 0x70, (short)(h - 1));
        }
    }
}
