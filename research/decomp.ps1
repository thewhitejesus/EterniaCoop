# Decompile functions containing the given RVAs using the analyzed Ghidra project.
# Usage: powershell -File decomp.ps1 <outfile> <rva> [<rva> ...]
# Tools folder (holding ghidra\, jdk\ and ghidra_projects\): $env:EC_TOOLS, default ..\..\tools next to the repo.
param([Parameter(Mandatory)][string]$Out, [Parameter(ValueFromRemainingArguments)][string[]]$Rvas)
$ErrorActionPreference = "Stop"
$tools = if ($env:EC_TOOLS) { $env:EC_TOOLS } else { Join-Path $PSScriptRoot "..\..\tools" }
$env:JAVA_HOME = (Get-ChildItem "$tools\jdk" -Directory | Select-Object -First 1).FullName
$ghidra = (Get-ChildItem "$tools\ghidra" -Directory | Select-Object -First 1).FullName
& "$ghidra\support\analyzeHeadless.bat" "$tools\ghidra_projects" EterniaCoop -process GameNative.dll -noanalysis -readOnly `
    -scriptPath "$PSScriptRoot\ghidra_scripts" -postScript DecompileAt.java $Out @Rvas 2>&1 |
    Select-String -Pattern "ERROR|Exception|SCRIPT" | ForEach-Object { $_.Line }
