"""Generates src/Sites.Generated.cs: a code signature for every GameNative spot the mod uses.

  python -I gen_sigs.py            write ..\\src\\Sites.Generated.cs from the installed game
  python -I gen_sigs.py --check    only report (pattern sizes, uniqueness)

Each site is the address of one instruction. Its signature is that instruction plus neighbouring whole instructions
(same function), with every byte that moves between builds wildcarded: RIP-relative displacements and relative
branch/call targets. The window grows until the signature occurs exactly once in .text - the same rule the mod
applies at runtime (src/Sites.cs), so a game update that shifts code still lets the mod find each spot, and a spot
it can't find uniquely keeps its feature off.

Data sites are instructions that reach a global RIP-relative; the mod reads the global's new address from them.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn  # loads GameNative.dll (game found through Steam / TOE_GAME_DIR)
from capstone.x86 import X86_OP_MEM, X86_OP_IMM, X86_REG_RIP

# name, group, rva, kind[, data target, addend]
#   kind "code": an instruction the mod checks or patches; "func": a function entry the mod hooks or calls;
#   "data": an instruction that reads/writes a global - the global is target (minus addend for e.g. Pad+4)
SITES = [
    # Core: FUN_18940 input setup immediates (player count, remap table, stick buffer)
    ("Count",        "Core", 0x18A41, "code"),
    ("RemapCmp1",    "Core", 0x18A68, "code"),
    ("RemapLea",     "Core", 0x18A6E, "code"),
    ("RemapCmp2",    "Core", 0x18A93, "code"),
    ("RemapMovEdx",  "Core", 0x18A99, "code"),
    ("RemapMovEax",  "Core", 0x18AA9, "code"),
    ("StickCmp1",    "Core", 0x18B76, "code"),
    ("StickCmp2",    "Core", 0x18B97, "code"),
    ("StickMovEdx",  "Core", 0x18B9D, "code"),
    ("StickMovEax",  "Core", 0x18BAD, "code"),
    ("LatchGuard",   "Core", 0x57AAE, "code"),      # FUN_57AA0 per-player guard (spinning-camera fix)
    ("RemapGuard",   "Core", 0x57F2F, "code"),      # FUN_57F20
    ("InputObject",  "Core", 0x5D2FB, "data", 0x6449F8, 0),
    ("PartyIds",     "Core", 0x84BE3, "data", 0x11CAD10, 0),
    ("SlotModes",    "Core", 0x84BD9, "data", 0x11CAD38, 0),
    ("Area",         "Core", 0x84BA4, "data", 0x11CAFC4, 0),
    # KeepModes: battle results reset slots 2-4 to Auto
    # three copies of the same code, told apart by the slot they write: constrained to SlotModes + 1/2/3
    ("ResetSlot2",   "KeepModes", 0x84C91, "data", 0x11CAD39, 0, "SlotModes", 1),
    ("ResetSlot3",   "KeepModes", 0x84D31, "data", 0x11CAD3A, 0, "SlotModes", 2),
    ("ResetSlot4",   "KeepModes", 0x84DD1, "data", 0x11CAD3B, 0, "SlotModes", 3),
    # MenuTab: Skills menu "leader only" gates
    # five identical inlined copies of one gate; the signature must match exactly these five (all get patched)
    ("TabJe",        "MenuTab", [0x22963D, 0x229822, 0x229E96, 0x22A0D1, 0x22B30E], "code"),
    ("TabCursor1",   "MenuTab", 0x2292F2, "code"),
    ("TabCursor2",   "MenuTab", 0x22B5EF, "code"),
    ("TabConfirm",   "MenuTab", 0x229161, "code"),
    ("TabDraw",      "MenuTab", 0x224B6A, "code"),
    # CoopBattle
    ("BattleCommands", "CoopBattle", 0x1121E0, "func"),
    ("BuildPad",       "CoopBattle", 0x19DBC0, "func"),
    ("Pad",            "CoopBattle", 0xD2DAD, "data", 0x8C9180, 0),
    ("PadHeld",        "CoopBattle", 0x1121EF, "data", 0x8C9184, 4),   # in FUN_1121E0; also gives Pad
    ("BattleFlags",    "CoopBattle", 0xD2EE3, "data", 0x12CEB7A, 0),
    ("MenuOpener",     "CoopBattle", 0x110130, "data", 0x12CEB8C, 0),
    ("CmdBlocks",      "CoopBattle", 0x112204, "data", 0x12CEBA0, 0),
    ("ActorArray",     "CoopBattle", 0xD3090, "data", 0x12CECD8, 0),
    # Camera (co-op camera): the battle camera's normal path loads the followed actor's x here, then calls the
    # scroll function (FUN_D6420); the zoom setting is copied to the zoom target right after. 0x12CEB7F = camera mode
    # (0 normal, >= 2 scripted: spells, mystic artes).
    ("CamTargetX",     "Camera", 0xD3BA3, "code"),
    ("CamZoom",        "Camera", 0xD3BBA, "data", 0x12CEB78, 0),
    ("CamScroll",      "Camera", 0xD3BAD, "data", 0x12CEAB8, 0),     # scroll = -(centre x >> 4), stored after FUN_D6420
    ("CamMode",        "Camera", 0xD3B28, "data", 0x12CEB7F, 0),
    # Rumble
    ("ApplyHit",       "Rumble", 0x8E320, "func"),
    ("HitPaused",      "Rumble", 0x8E32A, "data", 0x12CEB7A, 0),
    ("RumbleAtkBlock", "Rumble", 0x8E895, "code"),
    ("RumbleAtkSide",  "Rumble", 0x8E89B, "code"),
    ("GameOptions",    "Rumble", 0x8E8A1, "data", 0x6449E0, 0),
    ("RumbleStrength", "Rumble", 0x8E8A8, "data", 0x862E78, 0),
    ("RumbleOption",   "Rumble", 0x8E8B2, "code"),
]

MAX_LEN = 96
MIN_FIXED = 16   # unique isn't enough: a short signature could match elsewhere after an update
HOOK_ZONE = 16   # other mods hook functions by overwriting their first bytes: keep context out of there

text = next(s for s in gn.pe.sections if s.Name.rstrip(b"\0") == b".text")
T0, T1 = text.VirtualAddress, text.VirtualAddress + text.Misc_VirtualSize
TEXT = bytes(gn.img[T0:T1])


def insns_of_func(rva):
    f = gn.func_of(rva)
    lo, hi = (f if f else (max(T0, rva - 256), min(T1, rva + 256)))
    return list(gn.md.disasm(bytes(gn.img[lo:hi]), lo))


def rip_disp_offset(ins):
    """Offset of the 4-byte RIP-relative displacement in the instruction, or None. Found by value: Capstone's
    disp_offset isn't reliable for every encoding (e.g. 66-prefixed stores)."""
    for op in ins.operands:
        if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
            raw = (op.mem.disp & 0xFFFFFFFF).to_bytes(4, "little")
            k = bytes(ins.bytes).find(raw)
            assert k > 0 and bytes(ins.bytes).find(raw, k + 1) < 0, "0x%X: ambiguous displacement" % ins.address
            return k
    return None


