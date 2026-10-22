"""Simulated game update: checks that every site signature (src/Sites.Generated.cs) survives code and data moving.

  python -I sim_update.py [seed]

Builds a fake ".text" in which every function containing a site is moved to a new place (random filler between
them, in shuffled order), and the globals those functions use move too (by a different amount) - RIP-relative
references are rewritten so they still reach their data, as a recompiled build would. Then resolves every site with
the mod's rules (exact match count, constraints, data decoded from the instruction) and checks each lands on the
moved instruction and each global on its moved address.
"""
import os, random, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gn
from gen_sigs import rip_disp_offset, TEXT, T0

seed = int(sys.argv[1]) if len(sys.argv) > 1 else 1
rnd = random.Random(seed)

# ---- read the generated table ----
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Sites.Generated.cs"), encoding="utf-8").read()
SITES = []
for m in re.finditer(r'new Site\("(\w+)", "(\w+)", new\[\] \{ ([^}]*) \}, (\d+), SiteKind\.(\w+), (\d+), (\d+), (\d+), (null|"\w+"), (\d+),\s*"([^"]+)"\)', src):
    name, feat, rvas, off, kind, dispat, ilen, add, cs, coff, pat = m.groups()
    SITES.append(dict(name=name, rvas=[int(x, 16) for x in rvas.split(",")], off=int(off), kind=kind, dispat=int(dispat),
                      ilen=int(ilen), add=int(add), cs=None if cs == "null" else cs.strip('"'), coff=int(coff),
                      pat=[None if b == "??" else int(b, 16) for b in pat.split()]))
assert SITES, "couldn't parse Sites.Generated.cs"

# ---- build the "updated" .text ----
DATA_SHIFT = 0x3A510      # every global moves by this much
funcs = sorted({gn.func_of(r) for s in SITES for r in s["rvas"]})
order = funcs[:]; rnd.shuffle(order)
new_text = bytearray(rnd.randbytes(0x2000))      # filler before the first function
moved = {}                                        # old function start -> new start (rva)
NEW_T0 = T0
for f in order:
    new_text += rnd.randbytes(rnd.randrange(0x40, 0x4000))
    start = NEW_T0 + len(new_text)
    body = bytearray(gn.img[f[0]:f[1]])
    delta = start - f[0]
    # rewrite RIP-relative references: data moves by DATA_SHIFT, the instruction by delta
    for ins in gn.md.disasm(bytes(body), f[0]):
        k = rip_disp_offset(ins)
        if k is None: continue
        at = ins.address - f[0] + k
        disp = int.from_bytes(body[at:at + 4], "little", signed=True)
        body[at:at + 4] = (disp + DATA_SHIFT - delta).to_bytes(4, "little", signed=True)
    moved[f[0]] = start
    new_text += body
new_text += rnd.randbytes(0x2000)
TXT = bytes(new_text)


def new_rva(old):
    f = gn.func_of(old)
    return moved[f[0]] + (old - f[0])


# ---- resolve with the mod's rules ----
def match_at(s, instr, resolved):
    start = instr - s["off"] - NEW_T0
    if start < 0 or start + len(s["pat"]) > len(TXT): return False
    if any(v is not None and TXT[start + i] != v for i, v in enumerate(s["pat"])): return False
    if s["kind"] == "Data" and s["cs"]:
        c = resolved.get(s["cs"])
        if c is None: return False
        if target(s, instr) != c[1] + s["coff"]: return False
    return True


def target(s, instr):
    at = instr - NEW_T0 + s["dispat"]
    return instr + s["ilen"] + int.from_bytes(TXT[at:at + 4], "little", signed=True) - s["add"]


resolved, bad = {}, 0
for s in SITES:
    a0, a1, run, st = 0, 0, 0, 0
    for i, v in enumerate(s["pat"] + [None]):
        if v is None:
            if run > a1 - a0: a0, a1 = st, st + run
            run = 0; st = i + 1
        else: run += 1
    key = bytes(s["pat"][a0:a1])
    found, pos = [], TXT.find(key)
    while pos >= 0:
        instr = NEW_T0 + pos - a0 + s["off"]
        if match_at(s, instr, resolved): found.append(instr)
        pos = TXT.find(key, pos + 1)
    want = [new_rva(r) for r in s["rvas"]]
    ok = sorted(found) == sorted(want)
    tgt = target(s, found[0]) if ok and s["kind"] == "Data" else None
    if ok: resolved[s["name"]] = (found, tgt)
    if s["kind"] == "Data" and ok:
        old_t = s["rvas"][0] + s["ilen"] + int.from_bytes(gn.img[s["rvas"][0] + s["dispat"]:s["rvas"][0] + s["dispat"] + 4], "little", signed=True) - s["add"]
        ok = tgt == old_t + DATA_SHIFT
    if not ok: bad += 1
    print("%-15s %s" % (s["name"], "OK  " + ("data -> 0x%X" % tgt if tgt else "") if ok else "FAIL found %s want %s" % ([hex(x) for x in found], [hex(x) for x in want])))
print("\nseed %d: %d/%d sites resolved correctly after the simulated update" % (seed, len(SITES) - bad, len(SITES)))
sys.exit(1 if bad else 0)
