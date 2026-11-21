[CmdletBinding()]
param(
    [ValidateSet('v111.0', 'v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$Package = '',
    [string]$NativePck = '',
    [string]$LogPath = '',
    [switch]$LoadOnly,
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$probe = Join-Path $taskRoot 'tools\UnifiedStartupProbe'

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    if ($TargetVersion -eq 'v111.0') {
        $DataDir = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111\7c9537e2a06e\game\data_sts2_windows_x86_64'
    }
    elseif ($TargetVersion -eq 'v111.1') {
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

if ([string]::IsNullOrWhiteSpace($Package)) {
    $Package = Join-Path $taskRoot 'build\unified'
}
if ([string]::IsNullOrWhiteSpace($NativePck)) {
    $NativePck = $candidateGamePck
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

$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build (Join-Path $probe 'UnifiedStartupProbe.csproj') -t:Rebuild -c Debug --nologo `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:Sts2DataDir=$DataDir" `
    "/p:RuntimeDependencyDir=$RuntimeDependencyDir"
if ($LASTEXITCODE -ne 0) { throw 'Unified startup probe build failed.' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_ROLL_FORWARD = 'Major'

$log = if ($LogPath) { [IO.Path]::GetFullPath($LogPath) } else { Join-Path $taskRoot "build\unified-startup\startup-$TargetVersion.log" }
New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null
$arguments = @([IO.Path]::GetFullPath($Package), [IO.Path]::GetFullPath($NativePck), $TargetVersion)
if ($LoadOnly) { $arguments += '--load-only' }
& $GodotExe --headless --path $probe --log-file $log -- @arguments
if ($LASTEXITCODE -ne 0) { throw "Unified startup probe failed for $TargetVersion." }
$logText = Get-Content -Raw -LiteralPath $log
if ($logText -notmatch 'Unified startup probe: PASS' -or $logText -match '(?m)^ERROR:|SCRIPT ERROR:') {
    throw "Unified startup probe did not pass cleanly for $TargetVersion."
}
