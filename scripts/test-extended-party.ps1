[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')]
    [string]$TargetVersion = 'v114.0',

    [string]$DataDir = '',

    [string]$ImplementationDll = '',

    [string]$RuntimeDependencyDir = '',

    [string]$PackagePck = '',

    [string]$GodotExe = $env:GODOT_4_5_1_MONO
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$ProbeRoot = Join-Path $taskRoot 'tools\ExtendedPartyProbe'
$ProbeProject = Join-Path $ProbeRoot 'ExtendedPartyProbe.csproj'

function Resolve-GodotConsoleExecutable([string]$Requested) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        $candidates += $Requested
        $requestedDirectory = Split-Path -Parent $Requested
        $requestedName = [IO.Path]::GetFileNameWithoutExtension($Requested)
        if (-not $requestedName.EndsWith('_console', [StringComparison]::OrdinalIgnoreCase)) {
            $candidates += Join-Path $requestedDirectory ($requestedName + '_console.exe')
        }
    }

    $candidates += @(
        'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe',
        'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    )

    foreach ($candidate in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw 'Godot 4.5.1 Mono console executable was not found. Pass -GodotExe or set GODOT_4_5_1_MONO.'
}

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

foreach ($requiredPath in @($ProbeProject, $ImplementationDll, (Join-Path $DataDir 'sts2.dll'))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required Extended Party probe input was not found: $requiredPath"
    }
}

$GodotExe = Resolve-GodotConsoleExecutable $GodotExe
$dotnetExe = 'C:\Program Files\dotnet\dotnet.exe'
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue

& $dotnetExe build $ProbeProject -c Debug --nologo `
    "/p:ImplementationDll=$([IO.Path]::GetFullPath($ImplementationDll))" `
    "/p:Sts2TargetVersion=$TargetVersion" `
    "/p:Sts2DataDir=$DataDir" `
    "/p:RuntimeDependencyDir=$([IO.Path]::GetFullPath($RuntimeDependencyDir))"
if ($LASTEXITCODE -ne 0) {
    throw "ExtendedPartyProbe build failed with exit code $LASTEXITCODE"
}

$portableDotnet = Join-Path $taskRoot '.tmp\dotnet9'
if (Test-Path -LiteralPath (Join-Path $portableDotnet 'dotnet.exe') -PathType Leaf) {
    $env:DOTNET_ROOT = $portableDotnet
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}

$logPath = Join-Path $ProbeRoot "probe-$TargetVersion.log"
if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    Remove-Item -LiteralPath $logPath -Force
}

Write-Host "Running Extended Party (>4P) probe for $TargetVersion..."
& $GodotExe --headless --path $ProbeRoot --log-file $logPath
if ($LASTEXITCODE -ne 0) {
    throw "ExtendedPartyProbe failed for $TargetVersion with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "ExtendedPartyProbe log was not created: $logPath"
}

$logText = Get-Content -LiteralPath $logPath -Raw
$errors = Select-String -LiteralPath $logPath `
    -Pattern '(^|\s)(SCRIPT ERROR|ERROR:|WARNING:)|Failed loading resource' `
    -CaseSensitive:$false
if ($errors) {
    $preview = ($errors | Select-Object -First 12 | ForEach-Object Line) -join [System.Environment]::NewLine
    throw "ExtendedPartyProbe reported errors for ${TargetVersion}:`n$preview"
}
if ($logText.IndexOf('Extended Party (>4P) compatibility probe: PASS', [StringComparison]::Ordinal) -lt 0) {
    throw "ExtendedPartyProbe did not report PASS for $TargetVersion."
}

Write-Host "Extended Party (>4P) probe passed for $TargetVersion."
