[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$GodotExe = '',
    [switch]$Visual
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools\LanternFishProbe'
$taskLogs = Join-Path $taskRoot 'build\lantern_fish'
New-Item -ItemType Directory -Force -Path $taskLogs | Out-Null

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

if (-not $ImplementationDll) {
    $legacyDll = Join-Path $taskRoot "build\lantern_fish\$TargetVersion\STS2_Things.dll"
    if (Test-Path -LiteralPath $legacyDll -PathType Leaf) {
        $ImplementationDll = $legacyDll
    }
    else {
        $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
    }
}
$taskDll = [IO.Path]::GetFullPath($ImplementationDll)

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
& dotnet build (Join-Path $taskProbe 'LanternFishProbe.csproj') -c Debug --nologo "/p:Sts2DataDir=$DataDir" `
    "/p:Sts2TargetVersion=$TargetVersion" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$taskDll"
if ($LASTEXITCODE -ne 0) { throw 'Native probe compilation failed.' }

if (Test-Path (Join-Path $taskRoot '.tmp\dotnet9')) {
    $env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
}
if ($Visual) {
    $taskStdout = Join-Path $taskLogs 'visual.stdout.log'
    $taskStderr = Join-Path $taskLogs 'visual.stderr.log'
    $taskProcess = Start-Process -FilePath $GodotExe -ArgumentList @('--path', ('"' + $taskProbe + '"'), '--rendering-method', 'gl_compatibility', '--', '--visual') -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskStdout -RedirectStandardError $taskStderr
    try {
        $taskStarted = Get-Date
        while (-not $taskProcess.WaitForExit(45000)) {
            Write-Host 'Native visual capture is running...'
            if (((Get-Date) - $taskStarted).TotalSeconds -gt 300) { throw 'Visual capture timed out.' }
        }
        if ($taskProcess.ExitCode -ne 0) { throw 'Visual capture failed.' }
    }
    finally {
        if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
        $taskProcess.Dispose()
    }
    if (Select-String $taskStderr -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw 'Visual capture logged an engine error.' }
    if (-not (Select-String $taskStdout -Pattern 'Lantern Fish visual probe: PASS' -Quiet)) { throw 'No visual PASS marker.' }
    Get-Content $taskStdout -Tail 3
}
else {
    $taskLog = Join-Path $taskLogs "behavior-$TargetVersion.log"
    & $GodotExe --headless --path $taskProbe --log-file $taskLog
    if ($LASTEXITCODE -ne 0) { throw 'Native behavior probe failed.' }
    if (-not (Select-String $taskLog -Pattern 'Lantern Fish behavior probe: PASS' -Quiet)) { throw 'No behavior PASS marker.' }
}
