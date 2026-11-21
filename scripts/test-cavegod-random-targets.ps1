[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$ImplementationDll = '',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$GamePck = '',
    [string]$PackagePck = '',
    [string]$OutputDirectory = '',
    [switch]$Retaliation,
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools\CaveGodTargetProbe'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $taskRoot "build\cavegod-random-targets\$TargetVersion"
}
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null

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
if ($GamePck) {
    $env:THINGS_PROBE_GAME_PCK = [IO.Path]::GetFullPath($GamePck)
}

if ([string]::IsNullOrWhiteSpace($RuntimeDependencyDir)) {
    $RuntimeDependencyDir = $DataDir
}
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)

if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)

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

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $taskProbe 'CaveGodTargetProbe.csproj') -t:Rebuild -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:ImplementationDll=$ImplementationDll" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" *> (Join-Path $taskOutput 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $taskOutput 'build.log') -Tail 15; throw 'Random target probe compile failed.' }
$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:THINGS_PROBE_RUNTIME_DIR = $RuntimeDependencyDir
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
$taskArgs = @('--headless', '--path', $taskProbe, '--', '--random-target-regression')
if ($Retaliation) { $taskArgs += '--retaliation' }
$taskRun = Start-HeadlessSafeProcess -FilePath $GodotExe -ArgumentList $taskArgs -TimeoutMs 45000 -StdoutPath (Join-Path $taskOutput 'stdout.log') -StderrPath (Join-Path $taskOutput 'stderr.log')
if ($taskRun.TimedOut) { throw 'Random target probe timed out.' }
Get-Content -LiteralPath (Join-Path $taskOutput 'stdout.log') -Tail 5
if ($taskRun.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $taskOutput 'stderr.log') -TotalCount 12
    throw "Random target probe failed: $($taskRun.ExitCode)"
}
if (Select-String -LiteralPath (Join-Path $taskOutput 'stderr.log') -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw 'Native errors in random target probe.' }
$taskMarker = if ($Retaliation) { 'CaveGod retaliation regression: PASS' } else { 'CaveGod random target regression: PASS' }
if (-not (Select-String -LiteralPath (Join-Path $taskOutput 'stdout.log') -Pattern $taskMarker -Quiet)) { throw 'Target probe did not report PASS.' }
