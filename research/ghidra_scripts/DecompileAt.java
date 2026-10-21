// Headless: decompile the functions containing the given RVAs and list their callers.
// Args: <output file> <rva> [<rva> ...]   (RVAs in hex, relative to the image base)
// @category EterniaCoop
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

import java.io.PrintWriter;
import java.util.LinkedHashSet;
import java.util.Set;

public class DecompileAt extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        DecompInterface ifc = new DecompInterface();
        ifc.openProgram(currentProgram);
        Address base = currentProgram.getImageBase();
        Set<Function> done = new LinkedHashSet<>();
        try (PrintWriter out = new PrintWriter(args[0], "UTF-8")) {
            for (int i = 1; i < args.length; i++) {
                long rva = Long.parseLong(args[i].replace("0x", ""), 16);
                Address a = base.add(rva);
                Function f = getFunctionContaining(a);
                if (f == null) { out.println("// 0x" + Long.toHexString(rva) + ": no function"); continue; }
                if (!done.add(f)) continue;
                long fr = f.getEntryPoint().subtract(base);
                out.println("// ===== " + f.getName() + " @ rva 0x" + Long.toHexString(fr) + " (asked for 0x" + Long.toHexString(rva) + ")");
                StringBuilder callers = new StringBuilder();
                for (Reference r : getReferencesTo(f.getEntryPoint())) {
                    Function c = getFunctionContaining(r.getFromAddress());
                    callers.append(String.format(" 0x%x%s", r.getFromAddress().subtract(base),
                        c == null ? "" : "(" + c.getName() + ")"));
                }
                out.println("// callers:" + callers);
                DecompileResults res = ifc.decompileFunction(f, 120, monitor);
                out.println(res.decompileCompleted() ? res.getDecompiledFunction().getC() : "// decompile failed: " + res.getErrorMessage());
            }
        }
        ifc.dispose();
    }
}