def masked(ins):
    """Instruction bytes with moving parts wildcarded (None): RIP-relative displacements and the targets of
    relative jumps and calls."""
    b = [x for x in ins.bytes]
    k = rip_disp_offset(ins)
    if k is not None:
        for j in range(k, k + 4): b[j] = None
    if (ins.group(1) or ins.group(2) or ins.group(7)) and any(op.type == X86_OP_IMM for op in ins.operands):
        size = 1 if len(ins.bytes) == 2 else 4   # jcc/jmp rel8 or rel32 (call/jmp/jcc)
        for j in range(len(b) - size, len(b)): b[j] = None
    return b


def matches(pattern, accept=None, limit=16):
    """Start rvas of the pattern (list of int/None) in .text that accept(start rva) allows, up to limit."""
    first = next(i for i, v in enumerate(pattern) if v is not None)
    # anchor: longest fixed run
    best, run, start = (0, 0), 0, 0
    for i, v in enumerate(pattern + [None]):
        if v is None:
            if run > best[1] - best[0]: best = (start, start + run)
            run = 0; start = i + 1
        else:
            run += 1
    a0, a1 = best
    key = bytes(pattern[a0:a1])
    hits, pos = [], TEXT.find(key)
    while pos >= 0:
        s = pos - a0
        if 0 <= s and s + len(pattern) <= len(TEXT) and all(v is None or TEXT[s + k] == v for k, v in enumerate(pattern)) \
                and (accept is None or accept(T0 + s)):
            hits.append(T0 + s)
            if len(hits) >= limit: return hits
        pos = TEXT.find(key, pos + 1)
    return hits


