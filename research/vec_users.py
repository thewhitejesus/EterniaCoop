"""Find functions that compute a vector's element count with the /20 (0xCCCC...CD) or /24 (0xAAAA...AB)
magic multiply next to reads of [reg+0x48]/[reg+0x50] (sticks) or [reg+0x18]/[reg+0x20] (remap)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn
from capstone.x86 import X86_OP_MEM, X86_OP_IMM

PAIRS = {"sticks": (0x48, 0x50, 0xCCCCCCCCCCCCCCCD), "remap": (0x18, 0x20, 0xAAAAAAAAAAAAAAAB), "buttons": (0x30, 0x38, 0xCCCCCCCCCCCCCCCD)}
for s, e in gn.FUNCS:
    disps, imms = set(), set()
    for ins in gn.md.disasm(gn.img[s:e], s):
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.base != gn.X86_REG_RIP:
                disps.add(op.mem.disp)
            elif op.type == X86_OP_IMM:
                imms.add(op.imm & 0xFFFFFFFFFFFFFFFF)
    hits = [n for n, (b, en, m) in PAIRS.items() if b in disps and en in disps and m in imms]
    if hits:
        print("func 0x%06X-0x%06X  %s" % (s, e, ",".join(hits)))
