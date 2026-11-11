# Sampling profiler for the running game: pauses its busiest thread about a thousand times a second, reads where it is
# (the instruction pointer), and reports where the time goes - by module, by GameNative function (from its .pdata), and
# by code outside any module (code other mods generated: stubs, JIT-compiled C#). Read-only; nothing in the game changes.
#   powershell -ExecutionPolicy Bypass -File research\sample_game.ps1 [-Seconds 10] [-Top 25]
param([int]$Seconds = 10, [int]$Top = 25)
$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Sampler
{
    [DllImport("kernel32")] static extern IntPtr OpenThread(int access, bool inherit, int id);
    [DllImport("kernel32")] static extern IntPtr OpenProcess(int access, bool inherit, int id);
    [DllImport("kernel32")] static extern int SuspendThread(IntPtr t);
    [DllImport("kernel32")] static extern int ResumeThread(IntPtr t);
    [DllImport("kernel32")] static extern bool GetThreadContext(IntPtr t, IntPtr ctx);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32")] static extern IntPtr VirtualQueryEx(IntPtr p, IntPtr addr, out MBI info, IntPtr size);
    [DllImport("kernel32")] static extern bool QueryPerformanceCounter(out long c);
    [DllImport("kernel32")] static extern bool QueryPerformanceFrequency(out long f);
    [DllImport("kernel32")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);

    // Return-address candidates: the first 96 stack slots of each sample (with Run's stacks).
    public static List<long[]> Stacks = new List<long[]>();

    [StructLayout(LayoutKind.Sequential)]
    public struct MBI { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort Partition; public IntPtr RegionSize; public uint State, Protect, Type; }

    // Instruction pointers of the thread, sampled for the given time.
    public static List<long> Run(int pid, int tid, int seconds)
    {
        var rips = new List<long>();
        Stacks.Clear();
        IntPtr t = OpenThread(0x0002 | 0x0008 | 0x0040, false, tid);   // suspend/resume, get context, query
        if (t == IntPtr.Zero) throw new Exception("OpenThread failed");
        IntPtr proc = OpenProcess(0x0010, false, pid);                   // read memory
        var buf = new byte[96 * 8];
        IntPtr got;
        IntPtr raw = Marshal.AllocHGlobal(1232 + 16);
        IntPtr ctx = new IntPtr((raw.ToInt64() + 15) & ~15L);              // CONTEXT must be 16-byte aligned
        long f, start, now;
        QueryPerformanceFrequency(out f); QueryPerformanceCounter(out start);
        try
        {
            do
            {
                Marshal.WriteInt32(ctx, 0x30, 0x100001);                       // ContextFlags = CONTEXT_CONTROL
                if (SuspendThread(t) >= 0)
                {
                    if (GetThreadContext(t, ctx))
                    {
                        rips.Add(Marshal.ReadInt64(ctx, 0xF8));                     // Rip
                        long rsp = Marshal.ReadInt64(ctx, 0x98);                    // Rsp
                        var slots = new long[96];
                        if (ReadProcessMemory(proc, new IntPtr(rsp), buf, new IntPtr(buf.Length), out got))
                            for (int i = 0; i < 96; i++) slots[i] = BitConverter.ToInt64(buf, i * 8);
                        Stacks.Add(slots);
                    }
                    ResumeThread(t);
                }
                System.Threading.Thread.Sleep(1);
                QueryPerformanceCounter(out now);
            } while (now - start < f * seconds);
        }
        finally { CloseHandle(t); CloseHandle(proc); Marshal.FreeHGlobal(raw); }
        return rips;
    }

    // Whether addr follows a call instruction (call rel32, call [rip+x], call reg, call [reg+x]): a return address.
    public static bool AfterCall(int pid, long addr)
    {
        IntPtr p = OpenProcess(0x0010, false, pid);
        var b = new byte[8];
        IntPtr got;
        bool ok = ReadProcessMemory(p, new IntPtr(addr - 8), b, new IntPtr(8), out got);
        CloseHandle(p);
        if (!ok) return false;
        if (b[3] == 0xE8) return true;                                    // call rel32
        if (b[2] == 0xFF && b[3] == 0x15) return true;                    // call [rip+x]
        if (b[6] == 0xFF && (b[7] & 0xF8) == 0xD0) return true;           // call reg
        if (b[5] == 0xFF && (b[6] & 0xF8) == 0x50) return true;           // call [reg+x8]
        if (b[2] == 0xFF && (b[3] & 0xF8) == 0x90) return true;           // call [reg+x32]
        return false;
    }

    // Allocation base of the memory holding addr (groups code that isn't in any module).
    public static long AllocBase(int pid, long addr)
    {
        IntPtr p = OpenProcess(0x0400, false, pid);
        MBI m;
        VirtualQueryEx(p, new IntPtr(addr), out m, new IntPtr(Marshal.SizeOf(typeof(MBI))));
        CloseHandle(p);
        return m.AllocationBase.ToInt64();
    }
}
'@

