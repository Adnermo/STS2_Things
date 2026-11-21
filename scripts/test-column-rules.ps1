[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = 'build/human-face-column-update-20261007/native',
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    if ($TargetVersion -eq 'v111.1') {
        $DataDir = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    }
    else {
        $v1131Data = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v113.1\76cc75b6f097\game\data_sts2_windows_x86_64'
        if (Test-Path -LiteralPath $v1131Data -PathType Container) {
            $DataDir = $v1131Data
        }
        else {
            $DataDir = 'D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
        }
    }
}
$DataDir = [IO.Path]::GetFullPath($DataDir)
$candidateGamePck = Join-Path (Split-Path $DataDir -Parent) 'SlayTheSpire2.pck'
if (Test-Path -LiteralPath $candidateGamePck) {
    $env:THINGS_PROBE_GAME_PCK = $candidateGamePck
}

if ([string]::IsNullOrWhiteSpace($RuntimeDependencyDir)) {
    $RuntimeDependencyDir = $DataDir
}
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)

$taskProbe = Join-Path $taskRoot ('.tmp/column-rules-' + $TargetVersion.Replace('.', '-'))
$taskOut = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDir)) }
if (-not $ImplementationDll) { $ImplementationDll = Join-Path $taskRoot "build/$TargetVersion/STS2_Things.dll" }
if (-not $PackagePck) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build/$TargetVersion/STS2_Things.pck"),
        (Join-Path $taskRoot 'build/unified/STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $PackagePck = $candidateModPck
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = $env:GODOT_4_5_1_MONO
}
if ([string]::IsNullOrWhiteSpace($GodotExe) -or -not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    if (Test-Path -LiteralPath 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' -PathType Leaf) {
        $GodotExe = 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
    else {
        $GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
}
$GodotExe = [IO.Path]::GetFullPath($GodotExe)

New-Item -ItemType Directory -Path $taskProbe, $taskOut -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tools/DepthsProbe') -File |
    Where-Object { $_.Extension -in '.cs', '.csproj', '.tscn' -or $_.Name -eq 'project.godot' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskProbe $_.Name) }
$assemblyName = 'ColumnRulesProbe' + $TargetVersion.Replace('.', '').Replace('-', '')
$taskProject = Join-Path $taskProbe 'DepthsProbe.csproj'
$text = Get-Content -LiteralPath $taskProject -Raw
Set-Content -LiteralPath $taskProject -Value $text.Replace('<AssemblyName>DepthsProbe</AssemblyName>', "<AssemblyName>$assemblyName</AssemblyName>") -Encoding utf8
$taskGodotProject = Join-Path $taskProbe 'project.godot'
$text = Get-Content -LiteralPath $taskGodotProject -Raw
Set-Content -LiteralPath $taskGodotProject -Value $text.Replace('project/assembly_name="DepthsProbe"', ('project/assembly_name="' + $assemblyName + '"')) -Encoding utf8
$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build $taskProject -t:Rebuild -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$([IO.Path]::GetFullPath($ImplementationDll))"
if ($LASTEXITCODE -ne 0) { throw 'Isolated column probe failed to compile.' }
$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:THINGS_PROBE_OUTPUT = $taskOut
if ($PackagePck) { $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($PackagePck) }
$taskLog = Join-Path $taskOut 'column-rules.log'
& $GodotExe --headless --path $taskProbe --rendering-method gl_compatibility --log-file $taskLog -- --column-rules-only
if ($LASTEXITCODE -ne 0) { throw 'Native column rules failed.' }
$text = Get-Content -LiteralPath $taskLog -Raw
if ($text -match '(?m)^ERROR:|SCRIPT ERROR:' -or $text -notmatch 'Depths column rules probe: PASS') { throw 'Native column log did not pass cleanly.' }
