[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$GodotExe = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [switch]$Visual,
    [switch]$VarietyVisual,
    [switch]$EffectsOnly,
    [switch]$ColumnVariantsOnly,
    [switch]$ColumnRulesOnly,
    [switch]$CampOnly,
    [switch]$AscensionOnly,
    [switch]$ActualVictoryOnly,
    [switch]$BalanceOnly,
    [switch]$FleetingEchoOnly,
    [switch]$FleetingEchoCombatOnly,
    [switch]$FoolArrowsOnly,
    [switch]$HumanFaceTowerOnly,
    [switch]$AbyssalInkmawOnly
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools\DepthsProbe'
$taskOut = Join-Path $taskRoot 'build\depths'
if ($OutputDir) { $taskOut = [IO.Path]::GetFullPath($OutputDir) }
$env:THINGS_PROBE_OUTPUT = $taskOut
New-Item -ItemType Directory -Force -Path $taskOut | Out-Null

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

if ($ImplementationDll) {
    $taskDll = [IO.Path]::GetFullPath($ImplementationDll)
}
else {
    $depthsDll = Join-Path $taskRoot "build\depths\$TargetVersion\STS2_Things.dll"
    if (Test-Path -LiteralPath $depthsDll -PathType Leaf) {
        $taskDll = $depthsDll
    }
    else {
        $taskDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
    }
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
& dotnet build (Join-Path $taskProbe 'DepthsProbe.csproj') -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$taskDll"
if ($LASTEXITCODE -ne 0) { throw 'Depths probe compilation failed.' }

if (Test-Path (Join-Path $taskRoot '.tmp\dotnet9')) {
    $env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
}
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
$taskMode = if ($ActualVictoryOnly) { 'actual-victory' } elseif ($AscensionOnly) { 'ascension' } elseif ($ColumnRulesOnly) { 'column-rules' } elseif ($HumanFaceTowerOnly) { 'human-face-tower' } elseif ($AbyssalInkmawOnly) { 'abyssal-inkmaw' } elseif ($BalanceOnly) { 'balance' } elseif ($FoolArrowsOnly) { 'fool-arrows' } elseif ($FleetingEchoCombatOnly) { 'fleeting-echo-combat' } elseif ($FleetingEchoOnly) { 'fleeting-echo' } elseif ($ColumnVariantsOnly) { 'column-variants' } elseif ($EffectsOnly) { 'effects' } elseif ($CampOnly) { 'camp' } elseif ($VarietyVisual) { 'variety-visual' } elseif ($Visual) { 'visual' } else { "probe-$TargetVersion" }
$taskArgs = @('--path', $taskProbe, '--rendering-method', 'gl_compatibility')
if ($ActualVictoryOnly) { $taskArgs += @('--', '--actual-victory-only') } elseif ($AscensionOnly) { $taskArgs += @('--', '--ascension-only') } elseif ($ColumnRulesOnly) { $taskArgs += @('--', '--column-rules-only') } elseif ($HumanFaceTowerOnly) { $taskArgs += @('--headless', '--', '--human-face-tower-only') } elseif ($AbyssalInkmawOnly) { $taskArgs += @('--headless', '--', '--abyssal-inkmaw-only') } elseif ($BalanceOnly) { $taskArgs += @('--', '--balance-only') } elseif ($FoolArrowsOnly) { $taskArgs += @('--headless', '--', '--fool-arrows-only') } elseif ($FleetingEchoCombatOnly) { $taskArgs += @('--headless', '--', '--fleeting-echo-combat-only') } elseif ($FleetingEchoOnly) { $taskArgs += @('--', '--fleeting-echo-only') } elseif ($ColumnVariantsOnly) { $taskArgs += @('--', '--column-variants-only') } elseif ($EffectsOnly) { $taskArgs += @('--', '--effects-only') } elseif ($CampOnly) { $taskArgs += @('--', '--camp-only') } elseif ($VarietyVisual) { $taskArgs += @('--', '--variety-visual') } elseif ($Visual) { $taskArgs += @('--', '--visual') }
$taskStdout = Join-Path $taskOut "$taskMode.stdout.log"
$taskStderr = Join-Path $taskOut "$taskMode.stderr.log"
$taskRun = Start-HeadlessSafeProcess -FilePath $GodotExe -ArgumentList $taskArgs -TimeoutMs 240000 -StdoutPath $taskStdout -StderrPath $taskStderr
if ($taskRun.TimedOut) { throw 'Depths probe timed out.' }
if ($taskRun.ExitCode -ne 0) {
    if (Test-Path $taskStderr) { Get-Content $taskStderr -Tail 20 }
    if (Test-Path $taskStdout) { Get-Content $taskStdout -Tail 20 }
    throw 'Depths probe failed.'
}
Get-Content $taskStdout -Tail 6
if (Select-String $taskStderr -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw "Engine error; see $taskStderr" }
if (-not (Select-String $taskStdout -Pattern 'Depths .*probe: PASS' -Quiet)) { throw 'Missing PASS marker.' }
