[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$PythonExe = $env:STS2_PYTHON,

    [string]$GameDir = $env:STS2_GAME_DIR,

    [string]$DataDirV1111 = $env:STS2_DATA_DIR_V111_1,

    [string]$DataDirV1131 = $env:STS2_DATA_DIR_V113_1,

    [string]$BaseLibRef = $env:STS2_BASELIB_REF,

    [switch]$Install
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$BuildDir = Join-Path $Root 'build\unified'
$BootstrapProject = Join-Path $Root 'bootstrap\STS2_Things.Bootstrap.csproj'
$ImplementationV1111 = Join-Path $Root 'build\v111.1\STS2_Things.dll'
$ImplementationV1131 = Join-Path $Root 'build\v113.1\STS2_Things.dll'
$SharedPck = Join-Path $Root 'build\v113.1\STS2_Things.pck'
$Manifest = Join-Path $Root 'STS2_Things.json'

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = 'D:\Steam\steamapps\common\Slay the Spire 2'
}
$GameDir = [IO.Path]::GetFullPath($GameDir)

if ([string]::IsNullOrWhiteSpace($DataDirV1111)) {
    $candidateV1111 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    if (Test-Path -LiteralPath $candidateV1111 -PathType Container) {
        $DataDirV1111 = $candidateV1111
    }
}
if ([string]::IsNullOrWhiteSpace($DataDirV1111)) {
    throw 'V111.1 reference directory is required. Pass -DataDirV1111 or set STS2_DATA_DIR_V111_1.'
}

if ([string]::IsNullOrWhiteSpace($DataDirV1131)) {
    $candidateV1131 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v113.1\76cc75b6f097\game\data_sts2_windows_x86_64'
    if (Test-Path -LiteralPath $candidateV1131 -PathType Container) {
        $DataDirV1131 = $candidateV1131
    }
    else {
        $DataDirV1131 = Join-Path $GameDir 'data_sts2_windows_x86_64'
    }
}

$DataDirV1111 = [IO.Path]::GetFullPath($DataDirV1111)
$DataDirV1131 = [IO.Path]::GetFullPath($DataDirV1131)

foreach ($path in @(
    $BootstrapProject,
    $ImplementationV1111,
    $ImplementationV1131,
    $SharedPck,
    $Manifest,
    (Join-Path $DataDirV1111 'sts2.dll'),
    (Join-Path $DataDirV1111 '0Harmony.dll'),
    (Join-Path $DataDirV1131 'sts2.dll'),
    (Join-Path $DataDirV1131 '0Harmony.dll')
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Unified build input was not found: $path"
    }
}

New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null

Write-Host 'Building unified STS2_Things bootstrap...'
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build $BootstrapProject -t:Rebuild -c $Configuration --nologo `
    "/p:Sts2DataDir=$DataDirV1111" `
    "/p:ImplementationV1111=$ImplementationV1111" `
    "/p:ImplementationV1131=$ImplementationV1131"
if ($LASTEXITCODE -ne 0) {
    throw "Unified bootstrap build failed with exit code $LASTEXITCODE"
}

$BootstrapDll = Join-Path $Root "bootstrap\bin\$Configuration\net9.0\STS2_Things.Bootstrap.dll"
if (-not (Test-Path -LiteralPath $BootstrapDll -PathType Leaf)) {
    throw "Unified bootstrap DLL was not found: $BootstrapDll"
}

Copy-Item -LiteralPath $BootstrapDll -Destination (Join-Path $BuildDir 'STS2_Things.dll') -Force
Copy-Item -LiteralPath $SharedPck -Destination (Join-Path $BuildDir 'STS2_Things.pck') -Force
Copy-Item -LiteralPath $Manifest -Destination (Join-Path $BuildDir 'STS2_Things.json') -Force

# 可选 BaseLib 配置页桥：随统一包发布（无 BaseLib 时不会被加载）。
$bridgeSource = Join-Path $Root 'build\v113.1\STS2_Things.BaseLibBridge.dll'
if (Test-Path -LiteralPath $bridgeSource) {
    Copy-Item -LiteralPath $bridgeSource `
        -Destination (Join-Path $BuildDir 'STS2_Things.BaseLibBridge.dll') -Force
    Write-Host 'BaseLib config bridge: copied into the unified package.'
}
else {
    Write-Warning 'BaseLib config bridge is missing from build\v113.1; the unified package ships without it.'
}

