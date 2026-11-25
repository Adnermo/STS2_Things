[CmdletBinding()]
param(
    [string]$DataDirV1111 = $env:STS2_DATA_DIR_V111_1,

    [string]$DataDirV1140 = $env:STS2_DATA_DIR_V114_0,

    [string]$BootstrapDll = '',

    [string]$ImplementationV1111 = '',

    [string]$ImplementationV1140 = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$Project = Join-Path $Root 'tools\UnifiedPackageProbe\UnifiedPackageProbe.csproj'

if ([string]::IsNullOrWhiteSpace($DataDirV1111)) {
    $DataDirV1111 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
}
if ([string]::IsNullOrWhiteSpace($DataDirV1140)) {
    $candidateV1140 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v114\0e151996c7aa\game\data_sts2_windows_x86_64'
    if (Test-Path -LiteralPath $candidateV1140 -PathType Container) {
        $DataDirV1140 = $candidateV1140
    }
    else {
        $DataDirV1140 = 'D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
    }
}
if ([string]::IsNullOrWhiteSpace($BootstrapDll)) {
    $BootstrapDll = Join-Path $Root 'build\unified\STS2_Things.dll'
}
if ([string]::IsNullOrWhiteSpace($ImplementationV1111)) {
    $ImplementationV1111 = Join-Path $Root 'build\v111.1\STS2_Things.dll'
}
if ([string]::IsNullOrWhiteSpace($ImplementationV1140)) {
    $ImplementationV1140 = Join-Path $Root 'build\v114.0\STS2_Things.dll'
}

$DataDirV1111 = [IO.Path]::GetFullPath($DataDirV1111)
$DataDirV1140 = [IO.Path]::GetFullPath($DataDirV1140)
$BootstrapDll = [IO.Path]::GetFullPath($BootstrapDll)
$ImplementationV1111 = [IO.Path]::GetFullPath($ImplementationV1111)
$ImplementationV1140 = [IO.Path]::GetFullPath($ImplementationV1140)

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue

foreach ($probe in @(
    @($DataDirV1111, 'v111.1', $ImplementationV1111),
    @($DataDirV1140, 'v114.0', $ImplementationV1140)
)) {
    $depDir = if (Test-Path -LiteralPath (Join-Path $probe[0] 'SmartFormat.dll')) { $probe[0] } else { $DataDirV1140 }
    & dotnet run --project $Project -c Release -- `
        $probe[0] $BootstrapDll $probe[1] $probe[2] $depDir
    if ($LASTEXITCODE -ne 0) {
        throw "Unified $($probe[1]) package probe failed with exit code $LASTEXITCODE"
    }
}
