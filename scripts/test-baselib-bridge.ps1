[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')]
    [string]$TargetVersion = 'v113.1',

    [string]$DataDir = '',

    [string]$ImplementationDll = '',

    [string]$RuntimeDependencyDir = '',

    [string]$BaseLibDll = '',

    [string]$BridgeDll = '',

    [string]$PackagePck = '',

    [string]$GodotExe = $env:GODOT_4_5_1_MONO
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$ProbeRoot = Join-Path $taskRoot 'tools\BaseLibBridgeProbe'
$ProbeProject = Join-Path $ProbeRoot 'BaseLibBridgeProbe.csproj'

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
if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
if ([string]::IsNullOrWhiteSpace($BaseLibDll)) {
    $BaseLibDll = 'D:\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll'
}
if ([string]::IsNullOrWhiteSpace($BridgeDll)) {
    $BridgeDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.BaseLibBridge.dll"
}
if ($PackagePck) {
    $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($PackagePck)
}
elseif (-not $env:THINGS_PROBE_PCK) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($candidateModPck)
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($GodotExe) -or -not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    if (Test-Path -LiteralPath 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' -PathType Leaf) {
        $GodotExe = 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
    else {
        $GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
}

foreach ($requiredPath in @($ProbeProject, $ImplementationDll, $BaseLibDll, $BridgeDll, (Join-Path $DataDir 'sts2.dll'))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required BaseLib bridge probe input was not found: $requiredPath"
    }
}
if (-not (Test-Path -LiteralPath $RuntimeDependencyDir -PathType Container)) {
    throw "Probe runtime dependency directory was not found: $RuntimeDependencyDir"
}

$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)
$BaseLibDll = [IO.Path]::GetFullPath($BaseLibDll)
$BridgeDll = [IO.Path]::GetFullPath($BridgeDll)
$GodotExe = [IO.Path]::GetFullPath($GodotExe)

$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
Write-Host 'Building BaseLib bridge probe...'
& dotnet build $ProbeProject -t:Rebuild -c Debug --nologo `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:Sts2DataDir=$DataDir" `
    "/p:RuntimeDependencyDir=$RuntimeDependencyDir" `
    "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) {
    throw "BaseLib bridge probe build failed with exit code $LASTEXITCODE"
}

$portableDotnet = Join-Path $taskRoot '.tmp\dotnet9'
if (Test-Path -LiteralPath (Join-Path $portableDotnet 'dotnet.exe') -PathType Leaf) {
    $env:DOTNET_ROOT = $portableDotnet
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}

$logPath = Join-Path $ProbeRoot 'probe.log'
if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    Remove-Item -LiteralPath $logPath -Force
}

Write-Host 'Running BaseLib bridge probe...'
& $GodotExe --headless --path $ProbeRoot --log-file $logPath -- @($BaseLibDll, $ImplementationDll, $BridgeDll)
if ($LASTEXITCODE -ne 0) {
    throw "BaseLib bridge probe failed with exit code $LASTEXITCODE"
}
$logText = Get-Content -LiteralPath $logPath -Raw -ErrorAction Stop
if ($logText.IndexOf('BaseLib bridge probe: PASS', [StringComparison]::Ordinal) -lt 0) {
    throw 'BaseLib bridge probe did not report PASS.'
}

Write-Host 'BaseLib bridge probe passed.'
