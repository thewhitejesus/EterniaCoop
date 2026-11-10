# Checks NativeHook.Length (ported line by line from src/NativeHook.cs) against Capstone on the first instructions of
# every function in GameNative.dll: wherever Length gives a length, it must be Capstone's, and the instruction must
# not be position-dependent. Usage: python -I check_len.py <GameNative.dll> [rva ...]
import struct, sys
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_REG_RIP, X86_GRP_JUMP, X86_GRP_CALL, X86_GRP_RET, X86_GRP_BRANCH_RELATIVE


def length(p):
    i = 0
    op16 = rexW = False
    while p[i] in (0x66, 0xF2, 0xF3):
        if p[i] == 0x66: op16 = True
        i += 1
        if i > 3: return 0
    if (p[i] & 0xF0) == 0x40:
        rexW = (p[i] & 8) != 0
        i += 1
    op = p[i]; i += 1
    modrm = False
    imm = 0
    if op == 0x0F:
        op2 = p[i]; i += 1
        if op2 in (0x10, 0x11, 0x28, 0x29, 0x57, 0x6F, 0x7F, 0xB6, 0xB7, 0xBE, 0xBF, 0xAF, 0x1F) or (op2 & 0xF0) == 0x40: modrm = True
        else: return 0
    elif 0x50 <= op <= 0x5F: pass
    elif op < 0x40 and (op & 7) < 4: modrm = True
    elif op < 0x40 and (op & 7) == 4: imm = 1
    elif op < 0x40 and (op & 7) == 5: imm = 2 if op16 else 4
    elif op == 0x63 or 0x84 <= op <= 0x8B or op == 0x8D: modrm = True
    elif op in (0x80, 0x83, 0xC6, 0xC0, 0xC1, 0x6B): modrm = True; imm = 1
    elif op in (0x81, 0xC7, 0x69): modrm = True; imm = 2 if op16 else 4
    elif 0xB0 <= op <= 0xB7: imm = 1
    elif 0xB8 <= op <= 0xBF: imm = 8 if rexW else 2 if op16 else 4
    else: return 0
    if modrm:
        m = p[i]; i += 1
        mod, rm = m >> 6, m & 7
        if mod == 0 and rm == 5: return 0
        if mod != 3 and rm == 4:
            sib = p[i]; i += 1
            if (sib & 7) == 5 and mod == 0: i += 4
        if mod == 1: i += 1
        elif mod == 2: i += 4
    return i + imm


pe = pefile.PE(sys.argv[1], fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXCEPTION']])
image = pe.get_memory_mapped_image()
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True

if len(sys.argv) > 2:
    starts = [int(a, 16) for a in sys.argv[2:]]
else:
    d = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
    starts = sorted({struct.unpack_from('<I', image, d.VirtualAddress + k)[0] for k in range(0, d.Size, 12)})

ok = declined = wrong = 0
for rva in starts:
    at, moved, good = rva, 0, True
    while moved < 5:
        n = length(image[at:at + 24])
        if n == 0: good = False; break
        ins = next(md.disasm(bytes(image[at:at + 16]), at), None)
        bad = ins is None or ins.size != n
        if not bad:
            dependent = any(ins.group(g) for g in (X86_GRP_JUMP, X86_GRP_CALL, X86_GRP_RET, X86_GRP_BRANCH_RELATIVE)) or \
                any(o.type == X86_OP_MEM and o.mem.base == X86_REG_RIP for o in ins.operands)
            bad = dependent
        if bad:
            wrong += 1
            print('WRONG 0x%X: %s -> Length %d, Capstone %s' % (at, bytes(image[at:at + 12]).hex(' '), n,
                  'none' if ins is None else '%d %s %s' % (ins.size, ins.mnemonic, ins.op_str)))
            good = False
            break
        at += n; moved += n
    if good:
        ok += 1
        if len(sys.argv) > 2: print('0x%X: movable, %d bytes' % (rva, moved))
    else:
        declined += 1
        if len(sys.argv) > 2: print('0x%X: declined' % rva)
print('%d functions: %d movable, %d declined, %d WRONG' % (len(starts), ok, declined - wrong, wrong))
