[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$PythonExe = $env:STS2_PYTHON,

    [string]$GameDir = $env:STS2_GAME_DIR,

    [string]$DataDirV1111 = $env:STS2_DATA_DIR_V111_1,

    [string]$DataDirV1131 = $env:STS2_DATA_DIR_V113_1,

    [string]$SourceRootV1111 = $env:STS2_SOURCE_ROOT_V111_1,

    [string]$SourceRootV1131 = $env:STS2_SOURCE_ROOT_V113_1,

    [string]$BaseLibRef = $env:STS2_BASELIB_REF,

    [string]$RitsuLibRef = $env:STS2_RITSULIB_REF,

    [string]$GodotExe = $env:GODOT_4_5_1_MONO,

    [switch]$SkipPck,

    [switch]$SkipBehaviorProbe,

    [switch]$SkipVisualProbe,

    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$WorkspaceParent = Split-Path $Root -Parent
$buildScript = Join-Path $PSScriptRoot 'build.ps1'

function Invoke-VersionBuild(
    [string]$TargetVersion,
    [string]$DataDir,
    [string]$SourceRoot,
    [bool]$SkipTargetPck,
    [string]$ReusePck
) {
    $arguments = @{
        Configuration = $Configuration
        TargetVersion = $TargetVersion
        SkipPck = $SkipTargetPck
    }
    if (-not [string]::IsNullOrWhiteSpace($PythonExe)) {
        $arguments.PythonExe = $PythonExe
    }
    if (-not [string]::IsNullOrWhiteSpace($GameDir)) {
        $arguments.GameDir = $GameDir
    }
    if (-not [string]::IsNullOrWhiteSpace($DataDir)) {
        $arguments.DataDir = $DataDir
    }
    if (-not [string]::IsNullOrWhiteSpace($SourceRoot)) {
        $arguments.SourceRoot = $SourceRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($BaseLibRef)) {
        $arguments.BaseLibRef = $BaseLibRef
    }
    if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
        $arguments.GodotExe = $GodotExe
    }
    if (-not [string]::IsNullOrWhiteSpace($ReusePck)) {
        $arguments.ReusePck = $ReusePck
        [void]$arguments.Remove('SkipPck')
    }

    & $buildScript @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$TargetVersion build failed with exit code $LASTEXITCODE"
    }

    if (-not $SkipBehaviorProbe) {
        $targetRuntimeDeps = if (Test-Path -LiteralPath (Join-Path $DataDir 'SmartFormat.dll')) {
            $DataDir
        }
        else {
            $DataDirV1131
        }

        $probeScript = Join-Path $PSScriptRoot 'test-quirky-hopper.ps1'
        $probeArguments = @{
            TargetVersion = $TargetVersion
            DataDir = $DataDir
            RuntimeDependencyDir = $targetRuntimeDeps
            ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $probeArguments.GodotExe = $GodotExe
        }
        & $probeScript @probeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$TargetVersion Quirky Hopper probe failed with exit code $LASTEXITCODE"
        }

        $splitProbeScript = Join-Path $PSScriptRoot 'test-things-split.ps1'
        $splitProbeArguments = @{
            TargetVersion = $TargetVersion
            DataDir = $DataDir
            RuntimeDependencyDir = $targetRuntimeDeps
            ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $splitProbeArguments.GodotExe = $GodotExe
        }
        & $splitProbeScript @splitProbeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$TargetVersion Things Split probe failed with exit code $LASTEXITCODE"
        }

        $collisionProbeScript = Join-Path $PSScriptRoot 'test-things-collision.ps1'
        $collisionProbeArguments = @{
            TargetVersion = $TargetVersion
            DataDir = $DataDir
            RuntimeDependencyDir = $targetRuntimeDeps
            ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $collisionProbeArguments.GodotExe = $GodotExe
        }
        & $collisionProbeScript @collisionProbeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$TargetVersion Things Collision probe failed with exit code $LASTEXITCODE"
        }

        $modelIdProbeScript = Join-Path $PSScriptRoot 'test-model-id-namespace.ps1'
        $modelIdProbeArguments = @{
            TargetVersion = $TargetVersion
            DataDir = $DataDir
            RuntimeDependencyDir = $targetRuntimeDeps
            ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
            Pck = Join-Path $Root "build\$TargetVersion\STS2_Things.pck"
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $modelIdProbeArguments.GodotExe = $GodotExe
        }
        & $modelIdProbeScript @modelIdProbeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$TargetVersion ModelId namespace probe failed with exit code $LASTEXITCODE"
        }

        $configProbeScript = Join-Path $PSScriptRoot 'test-things-config.ps1'
        $configProbeArguments = @{
            TargetVersion = $TargetVersion
            DataDir = $DataDir
            RuntimeDependencyDir = $targetRuntimeDeps
            ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
            Pck = Join-Path $Root "build\$TargetVersion\STS2_Things.pck"
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $configProbeArguments.GodotExe = $GodotExe
        }
        & $configProbeScript @configProbeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$TargetVersion config probe failed with exit code $LASTEXITCODE"
        }

        $gravetideProbeScript = Join-Path $PSScriptRoot 'test-gravetide-slug.ps1'
        $gravetideProbeArguments = @{
            TargetVersion = $TargetVersion
            DataDir = $DataDir
            RuntimeDependencyDir = $targetRuntimeDeps
            ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $gravetideProbeArguments.GodotExe = $GodotExe
        }
        & $gravetideProbeScript @gravetideProbeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$TargetVersion Gravetide Slug probe failed with exit code $LASTEXITCODE"
        }

        if ($TargetVersion -in @('v111.1', 'v113.1')) {
            $merchantProbeScript = Join-Path $PSScriptRoot 'test-merchant-bargain.ps1'
            $merchantProbeArguments = @{
                TargetVersion = $TargetVersion
                DataDir = $DataDir
                RuntimeDependencyDir = $targetRuntimeDeps
                ImplementationDll = Join-Path $Root "build\$TargetVersion\STS2_Things.dll"
            }
            if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
                $merchantProbeArguments.GodotExe = $GodotExe
            }
            & $merchantProbeScript @merchantProbeArguments
            if ($LASTEXITCODE -ne 0) {
                throw "$TargetVersion Merchant Bargain probe failed with exit code $LASTEXITCODE"
            }
        }
    }

    if ($TargetVersion -eq 'v113.1' -and -not $SkipVisualProbe) {
        $visualProbeScript = Join-Path $PSScriptRoot 'test-quirky-hopper-visual.ps1'
        $visualProbeArguments = @{}
        if (-not [string]::IsNullOrWhiteSpace($PythonExe)) {
            $visualProbeArguments.PythonExe = $PythonExe
        }
        if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
            $visualProbeArguments.GodotExe = $GodotExe
        }
        & $visualProbeScript @visualProbeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Quirky Hopper visual probe failed with exit code $LASTEXITCODE"
        }
    }
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = 'D:\Steam\steamapps\common\Slay the Spire 2'
}

