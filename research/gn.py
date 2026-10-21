"""GameNative.dll research helper (Capstone + pefile).

  python -I gn.py index                 build/refresh the xref index (cached in gn_index.pkl)
  python -I gn.py dis <rva> [count]     disassemble from an RVA
  python -I gn.py func <rva>            disassemble the whole function containing an RVA
  python -I gn.py refs <rva> [rva2]     instructions that reference [rva, rva2] (rip-relative data or call/jmp target)
  python -I gn.py export <name>         RVA of an export
"""
import os, pickle, sys, bisect
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_OP_IMM, X86_REG_RIP

GAME = "Tales of Eternia Remastered"


def find_game():
    """$TOE_GAME_DIR if set, else every Steam library listed in libraryfolders.vdf."""
    if os.environ.get("TOE_GAME_DIR"):
        return os.environ["TOE_GAME_DIR"]
    import re, winreg
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
        steam = winreg.QueryValueEx(key, "SteamPath")[0]
    libraries = [steam]
    vdf = os.path.join(steam, "steamapps", "libraryfolders.vdf")
    if os.path.exists(vdf):
        libraries += [p.replace("\\\\", "\\") for p in re.findall(r'"path"\s+"([^"]+)"', open(vdf, encoding="utf-8").read())]
    for lib in libraries:
        d = os.path.join(lib, "steamapps", "common", GAME)
        if os.path.exists(os.path.join(d, GAME + ".exe")):
            return d
    sys.exit("Couldn't find the game; set TOE_GAME_DIR to its folder.")


DLL = os.path.join(find_game(), GAME + "_Data", "Plugins", "x86_64", "GameNative.dll")
HERE = os.path.dirname(os.path.abspath(__file__))
INDEX = os.path.join(HERE, "gn_index.pkl")

pe = pefile.PE(DLL, fast_load=True)
pe.parse_data_directories(directories=[
    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXPORT"],
    pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
img = pe.get_memory_mapped_image()
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True


def functions():
    """(start, end) of every function from .pdata, merged and sorted."""
    fs = sorted({(e.struct.BeginAddress, e.struct.EndAddress) for e in pe.DIRECTORY_ENTRY_EXCEPTION})
    return fs


FUNCS = functions()
STARTS = [f[0] for f in FUNCS]


def func_of(rva):
    i = bisect.bisect_right(STARTS, rva) - 1
    if i >= 0 and FUNCS[i][0] <= rva < FUNCS[i][1]:
        return FUNCS[i]
    return None


def build_index():
    refs = []  # (target, insn_rva, kind)
    for n, (s, e) in enumerate(FUNCS):
        for ins in md.disasm(img[s:e], s):
            for op in ins.operands:
                if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                    refs.append((ins.address + ins.size + op.mem.disp, ins.address, "mem"))
                elif op.type == X86_OP_IMM and ins.group(1) or (op.type == X86_OP_IMM and ins.mnemonic in ("call", "jmp")):
                    refs.append((op.imm, ins.address, ins.mnemonic))
    refs.sort()
    with open(INDEX, "wb") as f:
        pickle.dump(refs, f)
    return refs


def load_index():
    if not os.path.exists(INDEX):
        return build_index()
    with open(INDEX, "rb") as f:
        return pickle.load(f)


def fmt(ins):
    extra = ""
    for op in ins.operands:
        if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
            extra = "    ; -> 0x%X" % (ins.address + ins.size + op.mem.disp)
    return "0x%08X: %-24s %s %s%s" % (ins.address, ins.bytes.hex(" "), ins.mnemonic, ins.op_str, extra)


def dis(rva, count=40):
    for k, ins in enumerate(md.disasm(img[rva:rva + count * 15], rva)):
        if k >= count:
            break
        print(fmt(ins))


def func(rva):
    f = func_of(rva)
    if not f:
        print("no .pdata function contains 0x%X" % rva); return
    print("; function 0x%X - 0x%X (%d bytes)" % (f[0], f[1], f[1] - f[0]))
    for ins in md.disasm(img[f[0]:f[1]], f[0]):
        print(("=> " if ins.address <= rva < ins.address + ins.size else "   ") + fmt(ins))


def refs(lo, hi=None):
    hi = lo if hi is None else hi
    idx = load_index()
    i = bisect.bisect_left(idx, (lo, -1, ""))
    while i < len(idx) and idx[i][0] <= hi:
        t, a, kind = idx[i]
        f = func_of(a)
        ins = next(md.disasm(img[a:a + 15], a))
        print("target 0x%X  %-4s  in func 0x%X  %s" % (t, kind, f[0] if f else 0, fmt(ins)))
        i += 1


def export(name):
    for s in pe.DIRECTORY_ENTRY_EXPORT.symbols:
        if s.name and s.name.decode() == name:
            print("0x%X" % s.address); return s.address


if __name__ == "__main__":
    cmd, args = sys.argv[1], [int(a, 16) if a.lower().startswith("0x") else a for a in sys.argv[2:]]
    if cmd == "index":
        r = build_index(); print("indexed %d refs over %d functions" % (len(r), len(FUNCS)))
    elif cmd == "dis":
        dis(args[0], int(args[1]) if len(args) > 1 else 40)
    elif cmd == "func":
        func(args[0])
    elif cmd == "refs":
        refs(*args)
    elif cmd == "export":
        export(args[0])