$p = Get-Process "Tales of Eternia Remastered"
$p.Refresh()
# The busiest thread over a short window (the game's main thread).
$before = @{}; foreach ($t in $p.Threads) { $before[$t.Id] = $t.TotalProcessorTime }
Start-Sleep -Milliseconds 1500
$p.Refresh()
$busy = $p.Threads | Sort-Object { if ($before.ContainsKey($_.Id)) { ($_.TotalProcessorTime - $before[$_.Id]).TotalMilliseconds } else { 0 } } -Descending | Select-Object -First 1
Write-Host ("Sampling thread {0} for {1} s..." -f $busy.Id, $Seconds)
$rips = [Sampler]::Run($p.Id, $busy.Id, $Seconds)
$n = $rips.Count

$mods = $p.Modules | ForEach-Object { [pscustomobject]@{ Name = $_.ModuleName; Base = $_.BaseAddress.ToInt64(); End = $_.BaseAddress.ToInt64() + $_.ModuleMemorySize } }
$gn = $mods | Where-Object Name -eq "GameNative.dll"

# GameNative function starts from the file's .pdata (sorted RUNTIME_FUNCTION entries).
$file = [IO.File]::ReadAllBytes((Join-Path (Split-Path $p.Path) "Tales of Eternia Remastered_Data\Plugins\x86_64\GameNative.dll"))
$pe = [BitConverter]::ToInt32($file, 0x3C); $secs = [BitConverter]::ToInt16($file, $pe + 6); $tbl = $pe + 24 + [BitConverter]::ToInt16($file, $pe + 20)
$excRva = [BitConverter]::ToInt32($file, $pe + 24 + 112 + 24); $excSize = [BitConverter]::ToInt32($file, $pe + 24 + 112 + 28)
$excRaw = 0
for ($s = 0; $s -lt $secs; $s++) { $h = $tbl + $s * 40; $va = [BitConverter]::ToInt32($file, $h + 12); $sz = [BitConverter]::ToInt32($file, $h + 8)
    if ($excRva -ge $va -and $excRva -lt $va + $sz) { $excRaw = [BitConverter]::ToInt32($file, $h + 20) + ($excRva - $va) } }
$starts = New-Object 'System.Collections.Generic.List[int]'
for ($k = 0; $k -lt $excSize; $k += 12) { $starts.Add([BitConverter]::ToInt32($file, $excRaw + $k)) }
function FuncOf([int]$rva) { $i = $starts.BinarySearch($rva); if ($i -lt 0) { $i = (-bnot $i) - 1 }; if ($i -lt 0) { return 0 }; return $starts[$i] }

