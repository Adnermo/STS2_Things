[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')][string]$TargetVersion = 'v114.0',
    [string]$DataDir = '',

    [string]$ImplementationDll = '',

    [string]$RuntimeDependencyDir = '',

    [string]$PackagePck = '',

    [string]$GodotExe = $env:GODOT_4_5_1_MONO,
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$ProbeRoot = Join-Path $taskRoot 'tools\MerchantBargainProbe'
$ProbeProject = Join-Path $ProbeRoot 'MerchantBargainProbe.csproj'

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

foreach ($requiredPath in @(
    $ProbeProject,
    $ImplementationDll,
    (Join-Path $DataDir 'sts2.dll'),
    $GodotExe
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required Merchant Bargain probe input was not found: $requiredPath"
    }
}

$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)
$GodotExe = [IO.Path]::GetFullPath($GodotExe)

$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
Write-Host "Building Merchant Bargain behavior probe for $TargetVersion..."
& dotnet build $ProbeProject -t:Rebuild -c Debug --nologo `
    "/p:Sts2DataDir=$DataDir" `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:RuntimeDependencyDir=$RuntimeDependencyDir" `
    "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) {
    throw "Merchant Bargain probe build failed with exit code $LASTEXITCODE"
}

$portableDotnet = Join-Path $taskRoot '.tmp\dotnet9'
if (Test-Path -LiteralPath (Join-Path $portableDotnet 'dotnet.exe') -PathType Leaf) {
    $env:DOTNET_ROOT = $portableDotnet
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}

$logPath = Join-Path $ProbeRoot "probe-$TargetVersion.log"
if ($OutputDir) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    $logPath = Join-Path ([IO.Path]::GetFullPath($OutputDir)) "merchant-$TargetVersion.log"
}
if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    Remove-Item -LiteralPath $logPath -Force
}

Write-Host "Running Merchant Bargain behavior probe for $TargetVersion..."
& $GodotExe --headless --path $ProbeRoot --log-file $logPath
if ($LASTEXITCODE -ne 0) {
    throw "Merchant Bargain behavior probe failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Merchant Bargain probe log was not created: $logPath"
}

$logText = Get-Content -LiteralPath $logPath -Raw
$errors = Select-String -LiteralPath $logPath `
    -Pattern '(^|\s)(SCRIPT ERROR|ERROR:|WARNING:)|Failed loading resource' `
    -CaseSensitive:$false
if ($errors) {
    $preview = ($errors | Select-Object -First 12 | ForEach-Object Line) -join [Environment]::NewLine
    throw "Merchant Bargain probe reported errors:`n$preview"
}
if ($logText.IndexOf('Merchant bargain behavior probe: PASS', [StringComparison]::Ordinal) -lt 0) {
    throw 'Merchant Bargain probe did not report PASS.'
}

Write-Host "Merchant Bargain behavior probe passed for $TargetVersion."
