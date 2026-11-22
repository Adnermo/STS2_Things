[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')]
    [string]$TargetVersion = 'v113.1',

    [string]$DataDir = '',

    [string]$ImplementationDll = '',

    [string]$RuntimeDependencyDir = '',

    [string]$ActLikeIt2Dir = 'D:\Steam\steamapps\workshop\content\2868840\3808369281',

    [string]$PackagePck = '',

    [string]$GodotExe = $env:GODOT_4_5_1_MONO
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$ProbeRoot = Join-Path $taskRoot 'tools\ActLikeIt2CompatibilityProbe'
$ProbeProject = Join-Path $ProbeRoot 'ActLikeIt2CompatibilityProbe.csproj'

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
$GamePck = Join-Path (Split-Path $DataDir -Parent) 'SlayTheSpire2.pck'

if ([string]::IsNullOrWhiteSpace($RuntimeDependencyDir)) {
    $RuntimeDependencyDir = $DataDir
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

$ActLikeIt2Dir = [IO.Path]::GetFullPath($ActLikeIt2Dir)
$LoaderDll = Join-Path $ActLikeIt2Dir 'ActLikeIt2.dll'
$VariantSubdir = if ($TargetVersion -eq 'v111.1') { '0.111.0' } else { '0.113.0' }
$VariantDll = Join-Path $ActLikeIt2Dir "lib\$VariantSubdir\ActLikeIt2.dll"
$ActLikeItPck = Join-Path $ActLikeIt2Dir 'ActLikeIt2.pck'

if ([string]::IsNullOrWhiteSpace($GodotExe) -or -not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    if (Test-Path -LiteralPath 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' -PathType Leaf) {
        $GodotExe = 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
    else {
        $GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
}

foreach ($requiredPath in @($ProbeProject, $GamePck, $PackagePck, $ImplementationDll, $LoaderDll, $VariantDll, $ActLikeItPck, (Join-Path $DataDir 'sts2.dll'))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required ActLikeIt2 compatibility probe input was not found: $requiredPath"
    }
}

$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)
$PackagePck = [IO.Path]::GetFullPath($PackagePck)
$GodotExe = [IO.Path]::GetFullPath($GodotExe)

$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
Write-Host "Building ActLikeIt2 compatibility probe ($TargetVersion)..."
& dotnet build $ProbeProject -t:Rebuild -c Debug --nologo `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:Sts2DataDir=$DataDir" `
    "/p:RuntimeDependencyDir=$RuntimeDependencyDir" `
    "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) {
    throw "ActLikeIt2 compatibility probe build failed with exit code $LASTEXITCODE"
}

$portableDotnet = Join-Path $taskRoot '.tmp\dotnet9'
if (Test-Path -LiteralPath (Join-Path $portableDotnet 'dotnet.exe') -PathType Leaf) {
    $env:DOTNET_ROOT = $portableDotnet
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}

foreach ($mode in @('--standalone', '--things-first', '--actlikeit-first')) {
    $logName = "probe-$TargetVersion-$($mode.TrimStart('-')).log"
    $logPath = Join-Path $ProbeRoot $logName
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Remove-Item -LiteralPath $logPath -Force
    }

    Write-Host "Running ActLikeIt2 compatibility probe ($TargetVersion, $mode)..."
    & $GodotExe --headless --path $ProbeRoot --log-file $logPath -- @($GamePck, $PackagePck, $LoaderDll, $VariantDll, $ActLikeItPck, $mode)
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path -LiteralPath $logPath) {
            Get-Content -LiteralPath $logPath -Tail 40
        }
        throw "ActLikeIt2 compatibility probe ($TargetVersion, $mode) failed with exit code $LASTEXITCODE"
    }
    $logText = Get-Content -LiteralPath $logPath -Raw -ErrorAction Stop
    if ($logText.IndexOf("ACTLIKEIT2_COMPAT_PASS mode=$mode", [StringComparison]::Ordinal) -lt 0) {
        Get-Content -LiteralPath $logPath -Tail 40
        throw "ActLikeIt2 compatibility probe ($TargetVersion, $mode) did not report PASS."
    }
    Write-Host "  [PASS] $TargetVersion $mode"
}

Write-Host "ActLikeIt2 compatibility probe ($TargetVersion): ALL MODES PASSED."