def signature(rva, want_target=None, copies=None):
    """want_target: for constrained data sites, the address the instruction must point at.
    copies: for repeated code, every rva the signature must match (and nothing else)."""
    insns = insns_of_func(rva)
    idx = next(i for i, ins in enumerate(insns) if ins.address <= rva < ins.address + ins.size)
    assert insns[idx].address == rva, "0x%X is not an instruction start" % rva
    lo, hi = idx, idx + 1
    while True:
        pat = sum((masked(i) for i in insns[lo:hi]), [])
        start = insns[lo].address
        accept = None
        if want_target is not None:
            off, ins = rva - start, insns[idx]
            k = rip_disp_offset(ins)
            accept = lambda s: s + off + ins.size + int.from_bytes(bytes(gn.img[s + off + k:s + off + k + 4]), "little", signed=True) == want_target
        if sum(v is not None for v in pat) >= MIN_FIXED:
            found = [s + (rva - start) for s in matches(pat, accept)]
            if found == sorted(copies or [rva]):
                return start, pat, insns[idx]
        if len(pat) > MAX_LEN: raise SystemExit("0x%X: no unique signature within %d bytes" % (rva, MAX_LEN))
        # grow: after first (later code is less likely to be shared), then before - but not back into the first
        # HOOK_ZONE bytes of the function, where other mods put their hooks (unless the site is the function entry)
        f = gn.func_of(rva)
        can_back = lo > 0 and (f is None or insns[lo - 1].address >= f[0] + HOOK_ZONE or rva < f[0] + HOOK_ZONE)
        if hi < len(insns) and (hi - idx <= idx - lo or not can_back): hi += 1
        elif can_back: lo -= 1
        else: raise SystemExit("0x%X: function too small for a unique signature" % rva)


def main():
    rows = []
    for site in SITES:
        name, group, rva, kind = site[:4]
        copies = rva if isinstance(rva, list) else None
        rva = copies[0] if copies else rva
        constraint = site[6:8] if len(site) > 6 else ("", 0)
        start, pat, ins = signature(rva, site[4] if constraint[0] else None, copies)
        disp_at, end_at = 0, ins.size
        if kind == "data":
            target, addend = site[4], site[5]
            k = rip_disp_offset(ins)
            assert k is not None, name + ": not a RIP-relative instruction"
            got = ins.address + ins.size + int.from_bytes(bytes(ins.bytes[k:k + 4]), "little", signed=True)
            assert got == target, "%s: points at 0x%X, expected 0x%X" % (name, got, target)
            disp_at, end_at = k, ins.size
        hexpat = " ".join("??" if v is None else "%02X" % v for v in pat)
        rows.append((name, group, copies or [rva], rva - start, kind, disp_at, end_at, site[5] if kind == "data" else 0,
                     constraint[0], constraint[1], hexpat))
        print("%-15s %-10s 0x%06X  %3d bytes  %s%s" % (name, group, rva, len(pat), kind,
                                                      "  x%d" % len(copies) if copies else ""))
    if "--check" in sys.argv: return

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Sites.Generated.cs")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("// Generated by research/gen_sigs.py from GameNative.dll SHA-256 %s... - do not edit by hand.\n" % gn_hash()[:16])
        f.write("// Site(name, feature, rvas in the known build (repeated code: every copy), offset of the instruction in the\n")
        f.write("//      pattern, kind, displacement offset (data sites), instruction length, addend (data: target = instruction\n")
        f.write("//      end + disp - addend), constraint: only matches pointing at <site> + <offset> count, pattern)\n")
        f.write("namespace EterniaCoop\n{\n    static partial class Sites\n    {\n        static readonly Site[] All =\n        {\n")
        for name, group, rvas, off, kind, d, e, add, cname, coff, hexpat in rows:
            f.write('            new Site("%s", "%s", new[] { %s }, %d, SiteKind.%s, %d, %d, %d, %s, %d,\n                "%s"),\n'
                    % (name, group, ", ".join("0x%X" % r for r in rvas), off, kind.capitalize(), d, e, add,
                       '"%s"' % cname if cname else "null", coff, hexpat))
        f.write("        };\n    }\n}\n")
    print("wrote " + os.path.normpath(out))


def gn_hash():
    import hashlib
    with open(gn.DLL, "rb") as fh:
        return hashlib.sha256(fh.read()).hexdigest().upper()


if __name__ == "__main__":
    main()