# ntdll's exports (name by address), to name the system functions the thread is in.
$nt = $mods | Where-Object Name -eq "ntdll.dll"
$ntExports = New-Object 'System.Collections.Generic.SortedList[long,string]'
if ($nt) {
    $ntFile = [IO.File]::ReadAllBytes("$env:WINDIR\System32\ntdll.dll")
    $npe = [BitConverter]::ToInt32($ntFile, 0x3C); $nsecs = [BitConverter]::ToInt16($ntFile, $npe + 6); $ntbl = $npe + 24 + [BitConverter]::ToInt16($ntFile, $npe + 20)
    function RawOf([int]$rva) { for ($s = 0; $s -lt $nsecs; $s++) { $h = $ntbl + $s * 40; $va = [BitConverter]::ToInt32($ntFile, $h + 12); $sz = [BitConverter]::ToInt32($ntFile, $h + 16)
        if ($rva -ge $va -and $rva -lt $va + $sz) { return [BitConverter]::ToInt32($ntFile, $h + 20) + ($rva - $va) } }; return -1 }
    $ed = RawOf ([BitConverter]::ToInt32($ntFile, $npe + 24 + 112))
    $cnt = [BitConverter]::ToInt32($ntFile, $ed + 24); $funcs = RawOf ([BitConverter]::ToInt32($ntFile, $ed + 28))
    $names = RawOf ([BitConverter]::ToInt32($ntFile, $ed + 32)); $ords = RawOf ([BitConverter]::ToInt32($ntFile, $ed + 36))
    for ($k = 0; $k -lt $cnt; $k++) {
        $nr = RawOf ([BitConverter]::ToInt32($ntFile, $names + $k * 4)); $e = $nr; while ($ntFile[$e] -ne 0) { $e++ }
        $ord = [BitConverter]::ToUInt16($ntFile, $ords + $k * 2); $fr = [BitConverter]::ToInt32($ntFile, $funcs + $ord * 4)
        $ntExports[$nt.Base + $fr] = [Text.Encoding]::ASCII.GetString($ntFile, $nr, $e - $nr)
    }
}
function NtName([long]$addr) {
    $keys = $ntExports.Keys; $lo = 0; $hi = $keys.Count - 1; $best = -1
    while ($lo -le $hi) { $mid = [int](($lo + $hi) / 2); if ($keys[$mid] -le $addr) { $best = $mid; $lo = $mid + 1 } else { $hi = $mid - 1 } }
    if ($best -lt 0) { return "?" }; return "{0}+0x{1:X}" -f $ntExports.Values[$best], ($addr - $keys[$best])
}

$byModule = @{}; $byFunc = @{}; $byAnon = @{}; $byNt = @{}
foreach ($r in $rips) {
    $m = $mods | Where-Object { $r -ge $_.Base -and $r -lt $_.End } | Select-Object -First 1
    $name = if ($m) { $m.Name } else { "(no module)" }
    $byModule[$name] = 1 + [int]$byModule[$name]
    if ($m -and $m.Name -eq "GameNative.dll") { $f = "GameNative+0x{0:X}" -f (FuncOf ([int]($r - $gn.Base))); $byFunc[$f] = 1 + [int]$byFunc[$f] }
    elseif (-not $m) { $b = "alloc 0x{0:X}" -f [Sampler]::AllocBase($p.Id, $r); $byAnon[$b] = 1 + [int]$byAnon[$b] }
    elseif ($m.Name -eq "ntdll.dll") { $f = NtName $r; $f = $f -replace '\+0x[0-9A-F]+$', ''; $byNt[$f] = 1 + [int]$byNt[$f] }
}
function Show($title, $table) {
    Write-Host "`n$title"
    $table.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top | ForEach-Object { Write-Host ("  {0,5:N1}%  {1}" -f (100.0 * $_.Value / $n), $_.Key) }
}
Write-Host "$n samples"
Show "By module:" $byModule
Show "ntdll functions:" $byNt

# Who is calling: for samples inside ntdll / KernelBase / kernel32, the first stack value pointing into another module
# (or into code outside any module) - the nearest caller that isn't Windows itself.
$sys = @("ntdll.dll", "KERNELBASE.dll", "KERNEL32.DLL")
$byCaller = @{}
for ($i = 0; $i -lt $n; $i++) {
    $r = $rips[$i]
    $m = $mods | Where-Object { $r -ge $_.Base -and $r -lt $_.End } | Select-Object -First 1
    if (-not $m -or $sys -notcontains $m.Name) { continue }
    $caller = "?"
    $chain = @()
    foreach ($v in [Sampler]::Stacks[$i]) {
        if ($v -lt 0x10000 -or -not [Sampler]::AfterCall($p.Id, $v)) { continue }
        $cm = $mods | Where-Object { $v -ge $_.Base -and $v -lt $_.End } | Select-Object -First 1
        $chain += $(if ($cm) { "{0}+0x{1:X}" -f $cm.Name, ($v - $cm.Base) } else { "code@0x{0:X}" -f $v })
        if ($chain.Count -ge 4) { break }
    }
    if ($chain.Count -gt 0) { $caller = $chain -join " <- " }
    $byCaller[$caller] = 1 + [int]$byCaller[$caller]
}
Show "Callers of the Windows functions (nearest non-Windows return address on the stack):" $byCaller
Show "GameNative functions:" $byFunc
Show "Code outside modules (by allocation):" $byAnon
