[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')][string]$TargetVersion = 'v114.0',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [string]$GodotExe = ''
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskOutput = Join-Path $taskRoot 'build\depths_stone_map'
if ($OutputDir) { $taskOutput = [IO.Path]::GetFullPath($OutputDir) }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$env:THINGS_MAP_OUTPUT = $taskOutput
if ($PackagePck) {
    $env:THINGS_MAP_PCK = [IO.Path]::GetFullPath($PackagePck)
}
else {
    $env:THINGS_MAP_PCK = ''
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $env:THINGS_MAP_PCK = [IO.Path]::GetFullPath($candidateModPck)
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

$taskProbe = Join-Path $taskRoot 'tools\DepthsStoneMapProbe'
$taskStdout = Join-Path $taskOutput 'native.stdout.log'
$taskStderr = Join-Path $taskOutput 'native.stderr.log'
$taskProcess = Start-Process -FilePath $GodotExe -ArgumentList @('--path', ('"' + $taskProbe + '"'), '--script', 'res://preview.gd', '--rendering-method', 'gl_compatibility') -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskStdout -RedirectStandardError $taskStderr
try {
    if (-not $taskProcess.WaitForExit(60000)) { throw 'Map texture render timed out' }
    if ($taskProcess.ExitCode -ne 0) { Get-Content $taskStderr -Tail 24; throw 'Map texture render failed' }
}
finally {
    if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
    $taskProcess.Dispose()
}
Get-Content $taskStdout -Tail 5
if (Select-String $taskStderr -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { Get-Content $taskStderr -Tail 24; throw 'Map renderer logged errors' }
if (-not (Select-String $taskStdout -Pattern 'Depths stone map render: PASS' -Quiet)) { throw 'No map render PASS marker' }
