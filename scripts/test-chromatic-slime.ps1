[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')]
    [string]$TargetVersion = 'v113.1',
    [string]$GodotExe = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskBuild = Join-Path $taskRoot 'build\chromatic_slime'
if ($OutputDir) { $taskBuild = [IO.Path]::GetFullPath($OutputDir) }
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$env:THINGS_PROBE_OUTPUT = $taskBuild

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    if ($TargetVersion -eq 'v111.1') {
        $DataDir = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    }
    else {
        $v1131DefaultData = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v113.1\76cc75b6f097\game\data_sts2_windows_x86_64'
        if (Test-Path -LiteralPath $v1131DefaultData -PathType Container) {
            $DataDir = $v1131DefaultData
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

$taskProbe = Join-Path $taskRoot 'tools\ChromaticSlimeProbe'
$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

if (-not $ImplementationDll) {
    $builtDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
    if (Test-Path -LiteralPath $builtDll -PathType Leaf) {
        $ImplementationDll = $builtDll
    }
    else {
        Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
        & dotnet build (Join-Path $taskRoot 'STS2_Things.csproj') -c Debug --nologo -o (Join-Path $taskBuild $TargetVersion) "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir"
        if ($LASTEXITCODE -ne 0) { throw 'Chromatic Slime implementation compile failed' }
        $ImplementationDll = Join-Path $taskBuild "$TargetVersion\STS2_Things.dll"
    }
}

$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build (Join-Path $taskProbe 'ChromaticSlimeProbe.csproj') -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) { throw 'Chromatic Slime probe compile failed' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$taskMode = if ($Visual) { 'visual' } else { "behavior-$TargetVersion" }
$taskStdout = Join-Path $taskBuild "$taskMode.stdout.log"
$taskStderr = Join-Path $taskBuild "$taskMode.stderr.log"

$taskArgs = @('--path', ('"' + $taskProbe + '"'), '--rendering-method', 'gl_compatibility')
if ($Visual) {
    $taskArgs += @('--', '--visual')
}
else {
    $taskArgs += @('--headless')
}

$taskProcess = Start-Process -FilePath $GodotExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskStdout -RedirectStandardError $taskStderr
try {
    $taskStarted = Get-Date
    while (-not $taskProcess.WaitForExit(30000)) {
        Write-Host 'Chromatic Slime native checks are running...'
        if (((Get-Date) - $taskStarted).TotalSeconds -gt 240) { throw 'Chromatic Slime probe timed out' }
    }
    if ($taskProcess.ExitCode -ne 0) {
        Get-Content -LiteralPath $taskStderr -Tail 25
        throw "Chromatic Slime probe failed: $taskStderr"
    }
}
finally {
    if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
    $taskProcess.Dispose()
}

Get-Content -LiteralPath $taskStdout -Tail 12
if (Select-String -LiteralPath $taskStderr -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) {
    Get-Content -LiteralPath $taskStderr -Tail 25
    throw "Chromatic Slime probe logged Godot errors: $taskStderr"
}
