// Hooks on GameNative functions, written once.
//
// The function's first instructions (5 bytes or a little more, whole instructions) are copied to a small stub that
// then jumps back into the function - calling the stub calls the original - and a jump to our hook takes their place.
// After that the game's code is never touched again.
//
// MonoMod's NativeDetour, used before, has no such stub for native functions: each call of the original restores the
// function's first bytes, calls it and writes the jump back ("undo-call-redo"), so the game's code was rewritten
// several times per battle frame. That works on a PC, but the translators that run the game on Android and ARM
// devices keep their own translation of the code and have to redo it on every rewrite. It remains the fallback for
// a function whose first instructions can't be moved (unknown to Length below, or already replaced by another mod).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EterniaCoop
{
    static class NativeHook
    {
        // Native code holds pointers to the hook delegates (and the fallback detours): they must stay referenced.
        static readonly List<object> keep = new List<object>();

        // Sends the function at rva to hook; returns a delegate that calls the original function.
        public static T Install<T>(int rva, T hook, string name) where T : class
        {
            keep.Add(hook);
            IntPtr target = Marshal.GetFunctionPointerForDelegate((Delegate)(object)hook);
            IntPtr original = Fixed(rva, target);
            if (original != IntPtr.Zero) return (T)(object)Marshal.GetDelegateForFunctionPointer(original, typeof(T));

            Plugin.Log.LogInfo(name + ": the function's first instructions can't be moved (changed by another mod?), hooked with MonoMod's detour instead.");
            var detour = new MonoMod.RuntimeDetour.NativeDetour(Native.At(rva), target);
            keep.Add(detour);
            return (T)(object)MonoMod.Utils.Extensions.CreateDelegate(detour.GenerateTrampoline(typeof(T).GetMethod("Invoke")), typeof(T));
        }

        // Stub layout: +0 jmp [rip+0] ; dq hook (the hook can be anywhere in memory), +16 the moved instructions, then
        // jmp back to the rest of the function. Returns the address that calls the original, Zero if nothing was done.
        static unsafe IntPtr Fixed(int rva, IntPtr target)
        {
            byte* f = (byte*)Native.At(rva);
            int len = 0;
            while (len < 5)
            {
                int n = Length(f + len);
                if (n == 0) return IntPtr.Zero;
                len += n;
            }
            IntPtr stub = Native.AllocNear(64);
            if (stub == IntPtr.Zero) return IntPtr.Zero;
            byte* s = (byte*)stub, t = s + 16;
            s[0] = 0xFF; s[1] = 0x25; *(int*)(s + 2) = 0;
            *(long*)(s + 6) = target.ToInt64();
            for (int i = 0; i < len; i++) t[i] = f[i];
            t[len] = 0xE9;
            *(int*)(t + len + 1) = (int)((f + len) - (t + len + 5));

            var jmp = new byte[5];
            jmp[0] = 0xE9;
            BitConverter.GetBytes((int)(s - (f + 5))).CopyTo(jmp, 1);
            Native.Write(rva, jmp);
            Plugin.Log.LogInfo(string.Format("Hook at GameNative+0x{0:X}: written once ({1} bytes moved).", rva, len));
            return new IntPtr(t);
        }

        // Length of the instruction at p, if it is one of the kinds functions start with (push, mov, lea, sub rsp,
        // arithmetic, SSE moves...) and runs the same anywhere. 0 for anything else: jumps, calls, RIP-relative
        // operands, instructions this doesn't know.
        static unsafe int Length(byte* p)
        {
            int i = 0;
            bool op16 = false, rexW = false;
            while (p[i] == 0x66 || p[i] == 0xF2 || p[i] == 0xF3)
            {
                if (p[i] == 0x66) op16 = true;
                if (++i > 3) return 0;
            }
            if ((p[i] & 0xF0) == 0x40) { rexW = (p[i] & 8) != 0; i++; }
            byte op = p[i++];
            bool modrm = false;
            int imm = 0;
            if (op == 0x0F)
            {
                byte op2 = p[i++];
                // movups/movaps (load, store), xorps, movdqa/movdqu, movzx, movsx, imul, long nop, cmovcc
                if (op2 == 0x10 || op2 == 0x11 || op2 == 0x28 || op2 == 0x29 || op2 == 0x57 || op2 == 0x6F || op2 == 0x7F ||
                    op2 == 0xB6 || op2 == 0xB7 || op2 == 0xBE || op2 == 0xBF || op2 == 0xAF || op2 == 0x1F || (op2 & 0xF0) == 0x40) modrm = true;
                else return 0;
            }
            else if (op >= 0x50 && op <= 0x5F) { }                                                 // push / pop
            else if (op < 0x40 && (op & 7) < 4) modrm = true;                                      // add, or, and, sub, xor, cmp...
            else if (op < 0x40 && (op & 7) == 4) imm = 1;                                          // ... al, imm8
            else if (op < 0x40 && (op & 7) == 5) imm = op16 ? 2 : 4;                               // ... eax, imm32
            else if (op == 0x63 || (op >= 0x84 && op <= 0x8B) || op == 0x8D) modrm = true;         // movsxd, test, xchg, mov, lea
            else if (op == 0x80 || op == 0x83 || op == 0xC6 || op == 0xC0 || op == 0xC1 || op == 0x6B) { modrm = true; imm = 1; }
            else if (op == 0x81 || op == 0xC7 || op == 0x69) { modrm = true; imm = op16 ? 2 : 4; }
            else if (op >= 0xB0 && op <= 0xB7) imm = 1;                                            // mov r8, imm8
            else if (op >= 0xB8 && op <= 0xBF) imm = rexW ? 8 : op16 ? 2 : 4;                      // mov r, imm
            else return 0;

            if (modrm)
            {
                byte m = p[i++];
                int mod = m >> 6, rm = m & 7;
                if (mod == 0 && rm == 5) return 0;   // RIP-relative: can't be moved
                if (mod != 3 && rm == 4 && (p[i++] & 7) == 5 && mod == 0) i += 4;   // SIB byte, with a disp32 base
                if (mod == 1) i += 1;
                else if (mod == 2) i += 4;
            }
            return i + imm;
        }
    }
}
