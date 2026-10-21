"""Print instructions in a function that use a given [reg+disp], with N lines of context.
  python -I ctx.py <func_rva> <disp>[,<disp>...] [context]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn
from capstone.x86 import X86_OP_MEM

f = gn.func_of(int(sys.argv[1], 16))
disps = {int(d, 16) for d in sys.argv[2].split(",")}
ctx = int(sys.argv[3]) if len(sys.argv) > 3 else 4
insns = list(gn.md.disasm(gn.img[f[0]:f[1]], f[0]))
marks = [i for i, ins in enumerate(insns) if any(op.type == X86_OP_MEM and op.mem.base != gn.X86_REG_RIP and op.mem.disp in disps for op in ins.operands)]
shown = set()
print("; func 0x%X-0x%X" % f)
for m in marks:
    lo, hi = max(0, m - ctx), min(len(insns), m + ctx + 1)
    if lo > 0 and (lo - 1) not in shown: print("   ...")
    for i in range(lo, hi):
        if i in shown: continue
        shown.add(i)
        print(("=> " if i == m else "   ") + gn.fmt(insns[i]))
