[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')][string]$TargetVersion = 'v114.0',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [string]$GamePck = '',
    [string]$GodotExe = '',
    [string]$Encounter = '',
    [switch]$SourceScenes
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskOut = if ($OutputDir) { [IO.Path]::GetFullPath($OutputDir) } else { Join-Path $taskRoot "build\encounter_layout\$TargetVersion" }
New-Item -ItemType Directory -Force -Path $taskOut | Out-Null
$taskProbe = Join-Path $taskOut 'probe'
New-Item -ItemType Directory -Force -Path $taskProbe | Out-Null

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

if ($GamePck) {
    $env:THINGS_PROBE_GAME_PCK = [IO.Path]::GetFullPath($GamePck)
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

Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tools\DepthsProbe') -File |
    Where-Object Extension -In '.cs', '.csproj', '.godot', '.tscn' |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskProbe $_.Name) -Force }
$probeSupportSrc = Join-Path $taskRoot 'tools\ProbeSupport'
if (Test-Path -LiteralPath $probeSupportSrc -PathType Container) {
    $probeSupportDst = Join-Path (Split-Path $taskProbe -Parent) 'ProbeSupport'
    New-Item -ItemType Directory -Path $probeSupportDst -Force | Out-Null
    Get-ChildItem -LiteralPath $probeSupportSrc -File |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $probeSupportDst $_.Name) -Force }
}
$env:THINGS_PROBE_ROOT = $taskRoot
$env:THINGS_PROBE_OUTPUT = $taskOut
$env:THINGS_LAYOUT_SOURCE_SCENES = if ($SourceScenes) { '1' } else { '0' }
$env:THINGS_LAYOUT_ENCOUNTER = $Encounter

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $taskProbe 'DepthsProbe.csproj') -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) { throw 'Layout probe compilation failed.' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$taskProcess = Start-Process -FilePath $GodotExe -WindowStyle Hidden -PassThru `
    -ArgumentList @('--path', ('"' + $taskProbe + '"'), '--rendering-method', 'gl_compatibility', '--', '--layout-only') `
    -RedirectStandardOutput (Join-Path $taskOut 'stdout.log') -RedirectStandardError (Join-Path $taskOut 'stderr.log')
try {
    $taskStarted = Get-Date
    while (-not $taskProcess.WaitForExit(30000)) {
        Write-Host 'Checking native creature HUD placement...'
        if (((Get-Date) - $taskStarted).TotalSeconds -gt 180) { throw 'Layout probe timed out.' }
    }
    Get-Content -LiteralPath (Join-Path $taskOut 'stdout.log') -Tail 18
    if ($taskProcess.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $taskOut 'stderr.log') -Tail 12
        throw 'Layout probe failed; see measurements and render captures.'
    }
    if (Select-String -LiteralPath (Join-Path $taskOut 'stderr.log') -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw 'Native rendering logged errors.' }
    if (-not (Select-String -LiteralPath (Join-Path $taskOut 'stdout.log') -Pattern 'Depths encounter layout probe: PASS' -Quiet)) { throw 'Missing layout PASS marker.' }
}
finally {
    if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
    $taskProcess.Dispose()
}
