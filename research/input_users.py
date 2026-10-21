"""List functions that load the input object pointer (global 0x6449F8) and show which
object fields they touch afterwards (+0x10 count, +0x18 remap, +0x30 buttons, +0x48 sticks)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn
from capstone.x86 import X86_OP_MEM, X86_OP_REG

GLOBAL = 0x6449F8
FIELDS = {0x10: "count", 0x18: "remap", 0x30: "buttons", 0x48: "sticks", 0x50: "sticks_end", 0x38: "buttons_end"}

idx = gn.load_index()
sites = sorted({a for t, a, k in idx if t == GLOBAL})
seen = set()
for a in sites:
    f = gn.func_of(a)
    if not f or f in seen:
        continue
    seen.add(f)
    # Track the register the global is loaded into, then fields read off it within the function.
    used = set()
    regs = set()
    for ins in gn.md.disasm(gn.img[f[0]:f[1]], f[0]):
        ops = ins.operands
        if ins.mnemonic == "mov" and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_MEM \
                and ops[1].mem.base == gn.X86_REG_RIP and ins.address + ins.size + ops[1].mem.disp == GLOBAL:
            regs.add(ops[0].reg)
            continue
        for op in ops:
            if op.type == X86_OP_MEM and op.mem.base in regs and op.mem.disp in FIELDS:
                used.add(FIELDS[op.mem.disp])
    print("func 0x%06X-0x%06X  fields: %s" % (f[0], f[1], ", ".join(sorted(used)) or "-"))