$packageProbe = Join-Path $PSScriptRoot 'verify-unified-package.ps1'
& $packageProbe `
    -DataDirV1111 $DataDirV1111 `
    -DataDirV1131 $DataDirV1131 `
    -BootstrapDll (Join-Path $BuildDir 'STS2_Things.dll') `
    -ImplementationV1111 $ImplementationV1111 `
    -ImplementationV1131 $ImplementationV1131
if ($LASTEXITCODE -ne 0) {
    throw "Unified package probe failed with exit code $LASTEXITCODE"
}

if ([string]::IsNullOrWhiteSpace($PythonExe)) {
    $PythonExe = Join-Path $env:USERPROFILE `
        '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
}
$validator = Join-Path $env:USERPROFILE '.codex\skills\sts2-mod-master\scripts\validate_mod.py'
if ((Test-Path -LiteralPath $PythonExe -PathType Leaf) -and
    (Test-Path -LiteralPath $validator -PathType Leaf)) {
    $validatorSourceRoot = Join-Path (Split-Path $Root -Parent) 'STS2-V113.1'
    & $PythonExe -X utf8 $validator `
        (Join-Path $BuildDir 'STS2_Things.json') `
        --source-root $validatorSourceRoot `
        --require-artifacts --strict
    if ($LASTEXITCODE -ne 0) {
        throw "Unified package validation failed with exit code $LASTEXITCODE"
    }
}

if ($Install) {
    $modsDir = Join-Path $GameDir 'mods'
    if (-not (Test-Path -LiteralPath $modsDir -PathType Container)) {
        New-Item -ItemType Directory -Path $modsDir | Out-Null
    }
    $modsDir = (Resolve-Path -LiteralPath $modsDir).Path
    $installDir = Join-Path $modsDir 'STS2_Things'
    if ((Split-Path -Parent $installDir) -ne $modsDir) {
        throw "Unsafe install path: $installDir"
    }
    if (-not (Test-Path -LiteralPath $installDir -PathType Container)) {
        New-Item -ItemType Directory -Path $installDir | Out-Null
    }
    $unexpectedJson = Get-ChildItem -LiteralPath $installDir -File -Filter '*.json' |
        Where-Object Name -ne 'STS2_Things.json'
    if ($unexpectedJson) {
        throw "Refusing to install beside extra JSON manifests: $($unexpectedJson.Name -join ', ')"
    }
    foreach ($name in 'STS2_Things.json', 'STS2_Things.dll', 'STS2_Things.pck') {
        Copy-Item -LiteralPath (Join-Path $BuildDir $name) `
            -Destination (Join-Path $installDir $name) -Force
    }
    $bridgeSource = Join-Path $BuildDir 'STS2_Things.BaseLibBridge.dll'
    if (Test-Path -LiteralPath $bridgeSource) {
        Copy-Item -LiteralPath $bridgeSource `
            -Destination (Join-Path $installDir 'STS2_Things.BaseLibBridge.dll') -Force
    }
    Write-Host "Installed unified package to $installDir"
}

Write-Host 'Unified artifacts:'
foreach ($name in 'STS2_Things.json', 'STS2_Things.dll', 'STS2_Things.pck') {
    $path = Join-Path $BuildDir $name
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    Write-Host ("  {0,-18} {1,10} bytes  SHA256 {2}" -f $name, $item.Length, $hash)
}