if ([string]::IsNullOrWhiteSpace($DataDirV1111)) {
    $candidateDataV1111 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    if (Test-Path -LiteralPath $candidateDataV1111 -PathType Container) {
        $DataDirV1111 = $candidateDataV1111
    }
}
if ([string]::IsNullOrWhiteSpace($DataDirV1111)) {
    throw 'V111.1 reference directory is required. Pass -DataDirV1111 or set STS2_DATA_DIR_V111_1.'
}

if ([string]::IsNullOrWhiteSpace($DataDirV1131)) {
    $candidateDataV1131 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v113.1\76cc75b6f097\game\data_sts2_windows_x86_64'
    if (Test-Path -LiteralPath $candidateDataV1131 -PathType Container) {
        $DataDirV1131 = $candidateDataV1131
    }
    else {
        $DataDirV1131 = Join-Path $GameDir 'data_sts2_windows_x86_64'
    }
}

if ([string]::IsNullOrWhiteSpace($SourceRootV1111)) {
    $candidateSrcV1111 = Join-Path $WorkspaceParent 'STS2-V111.1'
    if (Test-Path -LiteralPath $candidateSrcV1111 -PathType Container) {
        $SourceRootV1111 = $candidateSrcV1111
    }
}

if ([string]::IsNullOrWhiteSpace($SourceRootV1131)) {
    $candidateSrcV1131 = Join-Path $WorkspaceParent 'STS2-V113.1'
    if (Test-Path -LiteralPath $candidateSrcV1131 -PathType Container) {
        $SourceRootV1131 = $candidateSrcV1131
    }
}

