"""Find every instruction that multiplies by a given constant (e.g. 0xAE = entries per player),
grouped by function. Usage: python -I find_imul.py 0xae"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn
from capstone.x86 import X86_OP_IMM

k = int(sys.argv[1], 16)
for s, e in gn.FUNCS:
    hits = [ins for ins in gn.md.disasm(gn.img[s:e], s)
            if ins.mnemonic == "imul" and any(op.type == X86_OP_IMM and op.imm == k for op in ins.operands)]
    if hits:
        print("func 0x%06X-0x%06X" % (s, e))
        for ins in hits:
            print("   " + gn.fmt(ins))
