"""Find vtables that contain a given function RVA and print their entries."""
import sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn

base = gn.pe.OPTIONAL_HEADER.ImageBase
for arg in sys.argv[1:]:
    rva = int(arg, 16)
    needle = struct.pack("<Q", base + rva)
    pos = gn.img.find(needle)
    while pos != -1:
        # walk back to the start of the table (entries are pointers into .text)
        start = pos
        while True:
            v = struct.unpack_from("<Q", gn.img, start - 8)[0] - base
            if 0x1000 <= v < 0x2A9000: start -= 8
            else: break
        print("== 0x%X found in table at 0x%X (slot %d)" % (rva, start, (pos - start) // 8))
        i = start
        while True:
            v = struct.unpack_from("<Q", gn.img, i)[0] - base
            if not (0x1000 <= v < 0x2A9000): break
            f = gn.func_of(v)
            print("  [%2d] 0x%X%s" % ((i - start) // 8, v, "" if f else "  (not a .pdata func start)"))
            i += 8
        pos = gn.img.find(needle, pos + 1)