if ($SkipPck) {
    Invoke-VersionBuild 'v111.1' $DataDirV1111 $SourceRootV1111 $true ''
    Invoke-VersionBuild 'v113.1' $DataDirV1131 $SourceRootV1131 $true ''
}
else {
    Invoke-VersionBuild 'v113.1' $DataDirV1131 $SourceRootV1131 $false ''
    $sharedPck = Join-Path $Root 'build\v113.1\STS2_Things.pck'
    Invoke-VersionBuild 'v111.1' $DataDirV1111 $SourceRootV1111 $false $sharedPck

    $unifiedBuild = Join-Path $PSScriptRoot 'build-unified.ps1'
    $unifiedArguments = @{
        Configuration = $Configuration
        GameDir = $GameDir
        DataDirV1111 = $DataDirV1111
        DataDirV1131 = $DataDirV1131
        Install = $Install
    }
    if (-not [string]::IsNullOrWhiteSpace($PythonExe)) {
        $unifiedArguments.PythonExe = $PythonExe
    }
    & $unifiedBuild @unifiedArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Unified package build failed with exit code $LASTEXITCODE"
    }

    # 可选配置页集成探针（真实 Workshop BaseLib/RitsuLib DLL；两库不是前置依赖）。
    if (-not $SkipBehaviorProbe) {
        $baseLibDll = $BaseLibRef
        if ([string]::IsNullOrWhiteSpace($baseLibDll)) {
            $baseLibDll = 'D:\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll'
        }
        $bridgeDll = Join-Path $Root 'build\v113.1\STS2_Things.BaseLibBridge.dll'
        if ((Test-Path -LiteralPath $baseLibDll -PathType Leaf) -and
            (Test-Path -LiteralPath $bridgeDll -PathType Leaf)) {
            Write-Host 'Running the BaseLib config bridge probe...'
            & (Join-Path $PSScriptRoot 'test-baselib-bridge.ps1') `
                -DataDir $DataDirV1131 `
                -ImplementationDll (Join-Path $Root 'build\v113.1\STS2_Things.dll') `
                -RuntimeDependencyDir $DataDirV1131 `
                -BaseLibDll $baseLibDll `
                -BridgeDll $bridgeDll
            if ($LASTEXITCODE -ne 0) {
                throw "BaseLib config bridge probe failed with exit code $LASTEXITCODE"
            }
        }
        else {
            Write-Warning "BaseLib config bridge probe skipped: BaseLib.dll or the bridge DLL is missing."
        }

        $ritsuLibDll = $RitsuLibRef
        if ([string]::IsNullOrWhiteSpace($ritsuLibDll)) {
            $ritsuLibDll = 'D:\Steam\steamapps\workshop\content\2868840\3747602295\shared\STS2-RitsuLib.Settings.dll'
            if (-not (Test-Path -LiteralPath $ritsuLibDll -PathType Leaf)) {
                $ritsuLibDll = 'D:\Steam\steamapps\workshop\content\2868840\3747602295\lib\0.111.0\STS2-RitsuLib.dll'
            }
        }
        if (Test-Path -LiteralPath $ritsuLibDll -PathType Leaf) {
            Write-Host 'Running the RitsuLib interop probe...'
            & (Join-Path $PSScriptRoot 'test-ritsulib-interop.ps1') `
                -DataDir $DataDirV1131 `
                -ImplementationDll (Join-Path $Root 'build\v113.1\STS2_Things.dll') `
                -RuntimeDependencyDir $DataDirV1131 `
                -RitsuLibDll $ritsuLibDll
            if ($LASTEXITCODE -ne 0) {
                throw "RitsuLib interop probe failed with exit code $LASTEXITCODE"
            }
        }
        else {
            Write-Warning "RitsuLib interop probe skipped: STS2-RitsuLib.dll was not found at '$ritsuLibDll'."
        }
    }
}

if ($SkipPck) {
    Write-Host 'Version-specific implementation artifacts are under build\v111.1 and build\v113.1.'
}
else {
    Write-Host 'Unified subscription artifacts are under build\unified.'
}
