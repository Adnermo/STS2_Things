param(
    [ValidateSet('v111.1', 'v114.0')]
    [string]$TargetVersion = 'v114.0',
    [string]$ImplementationDll = '',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$VanillaPck = '',
    [string]$ModPck = '',
    [string]$OutputDir = '',
    [switch]$LogicOnly,
    [int]$TimeoutMs = 90000,
    [string]$GodotExe = ''
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
$probeDir = Join-Path $taskRoot 'tools\FourEventsProbe'
$probeProj = Join-Path $probeDir 'FourEventsProbe.csproj'

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
if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
if (-not $PSBoundParameters.ContainsKey('VanillaPck') -and [string]::IsNullOrWhiteSpace($VanillaPck)) {
    $VanillaPck = $candidateGamePck
}
if ([string]::IsNullOrWhiteSpace($ModPck)) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $ModPck = $candidateModPck
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $taskRoot "build\probe-runs\four-events-$TargetVersion"
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

$dotnetExe = 'C:\Program Files\dotnet\dotnet.exe'
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& $dotnetExe build $probeProj -c Debug --nologo `
    "/p:ImplementationDll=$([IO.Path]::GetFullPath($ImplementationDll))" `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:Sts2DataDir=$DataDir" `
    "/p:RuntimeDependencyDir=$([IO.Path]::GetFullPath($RuntimeDependencyDir))"
if ($LASTEXITCODE -ne 0) {
    throw "Probe build failed ($TargetVersion) with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$logPath = Join-Path $OutputDir 'probe.log'
$stdoutPath = Join-Path $OutputDir 'stdout.log'
$stderrPath = Join-Path $OutputDir 'stderr.log'
$reportPath = Join-Path $OutputDir 'report.json'
if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath -Force }

$hasVanilla = (-not $LogicOnly) -and (-not [string]::IsNullOrWhiteSpace($VanillaPck)) -and (Test-Path -LiteralPath $VanillaPck)
$env:THINGS_VANILLA_PCK = if ($hasVanilla) { [IO.Path]::GetFullPath($VanillaPck) } else { '' }
$env:THINGS_PROBE_PCK = if ($ModPck -and (Test-Path -LiteralPath $ModPck)) { [IO.Path]::GetFullPath($ModPck) } else { '' }
$env:THINGS_PROBE_OUTPUT = [IO.Path]::GetFullPath($OutputDir)

if (Test-Path -LiteralPath (Join-Path $taskRoot '.tmp\dotnet9')) {
    $env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}
else {
    $env:DOTNET_ROLL_FORWARD = ''
}

$godotArgs = if ($hasVanilla) {
    @('--path', $probeDir, '--rendering-method', 'gl_compatibility', '--resolution', '1920x1080', '--log-file', $logPath)
}
else {
    @('--headless', '--path', $probeDir, '--log-file', $logPath)
}

$runResult = Start-HeadlessSafeProcess `
    -FilePath $GodotExe `
    -ArgumentList $godotArgs `
    -TimeoutMs $TimeoutMs `
    -StdoutPath $stdoutPath `
    -StderrPath $stderrPath

if ($runResult.TimedOut) {
    throw "Probe execution timed out after ${TimeoutMs}ms ($TargetVersion). See $logPath"
}
if ($runResult.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Tail 25 }
    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Tail 25 }
    throw "FourEventsProbe ($TargetVersion) exited with $($runResult.ExitCode). See $logPath"
}
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "Probe ($TargetVersion) did not write report.json. See $logPath"
}

Write-Host "Four events probe ($TargetVersion) complete. Outputs in $OutputDir"
