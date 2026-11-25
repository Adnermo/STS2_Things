[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')][string]$TargetVersion = 'v114.0',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$GodotExe = '',
    [switch]$Visual,
    [switch]$HostMode,
    [switch]$Matrix,
    [switch]$Loopback
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools/PetDeathProbe'
$taskOut = if ($OutputDir) { [IO.Path]::GetFullPath($OutputDir) } else { Join-Path $taskRoot "build/pet_death/$TargetVersion" }
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    if ($TargetVersion -eq 'v111.1') {
        $DataDir = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    }
    else {
        $v1140Data = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v114\0e151996c7aa\game\data_sts2_windows_x86_64'
        if (Test-Path -LiteralPath $v1140Data -PathType Container) {
            $DataDir = $v1140Data
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

$env:PATH = 'C:/Program Files/dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build (Join-Path $taskProbe 'PetDeathProbe.csproj') -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) { throw 'Pet death probe build failed' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:THINGS_PROBE_OUTPUT = $taskOut
$argsList = @('--path', ('"' + $taskProbe + '"'), '--rendering-method', 'gl_compatibility')
if (-not $Visual) { $argsList += '--headless' }
if ($Visual -or $HostMode -or $Matrix -or $Loopback) {
    $argsList += '--'
    if ($Visual) { $argsList += '--visual' }
    if ($HostMode) { $argsList += '--host' }
    if ($Matrix) { $argsList += '--matrix' }
    if ($Loopback) { $argsList += '--loopback' }
}
$taskProcess = Start-Process -FilePath $GodotExe -ArgumentList $argsList -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOut 'probe.stdout.log') -RedirectStandardError (Join-Path $taskOut 'probe.stderr.log')
try {
    if (-not $taskProcess.WaitForExit(45000)) { throw 'Pet death probe timed out' }
    Get-Content -LiteralPath (Join-Path $taskOut 'probe.stdout.log') -Tail 10
    if ($taskProcess.ExitCode -ne 0 -or (Select-String -LiteralPath (Join-Path $taskOut 'probe.stderr.log') -Pattern '^ERROR:|SCRIPT ERROR:' -Quiet)) {
        Get-Content -LiteralPath (Join-Path $taskOut 'probe.stderr.log') -TotalCount 22
        throw 'Pet death probe failed; inspect the captured exception'
    }
    if (-not (Select-String -LiteralPath (Join-Path $taskOut 'probe.stdout.log') -Pattern 'Pet death probe: PASS' -Quiet)) { throw 'Missing pass marker' }
}
finally {
    if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
    $taskProcess.Dispose()
}
