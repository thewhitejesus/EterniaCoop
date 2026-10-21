"""For each function that loads the input object global, show how it uses the player count
([obj+0x10]) with a few instructions of context."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn
from capstone.x86 import X86_OP_MEM, X86_OP_REG

GLOBAL = 0x6449F8
idx = gn.load_index()
funcs = sorted({gn.func_of(a) for t, a, k in idx if t == GLOBAL} - {None})
for f in funcs:
    insns = list(gn.md.disasm(gn.img[f[0]:f[1]], f[0]))
    regs, marks = set(), []
    for n, ins in enumerate(insns):
        ops = ins.operands
        if ins.mnemonic == "mov" and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_MEM \
                and ops[1].mem.base == gn.X86_REG_RIP and ins.address + ins.size + ops[1].mem.disp == GLOBAL:
            regs.add(ops[0].reg)
        elif any(op.type == X86_OP_MEM and op.mem.base in regs and op.mem.disp == 0x10 for op in ops):
            marks.append(n)
    if not marks:
        continue
    print("== func 0x%X-0x%X" % f)
    for m in marks:
        for i in range(m, min(len(insns), m + 4)):
            print(("=> " if i == m else "   ") + gn.fmt(insns[i]))
