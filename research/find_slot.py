"""Find the global function-pointer slot that RegisterUnityFunc fills for a given callback name,
then list every instruction that reads that slot (i.e. every native call site of the callback)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn

for name in sys.argv[1:]:
    data = gn.img
    needle = name.encode() + b"\0"
    pos = data.find(needle)
    while pos != -1 and data[pos - 1] != 0:          # must be a whole C string
        pos = data.find(needle, pos + 1)
    print("== %s : string at 0x%X" % (name, pos))
    if pos == -1:
        continue
    idx = gn.load_index()
    users = [a for t, a, k in idx if t == pos]
    for a in users:
        print("  string used at 0x%X; following code:" % a)
        gn.dis(a, 8)
