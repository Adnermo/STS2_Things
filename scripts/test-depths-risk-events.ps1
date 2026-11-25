[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')]
    [string]$TargetVersion = 'v114.0',

    [string]$DataDir = '',

    [string]$RuntimeDependencyDir = '',

    [string]$VanillaPck = '',

    [string]$ImplementationDll = '',

    [string]$PackagePck = '',

    [string]$OutputDir = '',

    [switch]$Render,

    [int]$TimeoutMs = 90000,

    [string]$GodotExe = ''
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')

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
if ([string]::IsNullOrWhiteSpace($VanillaPck)) {
    $VanillaPck = $candidateGamePck
}
if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
if ([string]::IsNullOrWhiteSpace($PackagePck)) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $PackagePck = $candidateModPck
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $taskRoot "build\probe-runs\depths-risk-$TargetVersion"
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
& 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $taskRoot 'tools/DepthsRiskEventsProbe/DepthsRiskEventsProbe.csproj') -c Debug --nologo `
    "/p:ImplementationDll=$([IO.Path]::GetFullPath($ImplementationDll))" `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:Sts2DataDir=$DataDir" `
    "/p:RuntimeDependencyDir=$([IO.Path]::GetFullPath($RuntimeDependencyDir))"
if ($LASTEXITCODE -ne 0) { throw "DepthsRiskEventsProbe build failed ($TargetVersion)" }

if (Test-Path -LiteralPath (Join-Path $taskRoot '.tmp\dotnet9')) {
    $env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
}
$env:THINGS_VANILLA_PCK = [IO.Path]::GetFullPath($VanillaPck)
$env:THINGS_PROBE_PCK = if ($PackagePck) { [IO.Path]::GetFullPath($PackagePck) } else { '' }
$env:THINGS_PROBE_OUTPUT = [IO.Path]::GetFullPath($OutputDir)
$env:THINGS_RISK_RENDER = if ($Render) { '1' } else { '0' }
New-Item -ItemType Directory -Path $env:THINGS_PROBE_OUTPUT -Force | Out-Null

$stdout = Join-Path $env:THINGS_PROBE_OUTPUT 'native.stdout.log'
$stderr = Join-Path $env:THINGS_PROBE_OUTPUT 'native.stderr.log'
$logPath = Join-Path $env:THINGS_PROBE_OUTPUT 'godot.log'
$arguments = if ($Render) {
    @('--path', (Join-Path $taskRoot 'tools/DepthsRiskEventsProbe'), '--rendering-method', 'gl_compatibility', '--resolution', '1920x1080', '--log-file', $logPath)
}
else {
    @('--headless', '--path', (Join-Path $taskRoot 'tools/DepthsRiskEventsProbe'), '--log-file', $logPath)
}

$result = Start-HeadlessSafeProcess `
    -FilePath $GodotExe `
    -ArgumentList $arguments `
    -TimeoutMs $TimeoutMs `
    -StdoutPath $stdout `
    -StderrPath $stderr

if ($result.TimedOut) {
    throw "DepthsRiskEventsProbe ($TargetVersion) timed out after $TimeoutMs ms"
}
if ($result.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Tail 40 }
    if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout -Tail 40 }
    throw "DepthsRiskEventsProbe ($TargetVersion) exited with code $($result.ExitCode)"
}
if ((Test-Path -LiteralPath $stderr) -and (Select-String -LiteralPath $stderr -Pattern '^ERROR:|SCRIPT ERROR:' -Quiet)) {
    Get-Content -LiteralPath $stderr -TotalCount 30
    throw "DepthsRiskEventsProbe ($TargetVersion) logged errors"
}
if (-not (Select-String -LiteralPath $stdout -Pattern 'Depths risk events probe: PASS' -Quiet)) {
    Get-Content -LiteralPath $stdout -Tail 30
    throw "DepthsRiskEventsProbe ($TargetVersion) missing PASS marker"
}
Get-Content -LiteralPath $stdout -Tail 10
